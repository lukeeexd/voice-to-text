using Avalonia.Media;

namespace VoiceToText.Linux.Ui.Dashboard;

/// <summary>FormattedText factory shared by the custom-drawn dashboard controls.</summary>
internal static class Draw
{
    public static FormattedText Text(string s, Typeface tf, double size, IBrush brush) =>
        new(s, System.Globalization.CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight, tf, size, brush);
}
