using System.Runtime.InteropServices;

namespace VoiceToText.App;

internal static partial class NativeIcon
{
    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool DestroyIcon(IntPtr hIcon);
}
