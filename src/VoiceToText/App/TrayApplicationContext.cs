using System.Diagnostics;
using VoiceToText.Audio;
using VoiceToText.Hotkeys;
using VoiceToText.Injection;
using VoiceToText.Settings;
using VoiceToText.Stt;
using VoiceToText.Update;

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
    private readonly ITextInjector _injector = new ClipboardTextInjector();
    private readonly AppSettings _settings;

    private readonly UpdateService _updates;
    private readonly string? _postUpdateTarget;

    private ISttEngine _stt;
    private AppState _state = AppState.Idle;
    private bool _busy;
    private int _updateInProgress; // 0/1, set/cleared via Interlocked

    private string VersionLabel => _updates.CurrentVersion is { } v ? $"v{v.ToString(3)}" : "";

    public TrayApplicationContext(string? postUpdateTarget = null)
    {
        _postUpdateTarget = postUpdateTarget;
        _settings = AppSettings.Load();
        _stt = new WhisperSttEngine(_settings.ModelType, _settings.Language);
        _updates = new UpdateService(_settings);

        // Clean leftover staged-update files on a normal start. On a post-update launch
        // the shim is still finishing, so skip it then to avoid deleting files in use.
        if (postUpdateTarget is null)
            UpdateService.CleanStaging();

        _window = new HiddenWindow();
        _window.EnsureHandle();

        _trayIcon = new NotifyIcon
        {
            Visible = true,
            Icon = _icons.Get(AppState.Idle),
            Text = $"Voice to Text {VersionLabel}".TrimEnd(),
            ContextMenuStrip = BuildMenu(),
        };
        _trayIcon.DoubleClick += (_, _) => ShowSettings();

        _hotkeys = new HotkeyManager(_window);
        _hotkeys.Pressed += OnHotkeyPressed;
        RegisterHotkey();

        _audio.SilenceDetected += OnSilenceDetected;

        _ = Task.Run(WarmUpAsync);

        if (_postUpdateTarget is not null)
            _window.BeginInvoke(ShowPostUpdateBalloon);
        else
            _ = Task.Run(() => CheckForUpdatesAsync(userInitiated: false));
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

    private ContextMenuStrip BuildMenu()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("Settings…", null, (_, _) => ShowSettings());
        menu.Items.Add("Check for updates…", null, (_, _) => _ = CheckForUpdatesAsync(userInitiated: true));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => ExitApp());
        return menu;
    }

    private void RegisterHotkey()
    {
        if (!_hotkeys.Register(_settings.Hotkey))
        {
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

    private void StartRecording()
    {
        try
        {
            _audio.Start(_settings.InputDeviceId, _settings.AutoStopEnabled, _settings.AutoStopSilenceSeconds);
            SetState(AppState.Recording);
        }
        catch (Exception ex)
        {
            ShowError($"Could not start recording: {ex.Message}");
            SetState(AppState.Idle);
        }
    }

    private async Task StopAndTranscribeAsync()
    {
        _busy = true;
        SetState(AppState.Transcribing);
        try
        {
            var samples = await _audio.StopAndGetSamplesAsync().ConfigureAwait(false);
            var text = await _stt.TranscribeAsync(samples).ConfigureAwait(false);

            if (!string.IsNullOrWhiteSpace(text))
                _window.BeginInvoke(() => _injector.Inject(text));
        }
        catch (Exception ex)
        {
            _window.BeginInvoke(() => ShowError($"Transcription failed: {ex.Message}"));
        }
        finally
        {
            _window.BeginInvoke(() =>
            {
                SetState(AppState.Idle);
                _busy = false;
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
            _window.BeginInvoke(() => _trayIcon.Text = $"Voice to Text {VersionLabel} — ready ({_settings.Hotkey.Describe()})");
        }
        catch (Exception ex)
        {
            _window.BeginInvoke(() => ShowError($"Speech model failed to load: {ex.Message}"));
        }
    }

    private void ShowSettings()
    {
        var previousHotkey = _settings.Hotkey;

        // Release the global hotkey while configuring so the user can re-capture
        // the current combo and doesn't accidentally start dictation.
        _hotkeys.Unregister();

        using (var form = new SettingsForm(_settings))
        {
            if (form.ShowDialog() != DialogResult.OK)
            {
                _hotkeys.Register(previousHotkey); // nothing saved; restore
                return;
            }
        }

        if (_hotkeys.Register(_settings.Hotkey))
        {
            _settings.Save();
            _trayIcon.Text = $"Voice to Text {VersionLabel} — ready ({_settings.Hotkey.Describe()})";
        }
        else
        {
            // The OS rejected the new combo (reserved/in use). Keep the old one
            // working rather than leaving the app with no hotkey at all.
            var rejected = _settings.Hotkey;
            _settings.Hotkey = previousHotkey;
            _settings.Save();
            _hotkeys.Register(previousHotkey);
            _trayIcon.ShowBalloonTip(
                6000,
                "Voice to Text",
                $"{rejected.Describe()} is reserved or already in use. Kept {previousHotkey.Describe()}.",
                ToolTipIcon.Warning);
        }
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
            _hotkeys.Dispose();
            _trayIcon.Dispose();
            _icons.Dispose();
            _stt.Dispose();
            _window.Dispose();
        }
        base.Dispose(disposing);
    }
}
