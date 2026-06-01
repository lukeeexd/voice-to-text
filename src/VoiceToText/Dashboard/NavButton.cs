using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;

namespace VoiceToText.Dashboard;

/// <summary>A sidebar navigation item: a rounded pill that highlights when active or hovered.</summary>
internal sealed class NavButton : Control
{
    private bool _active;
    private bool _hover;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool Active
    {
        get => _active;
        set { _active = value; Invalidate(); }
    }

    public NavButton(string text)
    {
        Text = text;
        Height = 40;
        Cursor = Cursors.Hand;
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint
               | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        BackColor = Theme.SidebarBg;
    }

    protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Theme.SidebarBg);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

        var pill = new Rectangle(10, 4, Width - 20, Height - 8);
        if (_active)
        {
            using (var b = new SolidBrush(Theme.NavActiveBg))
            using (var p = Theme.RoundedRect(pill, 7))
                g.FillPath(b, p);
            using var accent = new SolidBrush(Theme.Accent);
            g.FillRectangle(accent, pill.X, pill.Y + 6, 3, pill.Height - 12);
        }
        else if (_hover)
        {
            using var b = new SolidBrush(Theme.NavHoverBg);
            using var p = Theme.RoundedRect(pill, 7);
            g.FillPath(b, p);
        }

        var color = _active ? Theme.NavActiveText : Theme.TextSecondary;
        using var tb = new SolidBrush(color);
        using var sf = new StringFormat { LineAlignment = StringAlignment.Center };
        g.DrawString(Text, Theme.NavItem, tb, new RectangleF(pill.X + 14, pill.Y, pill.Width - 14, pill.Height), sf);
    }
}
