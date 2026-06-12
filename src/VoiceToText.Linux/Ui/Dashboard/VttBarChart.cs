using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using VoiceToText.Dashboard;

namespace VoiceToText.Linux.Ui.Dashboard;

/// <summary>Vertical bar chart of the daily-activity series, scaled to its max, on a card.</summary>
internal sealed class VttBarChart : Control
{
    private IReadOnlyList<DayBar> _series = Array.Empty<DayBar>();
    private long _max = 1;
    private string _title = "Activity";

    public void SetData(IReadOnlyList<DayBar> series, long max, string title)
    {
        _series = series;
        _max = Math.Max(1, max);
        _title = title;
        InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        var b = Bounds;
        var r = new Rect(0.5, 0.5, b.Width - 1, b.Height - 1);
        context.DrawRectangle(ThemeTokens.CardBgBrush, new Pen(ThemeTokens.CardBorderBrush), new RoundedRect(r, 9));

        context.DrawText(
            Draw.Text(_title, ThemeTokens.Regular, ThemeTokens.CaptionSize, ThemeTokens.TextSecondaryBrush),
            new Point(14, 12));

        if (_series.Count == 0) return;

        const double padL = 14, padR = 14, padTop = 36, padBottom = 26;
        var plot = new Rect(padL, padTop, b.Width - padL - padR, b.Height - padTop - padBottom);
        if (plot.Width <= 4 || plot.Height <= 4) return;

        int n = _series.Count;
        double slot = plot.Width / n;
        double barW = Math.Max(2, slot - 2);

        for (int i = 0; i < n; i++)
        {
            long words = _series[i].Words;
            double h = (double)words / _max * plot.Height;
            if (words > 0 && h < 2) h = 2; // keep tiny non-zero days visible
            if (h <= 0) continue;
            double x = plot.X + i * slot + (slot - barW) / 2;
            context.FillRectangle(ThemeTokens.BarGradient, new Rect(x, plot.Bottom - h, barW, h));
        }

        var axis = ThemeTokens.TextMutedBrush;
        var left = Draw.Text(_series[0].Date.ToString("MMM d"), ThemeTokens.Regular, ThemeTokens.CaptionSize, axis);
        var mid = Draw.Text(_series[n / 2].Date.ToString("MMM d"), ThemeTokens.Regular, ThemeTokens.CaptionSize, axis);
        var today = Draw.Text("Today", ThemeTokens.Regular, ThemeTokens.CaptionSize, axis);
        context.DrawText(left, new Point(plot.X, plot.Bottom + 6));
        context.DrawText(mid, new Point(plot.X + plot.Width / 2 - mid.Width / 2, plot.Bottom + 6));
        context.DrawText(today, new Point(plot.Right - today.Width, plot.Bottom + 6));
    }
}
