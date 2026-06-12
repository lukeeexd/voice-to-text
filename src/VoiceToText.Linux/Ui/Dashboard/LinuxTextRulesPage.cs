using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using VoiceToText.Settings;
using VoiceToText.TextProcessing;

namespace VoiceToText.Linux.Ui.Dashboard;

/// <summary>
/// The Text rules page: spoken-commands toggle, an editable find→replace rule list,
/// and a live preview (input → transformed output with real line breaks — the v0.8.8
/// semantics). Edits write through to settings immediately.
/// </summary>
internal sealed class LinuxTextRulesPage : UserControl
{
    private readonly AppSettings _settings;
    private readonly StackPanel _ruleRows = new() { Spacing = 6 };
    private readonly TextBox _previewIn = new() { Watermark = "Type something to preview…" };
    private readonly TextBox _previewOut = new()
    {
        IsReadOnly = true,
        AcceptsReturn = true,
        MinHeight = 56,
    };

    public LinuxTextRulesPage(AppSettings settings)
    {
        _settings = settings;
        Background = ThemeTokens.WindowBgBrush;

        var panel = new StackPanel { Margin = new Thickness(24), Spacing = 10 };

        panel.Children.Add(new TextBlock
        {
            Text = "Text rules",
            FontSize = ThemeTokens.HeadingSize,
            FontWeight = FontWeight.SemiBold,
            Foreground = ThemeTokens.TextPrimaryBrush,
        });

        var spoken = new CheckBox
        {
            Content = "Turn spoken \"new line\" / \"new paragraph\" into line breaks",
            IsChecked = settings.SpokenCommandsEnabled,
        };
        spoken.IsCheckedChanged += (_, _) =>
        {
            settings.SpokenCommandsEnabled = spoken.IsChecked == true;
            settings.Save();
            UpdatePreview();
        };
        panel.Children.Add(spoken);

        panel.Children.Add(Header("Replacements (applied in order)"));
        panel.Children.Add(_ruleRows);
        var add = new Button { Content = "Add rule" };
        add.Click += (_, _) =>
        {
            _settings.Replacements.Add(new ReplacementRule { Find = "", Replace = "" });
            _settings.Save();
            RebuildRules();
        };
        panel.Children.Add(add);

        panel.Children.Add(Header("Preview"));
        _previewIn.PropertyChanged += (_, e) =>
        {
            if (e.Property == TextBox.TextProperty) UpdatePreview();
        };
        panel.Children.Add(_previewIn);
        panel.Children.Add(_previewOut);

        Content = new ScrollViewer { Content = panel };
        RebuildRules();
        UpdatePreview();
    }

    private void RebuildRules()
    {
        _ruleRows.Children.Clear();
        for (var i = 0; i < _settings.Replacements.Count; i++)
        {
            var rule = _settings.Replacements[i];
            var find = new TextBox { Text = rule.Find, Watermark = "find", Width = 220 };
            var replace = new TextBox { Text = rule.Replace, Watermark = "replace with", Width = 220 };
            find.PropertyChanged += (_, e) =>
            {
                if (e.Property == TextBox.TextProperty && find.Text != rule.Find)
                {
                    rule.Find = find.Text ?? "";
                    _settings.Save();
                    UpdatePreview();
                }
            };
            replace.PropertyChanged += (_, e) =>
            {
                if (e.Property == TextBox.TextProperty && replace.Text != rule.Replace)
                {
                    rule.Replace = replace.Text ?? "";
                    _settings.Save();
                    UpdatePreview();
                }
            };
            var remove = new Button { Content = "✕" };
            var captured = rule;
            remove.Click += (_, _) =>
            {
                _settings.Replacements.Remove(captured);
                _settings.Save();
                RebuildRules();
                UpdatePreview();
            };
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            row.Children.Add(find);
            row.Children.Add(replace);
            row.Children.Add(remove);
            _ruleRows.Children.Add(row);
        }
        if (_settings.Replacements.Count == 0)
            _ruleRows.Children.Add(new TextBlock
            {
                Text = "No rules yet — add one to auto-correct words you dictate often.",
                Foreground = ThemeTokens.TextSecondaryBrush,
                FontSize = 12,
            });
    }

    private void UpdatePreview()
    {
        var input = _previewIn.Text ?? "";
        _previewOut.Text = input.Length == 0
            ? ""
            : TextRules.Apply(input, _settings.Replacements, _settings.SpokenCommandsEnabled);
    }

    private static TextBlock Header(string text) => new()
    {
        Text = text,
        FontWeight = FontWeight.SemiBold,
        Foreground = ThemeTokens.TextPrimaryBrush,
        Margin = new Thickness(0, 8, 0, 0),
    };
}
