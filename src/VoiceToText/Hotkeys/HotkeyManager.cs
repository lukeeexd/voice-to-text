using VoiceToText.App;

namespace VoiceToText.Hotkeys;

/// <summary>
/// Registers a single global hotkey via RegisterHotKey on the hidden window and raises
/// <see cref="Pressed"/> on key-down. In hold-to-talk mode it polls the key with
/// GetAsyncKeyState (RegisterHotKey gives no key-up) and raises <see cref="Released"/> when it
/// goes up — or after a safety backstop. No admin rights, no global keyboard hook.
/// </summary>
public sealed class HotkeyManager : IDisposable
{
    private const int HotkeyId = 0xB1A5;
    private const int PollIntervalMs = 40;
    private const int MaxHoldMs = 120_000; // stuck-key / missed-release backstop

    private readonly HiddenWindow _window;
    private readonly System.Windows.Forms.Timer _pollTimer;
    private bool _registered;
    private uint _vk;
    private int _holdStartTick;
    private bool _holdToTalk;

    /// <summary>Raised on the UI thread when the hotkey is pressed (key-down).</summary>
    public event Action? Pressed;

    /// <summary>Raised on the UI thread when a held hotkey is released (hold-to-talk only).</summary>
    public event Action? Released;

    /// <summary>When true, a press starts polling for release and raises <see cref="Released"/>.</summary>
    public bool HoldToTalk
    {
        get => _holdToTalk;
        set
        {
            _holdToTalk = value;
            if (!value) _pollTimer.Stop();
        }
    }

    internal HotkeyManager(HiddenWindow window)
    {
        _window = window;
        _window.HotkeyMessageReceived += OnHotkeyMessage;
        _pollTimer = new System.Windows.Forms.Timer { Interval = PollIntervalMs };
        _pollTimer.Tick += OnPollTick;
    }

    private void OnHotkeyMessage(int id)
    {
        if (id != HotkeyId)
            return;

        Pressed?.Invoke();

        if (_holdToTalk && !_pollTimer.Enabled)
        {
            _holdStartTick = Environment.TickCount;
            _pollTimer.Start();
        }
    }

    private void OnPollTick(object? sender, EventArgs e)
    {
        var keyUp = (NativeHotkeys.GetAsyncKeyState((int)_vk) & 0x8000) == 0;
        var timedOut = Environment.TickCount - _holdStartTick > MaxHoldMs;
        if (keyUp || timedOut)
        {
            _pollTimer.Stop();
            Released?.Invoke();
        }
    }

    /// <summary>
    /// Register (replacing any previous registration). Returns false if the OS rejected the
    /// combo — usually because another app already owns it.
    /// </summary>
    public bool Register(HotkeyDefinition hotkey)
    {
        Unregister();
        _vk = hotkey.VirtualKey;
        var modifiers = hotkey.Modifiers | HotkeyDefinition.ModNoRepeat;
        _registered = NativeHotkeys.RegisterHotKey(_window.Handle, HotkeyId, modifiers, hotkey.VirtualKey);
        return _registered;
    }

    public void Unregister()
    {
        _pollTimer.Stop();
        if (!_registered)
            return;
        NativeHotkeys.UnregisterHotKey(_window.Handle, HotkeyId);
        _registered = false;
    }

    public void Dispose()
    {
        _pollTimer.Stop();
        _pollTimer.Dispose();
        Unregister();
        _window.HotkeyMessageReceived -= OnHotkeyMessage;
    }
}
