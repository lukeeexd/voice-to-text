using VoiceToText.Diagnostics;
using VoiceToText.Settings;
using VoiceToText.Update;

namespace VoiceToText.Linux.Platform;

/// <summary>
/// The Linux self-updater: check the shared latest.json feed (Linux fields),
/// stage the new AppImage SHA-verified, and atomically replace our own file
/// ($APPIMAGE) — no installer, no relauncher shim. The user restarts when ready.
/// </summary>
public sealed class LinuxUpdater(AppSettings settings)
{
    private readonly UpdateService _service = new(settings);

    /// <summary>Check (and install when available). Returns a user-facing status line.</summary>
    public async Task<string> CheckAndInstallAsync(bool manual)
    {
        var result = await _service.CheckLinuxAsync(force: manual).ConfigureAwait(false);
        switch (result.Decision)
        {
            case UpdateDecision.UpToDate:
                return $"Up to date (v{result.CurrentVersion?.ToString(3)}).";
            case UpdateDecision.Disabled:
                return "Automatic updates are off.";
            case UpdateDecision.UpdateAvailable:
                break;
            default:
                return result.Message ?? result.Decision.ToString();
        }

        var available = result.AvailableVersion!.ToString(3);
        if (!manual && available == settings.UpdateSkippedVersion)
            return $"v{available} available (skipped).";

        var appImage = Environment.GetEnvironmentVariable("APPIMAGE");
        if (!OperatingSystem.IsLinux() || string.IsNullOrEmpty(appImage) || !File.Exists(appImage))
        {
            // Not running from an AppImage (dev build) — point at the release instead.
            Notifications.Show("VoiceToText update available",
                $"v{available} is out — download it from GitHub Releases.");
            return $"v{available} available — not running from an AppImage, so it can't self-install.";
        }

        try
        {
            var staged = await _service.StageLinuxAsync(result.Manifest!).ConfigureAwait(false);

            // Copy next to the target, fix the mode, then rename over it (atomic on
            // the same filesystem; the running AppImage's mount stays valid).
            var temp = Path.Combine(Path.GetDirectoryName(appImage)!, ".voicetotext-update.tmp");
            File.Copy(staged, temp, overwrite: true);
            File.SetUnixFileMode(temp,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
                UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
            File.Move(temp, appImage, overwrite: true);
            UpdateService.CleanStaging();

            Log.Info($"Self-updated the AppImage to v{available}.");
            Notifications.Show("VoiceToText updated",
                $"v{available} is installed — restart VoiceToText to use it.");
            return $"Updated to v{available} — restart VoiceToText to use it.";
        }
        catch (Exception ex)
        {
            Log.Error("AppImage self-update failed", ex);
            Notifications.Show("VoiceToText update available",
                $"v{available} is out, but it could not self-install ({ex.Message}). Download it from GitHub Releases.");
            return $"v{available} available, but the update failed: {ex.Message}";
        }
    }
}
