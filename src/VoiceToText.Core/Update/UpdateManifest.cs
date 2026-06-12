using System.Text.Json;

namespace VoiceToText.Update;

/// <summary>
/// The release descriptor the feed folder holds (latest.json), next to the setup exe.
/// </summary>
public sealed class UpdateManifest
{
    public const string ManifestFileName = "latest.json";

    public string? Version { get; set; }
    public string? SetupFileName { get; set; }
    public string? Sha256 { get; set; }
    public string? ReleaseNotes { get; set; }
    public bool Mandatory { get; set; }
    public DateTimeOffset? ReleasedUtc { get; set; }

    // Linux (AppImage) release — additive: old Windows clients ignore unknown
    // fields, and these stay null while no Linux build is published.
    public string? LinuxVersion { get; set; }
    public string? LinuxFileName { get; set; }
    public string? LinuxSha256 { get; set; }

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    /// <summary>Parse a manifest, returning null on any malformed/partial JSON (never throws).</summary>
    public static UpdateManifest? TryParse(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<UpdateManifest>(json, Options);
        }
        catch
        {
            return null;
        }
    }
}
