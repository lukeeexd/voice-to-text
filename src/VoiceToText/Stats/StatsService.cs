using System.Text.Json;

namespace VoiceToText.Stats;

/// <summary>
/// Loads/saves usage stats (%APPDATA%\VoiceToText\stats.json) and records each
/// dictation. Wraps the pure <see cref="StatsData"/>; Record is called on the UI thread.
/// Stats are non-critical — failures never throw into the dictation path.
/// </summary>
public sealed class StatsService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private static string StatsPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "VoiceToText", "stats.json");

    public StatsData Data { get; private set; } = new();

    public StatsService() => Load();

    private void Load()
    {
        try
        {
            var path = StatsPath;
            if (File.Exists(path))
            {
                var loaded = JsonSerializer.Deserialize<StatsData>(File.ReadAllText(path), JsonOptions);
                if (loaded is not null) Data = loaded;
            }
        }
        catch
        {
            Data = new StatsData(); // corrupt/unreadable — start fresh
        }
    }

    public void Save()
    {
        try
        {
            var path = StatsPath;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(Data, JsonOptions));
        }
        catch
        {
            // non-critical
        }
    }

    /// <summary>Record one dictation (UI thread) and persist.</summary>
    public void Record(int words, double seconds, string? app)
    {
        Data.Record(DateOnly.FromDateTime(DateTime.Now), words, seconds, app);
        Save();
    }

    /// <summary>Multi-line summary for the stopgap tray "Stats" view.</summary>
    public string Summary(double typingWpm)
    {
        if (Data.TotalDictations == 0)
            return "No dictations yet — press your hotkey and start talking.";

        var saved = FormatDuration(Data.EstimatedMinutesSaved(typingWpm));
        var streak = Data.CurrentStreak(DateOnly.FromDateTime(DateTime.Now));
        return $"{Data.TotalWords:N0} words across {Data.TotalDictations:N0} dictations\n" +
               $"~{saved} of typing saved (at {typingWpm:N0} WPM)\n" +
               $"{streak}-day streak  ·  ~{Data.SpeakingWpm:N0} words/min speaking";
    }

    private static string FormatDuration(double minutes)
    {
        if (minutes < 1) return "<1 min";
        if (minutes < 90) return $"{minutes:N0} min";
        return $"{minutes / 60.0:N1} hrs";
    }
}
