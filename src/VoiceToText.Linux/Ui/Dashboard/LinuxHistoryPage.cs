using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using VoiceToText.History;
using VoiceToText.Settings;
using VoiceToText.Stt;

namespace VoiceToText.Linux.Ui.Dashboard;

/// <summary>
/// The History page: newest-first dictation rows (text, meta line, per-row copy) with
/// a clear-all action. Reload is idempotent — unchanged data never rebuilds the rows
/// (the v0.8.11 no-flicker semantics), keyed on (count, newest timestamp).
/// </summary>
internal sealed class LinuxHistoryPage : UserControl
{
    private readonly HistoryService _history;
    private readonly AppSettings _settings;
    private readonly StackPanel _rows = new() { Spacing = 8 };
    private readonly TextBlock _hint;
    private (int Count, long NewestTicks) _signature = (-1, -1);

    public LinuxHistoryPage(HistoryService history, AppSettings settings)
    {
        _history = history;
        _settings = settings;
        Background = ThemeTokens.WindowBgBrush;

        var header = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
        var title = new TextBlock
        {
            Text = "History",
            FontSize = ThemeTokens.HeadingSize,
            FontWeight = FontWeight.SemiBold,
            Foreground = ThemeTokens.TextPrimaryBrush,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var clear = new Button { Content = "Clear all", HorizontalAlignment = HorizontalAlignment.Right };
        clear.Click += (_, _) =>
        {
            _history.Clear();
            Reload();
        };
        DockPanel.SetDock(clear, Avalonia.Controls.Dock.Right);
        header.Children.Add(clear);
        header.Children.Add(title);

        _hint = new TextBlock
        {
            Foreground = ThemeTokens.TextSecondaryBrush,
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 4, 0, 0),
        };

        var layout = new DockPanel { Margin = new Thickness(24) };
        DockPanel.SetDock(header, Avalonia.Controls.Dock.Top);
        DockPanel.SetDock(_hint, Avalonia.Controls.Dock.Top);
        layout.Children.Add(header);
        layout.Children.Add(_hint);
        layout.Children.Add(new ScrollViewer { Content = _rows });
        Content = layout;
    }

    public void Reload()
    {
        _hint.Text = _settings.HistoryEnabled
            ? (_history.Entries.Count == 0 ? "No dictations recorded yet." : "")
            : "History is off — your dictations are not being recorded. (This page only fills when history is enabled.)";

        var sig = (_history.Entries.Count,
                   _history.Entries.Count > 0 ? _history.Entries[0].Time.Ticks : 0L);
        if (sig == _signature) return; // unchanged: no rebuild, no flicker
        _signature = sig;

        _rows.Children.Clear();
        foreach (var entry in _history.Entries)
            _rows.Children.Add(BuildRow(entry));
    }

    private Control BuildRow(HistoryEntry entry)
    {
        var text = new TextBlock
        {
            Text = entry.Text,
            Foreground = ThemeTokens.TextPrimaryBrush,
            FontSize = ThemeTokens.EmptySize,
            TextWrapping = TextWrapping.Wrap,
        };

        var when = entry.Time.Date == DateTime.Today ? $"Today {entry.Time:HH:mm}" : entry.Time.ToString("MMM d HH:mm");
        var parts = new List<string> { when, entry.App, $"{entry.Words} words" };
        if (entry.Model is not null) parts.Add(ModelOption.ShortLabel(entry.Model));
        if (entry.TranscribeSeconds is double s) parts.Add($"{s:0.0}s");
        var meta = new TextBlock
        {
            Text = string.Join("   ·   ", parts),
            Foreground = ThemeTokens.TextSecondaryBrush,
            FontSize = ThemeTokens.CaptionSize + 1.5,
            Margin = new Thickness(0, 6, 0, 0),
        };

        var copy = new Button
        {
            Content = "Copy",
            FontSize = ThemeTokens.CaptionSize + 1.5,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
        };
        copy.Click += async (_, _) => await ClipboardHelper.SetTextAsync(entry.Text);

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        var left = new StackPanel();
        left.Children.Add(text);
        left.Children.Add(meta);
        Grid.SetColumn(left, 0);
        Grid.SetColumn(copy, 1);
        grid.Children.Add(left);
        grid.Children.Add(copy);

        return new Border
        {
            Background = ThemeTokens.CardBgBrush,
            BorderBrush = ThemeTokens.CardBorderBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12),
            Child = grid,
        };
    }
}
