using System.Drawing;
using VoiceToText.Dashboard.Controls;

namespace VoiceToText.Dashboard;

/// <summary>The Dashboard page: hero band, four stat tiles, the activity chart + top-apps
/// breakdown, and a records strip. Shows a centered empty state when there is no data.</summary>
internal sealed class DashboardPage : UserControl
{
    private readonly HeroPanel _hero = new();
    private readonly StatTile _tWords = new();
    private readonly StatTile _tDictations = new();
    private readonly StatTile _tAvg = new();
    private readonly StatTile _tWpm = new();
    private readonly StatTile _tSpeaking = new();
    private readonly BarChart _chart = new();
    private readonly BreakdownBars _apps = new();
    private readonly Button _r7 = MakeRangeButton("7");
    private readonly Button _r30 = MakeRangeButton("30");
    private readonly Button _rAll = MakeRangeButton("All");
    private ChartRange _range = ChartRange.Month;
    private DashboardModel? _model;
    private readonly Label _records = new()
    {
        AutoSize = false,
        ForeColor = Theme.TextSecondary,
        Font = Theme.Caption,
        BackColor = Theme.WindowBg,
        TextAlign = ContentAlignment.MiddleLeft,
    };
    private readonly Label _empty = new()
    {
        Dock = DockStyle.Fill,
        TextAlign = ContentAlignment.MiddleCenter,
        ForeColor = Theme.TextSecondary,
        Font = Theme.Empty,
        BackColor = Theme.WindowBg,
        Visible = false,
        Text = "No dictations yet — press your hotkey and start talking.",
    };

    public DashboardPage()
    {
        BackColor = Theme.WindowBg;
        DoubleBuffered = true;
        _r7.FlatAppearance.BorderColor = Theme.CardBorder;
        _r30.FlatAppearance.BorderColor = Theme.CardBorder;
        _rAll.FlatAppearance.BorderColor = Theme.CardBorder;
        _r7.Click += (_, _) => SetRange(ChartRange.Week);
        _r30.Click += (_, _) => SetRange(ChartRange.Month);
        _rAll.Click += (_, _) => SetRange(ChartRange.All);
        StyleRangeButtons(); // reflect the default (Month) active state from construction

        Controls.AddRange(new Control[]
        {
            _hero, _tWords, _tDictations, _tAvg, _tWpm, _tSpeaking, _chart, _apps, _records, _empty,
            _r7, _r30, _rAll,
        });
    }

    private static Button MakeRangeButton(string text) => new()
    {
        Text = text,
        FlatStyle = FlatStyle.Flat,
        Font = Theme.Caption,
        ForeColor = Theme.TextSecondary,
        BackColor = Theme.CardBg,
        TabStop = false,
    };

    public void Bind(DashboardModel m)
    {
        _empty.Visible = !m.HasData;
        foreach (var c in new Control[] { _hero, _tWords, _tDictations, _tAvg, _tWpm, _tSpeaking, _chart, _apps, _records, _r7, _r30, _rAll })
            c.Visible = m.HasData;
        if (!m.HasData) return;

        _hero.SetData(m.TimeSavedText, m.TimeSavedSubtext, m.Streak);
        _tWords.SetData(m.TotalWords.ToString("N0"), "Words dictated");
        _tDictations.SetData(m.TotalDictations.ToString("N0"), "Dictations");
        _tAvg.SetData(m.AvgWordsPerDictation.ToString("N0"), "Avg words/dictation");
        _tWpm.SetData(m.SpeakingWpm.ToString("N0"), "Speaking WPM");
        _tSpeaking.SetData(m.SpeakingTimeText, "Speaking time");
        _model = m;
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
            b.BackColor = on ? Theme.NavActiveBg : Theme.CardBg;
            b.ForeColor = on ? Theme.NavActiveText : Theme.TextSecondary;
            b.FlatAppearance.BorderColor = on ? Theme.Accent : Theme.CardBorder;
        }
        Style(_r7, ChartRange.Week);
        Style(_r30, ChartRange.Month);
        Style(_rAll, ChartRange.All);
    }

    protected override void OnSizeChanged(EventArgs e)
    {
        base.OnSizeChanged(e);
        DoLayout();
    }

    private void DoLayout()
    {
        const int pad = 20, gap = 10;
        int x = pad, w = Width - pad * 2;
        if (w <= 0 || Height <= 0) return;

        int y = pad;
        _hero.SetBounds(x, y, w, 116);
        y += 116 + 12;

        int tileW = (w - gap * 4) / 5;
        const int tileH = 64;
        _tWords.SetBounds(x, y, tileW, tileH);
        _tDictations.SetBounds(x + (tileW + gap), y, tileW, tileH);
        _tAvg.SetBounds(x + (tileW + gap) * 2, y, tileW, tileH);
        _tWpm.SetBounds(x + (tileW + gap) * 3, y, tileW, tileH);
        _tSpeaking.SetBounds(x + (tileW + gap) * 4, y, tileW, tileH);
        y += tileH + 12;

        const int recordsH = 22;
        int colsTop = y;
        int colsBottom = Height - pad - recordsH - 8;
        int colsH = Math.Max(80, colsBottom - colsTop);
        int chartW = (int)((w - gap) * 0.63);
        int appsW = w - gap - chartW;
        _chart.SetBounds(x, colsTop, chartW, colsH);
        // Range toggle in the chart card's top-right corner.
        const int segGap = 4, segH = 22, numW = 32, allW = 40;
        int segY = colsTop + 9;
        int segRight = x + chartW - 12;
        _rAll.SetBounds(segRight - allW, segY, allW, segH);
        _r30.SetBounds(_rAll.Left - segGap - numW, segY, numW, segH);
        _r7.SetBounds(_r30.Left - segGap - numW, segY, numW, segH);
        _r7.BringToFront();
        _r30.BringToFront();
        _rAll.BringToFront();
        _apps.SetBounds(x + chartW + gap, colsTop, appsW, colsH);
        _records.SetBounds(x, colsBottom + 8, w, recordsH);
    }
}
