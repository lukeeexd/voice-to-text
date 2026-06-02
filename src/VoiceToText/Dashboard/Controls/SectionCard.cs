using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;

namespace VoiceToText.Dashboard.Controls;

/// <summary>
/// A rounded dark section card with an accent header. The host sets a fixed <see cref="Control.Width"/>
/// then calls <see cref="AddRow"/> for each setting; the card auto-sizes its height to its rows.
/// Rows lay out as: name label (left) + control (right) + optional hint (full-width, below).
/// </summary>
internal sealed class SectionCard : Control
{
    private const int Radius = 9;
    private const int HeaderH = 34;
    private const int PadX = 14;
    private const int PadBottom = 12;

    private readonly string _header;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public FlowLayoutPanel Content { get; }

    public SectionCard(string header)
    {
        _header = header.ToUpperInvariant();
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint
               | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        BackColor = Theme.WindowBg;

        Content = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            BackColor = Theme.CardBg,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
        };
        Content.SizeChanged += (_, _) => AdjustHeight();
        Controls.Add(Content);
    }

    /// <summary>Add one setting row: name on the left, control right-aligned, optional hint below.</summary>
    public void AddRow(string name, Control control, Label? hint = null)
    {
        const int topH = 38;
        int rowW = Math.Max(40, Content.ClientSize.Width);
        int rowH = topH + (hint is null ? 0 : 22);
        var row = new Panel { BackColor = Theme.CardBg, Width = rowW, Height = rowH, Margin = Padding.Empty };

        var label = new Label { Text = name, AutoSize = true, ForeColor = Theme.TextPrimary, Font = Theme.NavItem };
        label.Location = new Point(4, (topH - label.PreferredHeight) / 2);
        row.Controls.Add(label);

        control.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        control.Location = new Point(rowW - control.Width - 4, (topH - control.Height) / 2);
        row.Controls.Add(control);

        if (hint is not null)
        {
            hint.AutoSize = false;
            hint.Location = new Point(4, topH - 2);
            hint.Size = new Size(rowW - 8, 22);
            hint.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            row.Controls.Add(hint);
        }

        Content.Controls.Add(row);
    }

    private void AdjustHeight()
    {
        Content.SetBounds(PadX, HeaderH, Math.Max(0, Width - PadX * 2), Content.Height);
        Height = HeaderH + Content.Height + PadBottom;
    }

    protected override void OnSizeChanged(EventArgs e)
    {
        base.OnSizeChanged(e);
        Content.Width = Math.Max(0, Width - PadX * 2);
        AdjustHeight();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
        g.Clear(Theme.WindowBg);

        var r = new Rectangle(0, 0, Width - 1, Height - 1);
        using (var path = Theme.RoundedRect(r, Radius))
        using (var fill = new SolidBrush(Theme.CardBg))
        using (var pen = new Pen(Theme.CardBorder))
        {
            g.FillPath(fill, path);
            g.DrawPath(pen, path);
        }

        using var brush = new SolidBrush(Theme.Accent);
        g.DrawString(_header, Theme.Caption, brush, PadX, 11);
    }
}
