using System.Text.Json.Serialization;

namespace VoiceToText.Stats;

public sealed class DayStat
{
    public int Words { get; set; }
    public int Dictations { get; set; }
    public double Seconds { get; set; }
}

public sealed class AppStat
{
    public int Words { get; set; }
    public int Dictations { get; set; }
}

/// <summary>
/// Pure, serializable usage-statistics model. All mutation goes through <see cref="Record"/>;
/// everything else is derived (no I/O, no threading) so it is fully unit-testable.
/// </summary>
public sealed class StatsData
{
    public long TotalWords { get; set; }
    public long TotalDictations { get; set; }
    public double TotalSeconds { get; set; }
    public int MaxWordsInOneDictation { get; set; }
    public Dictionary<string, DayStat> Days { get; set; } = new(); // key: yyyy-MM-dd
    public Dictionary<string, AppStat> Apps { get; set; } = new(); // key: friendly app name

    /// <summary>Words in transcribed text (whitespace-separated). Empty/blank => 0.</summary>
    public static int CountWords(string? text) =>
        string.IsNullOrWhiteSpace(text) ? 0 : text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;

    private static string DayKey(DateOnly day) => day.ToString("yyyy-MM-dd");

    /// <summary>Record one dictation. No-ops when <paramref name="words"/> &lt;= 0.</summary>
    public void Record(DateOnly day, int words, double seconds, string? app)
    {
        if (words <= 0) return;

        TotalWords += words;
        TotalDictations++;
        TotalSeconds += seconds;
        if (words > MaxWordsInOneDictation) MaxWordsInOneDictation = words;

        var dayKey = DayKey(day);
        if (!Days.TryGetValue(dayKey, out var d)) Days[dayKey] = d = new DayStat();
        d.Words += words;
        d.Dictations++;
        d.Seconds += seconds;

        var appKey = string.IsNullOrWhiteSpace(app) ? "Unknown" : app!;
        if (!Apps.TryGetValue(appKey, out var a)) Apps[appKey] = a = new AppStat();
        a.Words += words;
        a.Dictations++;
    }

    // ---- derived (computed, never stored) ----

    public double EstimatedMinutesSaved(double typingWpm) =>
        typingWpm <= 0 ? 0 : TotalWords / typingWpm;

    [JsonIgnore]
    public double AverageWordsPerDictation =>
        TotalDictations == 0 ? 0 : (double)TotalWords / TotalDictations;

    [JsonIgnore]
    public double SpeakingWpm =>
        TotalSeconds <= 0 ? 0 : TotalWords / (TotalSeconds / 60.0);

    public int WordsOn(DateOnly day) =>
        Days.TryGetValue(DayKey(day), out var d) ? d.Words : 0;

    public int WordsInLastDays(DateOnly today, int days)
    {
        var total = 0;
        for (var i = 0; i < days; i++) total += WordsOn(today.AddDays(-i));
        return total;
    }

    private bool HasActivity(DateOnly day) =>
        Days.TryGetValue(DayKey(day), out var d) && d.Words > 0;

    /// <summary>Consecutive days with activity ending today (or yesterday if nothing yet today).</summary>
    public int CurrentStreak(DateOnly today)
    {
        DateOnly? anchor = HasActivity(today) ? today
            : HasActivity(today.AddDays(-1)) ? today.AddDays(-1)
            : null;
        if (anchor is null) return 0;

        var streak = 0;
        for (var d = anchor.Value; HasActivity(d); d = d.AddDays(-1)) streak++;
        return streak;
    }

    [JsonIgnore]
    public string? BusiestDay
    {
        get
        {
            string? best = null;
            var most = -1;
            foreach (var (key, value) in Days)
                if (value.Words > most) { most = value.Words; best = key; }
            return best;
        }
    }
}
