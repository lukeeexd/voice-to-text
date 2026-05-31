namespace VoiceToText.Update;

public enum UpdateDecision
{
    UpToDate,
    UpdateAvailable,
    Disabled,
    NoFeedConfigured,
    ManifestInvalid,
    VersionUnknown,
}

public sealed record UpdateCheckResult(
    UpdateDecision Decision,
    Version? CurrentVersion,
    Version? AvailableVersion,
    UpdateManifest? Manifest,
    string? Message);

/// <summary>Version-string helpers shared by the checker, the service, and tests.</summary>
public static class VersionParsing
{
    /// <summary>
    /// Parse a version string into a normalized 4-part <see cref="Version"/>, tolerating
    /// SemVer "-prerelease" and "+build" suffixes (stripped). Returns null if unparseable.
    /// </summary>
    public static Version? TryNormalize(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        var s = raw.Trim();
        var plus = s.IndexOf('+');
        if (plus >= 0) s = s[..plus];
        var dash = s.IndexOf('-');
        if (dash >= 0) s = s[..dash];

        if (!Version.TryParse(s, out var v))
            return null;

        return new Version(v.Major, Math.Max(v.Minor, 0), Math.Max(v.Build, 0), Math.Max(v.Revision, 0));
    }
}

/// <summary>
/// Pure update decision logic — no file/network I/O, so it is fully unit-testable
/// (mirrors how SilenceDetector is tested via --vadtest).
/// </summary>
public static class UpdateChecker
{
    public static UpdateCheckResult Decide(bool enabled, string? feedFolder, Version? current, UpdateManifest? manifest)
    {
        if (!enabled)
            return new(UpdateDecision.Disabled, current, null, null, null);
        if (current is null)
            return new(UpdateDecision.VersionUnknown, null, null, null, "App version could not be determined.");
        if (string.IsNullOrWhiteSpace(feedFolder))
            return new(UpdateDecision.NoFeedConfigured, current, null, null, null);
        if (manifest is null)
            return new(UpdateDecision.ManifestInvalid, current, null, null, "No valid update manifest.");
        if (string.IsNullOrWhiteSpace(manifest.Version) || string.IsNullOrWhiteSpace(manifest.SetupFileName))
            return new(UpdateDecision.ManifestInvalid, current, null, manifest, "Manifest is missing the version or setup file name.");
        if (!IsSafeSetupFileName(manifest.SetupFileName!))
            return new(UpdateDecision.ManifestInvalid, current, null, manifest, "Manifest setup file name is not a plain file name in the feed folder.");

        var available = VersionParsing.TryNormalize(manifest.Version);
        if (available is null)
            return new(UpdateDecision.ManifestInvalid, current, null, manifest, "Manifest version is not a valid version number.");

        return available > current
            ? new(UpdateDecision.UpdateAvailable, current, available, manifest, manifest.ReleaseNotes)
            : new(UpdateDecision.UpToDate, current, available, manifest, null);
    }

    /// <summary>
    /// The setup must be a bare file name living inside the feed folder — reject rooted
    /// paths, separators, "..", drive/ADS colons, and invalid chars so a manifest can't
    /// point the updater at an arbitrary executable.
    /// </summary>
    public static bool IsSafeSetupFileName(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name != name.Trim())
            return false;
        if (Path.IsPathRooted(name))
            return false;
        if (name.Contains('/') || name.Contains('\\') || name.Contains("..") || name.Contains(':'))
            return false;
        if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            return false;
        return true;
    }
}
