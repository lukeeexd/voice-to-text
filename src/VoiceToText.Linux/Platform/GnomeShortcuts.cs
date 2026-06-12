using System.Diagnostics;
using VoiceToText.Hotkeys;

namespace VoiceToText.Linux.Platform;

/// <summary>Writes a GNOME custom keyboard shortcut running `voicetotext --toggle` —
/// the supported hotkey path on GNOME Wayland, which has no GlobalShortcuts portal.</summary>
public static class GnomeShortcuts
{
    private const string Schema = "org.gnome.settings-daemon.plugins.media-keys";
    private const string KeyPath = "/org/gnome/settings-daemon/plugins/media-keys/custom-keybindings/voicetotext/";
    private const string KeySchema = "org.gnome.settings-daemon.plugins.media-keys.custom-keybinding";

    public static string ExecutablePath =>
        Environment.GetEnvironmentVariable("APPIMAGE") ?? Environment.ProcessPath ?? "voicetotext";

    public static bool Register(HotkeyDefinition hotkey)
    {
        var binding = VkKeys.ToGnomeBinding(hotkey);
        if (binding is null) return false;
        try
        {
            var list = Run("get", Schema, "custom-keybindings")?.Trim() ?? "@as []";
            if (!list.Contains(KeyPath))
            {
                // The list reads either `@as []` (empty) or `['/path/a/', '/path/b/']`.
                var newList = list.StartsWith("@as") || list == "[]"
                    ? $"['{KeyPath}']"
                    : list.TrimEnd(']') + $", '{KeyPath}']";
                if (Run("set", Schema, "custom-keybindings", newList) is null) return false;
            }
            return Run("set", $"{KeySchema}:{KeyPath}", "name", "'VoiceToText dictation'") is not null
                && Run("set", $"{KeySchema}:{KeyPath}", "command", $"'{ExecutablePath} --toggle'") is not null
                && Run("set", $"{KeySchema}:{KeyPath}", "binding", $"'{binding}'") is not null;
        }
        catch (Exception ex)
        {
            Diagnostics.Log.Error("GNOME shortcut registration failed", ex);
            return false;
        }
    }

    private static string? Run(params string[] args)
    {
        var psi = new ProcessStartInfo("gsettings")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi);
        if (p is null) return null;
        var stdout = p.StandardOutput.ReadToEnd();
        p.WaitForExit(5000);
        return p.ExitCode == 0 ? stdout : null;
    }
}
