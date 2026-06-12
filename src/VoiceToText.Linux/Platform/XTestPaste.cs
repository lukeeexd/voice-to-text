namespace VoiceToText.Linux.Platform;

internal static class XTestPaste
{
    /// <summary>Fake Ctrl+V via XTEST. Returns false when X11 is unavailable.</summary>
    public static bool PasteCtrlV()
    {
        var display = X11Native.XOpenDisplay(null);
        if (display == IntPtr.Zero) return false;
        try
        {
            Thread.Sleep(80); // let physical hotkey modifiers clear
            var ctrl = X11Native.XKeysymToKeycode(display, 0xFFE3); // XK_Control_L
            var v = X11Native.XKeysymToKeycode(display, 0x76);      // XK_v
            if (ctrl == 0 || v == 0) return false;
            X11Native.XTestFakeKeyEvent(display, ctrl, 1, 0);
            X11Native.XTestFakeKeyEvent(display, v, 1, 0);
            X11Native.XTestFakeKeyEvent(display, v, 0, 0);
            X11Native.XTestFakeKeyEvent(display, ctrl, 0, 0);
            X11Native.XFlush(display);
            return true;
        }
        finally
        {
            X11Native.XCloseDisplay(display);
        }
    }
}
