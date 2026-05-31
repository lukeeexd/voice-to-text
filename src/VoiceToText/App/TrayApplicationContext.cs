using VoiceToText.Audio;
using VoiceToText.Hotkeys;
using VoiceToText.Injection;
using VoiceToText.Settings;
using VoiceToText.Stt;

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

    private ISttEngine _stt;
    private AppState _state = AppState.Idle;
    private bool _busy;

    public TrayApplicationContext()
    {
        _settings = AppSettings.Load();
        _stt = new WhisperSttEngine(_settings.ModelType, _settings.Language);

        _window = new HiddenWindow();
        _window.EnsureHandle();

        _trayIcon = new NotifyIcon
        {
            Visible = true,
            Icon = _icons.Get(AppState.Idle),
            Text = "Voice to Text",
            ContextMenuStrip = BuildMenu(),
        };
        _trayIcon.DoubleClick += (_, _) => ShowSettings();

        _hotkeys = new HotkeyManager(_window);
        _hotkeys.Pressed += OnHotkeyPressed;
        RegisterHotkey();

        _ = Task.Run(WarmUpAsync);
    }

    private ContextMenuStrip BuildMenu()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("Settings…", null, (_, _) => ShowSettings());
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
            _audio.Start(_settings.InputDeviceId);
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
            _ => "Voice to Text",
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
            _window.BeginInvoke(() => _trayIcon.Text = $"Voice to Text — ready ({_settings.Hotkey.Describe()})");
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
            _trayIcon.Text = $"Voice to Text — ready ({_settings.Hotkey.Describe()})";
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
