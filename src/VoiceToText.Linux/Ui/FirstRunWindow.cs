using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Threading;
using VoiceToText.Linux.Platform;
using VoiceToText.Stt;

namespace VoiceToText.Linux.Ui;

/// <summary>First-run welcome: model download + hotkey setup, then never again.</summary>
public sealed class FirstRunWindow : Window
{
    public FirstRunWindow(AppServices services)
    {
        var settings = services.Settings;

        Title = "Welcome to VoiceToText";
        Width = 480;
        SizeToContent = SizeToContent.Height;
        CanResize = false;

        var panel = new StackPanel { Margin = new Thickness(24), Spacing = 14 };
        panel.Children.Add(new TextBlock
        {
            Text = "VoiceToText turns speech into text anywhere: press the hotkey, talk, " +
                   "and the transcript is pasted into the focused window (and always copied " +
                   "to the clipboard).",
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
        });

        // --- Model download ---
        var downloadStatus = new TextBlock { Opacity = 0.7, FontSize = 12, TextWrapping = Avalonia.Media.TextWrapping.Wrap };
        var downloadButton = new Button { Content = $"Download the speech model ({ModelOption.ShortLabel(settings.ModelType.ToString())})" };
        if (ModelManager.IsModelPresent(settings.ModelType))
        {
            downloadButton.IsEnabled = false;
            downloadStatus.Text = "Model already downloaded.";
        }
        downloadButton.Click += async (_, _) =>
        {
            downloadButton.IsEnabled = false;
            downloadStatus.Text = "Downloading…";
            try
            {
                var progress = new Progress<string>(s => Dispatcher.UIThread.Post(() => downloadStatus.Text = s));
                await ModelManager.EnsureModelAsync(settings.ModelType, progress);
                downloadStatus.Text = "Model ready.";
            }
            catch (Exception ex)
            {
                downloadStatus.Text = $"Download failed: {ex.Message} (it will retry on first dictation)";
                downloadButton.IsEnabled = true;
            }
        };
        panel.Children.Add(downloadButton);
        panel.Children.Add(downloadStatus);

        // --- Hotkey setup ---
        panel.Children.Add(new TextBlock { Text = services.HotkeyStatus, TextWrapping = Avalonia.Media.TextWrapping.Wrap });
        if (services.HotkeyTier == HotkeyTier.IpcBinding && SessionInfo.IsGnome)
        {
            var hotkeyStatus = new TextBlock { Opacity = 0.7, FontSize = 12 };
            var hotkeyButton = new Button { Content = $"Set up {settings.Hotkey.Describe()} in GNOME" };
            hotkeyButton.Click += (_, _) =>
            {
                var ok = GnomeShortcuts.Register(settings.Hotkey);
                hotkeyStatus.Text = ok ? "Shortcut registered." : "Could not register (see the log).";
            };
            panel.Children.Add(hotkeyButton);
            panel.Children.Add(hotkeyStatus);
        }

        var done = new Button { Content = "Get started", HorizontalAlignment = HorizontalAlignment.Right };
        done.Click += (_, _) =>
        {
            settings.OnboardingCompleted = true;
            settings.Save();
            Close();
        };
        panel.Children.Add(done);

        Content = panel;
    }
}
