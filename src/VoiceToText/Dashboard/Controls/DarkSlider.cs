using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace VoiceToText.Dashboard.Controls;

/// <summary>
/// A premium, fully self-drawn dark slider (a native TrackBar is stock/ugly). Exposes a single
/// <see cref="Value"/> in 0..1 with a <see cref="ValueChanged"/> event. Used for the sound-cue
/// volume in Settings. Paints entirely in OnPaint (double-buffered, UserPaint) and fills its own
/// bounds — no Region, no CreateGraphics. Ignores mouse and draws muted when disabled.
/// </summary>
internal sealed class DarkSlider : Control
{
    private const int TrackHeight = 4;
    private const int ThumbRadius = 7;

    private double _value;
    private bool _dragging;

    public DarkSlider()
    {
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint
               | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        Cursor = Cursors.Hand;
        Size = new Size(160, 24); // set LAST (no child fields, but keep the ctor-order rule)
    }

    /// <summary>The slider position, clamped to 0..1. Raises <see cref="ValueChanged"/> on change.</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public double Value
    {
        get => _value;
        set => SetValue(value, raise: true);
    }

    public event EventHandler? ValueChanged;

    private void SetValue(double v, bool raise)
    {
        v = Math.Clamp(v, 0.0, 1.0);
        bool changed = v != _value;
        _value = v;
        if (changed)
        {
            Invalidate();
            if (raise) ValueChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    // Map a value in 0..1 to the thumb center X, padded by the thumb radius so 0 and 1 are reachable.
    private float ValueToX(double v) => ThumbRadius + (float)v * Math.Max(1, Width - 2 * ThumbRadius);

    // Inverse: map a mouse X to a value in 0..1.
    private double XToValue(int x) =>
        Math.Clamp((x - ThumbRadius) / (double)Math.Max(1, Width - 2 * ThumbRadius), 0.0, 1.0);

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (!Enabled || e.Button != MouseButtons.Left) return;
        _dragging = true;
        Capture = true;
        SetValue(XToValue(e.X), raise: true);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_dragging && Enabled)
            SetValue(XToValue(e.X), raise: true);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return; // ignore non-left releases (no stray preview cue)
        if (_dragging)
        {
            _dragging = false;
            Capture = false;
        }
        base.OnMouseUp(e); // the standard MouseUp then fires — the host uses it for the preview cue
    }

    protected override void OnEnabledChanged(EventArgs e)
    {
        base.OnEnabledChanged(e);
        Cursor = Enabled ? Cursors.Hand : Cursors.Default;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        // Fill our own bounds with the parent surface — never g.Clear().
        var parentBg = Parent?.BackColor ?? BackColor;
        using (var bg = new SolidBrush(parentBg))
            g.FillRectangle(bg, ClientRectangle);

        float cy = Height / 2f;
        float trackLeft = ThumbRadius;
        float trackRight = Width - ThumbRadius;
        float thumbX = ValueToX(_value);

        var trackColor = Enabled ? Theme.InputBorder : Theme.TextMuted;
        var fillColor = Enabled ? Theme.Accent : Theme.TextMuted;
        var thumbColor = Enabled ? Theme.AccentLight : Theme.InputBg;
        var thumbBorder = Enabled ? Theme.AccentDeep : Theme.InputBorder;

        // Background track (full width).
        var trackRect = new RectangleF(trackLeft, cy - TrackHeight / 2f, trackRight - trackLeft, TrackHeight);
        using (var tb = new SolidBrush(trackColor))
            FillRoundedF(g, tb, trackRect, TrackHeight / 2f);

        // Filled portion (left of the thumb).
        var fillRect = new RectangleF(trackLeft, cy - TrackHeight / 2f, thumbX - trackLeft, TrackHeight);
        if (fillRect.Width > 0.5f)
            using (var fb = new SolidBrush(fillColor))
                FillRoundedF(g, fb, fillRect, TrackHeight / 2f);

        // Circular thumb.
        var thumbRect = new RectangleF(thumbX - ThumbRadius, cy - ThumbRadius, ThumbRadius * 2, ThumbRadius * 2);
        using (var thb = new SolidBrush(thumbColor))
            g.FillEllipse(thb, thumbRect);
        using (var tp = new Pen(thumbBorder))
            g.DrawEllipse(tp, thumbRect);
    }

    // Fill a rounded-rect (float) — used for the thin track so its caps are round.
    private static void FillRoundedF(Graphics g, Brush brush, RectangleF r, float radius)
    {
        radius = Math.Min(radius, Math.Min(r.Width, r.Height) / 2f);
        using var path = new GraphicsPath();
        float d = radius * 2;
        if (d <= 0 || r.Width < d)
        {
            g.FillRectangle(brush, r);
            return;
        }
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        g.FillPath(brush, path);
    }
}
