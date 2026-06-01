using VoiceToText.Stats;

namespace VoiceToText.Dashboard;

/// <summary>One bar of the daily-activity chart.</summary>
public readonly record struct DayBar(DateOnly Date, long Words);

/// <summary>One row of the top-apps breakdown. Fraction is 0..1 of the widest row.</summary>
public readonly record struct AppBar(string Name, long Words, double Fraction);

/// <summary>
/// Pure view-model: turns a <see cref="StatsData"/> snapshot into ready-to-draw rows
/// (hero strings, tile values, a zero-filled 30-day series, the top-apps breakdown,
/// and records). No I/O, no UI, fully unit-testable via --dashtest.
/// </summary>
public sealed class DashboardModel
{
    public const int SeriesDays = 30;
    public const int MaxApps = 5;

    public bool HasData { get; }
    public string TimeSavedText { get; }
    public string TimeSavedSubtext { get; }
    public int Streak { get; }
    public long TotalWords { get; }
    public long TotalDictations { get; }
    public int AvgWordsPerDictation { get; }
    public int SpeakingWpm { get; }
    public IReadOnlyList<DayBar> DailySeries { get; }
    public long DailyMax { get; }
    public IReadOnlyList<AppBar> TopApps { get; }
    public string? BestDictationText { get; }
    public string? BusiestDayText { get; }

    public DashboardModel(StatsData data, DateOnly today, double typingWpm)
    {
        HasData = data.TotalDictations > 0;
        TotalWords = data.TotalWords;
        TotalDictations = data.TotalDictations;
        Streak = data.CurrentStreak(today);
        AvgWordsPerDictation = (int)Math.Round(data.AverageWordsPerDictation);
        SpeakingWpm = (int)Math.Round(data.SpeakingWpm);

        TimeSavedText = StatsFormat.Duration(data.EstimatedMinutesSaved(typingWpm));
        TimeSavedSubtext = $"vs typing at {typingWpm:N0} WPM";

        // 30-day series, oldest -> newest, zero-filled for missing days.
        var series = new List<DayBar>(SeriesDays);
        long max = 0;
        for (var i = SeriesDays - 1; i >= 0; i--)
        {
            var day = today.AddDays(-i);
            long words = data.WordsOn(day);
            if (words > max) max = words;
            series.Add(new DayBar(day, words));
        }
        DailySeries = series;
        DailyMax = Math.Max(1, max);

        // Top apps by words desc; remainder folded into a single "Other" row.
        var ordered = data.Apps
            .Select(kv => (Name: kv.Key, Words: (long)kv.Value.Words))
            .OrderByDescending(a => a.Words)
            .ToList();
        var rows = ordered.Take(MaxApps).ToList();
        if (ordered.Count > MaxApps)
        {
            long other = ordered.Skip(MaxApps).Sum(a => a.Words);
            if (other > 0) rows.Add((Name: "Other", Words: other));
        }
        // Fraction relative to the widest displayed row (computed AFTER "Other" exists,
        // so "Other" can never overflow the track).
        long maxWords = rows.Count > 0 ? rows.Max(r => r.Words) : 1;
        TopApps = rows
            .Select(r => new AppBar(r.Name, r.Words, maxWords <= 0 ? 0 : (double)r.Words / maxWords))
            .ToList();

        BestDictationText = data.MaxWordsInOneDictation > 0 ? $"{data.MaxWordsInOneDictation:N0} words" : null;
        BusiestDayText = FormatBusiestDay(data);
    }

    private static string? FormatBusiestDay(StatsData data)
    {
        var key = data.BusiestDay;
        if (key is null || !DateOnly.TryParse(key, out var day)) return null;
        return $"{day:MMM d} ({data.WordsOn(day):N0} words)";
    }
}