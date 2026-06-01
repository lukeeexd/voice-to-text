using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;

namespace VoiceToText.Dashboard.Controls;

/// <summary>Vertical bar chart of the daily-activity series, scaled to its max, on a card.</summary>
internal sealed class BarChart : Control
{
    private IReadOnlyList<DayBar> _series = Array.Empty<DayBar>();
    private long _max = 1;
    private string _title = "Activity";

    public BarChart()
    {
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint
               | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        BackColor = Theme.WindowBg;
    }

    public void SetData(IReadOnlyList<DayBar> series, long max, string title)
    {
        _series = series;
        _max = Math.Max(1, max);
        _title = title;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Theme.WindowBg);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

        var r = new Rectangle(0, 0, Width - 1, Height - 1);
        using (var path = Theme.RoundedRect(r, 9))
        using (var fill = new SolidBrush(Theme.CardBg))
        using (var pen = new Pen(Theme.CardBorder))
        {
            g.FillPath(fill, path);
            g.DrawPath(pen, path);
        }

        using var titleBrush = new SolidBrush(Theme.TextSecondary);
        g.DrawString(_title, Theme.Caption, titleBrush, 14, 12);

        if (_series.Count == 0) return;

        const int padL = 14, padR = 14, padTop = 36, padBottom = 26;
        var plot = new Rectangle(padL, padTop, Width - padL - padR, Height - padTop - padBottom);
        if (plot.Width <= 4 || plot.Height <= 4) return;

        int n = _series.Count;
        float slot = (float)plot.Width / n;
        float barW = Math.Max(2f, slot - 2f);

        using (var barBrush = new LinearGradientBrush(
            new Rectangle(0, plot.Top, Math.Max(1, Width), plot.Height),
            Theme.Accent, Theme.AccentDeep, LinearGradientMode.Vertical))
        {
            for (int i = 0; i < n; i++)
            {
                long words = _series[i].Words;
                float h = (float)words / _max * plot.Height;
                if (words > 0 && h < 2f) h = 2f; // keep tiny non-zero days visible
                if (h <= 0f) continue;
                float x = plot.Left + i * slot + (slot - barW) / 2f;
                float y = plot.Bottom - h;
                g.FillRectangle(barBrush, x, y, barW, h);
            }
        }

        using var axisBrush = new SolidBrush(Theme.TextMuted);
        var left = _series[0].Date.ToString("MMM d");
        var mid = _series[n / 2].Date.ToString("MMM d");
        g.DrawString(left, Theme.Caption, axisBrush, plot.Left, plot.Bottom + 6);
        var midSize = g.MeasureString(mid, Theme.Caption);
        g.DrawString(mid, Theme.Caption, axisBrush, plot.Left + plot.Width / 2f - midSize.Width / 2f, plot.Bottom + 6);
        var todaySize = g.MeasureString("Today", Theme.Caption);
        g.DrawString("Today", Theme.Caption, axisBrush, plot.Right - todaySize.Width, plot.Bottom + 6);
    }
}
