using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using VoiceToText.Linux.Platform;
using VoiceToText.Stt;

namespace VoiceToText.Linux.Ui.Dashboard;

/// <summary>
/// The Settings page in the Windows layout language: card sections with accent
/// ALL-CAPS captions and label-left/control-right rows. Every change saves
/// immediately; engine-affecting changes (model, language, GPU) apply on the next
/// daemon start and say so inline.
/// </summary>
internal sealed class LinuxSettingsPage : UserControl
{
    public LinuxSettingsPage(AppServices services)
    {
        var settings = services.Settings;
        Background = ThemeTokens.WindowBgBrush;

        var page = new StackPanel { Margin = new Thickness(24), Spacing = 14 };

        // ───────────────────────── DICTATION ─────────────────────────
        var dictation = Section("DICTATION", out var dictationBody);

        var modelCombo = new ComboBox
        {
            ItemsSource = ModelOption.All,
            SelectedItem = ModelOption.All.FirstOrDefault(o => o.Type == settings.ModelType) ?? ModelOption.Default,
            MinWidth = 280,
        };
        modelCombo.SelectionChanged += (_, _) =>
        {
            if (modelCombo.SelectedItem is ModelOption option && option.Type != settings.ModelType)
            {
                settings.ModelType = option.Type;
                settings.Save();
            }
        };
        dictationBody.Children.Add(Row("Speech model", modelCombo,
            "Downloads on first use; model and language changes apply after a restart."));

        var language = new TextBox { Text = settings.Language, MinWidth = 280 };
        language.PropertyChanged += (_, e) =>
        {
            if (e.Property == TextBox.TextProperty && !string.IsNullOrWhiteSpace(language.Text)
                && language.Text != settings.Language)
            {
                settings.Language = language.Text.Trim();
                settings.Save();
            }
        };
        dictationBody.Children.Add(Row("Language (\"auto\" to detect)", language, null));

        var autoStop = Toggle(settings.AutoStopEnabled, on =>
        {
            settings.AutoStopEnabled = on;
            settings.Save();
        });
        dictationBody.Children.Add(Row("Auto-stop after a pause in speech", autoStop, null));

        var silenceLabel = new TextBlock
        {
            Text = $"{settings.AutoStopSilenceSeconds:0.0} s",
            Foreground = ThemeTokens.TextSecondaryBrush,
            FontSize = 13,
            VerticalAlignment = VerticalAlignment.Center,
            MinWidth = 40,
        };
        var silenceSlider = new Slider
        {
            Minimum = 0.5, Maximum = 5.0, Value = settings.AutoStopSilenceSeconds,
            TickFrequency = 0.5, IsSnapToTickEnabled = true, Width = 220,
        };
        silenceSlider.PropertyChanged += (_, e) =>
        {
            if (e.Property == RangeBase.ValueProperty)
            {
                settings.AutoStopSilenceSeconds = Math.Round(silenceSlider.Value, 1);
                silenceLabel.Text = $"{settings.AutoStopSilenceSeconds:0.0} s";
                settings.Save();
            }
        };
        dictationBody.Children.Add(Row("Stop after", Inline(silenceSlider, silenceLabel),
            "Recording uses the system default microphone (change it in your sound settings)."));

        var wpm = new NumericUpDown
        {
            Minimum = 10, Maximum = 200, Increment = 5,
            Value = (decimal)settings.TypingSpeedWpm,
            FormatString = "0",
            Width = 120,
        };
        wpm.ValueChanged += (_, e) =>
        {
            if (e.NewValue is decimal v && (double)v != settings.TypingSpeedWpm)
            {
                settings.TypingSpeedWpm = (double)v;
                settings.Save();
            }
        };
        dictationBody.Children.Add(Row("Typing speed (WPM)", wpm,
            "Used for the dashboard's \"time saved\" estimate."));
        page.Children.Add(dictation);

        // ───────────────────── FEEDBACK & PRIVACY ─────────────────────
        var feedback = Section("FEEDBACK & PRIVACY", out var feedbackBody);

        var cues = Toggle(settings.SoundCuesEnabled, on =>
        {
            settings.SoundCuesEnabled = on;
            settings.Save();
        });
        feedbackBody.Children.Add(Row("Play a sound when dictation starts and stops", cues, null));

        var volumeLabel = new TextBlock
        {
            Text = $"{settings.SoundCuesVolume * 100:0}%",
            Foreground = ThemeTokens.TextSecondaryBrush,
            FontSize = 13,
            VerticalAlignment = VerticalAlignment.Center,
            MinWidth = 40,
        };
        var volume = new Slider { Minimum = 0, Maximum = 1.0, Value = settings.SoundCuesVolume, Width = 220 };
        volume.PropertyChanged += (_, e) =>
        {
            if (e.Property == RangeBase.ValueProperty)
            {
                settings.SoundCuesVolume = Math.Round(volume.Value, 2);
                volumeLabel.Text = $"{settings.SoundCuesVolume * 100:0}%";
                settings.Save();
            }
        };
        feedbackBody.Children.Add(Row("Volume", Inline(volume, volumeLabel), null));

        var history = Toggle(settings.HistoryEnabled, on =>
        {
            settings.HistoryEnabled = on;
            settings.Save();
        });
        feedbackBody.Children.Add(Row("Save recent dictation history", history, "Kept only on this PC."));
        page.Children.Add(feedback);

        // ───────────────────────── HOTKEY ─────────────────────────
        var hotkey = Section("HOTKEY", out var hotkeyBody);
        hotkeyBody.Children.Add(Note(services.HotkeyStatus));
        if (services.HotkeyTier == HotkeyTier.IpcBinding && SessionInfo.IsGnome)
        {
            var registerStatus = Note("");
            var registerButton = new Button { Content = $"Set up {settings.Hotkey.Describe()} in GNOME" };
            registerButton.Click += (_, _) =>
            {
                var ok = GnomeShortcuts.Register(settings.Hotkey);
                registerStatus.Text = ok
                    ? $"Done — {settings.Hotkey.Describe()} now toggles dictation."
                    : "Could not register the shortcut (see the log).";
            };
            hotkeyBody.Children.Add(registerButton);
            hotkeyBody.Children.Add(registerStatus);
            hotkeyBody.Children.Add(Note("Change the key combo any time in GNOME Settings → Keyboard → Custom Shortcuts."));
        }
        page.Children.Add(hotkey);

        // ───────────────────────── SYSTEM ─────────────────────────
        var system = Section("SYSTEM", out var systemBody);
        var autostart = Toggle(XdgAutostart.IsEnabled, on =>
        {
            if (on) XdgAutostart.Enable();
            else XdgAutostart.Disable();
        });
        systemBody.Children.Add(Row("Start VoiceToText when you log in", autostart, null));
        var useGpu = Toggle(settings.UseGpuExperimental, on =>
        {
            settings.UseGpuExperimental = on;
            settings.Save();
        });
        systemBody.Children.Add(Row("Use the GPU (Vulkan, experimental)", useGpu, "Applies after a restart."));
        page.Children.Add(system);

        // ───────────────────────── UPDATES ─────────────────────────
        var updates = Section("UPDATES", out var updatesBody);
        var autoUpdate = Toggle(settings.AutoUpdateEnabled, on =>
        {
            settings.AutoUpdateEnabled = on;
            settings.Save();
        });
        updatesBody.Children.Add(Row("Check for updates automatically", autoUpdate, null));
        var updateStatus = Note("");
        var checkButton = new Button { Content = "Check for updates now" };
        checkButton.Click += async (_, _) =>
        {
            checkButton.IsEnabled = false;
            updateStatus.Text = "Checking…";
            var status = await System.Threading.Tasks.Task.Run(() => new LinuxUpdater(settings).CheckAndInstallAsync(manual: true));
            updateStatus.Text = status;
            checkButton.IsEnabled = true;
        };
        updatesBody.Children.Add(checkButton);
        updatesBody.Children.Add(updateStatus);
        page.Children.Add(updates);

        Content = new ScrollViewer { Content = page };
    }

    // ── building blocks (the Windows SettingsPage layout language) ──

    private static Border Section(string caption, out StackPanel body)
    {
        body = new StackPanel { Spacing = 10 };
        var outer = new StackPanel { Spacing = 8 };
        outer.Children.Add(new TextBlock
        {
            Text = caption,
            Foreground = ThemeTokens.AccentBrush,
            FontSize = 11.33,
            FontWeight = FontWeight.SemiBold,
        });
        outer.Children.Add(body);
        return new Border
        {
            Background = ThemeTokens.CardBgBrush,
            BorderBrush = ThemeTokens.CardBorderBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(9),
            Padding = new Thickness(16),
            Child = outer,
        };
    }

    private static Control Row(string label, Control control, string? note)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        var l = new TextBlock
        {
            Text = label,
            Foreground = ThemeTokens.TextPrimaryBrush,
            FontSize = 13,
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
        };
        control.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(l, 0);
        Grid.SetColumn(control, 1);
        grid.Children.Add(l);
        grid.Children.Add(control);
        if (note is null) return grid;

        var stack = new StackPanel { Spacing = 3 };
        stack.Children.Add(grid);
        stack.Children.Add(Note(note));
        return stack;
    }

    private static StackPanel Inline(params Control[] controls)
    {
        var p = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        foreach (var c in controls) p.Children.Add(c);
        return p;
    }

    private static ToggleSwitch Toggle(bool value, Action<bool> changed)
    {
        var t = new ToggleSwitch { IsChecked = value, OnContent = null, OffContent = null };
        t.IsCheckedChanged += (_, _) => changed(t.IsChecked == true);
        return t;
    }

    private static TextBlock Note(string text) => new()
    {
        Text = text,
        Foreground = ThemeTokens.TextSecondaryBrush,
        FontSize = 12,
        TextWrapping = TextWrapping.Wrap,
    };
}
