using System.Reflection;
using System.Security.Cryptography;
using VoiceToText.Settings;

namespace VoiceToText.Update;

/// <summary>
/// Impure orchestrator around the pure <see cref="UpdateChecker"/>: reads the manifest
/// from the feed (a local/UNC folder or an https feed URL such as a GitHub Releases
/// "latest/download" prefix), stages (copies/downloads + integrity-checks) the installer
/// locally, and writes the relauncher shim that survives this process so the app always
/// comes back.
/// </summary>
public sealed class UpdateService(AppSettings settings)
{
    public static string StagingDir => Path.Combine(Path.GetTempPath(), "VoiceToText-Update");
    public static string UpdateLogPath => Path.Combine(StagingDir, "VoiceToText-Update.log");

    // One shared client: GitHub rejects requests without a User-Agent; redirects (e.g.
    // releases/latest/download → the versioned asset) are followed by the default handler.
    private static readonly HttpClient Http = CreateHttpClient();

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient();
        client.DefaultRequestHeaders.UserAgent.ParseAdd("VoiceToText-Updater");
        client.Timeout = TimeSpan.FromMinutes(5); // caps installer downloads; checks use a tighter CTS
        return client;
    }

    /// <summary>True when the update source is an http(s) feed URL rather than a folder.</summary>
    public static bool IsHttpFeed(string? feed)
    {
        var s = feed?.Trim();
        return s is not null
            && (s.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
             || s.StartsWith("http://", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Join the feed URL prefix and a bare file name (the manifest's safe-name rule applies).</summary>
    private string FeedUrl(string fileName) => settings.UpdateFeedFolder.Trim().TrimEnd('/') + "/" + fileName;

    /// <summary>Current running version, or null if it can't be determined (updates then stay off).</summary>
    public Version? CurrentVersion { get; } = ResolveCurrentVersion();

    private static Version? ResolveCurrentVersion()
    {
        try
        {
            var asm = Assembly.GetEntryAssembly();
            var asmVersion = asm?.GetName().Version;
            if (asmVersion is not null && asmVersion != new Version(0, 0, 0, 0))
                return asmVersion;

            // Fallback to the informational version attribute.
            var info = asm?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            return VersionParsing.TryNormalize(info);
        }
        catch
        {
            return null;
        }
    }

    public Task<UpdateCheckResult> CheckAsync(CancellationToken cancellationToken = default)
        => CheckCoreAsync(UpdateChecker.Decide, force: false, cancellationToken);

    /// <summary>Check using the manifest's Linux (AppImage) fields. <paramref name="force"/>
    /// bypasses the AutoUpdateEnabled gate (the settings window's manual check).</summary>
    public Task<UpdateCheckResult> CheckLinuxAsync(bool force = false, CancellationToken cancellationToken = default)
        => CheckCoreAsync(UpdateChecker.DecideLinux, force, cancellationToken);

    private async Task<UpdateCheckResult> CheckCoreAsync(
        Func<bool, string?, Version?, UpdateManifest?, UpdateCheckResult> decide,
        bool force,
        CancellationToken cancellationToken)
    {
        var enabled = settings.AutoUpdateEnabled || force;
        if (!enabled || string.IsNullOrWhiteSpace(settings.UpdateFeedFolder))
            return decide(enabled, settings.UpdateFeedFolder, CurrentVersion, null);

        try
        {
            // Bound the read so a hung share / stalled connection fails fast.
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(10));

            string json;
            if (IsHttpFeed(settings.UpdateFeedFolder))
            {
                json = await Http.GetStringAsync(FeedUrl(UpdateManifest.ManifestFileName), cts.Token).ConfigureAwait(false);
            }
            else
            {
                var manifestPath = Path.Combine(settings.UpdateFeedFolder, UpdateManifest.ManifestFileName);
                if (!File.Exists(manifestPath))
                    return new(UpdateDecision.ManifestInvalid, CurrentVersion, null, null, "No update manifest found in the update folder.");
                json = await File.ReadAllTextAsync(manifestPath, cts.Token).ConfigureAwait(false);
            }

            var manifest = UpdateManifest.TryParse(json);
            return decide(true, settings.UpdateFeedFolder, CurrentVersion, manifest);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException
                                       or OperationCanceledException or NotSupportedException or ArgumentException
                                       or HttpRequestException or UriFormatException or InvalidOperationException)
        {
            return new(UpdateDecision.ManifestInvalid, CurrentVersion, null, null, $"Could not read the update source: {ex.Message}");
        }
    }

    /// <summary>
    /// Copy (folder feed) or download (https feed) the setup exe to a local temp file —
    /// never run it straight off a share or the network — verify its SHA-256 if the
    /// manifest provides one, and return the staged path. Cleans up partials on failure.
    /// </summary>
    public Task<string> StageInstallerAsync(UpdateManifest manifest, CancellationToken cancellationToken = default)
        => StageFileAsync(manifest.SetupFileName!, manifest.Sha256, cancellationToken);

    /// <summary>Stage the Linux AppImage named by the manifest, SHA-verified.</summary>
    public Task<string> StageLinuxAsync(UpdateManifest manifest, CancellationToken cancellationToken = default)
        => StageFileAsync(manifest.LinuxFileName!, manifest.LinuxSha256, cancellationToken);

    private async Task<string> StageFileAsync(string fileName, string? sha256, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(StagingDir);
        var dest = Path.Combine(StagingDir, fileName);
        var part = dest + ".part";

        try
        {
            if (File.Exists(part)) File.Delete(part);

            if (IsHttpFeed(settings.UpdateFeedFolder))
            {
                using var response = await Http.GetAsync(FeedUrl(fileName), HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();
                await using var src = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
                await using var dst = new FileStream(part, FileMode.Create, FileAccess.Write, FileShare.None);
                await src.CopyToAsync(dst, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                var source = Path.Combine(settings.UpdateFeedFolder, fileName);
                await using var src = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read);
                await using var dst = new FileStream(part, FileMode.Create, FileAccess.Write, FileShare.None);
                await src.CopyToAsync(dst, cancellationToken).ConfigureAwait(false);
            }

            if (!string.IsNullOrWhiteSpace(sha256))
            {
                var actual = await ComputeSha256Async(part, cancellationToken).ConfigureAwait(false);
                if (!string.Equals(actual, sha256!.Trim(), StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("The downloaded update failed its integrity check (SHA-256 mismatch).");
            }

            if (File.Exists(dest)) File.Delete(dest);
            File.Move(part, dest);
            return dest;
        }
        catch
        {
            try { if (File.Exists(part)) File.Delete(part); } catch { /* best effort */ }
            throw;
        }
    }

    private static async Task<string> ComputeSha256Async(string path, CancellationToken cancellationToken)
    {
        using var sha = SHA256.Create();
        await using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        var hash = await sha.ComputeHashAsync(fs, cancellationToken).ConfigureAwait(false);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    /// <summary>Best-effort cleanup of leftover staged files (call only on a normal, non-post-update start).</summary>
    public static void CleanStaging()
    {
        try
        {
            if (Directory.Exists(StagingDir))
                Directory.Delete(StagingDir, recursive: true);
        }
        catch
        {
            // A shim may still be finishing; ignore and let the next start clean up.
        }
    }
}
