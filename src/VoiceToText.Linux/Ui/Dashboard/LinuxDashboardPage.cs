using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using VoiceToText.Dashboard;

namespace VoiceToText.Linux.Ui.Dashboard;

/// <summary>The Dashboard page: hero band, five stat tiles, the activity chart + top-apps
/// breakdown, and a records strip. Mirrors the Windows DashboardPage.DoLayout metrics
/// (pad 20, gap 10, hero 116, tiles 64, 63/37 column split). Centered empty state when
/// there is no data.</summary>
internal sealed class LinuxDashboardPage : Canvas
{
    private readonly VttHeroPanel _hero = new();
    private readonly VttStatTile _tWords = new();
    private readonly VttStatTile _tDictations = new();
    private readonly VttStatTile _tAvg = new();
    private readonly VttStatTile _tWpm = new();
    private readonly VttStatTile _tSpeaking = new();
    private readonly VttBarChart _chart = new();
    private readonly VttBreakdownBars _apps = new();
    private readonly Button _r7 = MakeRangeButton("7");
    private readonly Button _r30 = MakeRangeButton("30");
    private readonly Button _rAll = MakeRangeButton("All");
    private readonly TextBlock _records = new()
    {
        Foreground = ThemeTokens.TextSecondaryBrush,
        FontSize = ThemeTokens.CaptionSize,
    };
    private readonly TextBlock _empty = new()
    {
        Text = "No dictations yet — press your hotkey and start talking.",
        Foreground = ThemeTokens.TextSecondaryBrush,
        FontSize = ThemeTokens.EmptySize,
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
        IsVisible = false,
    };

    private ChartRange _range = ChartRange.Month;
    private DashboardModel? _model;

    public LinuxDashboardPage()
    {
        Background = ThemeTokens.WindowBgBrush;
        _r7.Click += (_, _) => SetRange(ChartRange.Week);
        _r30.Click += (_, _) => SetRange(ChartRange.Month);
        _rAll.Click += (_, _) => SetRange(ChartRange.All);

        Children.Add(_hero);
        Children.Add(_tWords);
        Children.Add(_tDictations);
        Children.Add(_tAvg);
        Children.Add(_tWpm);
        Children.Add(_tSpeaking);
        Children.Add(_chart);
        Children.Add(_apps);
        Children.Add(_records);
        Children.Add(_r7);
        Children.Add(_r30);
        Children.Add(_rAll);
        Children.Add(_empty);

        StyleRangeButtons();
        SizeChanged += (_, _) => DoLayout();
    }

    private static Button MakeRangeButton(string text) => new()
    {
        Content = text,
        FontSize = ThemeTokens.CaptionSize,
        Foreground = ThemeTokens.TextSecondaryBrush,
        Background = ThemeTokens.CardBgBrush,
        BorderBrush = ThemeTokens.CardBorderBrush,
        BorderThickness = new Thickness(1),
        Padding = new Thickness(0),
        HorizontalContentAlignment = HorizontalAlignment.Center,
        VerticalContentAlignment = VerticalAlignment.Center,
        Focusable = false,
    };

    public void Bind(DashboardModel m)
    {
        _model = m;
        _empty.IsVisible = !m.HasData;
        foreach (var c in new Control[] { _hero, _tWords, _tDictations, _tAvg, _tWpm, _tSpeaking, _chart, _apps, _records, _r7, _r30, _rAll })
            c.IsVisible = m.HasData;
        if (!m.HasData) return;

        _hero.SetData(m.TimeSavedText, m.TimeSavedSubtext, m.Streak);
        _tWords.SetData(m.TotalWords.ToString("N0"), "Words dictated");
        _tDictations.SetData(m.TotalDictations.ToString("N0"), "Dictations");
        _tAvg.SetData(m.AvgWordsPerDictation.ToString("N0"), "Avg words/dictation");
        _tWpm.SetData(m.SpeakingWpm.ToString("N0"), "Speaking WPM");
        _tSpeaking.SetData(m.SpeakingTimeText, "Speaking time");
        ApplyRange();
        _apps.SetData(m.TopApps);

        var best = m.BestDictationText is null ? "" : $"Best dictation: {m.BestDictationText}";
        var busy = m.BusiestDayText is null ? "" : $"        Busiest day: {m.BusiestDayText}";
        _records.Text = best + busy;

        DoLayout();
    }

    private void SetRange(ChartRange range)
    {
        _range = range;
        ApplyRange();
    }

    private void ApplyRange()
    {
        StyleRangeButtons();
        if (_model is null) return;
        var series = _model.Activity(_range);
        _chart.SetData(series.Bars, series.Max, ChartTitle(_range));
    }

    private static string ChartTitle(ChartRange range) => range switch
    {
        ChartRange.Week => "Activity — last 7 days",
        ChartRange.All => "Activity — all time",
        _ => "Activity — last 30 days",
    };

    private void StyleRangeButtons()
    {
        void Style(Button b, ChartRange r)
        {
            bool on = r == _range;
            b.Background = on ? ThemeTokens.NavActiveBgBrush : ThemeTokens.CardBgBrush;
            b.Foreground = on ? ThemeTokens.NavActiveTextBrush : ThemeTokens.TextSecondaryBrush;
            b.BorderBrush = on ? ThemeTokens.AccentBrush : ThemeTokens.CardBorderBrush;
        }
        Style(_r7, ChartRange.Week);
        Style(_r30, ChartRange.Month);
        Style(_rAll, ChartRange.All);
    }

    private static void Place(Control c, double x, double y, double w, double h)
    {
        SetLeft(c, x);
        SetTop(c, y);
        c.Width = Math.Max(0, w);
        c.Height = Math.Max(0, h);
    }

    private void DoLayout()
    {
        const double pad = 20, gap = 10;
        double width = Bounds.Width, height = Bounds.Height;
        double x = pad, w = width - pad * 2;
        if (w <= 0 || height <= 0) return;

        Place(_empty, 0, 0, width, height);

        double y = pad;
        Place(_hero, x, y, w, 116);
        y += 116 + 12;

        double tileW = (w - gap * 4) / 5;
        const double tileH = 64;
        Place(_tWords, x, y, tileW, tileH);
        Place(_tDictations, x + (tileW + gap), y, tileW, tileH);
        Place(_tAvg, x + (tileW + gap) * 2, y, tileW, tileH);
        Place(_tWpm, x + (tileW + gap) * 3, y, tileW, tileH);
        Place(_tSpeaking, x + (tileW + gap) * 4, y, tileW, tileH);
        y += tileH + 12;

        const double recordsH = 22;
        double colsTop = y;
        double colsBottom = height - pad - recordsH - 8;
        double colsH = Math.Max(80, colsBottom - colsTop);
        double chartW = (w - gap) * 0.63;
        double appsW = w - gap - chartW;
        Place(_chart, x, colsTop, chartW, colsH);

        // Range toggle in the chart card's top-right corner.
        const double segGap = 4, segH = 22, numW = 32, allW = 40;
        double segY = colsTop + 9;
        double segRight = x + chartW - 12;
        Place(_rAll, segRight - allW, segY, allW, segH);
        Place(_r30, segRight - allW - segGap - numW, segY, numW, segH);
        Place(_r7, segRight - allW - (segGap + numW) * 2, segY, numW, segH);

        Place(_apps, x + chartW + gap, colsTop, appsW, colsH);
        Place(_records, x, colsBottom + 8, w, recordsH);
    }
}
