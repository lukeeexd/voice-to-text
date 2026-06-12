using VoiceToText.App;
using VoiceToText.Diagnostics;
using VoiceToText.History;
using VoiceToText.Hotkeys;
using VoiceToText.Linux.Platform;
using VoiceToText.Linux.Ui;
using VoiceToText.Settings;
using VoiceToText.Stats;
using VoiceToText.Stt;

namespace VoiceToText.Linux;

/// <summary>
/// The daemon's composition root: engine services plus the active hotkey tier.
/// Created before Avalonia starts; the UI layer reads from it.
/// </summary>
public sealed class AppServices : IDisposable
{
    public AppSettings Settings { get; }
    public StatsService Stats { get; }
    public HistoryService History { get; }
    public DictationController Controller { get; }
    public LinuxTextInjector Injector { get; }
    public HotkeyTier HotkeyTier { get; }

    /// <summary>The UI shows this in the hotkey section.</summary>
    public string HotkeyStatus { get; private set; } = "";

    public event Action? OpenSettingsRequested;

    private readonly WhisperSttEngine _stt;
    private X11HotkeyService? _x11Hotkey;

    public AppServices()
    {
        Settings = AppSettings.Load();
        Stats = new StatsService();
        History = new HistoryService();
        WhisperRuntime.ConfigureForHost(Settings.UseGpuExperimental);
        _stt = new WhisperSttEngine(Settings.ModelType, Settings.Language);
        Injector = new LinuxTextInjector(ClipboardHelper.SetTextAsync, Settings);
        Controller = new DictationController(
            new PulseAudioSource(), _stt, Injector, new PulseCuePlayer(),
            Settings, Stats, History);
        Controller.AppNameProvider = "Desktop"; // Wayland has no focused-app API
        HotkeyTier = SessionInfo.PickHotkeyTier();
    }

    /// <summary>Background model load/warm-up; mirrors the Windows head.</summary>
    public void WarmUp() => _ = _stt.LoadAsync();

    /// <summary>Start the hotkey tier (called once the UI thread exists).</summary>
    public void StartHotkeys()
    {
        if (HotkeyTier == HotkeyTier.X11Grab)
        {
            _x11Hotkey = new X11HotkeyService();
            if (_x11Hotkey.Start(Settings.Hotkey))
            {
                _x11Hotkey.Pressed += () =>
                {
                    if (!Settings.HoldToTalk || Controller.State == DictationState.Idle)
                    {
                        ResolveAppName();
                        _ = Controller.ToggleAsync();
                    }
                };
                _x11Hotkey.Released += () =>
                {
                    if (Settings.HoldToTalk && Controller.State == DictationState.Recording)
                        _ = Controller.ToggleAsync();
                };
                HotkeyStatus = $"Global hotkey active (X11): {Settings.Hotkey.Describe()}";
                return;
            }
            _x11Hotkey.Dispose();
            _x11Hotkey = null;
        }

        HotkeyStatus = SessionInfo.IsGnome
            ? "Hotkey via GNOME custom shortcut (auto-setup available below)."
            : $"Bind a key in your desktop's keyboard settings to:  {GnomeShortcuts.ExecutablePath} --toggle";
        Log.Info($"Hotkey tier: {HotkeyTier} — {HotkeyStatus}");
    }

    /// <summary>IPC command dispatch (called off the UI thread).</summary>
    public string HandleCommand(string command) => command switch
    {
        "toggle" => Toggle(),
        "status" => Controller.State.ToString(),
        "settings" or "show" => RaiseOpenSettings(),
        "ping" => "pong",
        _ => $"unknown command: {command}",
    };

    private DateTime _lastToggleUtc = DateTime.MinValue;

    private string Toggle()
    {
        // Desktop keybindings re-fire CONTINUOUSLY on keyboard auto-repeat while the
        // chord is held (GNOME: ~every 30 ms after a ~500 ms delay). A fixed debounce
        // only postpones the spurious stop, so the window SLIDES: every request —
        // acted on or not — extends the quiet period, collapsing the entire held-key
        // stream into one action. The next genuine press (after release) acts again.
        var now = DateTime.UtcNow;
        var sinceLast = (now - _lastToggleUtc).TotalMilliseconds;
        _lastToggleUtc = now;
        if (sinceLast < 700)
            return "ignored (debounce)";

        var wasIdle = Controller.State == DictationState.Idle;
        if (wasIdle) ResolveAppName();
        _ = Controller.ToggleAsync();
        return wasIdle ? "starting" : "stopping";
    }

    /// <summary>Per-app stats attribution at dictation start: the focused app's WM_CLASS
    /// on X11 sessions; the "Desktop" bucket on Wayland (no API exists there).</summary>
    private void ResolveAppName()
    {
        if (HotkeyTier == HotkeyTier.X11Grab)
            Controller.AppNameProvider = X11FocusTracker.GetFocusedAppName() ?? "Desktop";
    }

    private string RaiseOpenSettings()
    {
        OpenSettingsRequested?.Invoke();
        return "ok";
    }

    public void Dispose()
    {
        _x11Hotkey?.Dispose();
        _stt.Dispose();
    }
}
