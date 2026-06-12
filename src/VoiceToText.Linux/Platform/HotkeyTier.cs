namespace VoiceToText.Linux.Platform;

public enum HotkeyTier { X11Grab, IpcBinding }

/// <summary>Session-environment detection driving the hotkey tier and paste backend.</summary>
public static class SessionInfo
{
    public static bool IsWayland =>
        string.Equals(Environment.GetEnvironmentVariable("XDG_SESSION_TYPE"), "wayland", StringComparison.OrdinalIgnoreCase);

    public static bool IsGnome =>
        (Environment.GetEnvironmentVariable("XDG_CURRENT_DESKTOP") ?? "")
            .Contains("GNOME", StringComparison.OrdinalIgnoreCase);

    public static bool HasX11Display =>
        !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DISPLAY"));

    /// <summary>X11 grab only on real X11 sessions; everywhere else the IPC tier
    /// (a desktop keybinding running `voicetotext --toggle`).</summary>
    public static HotkeyTier PickHotkeyTier()
        => !IsWayland && HasX11Display ? HotkeyTier.X11Grab : HotkeyTier.IpcBinding;
}
