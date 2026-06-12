using System.Runtime.InteropServices;

namespace VoiceToText.Linux.Platform;

/// <summary>
/// Minimal libX11/libXtst binding for the hotkey grab and XTEST paste. 64-bit
/// struct layouts only (the app ships linux-x64). An X error handler MUST be
/// installed before XGrabKey — the default handler exits the process on BadAccess
/// (combo already grabbed by another client).
/// </summary>
internal static partial class X11Native
{
    private const string LibX11 = "libX11.so.6";
    private const string LibXtst = "libXtst.so.6";

    public const int KeyPress = 2;
    public const int KeyRelease = 3;
    public const int GrabModeAsync = 1;

    public const uint ShiftMask = 1 << 0;
    public const uint LockMask = 1 << 1;    // CapsLock
    public const uint ControlMask = 1 << 2;
    public const uint Mod1Mask = 1 << 3;    // Alt
    public const uint Mod2Mask = 1 << 4;    // NumLock
    public const uint Mod4Mask = 1 << 6;    // Super/Win

    [LibraryImport(LibX11, StringMarshalling = StringMarshalling.Utf8)]
    public static partial IntPtr XOpenDisplay(string? display);

    [LibraryImport(LibX11)]
    public static partial int XCloseDisplay(IntPtr display);

    [LibraryImport(LibX11)]
    public static partial nuint XDefaultRootWindow(IntPtr display);

    [LibraryImport(LibX11)]
    public static partial byte XKeysymToKeycode(IntPtr display, nuint keysym);

    [LibraryImport(LibX11)]
    public static partial int XGrabKey(IntPtr display, int keycode, uint modifiers,
        nuint grabWindow, int ownerEvents, int pointerMode, int keyboardMode);

    [LibraryImport(LibX11)]
    public static partial int XUngrabKey(IntPtr display, int keycode, uint modifiers, nuint grabWindow);

    [LibraryImport(LibX11)]
    public static partial int XPending(IntPtr display);

    [LibraryImport(LibX11)]
    public static partial int XNextEvent(IntPtr display, out XEvent ev);

    [LibraryImport(LibX11)]
    public static partial int XFlush(IntPtr display);

    [LibraryImport(LibX11)]
    public static partial int XSync(IntPtr display, int discard);

    // Detectable auto-repeat: repeats arrive as KeyPress-only (no fake KeyRelease),
    // so press/release state tracking is reliable.
    [LibraryImport(LibX11)]
    public static partial int XkbSetDetectableAutoRepeat(IntPtr display, int detectable, IntPtr supportedOut);

    public delegate int XErrorHandler(IntPtr display, IntPtr errorEvent);

    [DllImport(LibX11)]
    public static extern IntPtr XSetErrorHandler(XErrorHandler handler);

    [LibraryImport(LibXtst)]
    public static partial int XTestFakeKeyEvent(IntPtr display, uint keycode, int isPress, nuint delay);

    [LibraryImport(LibX11, StringMarshalling = StringMarshalling.Utf8)]
    public static partial nuint XInternAtom(IntPtr display, string atomName, int onlyIfExists);

    [LibraryImport(LibX11)]
    public static partial int XGetWindowProperty(IntPtr display, nuint window, nuint property,
        long longOffset, long longLength, int delete, nuint reqType,
        out nuint actualType, out int actualFormat, out nuint nItems, out nuint bytesAfter,
        out IntPtr prop);

    [LibraryImport(LibX11)]
    public static partial int XFree(IntPtr data);

    [StructLayout(LayoutKind.Sequential)]
    public struct XClassHint
    {
        public IntPtr res_name;
        public IntPtr res_class;
    }

    [LibraryImport(LibX11)]
    public static partial int XGetClassHint(IntPtr display, nuint window, out XClassHint hint);

    /// <summary>X11 XKeyEvent (64-bit layout); embedded in the 192-byte XEvent union.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct XKeyEvent
    {
        public int type;
        public nuint serial;
        public int send_event;
        public IntPtr display;
        public nuint window;
        public nuint root;
        public nuint subwindow;
        public nuint time;
        public int x, y, x_root, y_root;
        public uint state;
        public uint keycode;
        public int same_screen;
    }

    [StructLayout(LayoutKind.Explicit, Size = 192)]
    public struct XEvent
    {
        [FieldOffset(0)] public int type;
        [FieldOffset(0)] public XKeyEvent xkey;
    }
}
