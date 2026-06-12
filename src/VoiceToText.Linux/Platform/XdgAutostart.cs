namespace VoiceToText.Linux.Platform;

/// <summary>XDG autostart: a .desktop entry under ~/.config/autostart.</summary>
public static class XdgAutostart
{
    private static string DesktopFile => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "autostart", "voicetotext.desktop");

    public static bool IsEnabled => File.Exists(DesktopFile);

    public static void Enable()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(DesktopFile)!);
        File.WriteAllText(DesktopFile, $"""
            [Desktop Entry]
            Type=Application
            Name=VoiceToText
            Comment=Voice dictation (start hidden in the tray)
            Exec={GnomeShortcuts.ExecutablePath}
            X-GNOME-Autostart-enabled=true
            """ + "\n");
    }

    public static void Disable()
    {
        try { File.Delete(DesktopFile); } catch { /* best effort */ }
    }
}
