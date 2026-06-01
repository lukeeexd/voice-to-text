using VoiceToText.Stats;

namespace VoiceToText.Dashboard;

/// <summary>One bar of the daily-activity chart.</summary>
public readonly record struct DayBar(DateOnly Date, long Words);

/// <summary>One row of the top-apps breakdown. Fraction is 0..1 of the widest row.</summary>
public readonly record struct AppBar(string Name, long Words, double Fraction);

/// <summary>Which window of daily activity the chart shows.</summary>
public enum ChartRange { Week, Month, All }

/// <summary>A zero-filled daily series (oldest→newest) plus its max bar height.</summary>
public readonly record struct ActivitySeries(IReadOnlyList<DayBar> Bars, long Max);

/// <summary>
/// Pure view-model: turns a <see cref="StatsData"/> snapshot into ready-to-draw rows
/// (hero strings, tile values, a zero-filled 30-day series, the top-apps breakdown,
/// and records). No I/O, no UI, fully unit-testable via --dashtest.
/// </summary>
public sealed class DashboardModel
{
    private readonly StatsData _data;
    private readonly DateOnly _today;

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
    public string SpeakingTimeText { get; }
    public IReadOnlyList<DayBar> DailySeries { get; }
    public long DailyMax { get; }
    public IReadOnlyList<AppBar> TopApps { get; }
    public string? BestDictationText { get; }
    public string? BusiestDayText { get; }

    public DashboardModel(StatsData data, DateOnly today, double typingWpm)
    {
        _data = data;
        _today = today;
        HasData = data.TotalDictations > 0;
        TotalWords = data.TotalWords;
        TotalDictations = data.TotalDictations;
        Streak = data.CurrentStreak(today);
        AvgWordsPerDictation = (int)Math.Round(data.AverageWordsPerDictation);
        SpeakingWpm = (int)Math.Round(data.SpeakingWpm);
        SpeakingTimeText = StatsFormat.Duration(data.TotalSeconds / 60.0);

        TimeSavedText = StatsFormat.Duration(data.EstimatedMinutesSaved(typingWpm));
        TimeSavedSubtext = $"vs typing at {typingWpm:N0} WPM";

        // Default chart window (30 days). The page re-queries other ranges via Activity().
        var month = Activity(ChartRange.Month);
        DailySeries = month.Bars;
        DailyMax = month.Max;

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
        // AppStat.Words is always > 0 in practice (StatsData.Record no-ops on words <= 0); the
        // <= 0 guard below only defends against a StatsData deserialized with zero-word entries.
        long maxWords = rows.Count > 0 ? rows.Max(r => r.Words) : 1;
        TopApps = rows
            .Select(r => new AppBar(r.Name, r.Words, maxWords <= 0 ? 0 : (double)r.Words / maxWords))
            .ToList();

        BestDictationText = data.MaxWordsInOneDictation > 0 ? $"{data.MaxWordsInOneDictation:N0} words" : null;
        BusiestDayText = FormatBusiestDay(data);
    }

    /// <summary>
    /// Build the daily-activity series for a range: Week = last 7 days, Month = last 30,
    /// All = earliest recorded day → today (one bar per day). All falls back to the Month
    /// window when no activity is recorded, so the chart is never degenerate. Pure.
    /// </summary>
    public ActivitySeries Activity(ChartRange range)
    {
        int days = range switch
        {
            ChartRange.Week => 7,
            ChartRange.All => AllRangeDays(),
            _ => SeriesDays,
        };

        var bars = new List<DayBar>(days);
        long max = 0;
        for (var i = days - 1; i >= 0; i--)
        {
            var day = _today.AddDays(-i);
            long words = _data.WordsOn(day);
            if (words > max) max = words;
            bars.Add(new DayBar(day, words));
        }
        return new ActivitySeries(bars, Math.Max(1, max));
    }

    // Inclusive day count from the earliest recorded day to today; Month-window fallback when empty.
    private int AllRangeDays()
    {
        DateOnly? earliest = null;
        foreach (var key in _data.Days.Keys)
            if (DateOnly.TryParse(key, out var day) && (earliest is null || day < earliest))
                earliest = day;

        if (earliest is null || earliest > _today)
            return SeriesDays;

        return _today.DayNumber - earliest.Value.DayNumber + 1;
    }

    private static string? FormatBusiestDay(StatsData data)
    {
        var key = data.BusiestDay;
        if (key is null || !DateOnly.TryParse(key, out var day)) return null;
        return $"{day:MMM d} ({data.WordsOn(day):N0} words)";
    }
}
