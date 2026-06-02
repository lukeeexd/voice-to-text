using System.Runtime.InteropServices;

namespace VoiceToText.Dashboard;

/// <summary>Applies Windows' immersive dark-mode caption so the title bar matches the dark theme.</summary>
internal static partial class DarkTitleBar
{
    // 20 on Windows 10 20H1+ / Windows 11; 19 on early Windows 10 builds.
    private const int DwmwaUseImmersiveDarkMode = 20;
    private const int DwmwaUseImmersiveDarkModeOld = 19;

    [LibraryImport("dwmapi.dll")]
    private static partial int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    /// <summary>Best-effort: no-ops on older Windows or if dwmapi is unavailable.</summary>
    public static void Apply(IntPtr handle)
    {
        if (handle == IntPtr.Zero)
            return;
        try
        {
            int enabled = 1;
            if (DwmSetWindowAttribute(handle, DwmwaUseImmersiveDarkMode, ref enabled, sizeof(int)) != 0)
                DwmSetWindowAttribute(handle, DwmwaUseImmersiveDarkModeOld, ref enabled, sizeof(int));
        }
        catch
        {
            // Cosmetic — never block window creation.
        }
    }
}
