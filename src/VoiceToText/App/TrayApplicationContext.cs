using System.Diagnostics;
using System.Drawing;
using System.Threading;
using VoiceToText.Audio;
using VoiceToText.Diagnostics;
using VoiceToText.Dashboard;
using VoiceToText.Hotkeys;
using VoiceToText.Injection;
using VoiceToText.Overlay;
using VoiceToText.Onboarding;
using VoiceToText.History;
using VoiceToText.Stats;
using VoiceToText.Settings;
using VoiceToText.Stt;
using VoiceToText.TextProcessing;
using VoiceToText.Update;
using Whisper.net.Ggml;

namespace VoiceToText.App;

/// <summary>
/// The running application: a tray icon, a global hotkey, and the
/// capture -> transcribe -> inject loop.
/// </summary>
internal sealed class TrayApplicationContext : ApplicationContext
{
    private readonly HiddenWindow _window;
    private readonly NotifyIcon _trayIcon;
    private readonly HotkeyManager _hotkeys;
    private readonly IconCache _icons = new();
    private readonly IAudioSource _audio = new WasapiAudioSource();
    private readonly SoundCues _cues = new();
    private readonly ITextInjector _injector = new ClipboardTextInjector();
    private readonly AppSettings _settings;

    private readonly UpdateService _updates;
    private readonly StatsService _stats = new();
    private readonly HistoryService _history = new();
    private readonly string? _postUpdateTarget;

    private ISttEngine _stt;
    private ListeningOverlay? _overlay;
    private DashboardForm? _dashboard;
    private OnboardingWizard? _onboarding;
    private HotkeyDefinition _registeredHotkey;
    private AppState _state = AppState.Idle;
    private bool _busy;
    private GgmlType _loadedModelType;
    private bool _modelReloadPending;
    private int _updateInProgress; // 0/1, set/cleared via Interlocked

    private string VersionLabel => _updates.CurrentVersion is { } v ? $"v{v.ToString(3)}" : "";

    public TrayApplicationContext(string? postUpdateTarget = null)
    {
        _postUpdateTarget = postUpdateTarget;
        _settings = AppSettings.Load();
        Application.ThreadException += OnUiThreadException;
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        _registeredHotkey = _settings.Hotkey;
        _stt = new WhisperSttEngine(_settings.ModelType, _settings.Language);
        _loadedModelType = _settings.ModelType;
        _updates = new UpdateService(_settings);

        // Clean leftover staged-update files on a normal start. On a post-update launch
        // the shim is still finishing, so skip it then to avoid deleting files in use.
        if (postUpdateTarget is null)
            UpdateService.CleanStaging();

        _window = new HiddenWindow();
        _window.EnsureHandle();

        if (_settings.ShowOverlay)
            CreateOverlay();
        _audio.LevelChanged += level => _overlay?.SetLevel(level);

        _trayIcon = new NotifyIcon
        {
            Visible = true,
            Icon = _icons.Get(AppState.Idle),
            Text = $"Voice to Text {VersionLabel}".TrimEnd(),
            ContextMenuStrip = BuildMenu(),
        };
        _trayIcon.DoubleClick += (_, _) => ShowDashboard(DashboardPageKind.Dashboard);

        _hotkeys = new HotkeyManager(_window);
        _hotkeys.Pressed += OnHotkeyPressed;
        _hotkeys.Released += OnHotkeyReleased;
        RegisterHotkey();
        _hotkeys.HoldToTalk = _settings.HoldToTalk;

        _audio.SilenceDetected += OnSilenceDetected;
        _audio.RecordingFailed += OnRecordingFailed;

        _ = Task.Run(WarmUpAsync);

        if (_postUpdateTarget is not null)
            _window.BeginInvoke(ShowPostUpdateBalloon);
        else
            _ = Task.Run(() => CheckForUpdatesAsync(userInitiated: false));

        if (!_settings.OnboardingCompleted)
        {
            // Mark + persist immediately so the welcome never re-shows (even on a crash/close).
            _settings.OnboardingCompleted = true;
            _settings.Save();
            ShowOnboarding();
        }
    }

    private void ShowOnboarding()
    {
        _onboarding = new OnboardingWizard(_settings);
        _onboarding.Completed += OnSettingsSaved; // persist + re-register the (possibly changed) hotkey
        _onboarding.FormClosed += (_, _) => _onboarding = null;
        _onboarding.Show();
    }

    private void OnUiThreadException(object? sender, ThreadExceptionEventArgs e)
    {
        Log.Error("Unhandled UI-thread exception", e.Exception);
        try { ShowError("Something went wrong. Details are in the log (About → Open log folder)."); }
        catch { /* never re-throw from the handler */ }
    }

    private void OnUnhandledException(object? sender, UnhandledExceptionEventArgs e)
        => Log.Error("Unhandled exception", e.ExceptionObject as Exception);

    private void CreateOverlay()
    {
        try
        {
            var pos = _settings.OverlayX is int x && _settings.OverlayY is int y ? new Point(x, y) : (Point?)null;
            _overlay = new ListeningOverlay(pos);
            _overlay.PositionChanged += p =>
            {
                _settings.OverlayX = p.X;
                _settings.OverlayY = p.Y;
                _settings.Save();
            };
        }
        catch
        {
            _overlay = null; // overlay is cosmetic — never block startup
        }
    }

    private void ApplyOverlaySetting()
    {
        if (_settings.ShowOverlay && _overlay is null)
        {
            CreateOverlay();
        }
        else if (!_settings.ShowOverlay && _overlay is not null)
        {
            _overlay.Dispose();
            _overlay = null;
        }
    }

    // Fires on the capture thread when auto-stop detects a pause; marshal to the
    // UI thread and stop exactly as a manual hotkey-stop would.
    private void OnSilenceDetected()
    {
        if (!_window.IsHandleCreated)
            return;
        _window.BeginInvoke(() =>
        {
            if (_state == AppState.Recording && !_busy)
                _ = StopAndTranscribeAsync();
        });
    }

    // Fires on the capture thread when the mic drops out mid-recording; marshal to the UI
    // thread, tell the user, discard the partial capture, and return to Idle.
    private void OnRecordingFailed(Exception ex)
    {
        if (!_window.IsHandleCreated)
            return;
        _window.BeginInvoke(() =>
        {
            // If a normal stop already won the race, _state is Transcribing (or _busy is set) — no-op.
            if (_state != AppState.Recording || _busy)
                return;
            _busy = true;
            SetState(AppState.Idle); // reflect Idle now; _busy guards re-entry during async cleanup
            Log.Error("Microphone lost during recording", ex);
            ShowError("Microphone disconnected — recording stopped.");
            _ = ResetAfterFailureAsync();
        });
    }

    private async Task ResetAfterFailureAsync()
    {
        try { await _audio.StopAndGetSamplesAsync().ConfigureAwait(false); }
        catch (Exception ex) { Log.Error("Cleanup after mic loss failed", ex); }
        if (_window.IsHandleCreated)
            _window.BeginInvoke(() =>
            {
                SetState(AppState.Idle);
                _busy = false;
            });
        else
            _busy = false; // window gone (app exiting) — clear the guard so dictation never sticks
    }

    private ContextMenuStrip BuildMenu()
    {
        var menu = new ContextMenuStrip();
        var open = new ToolStripMenuItem("Open Dashboard", null, (_, _) => ShowDashboard(DashboardPageKind.Dashboard))
        {
            Font = new Font(menu.Font, FontStyle.Bold),
        };
        menu.Items.Add(open);
        menu.Items.Add("Settings…", null, (_, _) => ShowDashboard(DashboardPageKind.Settings));
        menu.Items.Add("Check for updates…", null, (_, _) => _ = CheckForUpdatesAsync(userInitiated: true));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => ExitApp());
        return menu;
    }

    private void RegisterHotkey()
    {
        if (_hotkeys.Register(_settings.Hotkey))
        {
            _registeredHotkey = _settings.Hotkey;
        }
        else
        {
            _registeredHotkey = _settings.Hotkey; // nothing else is registered; keep intent for re-tries
            _trayIcon.ShowBalloonTip(
                6000,
                "Voice to Text",
                $"Hotkey {_settings.Hotkey.Describe()} is already in use. Open Settings to choose another.",
                ToolTipIcon.Warning);
        }
    }

    private void OnHotkeyPressed()
    {
        if (_busy)
            return;

        // Hold-to-talk: a press only starts; release (OnHotkeyReleased) stops.
        if (_settings.HoldToTalk)
        {
            if (_state == AppState.Idle)
                StartRecording();
            return;
        }

        // Press-to-toggle.
        switch (_state)
        {
            case AppState.Idle:
                StartRecording();
                break;
            case AppState.Recording:
                _ = StopAndTranscribeAsync();
                break;
        }
    }

    private void OnHotkeyReleased()
    {
        if (_settings.HoldToTalk && _state == AppState.Recording && !_busy)
            _ = StopAndTranscribeAsync();
    }

    private void StartRecording()
    {
        if (Audio.AudioDevices.GetInputDevices().Count == 0)
        {
            Log.Error("Start recording aborted: no input device present.");
            ShowError("No microphone found — connect one and try again.");
            return;
        }

        try
        {
            var autoStop = !_settings.HoldToTalk && _settings.AutoStopEnabled;
            _audio.Start(_settings.InputDeviceId, autoStop, _settings.AutoStopSilenceSeconds);
            SetState(AppState.Recording);
            if (_settings.SoundCuesEnabled) { _cues.Volume = (float)_settings.SoundCuesVolume; _cues.PlayStart(); }
        }
        catch (Exception ex)
        {
            Log.Error("Could not start recording", ex);
            ShowError($"Could not start recording: {ex.Message}");
            SetState(AppState.Idle);
        }
    }

    private async Task StopAndTranscribeAsync()
    {
        // Play the stop cue immediately on release/stop, before capture ends, so it never bleeds
        // into the captured audio (covers hold-release + toggle-stop — both call this).
        if (_settings.SoundCuesEnabled) { _cues.Volume = (float)_settings.SoundCuesVolume; _cues.PlayStop(); }
        _busy = true;
        SetState(AppState.Transcribing);
        try
        {
            var samples = await _audio.StopAndGetSamplesAsync().ConfigureAwait(false);
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var text = await _stt.TranscribeAsync(samples).ConfigureAwait(false);
            sw.Stop();
            text = TextRules.Apply(text, _settings.Replacements, _settings.SpokenCommandsEnabled);
            Log.Info($"Transcribed {StatsData.CountWords(text)} words in {sw.Elapsed.TotalSeconds:F2}s ({samples.Length / 16000.0:F1}s audio).");

            if (!string.IsNullOrWhiteSpace(text))
            {
                var words = StatsData.CountWords(text);
                var seconds = samples.Length / 16000.0;
                var transcribeSeconds = sw.Elapsed.TotalSeconds;
                var model = _settings.ModelType.ToString();
                _window.BeginInvoke(() =>
                {
                    var app = NativeForeground.GetForegroundProcessName();
                    _injector.Inject(text);
                    _stats.Record(words, seconds, app);
                    if (_settings.HistoryEnabled)
                        _history.Record(text, words, app, transcribeSeconds, model);
                });
            }
        }
        catch (Exception ex)
        {
            Log.Error("Transcription failed", ex);
            _window.BeginInvoke(() => ShowError($"Transcription failed: {ex.Message}"));
        }
        finally
        {
            _window.BeginInvoke(() =>
            {
                SetState(AppState.Idle);
                _busy = false;
                MaybeReloadModel();
            });
        }
    }

    private void SetState(AppState state)
    {
        _state = state;
        _trayIcon.Icon = _icons.Get(state);
        _trayIcon.Text = state switch
        {
            AppState.Recording => "Voice to Text — Recording…",
            AppState.Transcribing => "Voice to Text — Transcribing…",
            _ => $"Voice to Text {VersionLabel}".TrimEnd(),
        };
        try
        {
            _overlay?.SetState(state switch
            {
                AppState.Recording => OverlayState.Recording,
                AppState.Transcribing => OverlayState.Transcribing,
                _ => OverlayState.Hidden,
            });
        }
        catch { /* overlay is cosmetic — never disrupt dictation */ }
    }

    private async Task WarmUpAsync()
    {
        try
        {
            if (!ModelManager.IsModelPresent(_settings.ModelType))
            {
                _window.BeginInvoke(() => _trayIcon.ShowBalloonTip(
                    9000,
                    "Voice to Text",
                    $"Downloading the {_settings.ModelType} speech model (first run only). Dictation will be ready shortly.",
                    ToolTipIcon.Info));
            }

            await _stt.LoadAsync().ConfigureAwait(false);
            Log.Info($"Speech model ready ({_settings.ModelType}), runtime: {RuntimeProbe.LoadedRuntime()}.");
            _window.BeginInvoke(() => _trayIcon.Text = $"Voice to Text {VersionLabel} — ready ({_settings.Hotkey.Describe()})");
        }
        catch (Exception ex)
        {
            Log.Error("Speech model load/warm-up failed", ex);
            var msg = !ModelManager.IsModelPresent(_settings.ModelType)
                ? "Couldn't download the speech model — check your internet connection."
                : "Speech model failed to load — see the log (About → Open log folder).";
            _window.BeginInvoke(() => ShowError(msg));
        }
    }

    private void ShowDashboard(DashboardPageKind page)
    {
        if (_dashboard is null || _dashboard.IsDisposed)
        {
            _dashboard = new DashboardForm(_settings, _stats, _history, VersionLabel);
            _dashboard.HotkeyCaptureStarted += OnHotkeyCaptureStarted;
            _dashboard.HotkeyCaptureEnded += OnHotkeyCaptureEnded;
            _dashboard.SettingsSaved += OnSettingsSaved;
            _dashboard.CheckForUpdatesRequested += () => _ = CheckForUpdatesAsync(userInitiated: true);
            _dashboard.FormClosed += (_, _) => _dashboard = null;
        }

        // If an owned modal (an unsaved-settings or clear-history confirm) is open, the form is
        // disabled — surface that modal instead of pulling the disabled owner in front of it, which
        // would hide the dialog and wedge the window (every click just flickers). v0.8.10 freeze fix.
        if (_dashboard.IsModalOpen)
        {
            _dashboard.Activate();
            return;
        }

        _dashboard.ShowPage(page);
        if (!_dashboard.Visible) _dashboard.Show();
        if (_dashboard.WindowState == FormWindowState.Minimized) _dashboard.WindowState = FormWindowState.Normal;
        _dashboard.Activate();
        _dashboard.BringToFront();
    }

    // Release the global hotkey only while the user is capturing one in Settings, so the
    // captured keypress can't start dictation; restore the last good one when they leave the box.
    private void OnHotkeyCaptureStarted() => _hotkeys.Unregister();
    private void OnHotkeyCaptureEnded() => _hotkeys.Register(_registeredHotkey);

    /// <summary>Swap the STT engine to the newly-selected model once idle (never mid-dictation).</summary>
    private void MaybeReloadModel()
    {
        if (!_modelReloadPending || _busy || _state != AppState.Idle)
            return;
        _modelReloadPending = false;
        var old = _stt;
        _loadedModelType = _settings.ModelType;
        _stt = new WhisperSttEngine(_settings.ModelType, _settings.Language);
        try { old.Dispose(); } catch { /* best effort */ }
        _ = Task.Run(WarmUpAsync);
    }

    // Re-apply settings after a Save on the Settings page (mirrors the old post-dialog logic).
    private void OnSettingsSaved()
    {
        _hotkeys.Unregister();
        if (_hotkeys.Register(_settings.Hotkey))
        {
            _registeredHotkey = _settings.Hotkey;
            _settings.Save();
            _trayIcon.Text = $"Voice to Text {VersionLabel} — ready ({_settings.Hotkey.Describe()})";
        }
        else
        {
            // OS rejected the new combo — keep the previous working one instead of none.
            var rejected = _settings.Hotkey;
            _settings.Hotkey = _registeredHotkey;
            _settings.Save();
            _hotkeys.Register(_registeredHotkey);
            _dashboard?.ReloadSettings();
            _trayIcon.ShowBalloonTip(
                6000,
                "Voice to Text",
                $"{rejected.Describe()} is reserved or already in use. Kept {_registeredHotkey.Describe()}.",
                ToolTipIcon.Warning);
        }

        ApplyOverlaySetting();
        _hotkeys.HoldToTalk = _settings.HoldToTalk;

        if (_settings.ModelType != _loadedModelType)
        {
            _modelReloadPending = true;
            MaybeReloadModel();
        }

        if (_settings.AutoUpdateEnabled && !string.IsNullOrWhiteSpace(_settings.UpdateFeedFolder))
            _ = CheckForUpdatesAsync(userInitiated: false);
    }

    private async Task CheckForUpdatesAsync(bool userInitiated)
    {
        try
        {
            var result = await _updates.CheckAsync().ConfigureAwait(false);
            if (!_window.IsHandleCreated)
                return;
            _window.BeginInvoke(() => HandleUpdateResult(result, userInitiated));
        }
        catch
        {
            // A background update check must never crash the tray.
        }
    }

    private void HandleUpdateResult(UpdateCheckResult result, bool userInitiated)
    {
        switch (result.Decision)
        {
            case UpdateDecision.UpdateAvailable:
                // Don't nag at startup about a version the user already declined.
                if (!userInitiated && result.AvailableVersion?.ToString() == _settings.UpdateSkippedVersion)
                    return;
                if (userInitiated)
                    PromptInstall(result);
                else
                    _trayIcon.ShowBalloonTip(8000, "Voice to Text",
                        $"Update v{result.AvailableVersion} is available — tray menu → \"Check for updates\" to install.",
                        ToolTipIcon.Info);
                break;
            case UpdateDecision.UpToDate:
                if (userInitiated)
                    _trayIcon.ShowBalloonTip(4000, "Voice to Text", $"You're on the latest version (v{result.CurrentVersion}).", ToolTipIcon.Info);
                break;
            case UpdateDecision.NoFeedConfigured:
                if (userInitiated)
                    _trayIcon.ShowBalloonTip(6000, "Voice to Text", "Set an update folder in Settings first.", ToolTipIcon.Info);
                break;
            case UpdateDecision.Disabled:
                if (userInitiated)
                    _trayIcon.ShowBalloonTip(6000, "Voice to Text", "Automatic updates are off — enable them in Settings.", ToolTipIcon.Info);
                break;
            case UpdateDecision.VersionUnknown:
                if (userInitiated)
                    ShowError("Couldn't determine the running version, so updates are disabled for this build.");
                break;
            default: // ManifestInvalid
                if (userInitiated)
                    ShowError(result.Message ?? "Couldn't check for updates.");
                break;
        }
    }

    private void PromptInstall(UpdateCheckResult result)
    {
        var notes = string.IsNullOrWhiteSpace(result.Message) ? "" : $"\n\n{result.Message}";
        var choice = MessageBox.Show(
            $"Version {result.AvailableVersion} is available (you have {result.CurrentVersion}).\n\n" +
            $"Install now? The app will close briefly and reopen.{notes}\n\n(Choose No to skip this version.)",
            "Voice to Text — Update available",
            MessageBoxButtons.YesNo, MessageBoxIcon.Question);

        if (choice == DialogResult.Yes)
        {
            _ = DownloadAndApplyAsync(result.Manifest!, result.AvailableVersion!);
        }
        else
        {
            _settings.UpdateSkippedVersion = result.AvailableVersion!.ToString();
            _settings.Save();
        }
    }

    private async Task DownloadAndApplyAsync(UpdateManifest manifest, Version targetVersion)
    {
        if (Interlocked.CompareExchange(ref _updateInProgress, 1, 0) != 0)
            return; // an apply is already in flight

        try
        {
            if (_window.IsHandleCreated)
                _window.BeginInvoke(() => _trayIcon.ShowBalloonTip(4000, "Voice to Text", "Downloading update…", ToolTipIcon.Info));

            var setupPath = await _updates.StageInstallerAsync(manifest).ConfigureAwait(false);

            if (!_window.IsHandleCreated)
            {
                Interlocked.Exchange(ref _updateInProgress, 0);
                return;
            }
            _window.BeginInvoke(() => ApplyUpdate(setupPath, targetVersion));
            // Leave the guard set: the process is about to exit and relaunch.
        }
        catch (Exception ex)
        {
            Interlocked.Exchange(ref _updateInProgress, 0);
            if (_window.IsHandleCreated)
                _window.BeginInvoke(() => ShowError($"Update failed: {ex.Message}"));
        }
    }

    // UI thread: release native/file locks, launch the relauncher shim, and exit so the
    // installer can replace files. The shim relaunches the app afterwards.
    private void ApplyUpdate(string setupPath, Version targetVersion)
    {
        var appExe = Environment.ProcessPath;
        if (string.IsNullOrEmpty(appExe))
        {
            ShowError("Couldn't locate the app executable to relaunch after the update.");
            Interlocked.Exchange(ref _updateInProgress, 0);
            return;
        }

        string shim;
        try
        {
            shim = _updates.WriteRelauncherShim();
        }
        catch (Exception ex)
        {
            ShowError($"Update failed: {ex.Message}");
            Interlocked.Exchange(ref _updateInProgress, 0);
            return;
        }

        // Release the hotkey and the Whisper native DLLs (in runtimes\) so the installer
        // can overwrite them; Inno's Restart Manager + AppMutex close any straggler.
        try { _hotkeys.Unregister(); } catch { /* best effort */ }
        try { _stt.Dispose(); } catch { /* best effort */ }

        var psi = new ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = $"/c \"\"{shim}\" {Environment.ProcessId} \"{setupPath}\" \"{appExe}\" \"{UpdateService.UpdateLogPath}\" {targetVersion}\"",
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        Process.Start(psi);

        ExitApp();
    }

    private void ShowPostUpdateBalloon()
    {
        var current = _updates.CurrentVersion;
        if (Version.TryParse(_postUpdateTarget, out var target) && current is not null && current >= target)
            _trayIcon.ShowBalloonTip(5000, "Voice to Text", $"Updated to v{current}.", ToolTipIcon.Info);
        else
            _trayIcon.ShowBalloonTip(9000, "Voice to Text",
                $"The update may not have completed (running v{current?.ToString() ?? "?"}). See {UpdateService.UpdateLogPath}.",
                ToolTipIcon.Warning);
    }

    private void ShowError(string message)
        => _trayIcon.ShowBalloonTip(6000, "Voice to Text", message, ToolTipIcon.Error);

    private void ExitApp()
    {
        _trayIcon.Visible = false;
        ExitThread();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            Application.ThreadException -= OnUiThreadException;
            AppDomain.CurrentDomain.UnhandledException -= OnUnhandledException;
            _hotkeys.Dispose();
            _trayIcon.Dispose();
            _icons.Dispose();
            _cues.Dispose();
            _stt.Dispose();
            _overlay?.Dispose();
            _dashboard?.Dispose();
            _window.Dispose();
        }
        base.Dispose(disposing);
    }
}
