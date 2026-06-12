using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace VoiceToText.Linux.Ui.Dashboard;

/// <summary>The "time saved" hero band: big value + label + subtext, with a streak on the right.</summary>
internal sealed class VttHeroPanel : Control
{
    private string _value = "—";
    private string _subtext = "";
    private int _streak;

    public void SetData(string value, string subtext, int streak)
    {
        _value = value;
        _subtext = subtext;
        _streak = streak;
        InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        var b = Bounds;
        var r = new Rect(0.5, 0.5, b.Width - 1, b.Height - 1);
        context.DrawRectangle(ThemeTokens.HeroGradient, new Pen(ThemeTokens.HeroBorderBrush), new RoundedRect(r, 10));

        context.DrawText(
            Draw.Text("TIME SAVED", ThemeTokens.Regular, ThemeTokens.CaptionSize, ThemeTokens.AccentBrush),
            new Point(20, 18));
        context.DrawText(
            Draw.Text(_value, ThemeTokens.Bold, ThemeTokens.HeroNumberSize, ThemeTokens.TextPrimaryBrush),
            new Point(18, 38));
        context.DrawText(
            Draw.Text(_subtext, ThemeTokens.Regular, ThemeTokens.CaptionSize, ThemeTokens.TextSecondaryBrush),
            new Point(20, b.Height - 28));

        var streak = Draw.Text($"{_streak}-day streak", ThemeTokens.Bold, ThemeTokens.LabelBoldSize, ThemeTokens.GoldBrush);
        context.DrawText(streak, new Point(b.Width - streak.Width - 24, (b.Height - streak.Height) / 2));
    }
}
