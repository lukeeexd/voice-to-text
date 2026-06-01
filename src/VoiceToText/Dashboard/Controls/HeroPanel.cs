using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;

namespace VoiceToText.Dashboard.Controls;

/// <summary>The "time saved" hero band: big value + label + subtext, with a streak on the right.</summary>
internal sealed class HeroPanel : Control
{
    private string _value = "—";
    private string _subtext = "";
    private int _streak;

    public HeroPanel()
    {
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint
               | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        BackColor = Theme.WindowBg;
    }

    public void SetData(string value, string subtext, int streak)
    {
        _value = value;
        _subtext = subtext;
        _streak = streak;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Theme.WindowBg);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

        var r = new Rectangle(0, 0, Width - 1, Height - 1);
        using (var path = Theme.RoundedRect(r, 10))
        using (var brush = new LinearGradientBrush(r, Theme.HeroFrom, Theme.HeroTo, LinearGradientMode.Horizontal))
        using (var pen = new Pen(Theme.HeroBorder))
        {
            g.FillPath(brush, path);
            g.DrawPath(pen, path);
        }

        using var accent = new SolidBrush(Theme.Accent);
        using var primary = new SolidBrush(Theme.TextPrimary);
        using var secondary = new SolidBrush(Theme.TextSecondary);
        using var gold = new SolidBrush(Theme.Gold);

        g.DrawString("TIME SAVED", Theme.Caption, accent, 20, 16);
        g.DrawString(_value, Theme.HeroNumber, primary, 18, 32);
        g.DrawString(_subtext, Theme.Caption, secondary, 20, Height - 26);

        var streak = $"{_streak}-day streak";
        var size = g.MeasureString(streak, Theme.LabelBold);
        g.DrawString(streak, Theme.LabelBold, gold, Width - size.Width - 24, (Height - size.Height) / 2f);
    }
}
