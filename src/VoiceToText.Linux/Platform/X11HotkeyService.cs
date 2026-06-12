using VoiceToText.Diagnostics;
using VoiceToText.Hotkeys;

namespace VoiceToText.Linux.Platform;

/// <summary>Tier-1 hotkey: XGrabKey on the X11 root window. Used on X11 sessions only
/// (under Wayland an X grab never fires while native windows have focus).</summary>
public sealed class X11HotkeyService : IDisposable
{
    private IntPtr _display;
    private Thread? _thread;
    private volatile bool _stop;
    private int _keycode;
    private uint _x11Mods;
    private bool _keyDown;
    private static X11Native.XErrorHandler? _errorHandler; // GC anchor for the native callback

    public event Action? Pressed;
    public event Action? Released;

    /// <summary>Try to start grabbing. False when X11 or the keysym is unavailable.</summary>
    public bool Start(HotkeyDefinition hotkey)
    {
        var keysym = VkKeys.ToKeysym(hotkey.VirtualKey);
        if (keysym is null) return false;

        _display = X11Native.XOpenDisplay(null);
        if (_display == IntPtr.Zero) return false;

        _errorHandler = static (_, _) => 0; // swallow BadAccess (combo grabbed elsewhere)
        X11Native.XSetErrorHandler(_errorHandler);
        X11Native.XkbSetDetectableAutoRepeat(_display, 1, IntPtr.Zero);

        _keycode = X11Native.XKeysymToKeycode(_display, keysym.Value);
        if (_keycode == 0)
        {
            X11Native.XCloseDisplay(_display);
            _display = IntPtr.Zero;
            return false;
        }

        _x11Mods = VkKeys.ToX11Modifiers(hotkey.Modifiers);
        var root = X11Native.XDefaultRootWindow(_display);
        foreach (var extra in IgnorableLockMasks)
            X11Native.XGrabKey(_display, _keycode, _x11Mods | extra, root, 0,
                X11Native.GrabModeAsync, X11Native.GrabModeAsync);
        X11Native.XSync(_display, 0);

        _stop = false;
        _thread = new Thread(EventLoop) { IsBackground = true, Name = "x11-hotkey" };
        _thread.Start();
        Log.Info($"X11 hotkey grab active: {hotkey.Describe()}");
        return true;
    }

    private static readonly uint[] IgnorableLockMasks =
        [0, X11Native.LockMask, X11Native.Mod2Mask, X11Native.LockMask | X11Native.Mod2Mask];

    private void EventLoop()
    {
        while (!_stop)
        {
            while (!_stop && X11Native.XPending(_display) > 0)
            {
                X11Native.XNextEvent(_display, out var ev);
                if (ev.type == X11Native.KeyPress && ev.xkey.keycode == _keycode && !_keyDown)
                {
                    _keyDown = true;
                    Pressed?.Invoke();
                }
                else if (ev.type == X11Native.KeyRelease && ev.xkey.keycode == _keycode && _keyDown)
                {
                    _keyDown = false;
                    Released?.Invoke();
                }
            }
            Thread.Sleep(30);
        }
    }

    public void Dispose()
    {
        _stop = true;
        _thread?.Join(500);
        if (_display != IntPtr.Zero)
        {
            var root = X11Native.XDefaultRootWindow(_display);
            foreach (var extra in IgnorableLockMasks)
                X11Native.XUngrabKey(_display, _keycode, _x11Mods | extra, root);
            X11Native.XCloseDisplay(_display);
            _display = IntPtr.Zero;
        }
    }
}
