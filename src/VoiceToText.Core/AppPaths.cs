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
        Resolve(Environment.SpecialFolder.ApplicationData, "XDG_CONFIG_HOME", ".config"),
        "VoiceToText");

    public static string DataDir => OperatingSystem.IsWindows()
        ? ConfigDir
        : Path.Combine(
            Resolve(Environment.SpecialFolder.LocalApplicationData, "XDG_DATA_HOME", ".local/share"),
            "VoiceToText");

    /// <summary>
    /// GetFolderPath can return "" in stripped-down environments (observed on Linux
    /// under WSL); an empty base would scatter our files relative to the CWD. Fall
    /// back through the XDG variable, then $HOME, and finally the temp dir — always
    /// an absolute path.
    /// </summary>
    private static string Resolve(Environment.SpecialFolder folder, string xdgVar, string homeSuffix)
    {
        var path = Environment.GetFolderPath(folder);
        if (!string.IsNullOrEmpty(path))
            return path;
        var xdg = Environment.GetEnvironmentVariable(xdgVar);
        if (!string.IsNullOrEmpty(xdg))
            return xdg;
        var home = Environment.GetEnvironmentVariable("HOME");
        if (!string.IsNullOrEmpty(home))
            return Path.Combine(home, homeSuffix);
        return Path.GetTempPath();
    }
}
