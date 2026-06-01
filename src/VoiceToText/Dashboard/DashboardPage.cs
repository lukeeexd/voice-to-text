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
    private readonly BarChart _chart = new();
    private readonly BreakdownBars _apps = new();
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
        Controls.AddRange(new Control[]
        {
            _hero, _tWords, _tDictations, _tAvg, _tWpm, _chart, _apps, _records, _empty,
        });
    }

    public void Bind(DashboardModel m)
    {
        _empty.Visible = !m.HasData;
        foreach (var c in new Control[] { _hero, _tWords, _tDictations, _tAvg, _tWpm, _chart, _apps, _records })
            c.Visible = m.HasData;
        if (!m.HasData) return;

        _hero.SetData(m.TimeSavedText, m.TimeSavedSubtext, m.Streak);
        _tWords.SetData(m.TotalWords.ToString("N0"), "Words dictated");
        _tDictations.SetData(m.TotalDictations.ToString("N0"), "Dictations");
        _tAvg.SetData(m.AvgWordsPerDictation.ToString("N0"), "Avg words/dictation");
        _tWpm.SetData(m.SpeakingWpm.ToString("N0"), "Speaking WPM");
        _chart.SetData(m.DailySeries, m.DailyMax);
        _apps.SetData(m.TopApps);

        var best = m.BestDictationText is null ? "" : $"Best dictation: {m.BestDictationText}";
        var busy = m.BusiestDayText is null ? "" : $"        Busiest day: {m.BusiestDayText}";
        _records.Text = best + busy;

        DoLayout();
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
        _hero.SetBounds(x, y, w, 96);
        y += 96 + 12;

        int tileW = (w - gap * 3) / 4;
        const int tileH = 64;
        _tWords.SetBounds(x, y, tileW, tileH);
        _tDictations.SetBounds(x + (tileW + gap), y, tileW, tileH);
        _tAvg.SetBounds(x + (tileW + gap) * 2, y, tileW, tileH);
        _tWpm.SetBounds(x + (tileW + gap) * 3, y, tileW, tileH);
        y += tileH + 12;

        const int recordsH = 22;
        int colsTop = y;
        int colsBottom = Height - pad - recordsH - 8;
        int colsH = Math.Max(80, colsBottom - colsTop);
        int chartW = (int)((w - gap) * 0.63);
        int appsW = w - gap - chartW;
        _chart.SetBounds(x, colsTop, chartW, colsH);
        _apps.SetBounds(x + chartW + gap, colsTop, appsW, colsH);
        _records.SetBounds(x, colsBottom + 8, w, recordsH);
    }
}
