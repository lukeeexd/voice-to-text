using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;

namespace VoiceToText.Dashboard.Controls;

/// <summary>
/// A dark, flat, rounded combo box. Keeps all ComboBox behavior; self-draws the dropdown items
/// dark (OnDrawItem) and repaints the closed control's border + dropdown button + chevron dark
/// (WM_PAINT), covering the light system chrome. Rounded via a clipping Region.
/// </summary>
internal sealed class DarkComboBox : ComboBox
{
    private const int Radius = 6;
    private const int ButtonW = 22;

    public DarkComboBox()
    {
        DropDownStyle = ComboBoxStyle.DropDownList;
        FlatStyle = FlatStyle.Flat;
        DrawMode = DrawMode.OwnerDrawFixed;
        BackColor = Theme.InputBg;
        ForeColor = Theme.TextPrimary;
        ItemHeight = 22;
        Height = 30;
    }

    protected override void OnSizeChanged(EventArgs e)
    {
        base.OnSizeChanged(e);
        var old = Region;
        using var path = Theme.RoundedRect(new Rectangle(0, 0, Width, Height), Radius);
        Region = new Region(path);
        old?.Dispose(); // don't leak the previous HRGN on each resize
    }

    protected override void OnDrawItem(DrawItemEventArgs e)
    {
        if (e.Index < 0) { e.DrawBackground(); return; }
        bool selected = (e.State & DrawItemState.Selected) != 0;
        using var back = new SolidBrush(selected ? Theme.NavActiveBg : Theme.InputBg);
        e.Graphics.FillRectangle(back, e.Bounds);
        using var text = new SolidBrush(selected ? Theme.NavActiveText : Theme.TextPrimary);
        using var fmt = new StringFormat { LineAlignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap };
        var rect = new Rectangle(e.Bounds.X + 6, e.Bounds.Y, e.Bounds.Width - 8, e.Bounds.Height);
        e.Graphics.DrawString(GetItemText(Items[e.Index]), e.Font ?? Font, text, rect, fmt);
    }

    protected override void WndProc(ref Message m)
    {
        base.WndProc(ref m);
        const int WM_PAINT = 0x000F;
        if (m.Msg == WM_PAINT)
            PaintChrome();
    }

    // Fully own the closed-control face so no light native chrome (border/dropdown button) shows
    // through — in particular the corner specks the rounded Region would otherwise expose. The
    // control's rounded Region clips this DC, so the fill keeps its rounded shape (corners show the
    // parent card). We repaint the selected text ourselves since the fill covers what the base drew.
    private void PaintChrome()
    {
        using var g = CreateGraphics();
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

        // Paint the whole face InputBg, covering the native border + dropdown button.
        using (var fill = new SolidBrush(Theme.InputBg))
            g.FillRectangle(fill, ClientRectangle);

        // Selected text on the left.
        if (SelectedItem is not null)
        {
            using var tb = new SolidBrush(Enabled ? Theme.TextPrimary : Theme.TextMuted);
            using var tf = new StringFormat { LineAlignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap };
            g.DrawString(GetItemText(SelectedItem), Font, tb, new Rectangle(10, 0, Width - ButtonW - 12, Height), tf);
        }

        // Chevron on the right.
        using (var cb = new SolidBrush(Theme.TextSecondary))
        using (var cf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
            g.DrawString("▾", Font, cb, new Rectangle(Width - ButtonW, 0, ButtonW, Height), cf);

        // Rounded border on top.
        using (var path = Theme.RoundedRect(new Rectangle(0, 0, Width - 1, Height - 1), Radius))
        using (var pen = new Pen(Theme.InputBorder))
            g.DrawPath(pen, path);
    }
}
