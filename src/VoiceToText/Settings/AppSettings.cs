using System.Text.Json;
using System.Text.Json.Serialization;
using VoiceToText.Hotkeys;
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

    public GgmlType ModelType { get; set; } = GgmlType.LargeV3Turbo;

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

    private static string SettingsPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "VoiceToText", "settings.json");

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
