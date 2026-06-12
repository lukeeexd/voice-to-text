using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace VoiceToText.Linux.Ui.Dashboard;

/// <summary>A sidebar navigation item: a rounded pill that highlights when active or hovered.
/// Mirrors the Windows NavButton (40px row, pill margin 10/4, radius 7, 3px accent bar).</summary>
internal sealed class VttNavButton : Control
{
    private readonly string _text;
    private bool _active;
    private bool _hover;

    public event Action? Clicked;

    public bool Active
    {
        get => _active;
        set
        {
            _active = value;
            InvalidateVisual();
        }
    }

    public VttNavButton(string text)
    {
        _text = text;
        Height = 40;
        Cursor = new Cursor(StandardCursorType.Hand);
        PointerEntered += (_, _) => { _hover = true; InvalidateVisual(); };
        PointerExited += (_, _) => { _hover = false; InvalidateVisual(); };
        PointerPressed += (_, _) => Clicked?.Invoke();
    }

    public override void Render(DrawingContext context)
    {
        var b = Bounds;
        context.FillRectangle(ThemeTokens.SidebarBgBrush, new Rect(0, 0, b.Width, b.Height));

        var pill = new Rect(10, 4, Math.Max(0, b.Width - 20), Math.Max(0, b.Height - 8));
        if (_active)
        {
            context.DrawRectangle(ThemeTokens.NavActiveBgBrush, null, new RoundedRect(pill, 7));
            context.FillRectangle(ThemeTokens.AccentBrush,
                new Rect(pill.X, pill.Y + 6, 3, Math.Max(0, pill.Height - 12)));
        }
        else if (_hover)
        {
            context.DrawRectangle(ThemeTokens.NavHoverBgBrush, null, new RoundedRect(pill, 7));
        }

        var brush = _active ? ThemeTokens.NavActiveTextBrush : ThemeTokens.TextSecondaryBrush;
        var ft = Draw.Text(_text, ThemeTokens.Regular, ThemeTokens.NavItemSize, brush);
        context.DrawText(ft, new Point(pill.X + 14, pill.Y + (pill.Height - ft.Height) / 2));
    }
}
