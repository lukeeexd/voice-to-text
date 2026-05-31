using VoiceToText.App;

namespace VoiceToText.Hotkeys;

/// <summary>
/// Registers a single global hotkey via the Win32 RegisterHotKey API on the
/// hidden window's handle and raises <see cref="Pressed"/> when it fires.
/// Toggle semantics (no hold-to-talk) — robust, needs no admin rights.
/// </summary>
public sealed class HotkeyManager : IDisposable
{
    private const int HotkeyId = 0xB1A5;

    private readonly HiddenWindow _window;
    private bool _registered;

    /// <summary>Raised on the UI thread each time the hotkey is pressed.</summary>
    public event Action? Pressed;

    internal HotkeyManager(HiddenWindow window)
    {
        _window = window;
        _window.HotkeyMessageReceived += OnHotkeyMessage;
    }

    private void OnHotkeyMessage(int id)
    {
        if (id == HotkeyId)
            Pressed?.Invoke();
    }

    /// <summary>
    /// Register (replacing any previous registration). Returns false if the OS
    /// rejected the combo — usually because another app already owns it.
    /// </summary>
    public bool Register(HotkeyDefinition hotkey)
    {
        Unregister();
        var modifiers = hotkey.Modifiers | HotkeyDefinition.ModNoRepeat;
        _registered = NativeHotkeys.RegisterHotKey(_window.Handle, HotkeyId, modifiers, hotkey.VirtualKey);
        return _registered;
    }

    public void Unregister()
    {
        if (!_registered)
            return;
        NativeHotkeys.UnregisterHotKey(_window.Handle, HotkeyId);
        _registered = false;
    }

    public void Dispose()
    {
        Unregister();
        _window.HotkeyMessageReceived -= OnHotkeyMessage;
    }
}
