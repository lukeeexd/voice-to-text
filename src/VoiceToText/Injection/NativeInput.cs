using System.Runtime.InteropServices;

namespace VoiceToText.Injection;

/// <summary>Thin SendInput wrapper for synthesizing keystrokes.</summary>
internal static partial class NativeInput
{
    private const uint INPUT_KEYBOARD = 1;
    private const uint KEYEVENTF_KEYUP = 0x0002;

    private const ushort VK_SHIFT = 0x10;
    private const ushort VK_CONTROL = 0x11;
    private const ushort VK_MENU = 0x12; // Alt
    private const ushort VK_LWIN = 0x5B;
    private const ushort VK_RWIN = 0x5C;
    private const ushort VK_V = 0x56;

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint type;
        public InputUnion u;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public MOUSEINPUT mi;
        [FieldOffset(0)] public KEYBDINPUT ki;
        [FieldOffset(0)] public HARDWAREINPUT hi;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct HARDWAREINPUT
    {
        public uint uMsg;
        public ushort wParamL;
        public ushort wParamH;
    }

    [LibraryImport("user32.dll", SetLastError = true)]
    private static partial uint SendInput(uint nInputs, [In] INPUT[] pInputs, int cbSize);

    /// <summary>Synthesize Ctrl+V (paste) into the focused window.</summary>
    public static void SendPaste()
        => Send(
            Key(VK_CONTROL, false),
            Key(VK_V, false),
            Key(VK_V, true),
            Key(VK_CONTROL, true));

    /// <summary>
    /// Release any modifier keys that may still be physically held from the
    /// hotkey, so they don't corrupt the synthesized paste.
    /// </summary>
    public static void ReleaseModifiers()
        => Send(
            Key(VK_CONTROL, true),
            Key(VK_MENU, true),
            Key(VK_SHIFT, true),
            Key(VK_LWIN, true),
            Key(VK_RWIN, true));

    private static INPUT Key(ushort vk, bool keyUp) => new()
    {
        type = INPUT_KEYBOARD,
        u = new InputUnion
        {
            ki = new KEYBDINPUT { wVk = vk, dwFlags = keyUp ? KEYEVENTF_KEYUP : 0 },
        },
    };

    private static void Send(params INPUT[] inputs)
        => SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
}
