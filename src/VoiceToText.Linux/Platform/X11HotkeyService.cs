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

    // Static state for the process-wide X error handler (GC anchor + grab outcome).
    private static X11Native.XErrorHandler? _errorHandler;
    private static IntPtr s_ourDisplay;
    private static IntPtr s_previousHandler;
    private static volatile bool s_errorSeen;

    public event Action? Pressed;
    public event Action? Released;

    /// <summary>Try to start grabbing. False when X11 or the keysym is unavailable,
    /// or when another client already owns the combo (BadAccess).</summary>
    public bool Start(HotkeyDefinition hotkey)
    {
        var keysym = VkKeys.ToKeysym(hotkey.VirtualKey);
        if (keysym is null) return false;

        _display = X11Native.XOpenDisplay(null);
        if (_display == IntPtr.Zero) return false;

        // Swallow errors on OUR display (BadAccess when the combo is taken would
        // otherwise exit the process) and chain everything else to the previously
        // installed handler (Avalonia's own X11 connection).
        s_ourDisplay = _display;
        s_errorSeen = false;
        _errorHandler = (display, errorEvent) =>
        {
            if (display == s_ourDisplay)
            {
                s_errorSeen = true;
                return 0;
            }
            return s_previousHandler == IntPtr.Zero
                ? 0
                : System.Runtime.InteropServices.Marshal
                    .GetDelegateForFunctionPointer<X11Native.XErrorHandler>(s_previousHandler)(display, errorEvent);
        };
        s_previousHandler = X11Native.XSetErrorHandler(_errorHandler);
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
        X11Native.XSync(_display, 0); // flush so a BadAccess lands before we report success

        if (s_errorSeen)
        {
            // Another app owns the combo: report failure so the caller can fall back
            // to the IPC-binding tier instead of claiming a hotkey that never fires.
            foreach (var extra in IgnorableLockMasks)
                X11Native.XUngrabKey(_display, _keycode, _x11Mods | extra, root);
            X11Native.XCloseDisplay(_display);
            _display = IntPtr.Zero;
            Log.Info($"X11 hotkey {hotkey.Describe()} is taken by another app; using the IPC binding instead.");
            return false;
        }

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
        if (_thread is not null && !_thread.Join(2000))
        {
            // The loop thread is stuck inside a Pressed/Released handler; leak the
            // display rather than free it under a thread that still uses it.
            Log.Error("X11 hotkey thread did not stop in time; leaking its display connection.");
            _display = IntPtr.Zero;
            return;
        }
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
