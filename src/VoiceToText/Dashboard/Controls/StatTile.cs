using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;

namespace VoiceToText.Dashboard.Controls;

/// <summary>A card showing a big number and a caption beneath it.</summary>
internal sealed class StatTile : Control
{
    private string _number = "—";
    private string _caption = "";

    public StatTile()
    {
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint
               | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        BackColor = Theme.WindowBg;
    }

    public void SetData(string number, string caption)
    {
        _number = number;
        _caption = caption;
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

        using var numBrush = new SolidBrush(Theme.TextPrimary);
        using var capBrush = new SolidBrush(Theme.TextSecondary);
        g.DrawString(_number, Theme.TileNumber, numBrush, 13, 10);
        g.DrawString(_caption, Theme.Caption, capBrush, 14, 42);
    }
}
