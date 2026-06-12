using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using VoiceToText.Diagnostics;
using VoiceToText.Settings;

namespace VoiceToText.Linux.Ui.Dashboard;

/// <summary>The About page: diagnostics rows (acceleration tinted accent/gold like the
/// Windows green/amber intent) and a copy-diagnostics button.</summary>
internal sealed class LinuxAboutPage : UserControl
{
    public LinuxAboutPage(AppSettings settings)
    {
        Background = ThemeTokens.WindowBgBrush;

        var panel = new StackPanel { Margin = new Thickness(24), Spacing = 8 };
        panel.Children.Add(new TextBlock
        {
            Text = "Voice to Text",
            FontSize = ThemeTokens.HeadingSize,
            FontWeight = FontWeight.SemiBold,
            Foreground = ThemeTokens.TextPrimaryBrush,
            Margin = new Thickness(0, 0, 0, 8),
        });

        var info = DiagnosticsInfo.Current(settings);
        foreach (var (label, value) in info.Rows)
        {
            var valueBrush = label == "Acceleration"
                ? (info.IsGpuAccelerated ? ThemeTokens.AccentBrush : ThemeTokens.GoldBrush)
                : ThemeTokens.TextPrimaryBrush;

            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("130,*") };
            var l = new TextBlock { Text = label, Foreground = ThemeTokens.TextSecondaryBrush, FontSize = 12 };
            var v = new TextBlock { Text = value, Foreground = valueBrush, FontSize = 12, TextWrapping = TextWrapping.Wrap };
            Grid.SetColumn(l, 0);
            Grid.SetColumn(v, 1);
            row.Children.Add(l);
            row.Children.Add(v);
            panel.Children.Add(row);
        }

        var status = new TextBlock { Foreground = ThemeTokens.TextSecondaryBrush, FontSize = 12 };
        var copy = new Button { Content = "Copy diagnostics", Margin = new Thickness(0, 12, 0, 0), HorizontalAlignment = HorizontalAlignment.Left };
        copy.Click += async (_, _) =>
        {
            await ClipboardHelper.SetTextAsync(info.ToClipboardText());
            status.Text = "Copied.";
        };
        panel.Children.Add(copy);
        panel.Children.Add(status);

        Content = panel;
    }
}
