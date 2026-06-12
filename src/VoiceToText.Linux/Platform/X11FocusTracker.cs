using System.Runtime.InteropServices;

namespace VoiceToText.Linux.Platform;

/// <summary>
/// Resolves the focused application's name on X11 sessions (per-app stats attribution):
/// _NET_ACTIVE_WINDOW on the root window → that window's WM_CLASS class name.
/// Returns null on any failure; never throws and never blocks dictation.
/// </summary>
public static class X11FocusTracker
{
    private const nuint XA_WINDOW = 33;

    public static string? GetFocusedAppName()
    {
        IntPtr display = IntPtr.Zero;
        try
        {
            display = X11Native.XOpenDisplay(null);
            if (display == IntPtr.Zero) return null;

            var atom = X11Native.XInternAtom(display, "_NET_ACTIVE_WINDOW", 1);
            if (atom == 0) return null;

            var root = X11Native.XDefaultRootWindow(display);
            if (X11Native.XGetWindowProperty(display, root, atom, 0, 1, 0, XA_WINDOW,
                    out _, out var format, out var nItems, out _, out var prop) != 0
                || prop == IntPtr.Zero || nItems == 0 || format != 32)
                return null;

            // 32-bit-format items are stored as native longs on 64-bit Xlib.
            var window = (nuint)Marshal.ReadInt64(prop);
            X11Native.XFree(prop);
            if (window == 0) return null;

            if (X11Native.XGetClassHint(display, window, out var hint) == 0)
                return null;
            try
            {
                return hint.res_class != IntPtr.Zero ? Marshal.PtrToStringUTF8(hint.res_class) : null;
            }
            finally
            {
                if (hint.res_name != IntPtr.Zero) X11Native.XFree(hint.res_name);
                if (hint.res_class != IntPtr.Zero) X11Native.XFree(hint.res_class);
            }
        }
        catch
        {
            return null;
        }
        finally
        {
            if (display != IntPtr.Zero) X11Native.XCloseDisplay(display);
        }
    }
}
