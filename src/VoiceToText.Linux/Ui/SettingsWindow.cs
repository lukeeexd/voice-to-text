using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using VoiceToText.Linux.Platform;
using VoiceToText.Stt;

namespace VoiceToText.Linux.Ui;

/// <summary>
/// The lean Linux settings window, built entirely in code. Every change saves
/// immediately; engine-affecting changes (model, language, Force CPU) apply on the
/// next daemon start and say so inline.
/// </summary>
public sealed class SettingsWindow : Window
{
    private readonly AppServices _services;

    public SettingsWindow(AppServices services)
    {
        _services = services;
        var settings = services.Settings;

        Title = "VoiceToText settings";
        Width = 460;
        SizeToContent = SizeToContent.Height;
        CanResize = false;

        var panel = new StackPanel { Margin = new Thickness(20), Spacing = 12 };

        // --- Speech model ---
        panel.Children.Add(Header("Speech model"));
        var modelCombo = new ComboBox
        {
            ItemsSource = ModelOption.All,
            SelectedItem = ModelOption.All.FirstOrDefault(o => o.Type == settings.ModelType) ?? ModelOption.Default,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        modelCombo.SelectionChanged += (_, _) =>
        {
            if (modelCombo.SelectedItem is ModelOption option && option.Type != settings.ModelType)
            {
                settings.ModelType = option.Type;
                settings.Save();
            }
        };
        panel.Children.Add(modelCombo);
        panel.Children.Add(Note("Downloads on first use; model and language changes apply after a restart."));

        // --- Language ---
        panel.Children.Add(Header("Language (\"auto\" to detect)"));
        var language = new TextBox { Text = settings.Language };
        language.PropertyChanged += (_, e) =>
        {
            if (e.Property == TextBox.TextProperty && !string.IsNullOrWhiteSpace(language.Text)
                && language.Text != settings.Language)
            {
                settings.Language = language.Text.Trim();
                settings.Save();
            }
        };
        panel.Children.Add(language);

        // --- Auto-stop ---
        var autoStop = new CheckBox { Content = "Stop recording after a pause in speech", IsChecked = settings.AutoStopEnabled };
        var silenceSlider = new Slider
        {
            Minimum = 0.5, Maximum = 5.0, Value = settings.AutoStopSilenceSeconds,
            TickFrequency = 0.5, IsSnapToTickEnabled = true,
        };
        var silenceLabel = Note($"Pause length: {settings.AutoStopSilenceSeconds:0.0}s");
        autoStop.IsCheckedChanged += (_, _) =>
        {
            settings.AutoStopEnabled = autoStop.IsChecked == true;
            settings.Save();
        };
        silenceSlider.PropertyChanged += (_, e) =>
        {
            if (e.Property == RangeBase.ValueProperty)
            {
                settings.AutoStopSilenceSeconds = Math.Round(silenceSlider.Value, 1);
                silenceLabel.Text = $"Pause length: {settings.AutoStopSilenceSeconds:0.0}s";
                settings.Save();
            }
        };
        panel.Children.Add(Header("Recording"));
        panel.Children.Add(autoStop);
        panel.Children.Add(silenceSlider);
        panel.Children.Add(silenceLabel);
        panel.Children.Add(Note("Recording uses the system default microphone (change it in your sound settings)."));

        // --- Sound cues ---
        panel.Children.Add(Header("Sound cues"));
        var cues = new CheckBox { Content = "Play a sound when dictation starts and stops", IsChecked = settings.SoundCuesEnabled };
        cues.IsCheckedChanged += (_, _) =>
        {
            settings.SoundCuesEnabled = cues.IsChecked == true;
            settings.Save();
        };
        var volume = new Slider { Minimum = 0, Maximum = 1.0, Value = settings.SoundCuesVolume };
        volume.PropertyChanged += (_, e) =>
        {
            if (e.Property == RangeBase.ValueProperty)
            {
                settings.SoundCuesVolume = Math.Round(volume.Value, 2);
                settings.Save();
            }
        };
        panel.Children.Add(cues);
        panel.Children.Add(volume);

        // --- Hotkey ---
        panel.Children.Add(Header("Hotkey"));
        panel.Children.Add(Note(services.HotkeyStatus));
        if (services.HotkeyTier == HotkeyTier.IpcBinding && SessionInfo.IsGnome)
        {
            var registerButton = new Button { Content = $"Set up {settings.Hotkey.Describe()} in GNOME" };
            var registerStatus = Note("");
            registerButton.Click += (_, _) =>
            {
                var ok = GnomeShortcuts.Register(settings.Hotkey);
                registerStatus.Text = ok
                    ? $"Done — {settings.Hotkey.Describe()} now toggles dictation."
                    : "Could not register the shortcut (see the log).";
            };
            panel.Children.Add(registerButton);
            panel.Children.Add(registerStatus);
        }

        // --- System ---
        panel.Children.Add(Header("System"));
        var autostart = new CheckBox { Content = "Start VoiceToText when you log in", IsChecked = XdgAutostart.IsEnabled };
        autostart.IsCheckedChanged += (_, _) =>
        {
            if (autostart.IsChecked == true) XdgAutostart.Enable();
            else XdgAutostart.Disable();
        };
        var forceCpu = new CheckBox { Content = "Force CPU transcription (skip the GPU) — applies after a restart", IsChecked = settings.ForceCpu };
        forceCpu.IsCheckedChanged += (_, _) =>
        {
            settings.ForceCpu = forceCpu.IsChecked == true;
            settings.Save();
        };
        panel.Children.Add(autostart);
        panel.Children.Add(forceCpu);

        Content = new ScrollViewer { Content = panel };
    }

    private static TextBlock Header(string text) => new()
    {
        Text = text,
        FontWeight = Avalonia.Media.FontWeight.SemiBold,
        Margin = new Thickness(0, 8, 0, 0),
    };

    private static TextBlock Note(string text) => new()
    {
        Text = text,
        Opacity = 0.7,
        FontSize = 12,
        TextWrapping = Avalonia.Media.TextWrapping.Wrap,
    };
}
