using System.Runtime.InteropServices;

namespace VoiceToText.Diagnostics;

/// <summary>Best-effort primary GPU adapter name via EnumDisplayDevices. "Unknown" on failure.</summary>
public static class GpuInfo
{
    private const int DisplayDeviceAttachedToDesktop = 0x1;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DISPLAY_DEVICE
    {
        public int cb;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DeviceName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceString;
        public int StateFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceID;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceKey;
    }

    // Classic DllImport: source-gen LibraryImport does not marshal ByValTStr struct fields.
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumDisplayDevices(string? lpDevice, uint iDevNum, ref DISPLAY_DEVICE lpDisplayDevice, uint dwFlags);

    public static string PrimaryGpuName()
    {
        try
        {
            string? firstAdapter = null;
            for (uint i = 0; ; i++)
            {
                var dd = new DISPLAY_DEVICE { cb = Marshal.SizeOf<DISPLAY_DEVICE>() };
                if (!EnumDisplayDevices(null, i, ref dd, 0))
                    break;
                if (string.IsNullOrWhiteSpace(dd.DeviceString))
                    continue;
                firstAdapter ??= dd.DeviceString;
                if ((dd.StateFlags & DisplayDeviceAttachedToDesktop) != 0)
                    return dd.DeviceString;
            }
            return firstAdapter ?? "Unknown";
        }
        catch
        {
            return "Unknown";
        }
    }
}
