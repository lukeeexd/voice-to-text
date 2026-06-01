using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;

namespace VoiceToText.Dashboard.Controls;

/// <summary>Horizontal "top apps" breakdown: name + word count over a filled track.</summary>
internal sealed class BreakdownBars : Control
{
    private IReadOnlyList<AppBar> _apps = Array.Empty<AppBar>();

    public BreakdownBars()
    {
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint
               | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        BackColor = Theme.WindowBg;
    }

    public void SetData(IReadOnlyList<AppBar> apps)
    {
        _apps = apps;
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
        g.DrawString("Top apps", Theme.Caption, titleBrush, 14, 12);

        if (_apps.Count == 0) return;

        using var nameBrush = new SolidBrush(Theme.TextPrimary);
        using var countBrush = new SolidBrush(Theme.TextSecondary);
        using var trackBrush = new SolidBrush(Theme.CardBorder);
        using var fillBrush = new LinearGradientBrush(
            new Rectangle(0, 0, Math.Max(1, Width), 10), Theme.Accent, Theme.AccentLight, LinearGradientMode.Horizontal);

        const int x = 14, top = 40, rowH = 30, trackH = 7, rightPad = 14;
        int trackW = Width - x - rightPad;
        if (trackW <= 0) return;

        for (int i = 0; i < _apps.Count; i++)
        {
            int y = top + i * rowH;
            if (y + rowH > Height) break;
            var a = _apps[i];
            g.DrawString(a.Name, Theme.Caption, nameBrush, x, y);
            var countStr = a.Words.ToString("N0");
            var cs = g.MeasureString(countStr, Theme.Caption);
            g.DrawString(countStr, Theme.Caption, countBrush, x + trackW - cs.Width, y);

            int ty = y + 18;
            g.FillRectangle(trackBrush, x, ty, trackW, trackH);
            int fillW = (int)Math.Round(trackW * Math.Clamp(a.Fraction, 0, 1));
            if (fillW > 0) g.FillRectangle(fillBrush, x, ty, fillW, trackH);
        }
    }
}
