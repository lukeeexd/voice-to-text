using Avalonia;
using Avalonia.Media;

namespace VoiceToText.Linux.Ui.Dashboard;

/// <summary>The Windows dashboard palette (Theme.cs) as Avalonia brushes, plus the
/// type scale. Fonts: the platform default family at Segoe-equivalent sizes.</summary>
internal static class ThemeTokens
{
    public static readonly Color WindowBg      = Color.FromRgb(0x17, 0x18, 0x1C);
    public static readonly Color SidebarBg     = Color.FromRgb(0x12, 0x13, 0x17);
    public static readonly Color CardBg        = Color.FromRgb(0x20, 0x22, 0x29);
    public static readonly Color CardBorder    = Color.FromRgb(0x2C, 0x2E, 0x36);
    public static readonly Color Accent        = Color.FromRgb(0x4C, 0x8D, 0xFF);
    public static readonly Color AccentLight   = Color.FromRgb(0x6A, 0xA0, 0xFF);
    public static readonly Color AccentDeep    = Color.FromRgb(0x27, 0x45, 0x7E);
    public static readonly Color HeroFrom      = Color.FromRgb(0x1D, 0x28, 0x40);
    public static readonly Color HeroTo        = Color.FromRgb(0x1B, 0x20, 0x30);
    public static readonly Color HeroBorder    = Color.FromRgb(0x2B, 0x35, 0x50);
    public static readonly Color NavActiveBg   = Color.FromRgb(0x22, 0x2B, 0x3D);
    public static readonly Color NavHoverBg    = Color.FromRgb(0x1B, 0x1C, 0x22);
    public static readonly Color NavActiveText = Color.FromRgb(0xCF, 0xE0, 0xFF);
    public static readonly Color TextPrimary   = Color.FromRgb(0xE8, 0xE9, 0xED);
    public static readonly Color TextSecondary = Color.FromRgb(0x8A, 0x8C, 0x95);
    public static readonly Color TextMuted     = Color.FromRgb(0x54, 0x56, 0x5F);
    public static readonly Color Gold          = Color.FromRgb(0xFF, 0xCE, 0x6B);
    public static readonly Color InputBg       = Color.FromRgb(0x2A, 0x2C, 0x34);
    public static readonly Color InputBorder   = Color.FromRgb(0x3A, 0x3D, 0x47);

    public static readonly IBrush WindowBgBrush      = new SolidColorBrush(WindowBg);
    public static readonly IBrush SidebarBgBrush     = new SolidColorBrush(SidebarBg);
    public static readonly IBrush CardBgBrush        = new SolidColorBrush(CardBg);
    public static readonly IBrush CardBorderBrush    = new SolidColorBrush(CardBorder);
    public static readonly IBrush AccentBrush        = new SolidColorBrush(Accent);
    public static readonly IBrush NavActiveBgBrush   = new SolidColorBrush(NavActiveBg);
    public static readonly IBrush NavHoverBgBrush    = new SolidColorBrush(NavHoverBg);
    public static readonly IBrush NavActiveTextBrush = new SolidColorBrush(NavActiveText);
    public static readonly IBrush TextPrimaryBrush   = new SolidColorBrush(TextPrimary);
    public static readonly IBrush TextSecondaryBrush = new SolidColorBrush(TextSecondary);
    public static readonly IBrush TextMutedBrush     = new SolidColorBrush(TextMuted);
    public static readonly IBrush GoldBrush          = new SolidColorBrush(Gold);
    public static readonly IBrush HeroBorderBrush    = new SolidColorBrush(HeroBorder);
    public static readonly IBrush InputBgBrush       = new SolidColorBrush(InputBg);
    public static readonly IBrush InputBorderBrush   = new SolidColorBrush(InputBorder);

    public static readonly IBrush HeroGradient = new LinearGradientBrush
    {
        StartPoint = new RelativePoint(0, 0.5, RelativeUnit.Relative),
        EndPoint = new RelativePoint(1, 0.5, RelativeUnit.Relative),
        GradientStops = { new GradientStop(HeroFrom, 0), new GradientStop(HeroTo, 1) },
    };
    public static readonly IBrush BarGradient = new LinearGradientBrush
    {
        StartPoint = new RelativePoint(0.5, 0, RelativeUnit.Relative),
        EndPoint = new RelativePoint(0.5, 1, RelativeUnit.Relative),
        GradientStops = { new GradientStop(Accent, 0), new GradientStop(AccentDeep, 1) },
    };
    public static readonly IBrush TrackGradient = new LinearGradientBrush
    {
        StartPoint = new RelativePoint(0, 0.5, RelativeUnit.Relative),
        EndPoint = new RelativePoint(1, 0.5, RelativeUnit.Relative),
        GradientStops = { new GradientStop(Accent, 0), new GradientStop(AccentLight, 1) },
    };

    public static readonly FontFamily Family = FontFamily.Default;
    public static readonly Typeface Regular = new(Family);
    public static readonly Typeface Bold = new(Family, weight: FontWeight.Bold);

    // Theme.cs sizes: HeroNumber 30b, TileNumber 18b, Heading 14b, Brand 11.5b,
    // Empty 11, NavItem 10.5, LabelBold 10b, Caption 8.5.
    public const double HeroNumberSize = 30, TileNumberSize = 18, HeadingSize = 14,
        BrandSize = 11.5, EmptySize = 11, NavItemSize = 10.5, LabelBoldSize = 10, CaptionSize = 8.5;
}
