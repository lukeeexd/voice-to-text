using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using VoiceToText.Dashboard;

namespace VoiceToText.Linux.Ui.Dashboard;

/// <summary>Horizontal "top apps" breakdown: name + word count over a filled track.</summary>
internal sealed class VttBreakdownBars : Control
{
    private IReadOnlyList<AppBar> _apps = Array.Empty<AppBar>();

    public void SetData(IReadOnlyList<AppBar> apps)
    {
        _apps = apps;
        InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        var b = Bounds;
        var r = new Rect(0.5, 0.5, b.Width - 1, b.Height - 1);
        context.DrawRectangle(ThemeTokens.CardBgBrush, new Pen(ThemeTokens.CardBorderBrush), new RoundedRect(r, 9));

        context.DrawText(
            Draw.Text("Top apps", ThemeTokens.Regular, ThemeTokens.CaptionSize, ThemeTokens.TextSecondaryBrush),
            new Point(14, 12));

        if (_apps.Count == 0) return;

        const double x = 14, top = 40, rowH = 30, trackH = 7, rightPad = 14;
        double trackW = b.Width - x - rightPad;
        if (trackW <= 0) return;

        for (int i = 0; i < _apps.Count; i++)
        {
            double y = top + i * rowH;
            if (y + rowH > b.Height) break;
            var a = _apps[i];

            context.DrawText(
                Draw.Text(a.Name, ThemeTokens.Regular, ThemeTokens.CaptionSize, ThemeTokens.TextPrimaryBrush),
                new Point(x, y));
            var count = Draw.Text(a.Words.ToString("N0"), ThemeTokens.Regular, ThemeTokens.CaptionSize, ThemeTokens.TextSecondaryBrush);
            context.DrawText(count, new Point(x + trackW - count.Width, y));

            double ty = y + 18;
            context.FillRectangle(ThemeTokens.CardBorderBrush, new Rect(x, ty, trackW, trackH));
            double fillW = Math.Round(trackW * Math.Clamp(a.Fraction, 0, 1));
            if (fillW > 0)
            {
                // GDI+ maps the gradient across the FULL track width; clip the fill
                // to match (partial fills show only the left part of the ramp).
                using (context.PushClip(new Rect(x, ty, fillW, trackH)))
                    context.FillRectangle(ThemeTokens.TrackGradient, new Rect(x, ty, trackW, trackH));
            }
        }
    }
}
