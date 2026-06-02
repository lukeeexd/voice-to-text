using System.Drawing;
using System.Drawing.Drawing2D;

namespace VoiceToText.Dashboard;

/// <summary>Central dark/blue palette, fonts, and a rounded-rect helper for the dashboard.
/// Fonts live for the process lifetime (never disposed) — intentional for a shared theme.</summary>
internal static class Theme
{
    public static readonly Color WindowBg      = Color.FromArgb(0x17, 0x18, 0x1C);
    public static readonly Color SidebarBg     = Color.FromArgb(0x12, 0x13, 0x17);
    public static readonly Color CardBg        = Color.FromArgb(0x20, 0x22, 0x29);
    public static readonly Color CardBorder    = Color.FromArgb(0x2C, 0x2E, 0x36);
    public static readonly Color Accent        = Color.FromArgb(0x4C, 0x8D, 0xFF);
    public static readonly Color AccentLight   = Color.FromArgb(0x6A, 0xA0, 0xFF);
    public static readonly Color AccentDeep    = Color.FromArgb(0x27, 0x45, 0x7E);
    public static readonly Color HeroFrom      = Color.FromArgb(0x1D, 0x28, 0x40);
    public static readonly Color HeroTo        = Color.FromArgb(0x1B, 0x20, 0x30);
    public static readonly Color HeroBorder    = Color.FromArgb(0x2B, 0x35, 0x50);
    public static readonly Color NavActiveBg   = Color.FromArgb(0x22, 0x2B, 0x3D);
    public static readonly Color NavHoverBg    = Color.FromArgb(0x1B, 0x1C, 0x22);
    public static readonly Color NavActiveText = Color.FromArgb(0xCF, 0xE0, 0xFF);
    public static readonly Color TextPrimary   = Color.FromArgb(0xE8, 0xE9, 0xED);
    public static readonly Color TextSecondary = Color.FromArgb(0x8A, 0x8C, 0x95);
    public static readonly Color TextMuted     = Color.FromArgb(0x54, 0x56, 0x5F);
    public static readonly Color Gold          = Color.FromArgb(0xFF, 0xCE, 0x6B);
    public static readonly Color Warning       = Color.FromArgb(0xE0, 0x9A, 0x3A);
    public static readonly Color InputBg     = Color.FromArgb(0x2A, 0x2C, 0x34);
    public static readonly Color InputBorder = Color.FromArgb(0x3A, 0x3D, 0x47);

    public static readonly Font HeroNumber = new("Segoe UI", 30f, FontStyle.Bold);
    public static readonly Font TileNumber = new("Segoe UI", 18f, FontStyle.Bold);
    public static readonly Font LabelBold  = new("Segoe UI", 10f, FontStyle.Bold);
    public static readonly Font Caption    = new("Segoe UI", 8.5f, FontStyle.Regular);
    public static readonly Font NavItem    = new("Segoe UI", 10.5f, FontStyle.Regular);
    public static readonly Font Heading    = new("Segoe UI", 14f, FontStyle.Bold);
    public static readonly Font Brand      = new("Segoe UI", 11.5f, FontStyle.Bold);
    public static readonly Font Empty      = new("Segoe UI", 11f, FontStyle.Regular);

    /// <summary>Paint a rounded dark input field: clear to the parent bg, fill InputBg, stroke InputBorder.</summary>
    public static void PaintField(Graphics g, Rectangle bounds, Color parentBg, int radius = 6)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(parentBg);
        var r = new Rectangle(bounds.X, bounds.Y, bounds.Width - 1, bounds.Height - 1);
        using var path = RoundedRect(r, radius);
        using var fill = new SolidBrush(InputBg);
        using var pen = new Pen(InputBorder);
        g.FillPath(fill, path);
        g.DrawPath(pen, path);
    }

    /// <summary>A rounded-rectangle path. Caller disposes (use `using`).</summary>
    public static GraphicsPath RoundedRect(Rectangle r, int radius)
    {
        var path = new GraphicsPath();
        int d = radius * 2;
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }
}
