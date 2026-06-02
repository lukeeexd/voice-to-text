using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace VoiceToText.Dashboard.Controls;

/// <summary>A small on/off toggle switch used in place of a checkbox: accent track when on,
/// muted when off, greyed when disabled. Click or Space toggles it.</summary>
internal sealed class ToggleSwitch : Control
{
    private static readonly Color OffTrack = Color.FromArgb(0x3A, 0x3D, 0x47);
    private bool _checked;

    public ToggleSwitch()
    {
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint
               | ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
        Size = new Size(44, 24);
        Cursor = Cursors.Hand;
        TabStop = true;
        BackColor = Theme.CardBg; // toggles live inside cards; host may override
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool Checked
    {
        get => _checked;
        set
        {
            if (_checked == value) return;
            _checked = value;
            Invalidate();
            CheckedChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public event EventHandler? CheckedChanged;

    protected override void OnClick(EventArgs e)
    {
        base.OnClick(e);
        if (Enabled) { Focus(); Checked = !Checked; }
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (Focused && Enabled && keyData == Keys.Space)
        {
            Checked = !Checked;
            return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(BackColor);

        int h = Math.Min(Height, 22);
        int top = (Height - h) / 2;
        var track = new Rectangle(0, top, Math.Max(2, Width - 1), h - 1);

        Color trackColor = !Enabled ? Theme.CardBorder : _checked ? Theme.Accent : OffTrack;
        using (var path = Theme.RoundedRect(track, (h - 1) / 2))
        using (var fill = new SolidBrush(trackColor))
            g.FillPath(fill, path);

        int knob = h - 6;
        int kx = _checked ? track.Right - knob - 3 : track.Left + 3;
        using var knobBrush = new SolidBrush(Enabled ? Color.White : Theme.TextMuted);
        g.FillEllipse(knobBrush, kx, top + 3, knob, knob);
    }
}
