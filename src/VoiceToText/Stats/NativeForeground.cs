using System.Diagnostics;
using System.Runtime.InteropServices;

namespace VoiceToText.Stats;

/// <summary>
/// Identifies the focused window's app (process name) for the per-app stats.
/// Reads only the process name — never window titles or any content.
/// </summary>
internal static partial class NativeForeground
{
    [LibraryImport("user32.dll")]
    private static partial IntPtr GetForegroundWindow();

    [LibraryImport("user32.dll")]
    private static partial uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    public static string GetForegroundProcessName()
    {
        try
        {
            var hwnd = GetForegroundWindow();
            if (hwnd == IntPtr.Zero) return "Unknown";
            _ = GetWindowThreadProcessId(hwnd, out var pid);
            if (pid == 0) return "Unknown";
            using var process = Process.GetProcessById((int)pid);
            return Prettify(process.ProcessName);
        }
        catch
        {
            return "Unknown";
        }
    }

    private static string Prettify(string processName) =>
        string.IsNullOrEmpty(processName)
            ? "Unknown"
            : char.ToUpperInvariant(processName[0]) + processName[1..].ToLowerInvariant();
}
