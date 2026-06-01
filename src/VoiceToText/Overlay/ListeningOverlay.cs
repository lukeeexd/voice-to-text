using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace VoiceToText.Overlay;

/// <summary>
/// A borderless, top-most, no-activate layered window showing a draggable "pill":
/// mic + live level bars while recording, pencil + bouncing dots while transcribing.
/// Never takes keyboard focus, so dictation still lands in the focused app.
/// </summary>
internal sealed class ListeningOverlay : Form
{
    private const int W = 230;
    private const int H = 64;
    private const int Bars = 14;

    private const int WM_NCHITTEST = 0x0084;
    private const int HTCAPTION = 2;
    private const int WM_EXITSIZEMOVE = 0x0232;

    private readonly LevelMeter _meter = new(Bars);
    private readonly System.Windows.Forms.Timer _timer;
    private volatile float _level;
    private OverlayState _state = OverlayState.Hidden;
    private int _frame;
    private int _alpha;        // current fade alpha (0..255)
    private int _targetAlpha;  // 0 or 255

    /// <summary>Raised after the user finishes dragging, with the new top-left screen location.</summary>
    public event Action<Point>? PositionChanged;

    public ListeningOverlay(Point? savedPosition)
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        Size = new Size(W, H);
        TopMost = true;
        Location = savedPosition ?? DefaultLocation();

        _timer = new System.Windows.Forms.Timer { Interval = 33 }; // ~30 fps
        _timer.Tick += (_, _) => OnFrame();
    }

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= NativeOverlay.WS_EX_LAYERED | NativeOverlay.WS_EX_TOPMOST
                        | NativeOverlay.WS_EX_NOACTIVATE | NativeOverlay.WS_EX_TOOLWINDOW;
            return cp;
        }
    }

    private static Point DefaultLocation()
    {
        var wa = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1920, 1080);
        return new Point(wa.Left + (wa.Width - W) / 2, wa.Bottom - H - 72);
    }

    /// <summary>Latest raw RMS from the capture thread (volatile; read by the render timer).</summary>
    public void SetLevel(float level) => _level = level;

    /// <summary>Change visual state. Call on the UI thread.</summary>
    public void SetState(OverlayState state)
    {
        if (state == _state) return;
        _state = state;

        if (state == OverlayState.Hidden)
        {
            _targetAlpha = 0; // fade out; OnFrame hides when fully faded
            return;
        }

        if (state == OverlayState.Recording)
            _meter.Reset();
        _targetAlpha = 255;
        if (!Visible)
        {
            _alpha = 0;
            Visible = true; // WS_EX_NOACTIVATE + ShowWithoutActivation => no focus theft
        }
        if (!_timer.Enabled)
            _timer.Start();
    }

    private void OnFrame()
    {
        _frame++;
        const int step = 28;
        if (_alpha < _targetAlpha) _alpha = Math.Min(_targetAlpha, _alpha + step);
        else if (_alpha > _targetAlpha) _alpha = Math.Max(_targetAlpha, _alpha - step);

        if (_alpha == 0 && _targetAlpha == 0)
        {
            _timer.Stop();
            if (Visible) Visible = false;
            return;
        }

        try
        {
            using var bmp = Render();
            NativeOverlay.SetBitmap(Handle, bmp, Left, Top, (byte)_alpha);
        }
        catch
        {
            // Overlay is cosmetic — never let a render error escape.
        }
    }

    private Bitmap Render()
    {
        var bmp = new Bitmap(W, H, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Color.Transparent);

        var pill = new Rectangle(10, 12, W - 20, H - 28);
        var accent = Color.FromArgb(95, 165, 250);

        using (var shadow = RoundedRect(new Rectangle(pill.X, pill.Y + 3, pill.Width, pill.Height), pill.Height))
        using (var shadowBrush = new SolidBrush(Color.FromArgb(70, 0, 0, 0)))
            g.FillPath(shadowBrush, shadow);

        using (var path = RoundedRect(pill, pill.Height))
        {
            using var body = new SolidBrush(Color.FromArgb(238, 17, 21, 28));
            g.FillPath(body, path);
            using var border = new Pen(Color.FromArgb(150, 95, 165, 250));
            g.DrawPath(border, path);
        }

        var icon = new Rectangle(pill.X + 16, pill.Y + pill.Height / 2 - 9, 18, 18);
        var content = Rectangle.FromLTRB(icon.Right + 10, pill.Y, pill.Right - 16, pill.Bottom);

        if (_state == OverlayState.Transcribing)
        {
            DrawPencil(g, icon, accent);
            DrawDots(g, content, accent);
        }
        else
        {
            DrawMic(g, icon, accent);
            DrawBars(g, content, accent);
        }
        return bmp;
    }

    private void DrawBars(Graphics g, Rectangle area, Color color)
    {
        var heights = _meter.Update(_level);
        var n = heights.Length;
        const float barW = 3f;
        var gap = Math.Max(1f, (area.Width - n * barW) / (n - 1));
        var maxH = area.Height - 6;
        using var brush = new SolidBrush(color);
        for (var i = 0; i < n; i++)
        {
            var h = Math.Max(3f, heights[i] * maxH);
            var x = area.X + i * (barW + gap);
            var y = area.Y + (area.Height - h) / 2f;
            using var p = RoundedRectF(new RectangleF(x, y, barW, h), barW / 2f);
            g.FillPath(brush, p);
        }
    }

    private void DrawDots(Graphics g, Rectangle area, Color color)
    {
        using var brush = new SolidBrush(color);
        const float r = 3f, spacing = 13f;
        var startX = area.X + (area.Width - spacing * 2) / 2f;
        var cy = area.Y + area.Height / 2f;
        for (var i = 0; i < 3; i++)
        {
            var dy = (float)(-3 * Math.Max(0, Math.Sin(_frame * 0.18 + i * 0.7)));
            var x = startX + i * spacing;
            g.FillEllipse(brush, x - r, cy + dy - r, r * 2, r * 2);
        }
    }

    private static void DrawMic(Graphics g, Rectangle r, Color color)
    {
        using var pen = new Pen(color, 2f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        using var brush = new SolidBrush(color);
        var bodyW = r.Width * 0.42f;
        var bodyH = r.Height * 0.56f;
        var bx = r.X + (r.Width - bodyW) / 2f;
        var by = r.Y + r.Height * 0.05f;
        using (var body = RoundedRectF(new RectangleF(bx, by, bodyW, bodyH), bodyW / 2f))
            g.FillPath(brush, body);
        var arc = new RectangleF(r.X + r.Width * 0.12f, r.Y + r.Height * 0.30f, r.Width * 0.76f, r.Height * 0.56f);
        g.DrawArc(pen, arc, 20, 140);
        var cx = r.X + r.Width / 2f;
        g.DrawLine(pen, cx, r.Y + r.Height * 0.80f, cx, r.Bottom - 1);
        g.DrawLine(pen, cx - 4, r.Bottom - 1, cx + 4, r.Bottom - 1);
    }

    // A filled pencil leaning like it's writing: rounded body (eraser end) + a
    // triangular graphite tip pointing down-left. Drawn in a rotated frame.
    private static void DrawPencil(Graphics g, Rectangle r, Color color)
    {
        using var brush = new SolidBrush(color);
        var state = g.Save();
        g.TranslateTransform(r.X + r.Width / 2f, r.Y + r.Height / 2f);
        g.RotateTransform(-45f); // tip points down-left
        const float w = 5.5f, half = w / 2f;
        // Vertical pencil centered on the origin: rounded body (top = eraser),
        // triangular tip at the bottom.
        using (var bodyPath = RoundedRectF(new RectangleF(-half, -8f, w, 10f), 1.6f))
            g.FillPath(brush, bodyPath);
        g.FillPolygon(brush, new[]
        {
            new PointF(-half, 2f),
            new PointF(half, 2f),
            new PointF(0f, 8f),
        });
        g.Restore(state);
    }

    private static GraphicsPath RoundedRect(Rectangle r, int diameter) => RoundedRectF(r, diameter / 2f);

    private static GraphicsPath RoundedRectF(RectangleF r, float radius)
    {
        var d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
        var path = new GraphicsPath();
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    protected override void OnPaintBackground(PaintEventArgs e) { /* painted via UpdateLayeredWindow */ }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WM_NCHITTEST)
        {
            m.Result = (IntPtr)HTCAPTION; // whole pill is a drag handle; no click action
            return;
        }
        if (m.Msg == WM_EXITSIZEMOVE)
            PositionChanged?.Invoke(Location);
        base.WndProc(ref m);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            _timer.Dispose();
        base.Dispose(disposing);
    }
}
