using System.Text.Json;
using System.Text.Json.Serialization;
using VoiceToText.Hotkeys;
using VoiceToText.TextProcessing;
using Whisper.net.Ggml;

namespace VoiceToText.Settings;

/// <summary>Persisted user settings (%APPDATA%\VoiceToText\settings.json).</summary>
public sealed class AppSettings
{
    /// <summary>Selected capture device id, or null to use the system default.</summary>
    public string? InputDeviceId { get; set; }

    public uint HotkeyModifiers { get; set; } = HotkeyDefinition.Default.Modifiers;
    public uint HotkeyVirtualKey { get; set; } = HotkeyDefinition.Default.VirtualKey;

    /// <summary>Whisper language code, or "auto" to detect.</summary>
    public string Language { get; set; } = "auto";

    /// <summary>Automatically stop recording after a pause in speech.</summary>
    public bool AutoStopEnabled { get; set; } = true;

    /// <summary>Seconds of silence (after speech) before auto-stopping.</summary>
    public double AutoStopSilenceSeconds { get; set; } = 1.5;

    /// <summary>Assumed typing speed (WPM) for the "time saved" estimate.</summary>
    public double TypingSpeedWpm { get; set; } = 40;

    /// <summary>Show the on-screen "listening" indicator while dictating.</summary>
    public bool ShowOverlay { get; set; } = true;

    /// <summary>Remembered overlay top-left position; null = default bottom-center of the primary screen.</summary>
    public int? OverlayX { get; set; }
    public int? OverlayY { get; set; }

    /// <summary>
    /// Update source holding latest.json + the setup exe: a folder (local or UNC) or an https
    /// feed URL (e.g. a GitHub Releases latest/download prefix). Empty = no feed.
    /// </summary>
    public string UpdateFeedFolder { get; set; } = "https://github.com/lukeeexd/voice-to-text/releases/latest/download";

    /// <summary>Check for updates on startup and via the tray menu. Off by default (runs an installer).</summary>
    public bool AutoUpdateEnabled { get; set; } = false;

    /// <summary>Whether the user accepted the "this runs an installer from that folder" warning.</summary>
    public bool UpdateConsentAccepted { get; set; }

    /// <summary>A version the user declined; suppresses the automatic startup nag for it.</summary>
    public string UpdateSkippedVersion { get; set; } = "";

    public GgmlType ModelType { get; set; } = GgmlType.LargeV3Turbo;

    /// <summary>Custom find→replace rules applied to transcribed text before pasting.</summary>
    public List<ReplacementRule> Replacements { get; set; } = new();

    /// <summary>Turn spoken "new line"/"new paragraph" into line breaks.</summary>
    public bool SpokenCommandsEnabled { get; set; } = true;

    /// <summary>Hold the hotkey to record (release to stop) instead of press-to-toggle.</summary>
    public bool HoldToTalk { get; set; } = false;

    /// <summary>Keep a local, opt-in log of recent dictations (history.json). Off by default.</summary>
    public bool HistoryEnabled { get; set; } = false;

    /// <summary>Play a short sound when dictation starts and stops. On by default.</summary>
    public bool SoundCuesEnabled { get; set; } = true;

    /// <summary>Loudness of the start/stop sound cues, 0..1. 1 = the original (current) loudness.</summary>
    public double SoundCuesVolume { get; set; } = 1.0;

    /// <summary>Whether the first-run welcome has been shown. Set once on first launch.</summary>
    public bool OnboardingCompleted { get; set; } = false;

    /// <summary>Linux/Wayland only: the RemoteDesktop portal's restore token, so the
    /// paste-injection permission persists across restarts. Unused on Windows.</summary>
    public string? PortalRestoreToken { get; set; }

    /// <summary>Linux only: opt INTO GPU (Vulkan) inference. Off by default because
    /// Whisper.net 1.9's Vulkan→CPU fallback dlopens ggml-base twice and aborts the
    /// process on hosts where Vulkan is present but broken; with the opt-in we pin a
    /// single runtime (probed in a child process first) and the bug cannot trigger.</summary>
    public bool UseGpuExperimental { get; set; } = false;

    [JsonIgnore]
    public HotkeyDefinition Hotkey
    {
        get => new(HotkeyModifiers, HotkeyVirtualKey);
        set
        {
            HotkeyModifiers = value.Modifiers;
            HotkeyVirtualKey = value.VirtualKey;
        }
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private static string SettingsPath => Path.Combine(AppPaths.ConfigDir, "settings.json");

    public static AppSettings Load()
    {
        try
        {
            var path = SettingsPath;
            if (File.Exists(path))
            {
                var json = File.ReadAllText(path);
                var loaded = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions);
                if (loaded is not null)
                    return loaded;
            }
        }
        catch
        {
            // Corrupt/unreadable settings — fall back to defaults.
        }
        return new AppSettings();
    }

    public void Save()
    {
        var path = SettingsPath;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(this, JsonOptions));
    }
}
