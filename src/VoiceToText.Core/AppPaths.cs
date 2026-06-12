namespace VoiceToText;

/// <summary>
/// The app's storage roots, in one place. On Windows both are %APPDATA%\VoiceToText
/// (exactly the historical layout). On Linux they split per the XDG convention:
/// config (settings.json) under ~/.config, data (models/history/stats/logs) under
/// ~/.local/share — .NET maps the two SpecialFolders to those XDG dirs natively.
/// </summary>
public static class AppPaths
{
    public static string ConfigDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "VoiceToText");

    public static string DataDir => OperatingSystem.IsWindows()
        ? ConfigDir
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VoiceToText");
}
