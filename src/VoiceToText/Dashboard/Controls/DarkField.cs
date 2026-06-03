using System.Drawing;

namespace VoiceToText.Dashboard.Controls;

/// <summary>A rounded dark input field that wraps one borderless child control (e.g. a TextBox).</summary>
internal sealed class DarkField : Panel
{
    private const int Radius = 6;
    private readonly Control _inner;

    public DarkField(Control inner, int width, int height = 30)
    {
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint
               | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        BackColor = Theme.CardBg;
        _inner = inner;
        _inner.BackColor = Theme.InputBg;
        _inner.ForeColor = Theme.TextPrimary;
        Controls.Add(_inner);
        Size = new Size(width, height); // after _inner exists, so the OnLayout pass this triggers is safe
    }

    protected override void OnLayout(LayoutEventArgs e)
    {
        base.OnLayout(e);
        // Multiline text boxes fill the field with a small inset; single-line controls sit centered.
        if (_inner is TextBox { Multiline: true })
        {
            _inner.SetBounds(10, 6, Math.Max(10, Width - 20), Math.Max(10, Height - 12));
            return;
        }
        int ih = _inner.PreferredSize.Height > 0 ? _inner.PreferredSize.Height : _inner.Height;
        _inner.SetBounds(10, Math.Max(1, (Height - ih) / 2), Math.Max(10, Width - 20), ih);
    }

    protected override void OnPaint(PaintEventArgs e) => Theme.PaintField(e.Graphics, ClientRectangle, BackColor, Radius);
}
