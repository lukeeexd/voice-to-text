using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace VoiceToText.Linux.Ui.Dashboard;

/// <summary>A card showing a big number and a caption beneath it.</summary>
internal sealed class VttStatTile : Control
{
    private string _number = "—";
    private string _caption = "";

    public void SetData(string number, string caption)
    {
        _number = number;
        _caption = caption;
        InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        var b = Bounds;
        var r = new Rect(0.5, 0.5, b.Width - 1, b.Height - 1);
        context.DrawRectangle(ThemeTokens.CardBgBrush, new Pen(ThemeTokens.CardBorderBrush), new RoundedRect(r, 9));

        context.DrawText(
            Draw.Text(_number, ThemeTokens.Bold, ThemeTokens.TileNumberSize, ThemeTokens.TextPrimaryBrush),
            new Point(13, 10));
        context.DrawText(
            Draw.Text(_caption, ThemeTokens.Regular, ThemeTokens.CaptionSize, ThemeTokens.TextSecondaryBrush),
            new Point(14, 42));
    }
}
