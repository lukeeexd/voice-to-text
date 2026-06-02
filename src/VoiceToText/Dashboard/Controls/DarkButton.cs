using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;

namespace VoiceToText.Dashboard.Controls;

/// <summary>Which look a <see cref="DarkButton"/> wears.</summary>
internal enum DarkButtonVariant
{
    /// <summary>A filled accent button for the primary action.</summary>
    Primary,
    /// <summary>A subtle outlined button for secondary actions.</summary>
    Secondary,
}

/// <summary>
/// A premium, fully self-drawn rounded button matching the dark input controls (6px radius,
/// InputBg/InputBorder family). Still a <see cref="Button"/>, so Click and all behavior are intact.
/// Two looks via <see cref="Variant"/>: a filled accent Primary and an outlined Secondary, each with
/// hover/pressed/disabled states. Paints entirely in OnPaint (double-buffered, UserPaint) and blends
/// its rounded corners with the parent background — no Region, no CreateGraphics.
/// </summary>
internal sealed class DarkButton : Button
{
    private const int Radius = 6;

    private DarkButtonVariant _variant = DarkButtonVariant.Primary;
    private bool _hover;
    private bool _pressed;

    public DarkButton()
    {
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint
               | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        Cursor = Cursors.Hand;
        Font = Theme.LabelBold;
        ForeColor = Color.White;
        BackColor = Theme.WindowBg;
        Size = new Size(96, 32);
    }

    /// <summary>Primary (filled accent) or Secondary (outlined). Default Primary.</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public DarkButtonVariant Variant
    {
        get => _variant;
        set { _variant = value; Invalidate(); }
    }

    protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hover = false; _pressed = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e) { if (e.Button == MouseButtons.Left) { _pressed = true; Invalidate(); } base.OnMouseDown(e); }
    protected override void OnMouseUp(MouseEventArgs e) { if (e.Button == MouseButtons.Left) { _pressed = false; Invalidate(); } base.OnMouseUp(e); }
    protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

        // Blend the rounded corners with whatever this button sits on (Save bar = WindowBg,
        // Browse panel = CardBg). Fill only our own bounds — never g.Clear().
        var parentBg = Parent?.BackColor ?? BackColor;
        using (var bg = new SolidBrush(parentBg))
            g.FillRectangle(bg, ClientRectangle);

        var bounds = new Rectangle(0, 0, Width - 1, Height - 1);
        using var path = Theme.RoundedRect(bounds, Radius);

        Color fill, text;
        Color? border = null;

        if (_variant == DarkButtonVariant.Primary)
        {
            fill = !Enabled ? Blend(Theme.AccentDeep, Theme.CardBg, 0.55f)
                 : _pressed ? Theme.AccentDeep
                 : _hover   ? Theme.AccentLight
                            : Theme.Accent;
            text = Enabled ? Color.White : Theme.TextMuted;
        }
        else // Secondary
        {
            fill = !Enabled ? Theme.CardBg
                 : _pressed ? Blend(Theme.InputBg, Color.Black, 0.18f)
                 : _hover   ? Theme.InputBg
                            : Theme.CardBg;
            border = Enabled ? Theme.InputBorder : Blend(Theme.InputBorder, Theme.CardBg, 0.5f);
            text = Enabled ? Theme.TextPrimary : Theme.TextMuted;
        }

        using (var fb = new SolidBrush(fill))
            g.FillPath(fb, path);
        if (border is { } bc)
            using (var pen = new Pen(bc))
                g.DrawPath(pen, path);

        TextRenderer.DrawText(g, Text, Font, ClientRectangle, text,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
    }

    // Linear blend toward <paramref name="b"/> by <paramref name="t"/> (0 = all a, 1 = all b).
    private static Color Blend(Color a, Color b, float t)
    {
        t = Math.Clamp(t, 0f, 1f);
        return Color.FromArgb(
            (int)(a.R + (b.R - a.R) * t),
            (int)(a.G + (b.G - a.G) * t),
            (int)(a.B + (b.B - a.B) * t));
    }
}
