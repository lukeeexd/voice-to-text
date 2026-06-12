using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using VoiceToText.App;
using VoiceToText.Linux.Platform;

namespace VoiceToText.Linux.Ui;

/// <summary>
/// The tray-resident Avalonia application (no main window; explicit shutdown only).
/// All UI is built in code — no XAML pipeline. Engine services are created by the
/// daemon before Avalonia starts and handed over via <see cref="Services"/>.
/// </summary>
public sealed class VttApp : Application
{
    /// <summary>Set by Daemon.Run before the Avalonia lifetime starts.</summary>
    public static AppServices? Services { get; set; }

    /// <summary>The hidden utility window's clipboard (tray apps have no main window).</summary>
    public static Avalonia.Input.Platform.IClipboard? SharedClipboard { get; private set; }

    private TrayIcon? _tray;
    private SettingsWindow? _settingsWindow;
    private Window? _hiddenWindow;

    public override void Initialize()
    {
        Styles.Add(new Avalonia.Themes.Fluent.FluentTheme());
        RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Dark;
    }

    public override void OnFrameworkInitializationCompleted()
    {
        var services = Services;
        if (services is null)
            return; // headless UI self-test boots the app class without engine services

        services.Controller.StatusChanged += (state, message) =>
            Dispatcher.UIThread.Post(() => OnStatus(state, message));
        services.OpenSettingsRequested += () =>
            Dispatcher.UIThread.Post(ShowSettings);

        // A never-activated 1x1 transparent window whose only job is providing the
        // platform clipboard service (tray apps have no main window to borrow it from).
        _hiddenWindow = new Window
        {
            Width = 1,
            Height = 1,
            Opacity = 0,
            ShowInTaskbar = false,
            ShowActivated = false,
            SystemDecorations = SystemDecorations.None,
            WindowStartupLocation = WindowStartupLocation.Manual,
            Position = new PixelPoint(-10_000, -10_000),
        };
        _hiddenWindow.Show();
        _hiddenWindow.Hide();
        SharedClipboard = _hiddenWindow.Clipboard;

        BuildTray(services);
        services.StartHotkeys();

        if (!services.Settings.OnboardingCompleted)
            Dispatcher.UIThread.Post(() => new FirstRunWindow(services).Show());

        base.OnFrameworkInitializationCompleted();
    }

    private void BuildTray(AppServices services)
    {
        var menu = new NativeMenu();
        var toggle = new NativeMenuItem("Toggle dictation");
        toggle.Click += (_, _) => _ = services.Controller.ToggleAsync();
        var settings = new NativeMenuItem("Settings…");
        settings.Click += (_, _) => ShowSettings();
        var quit = new NativeMenuItem("Quit");
        quit.Click += (_, _) =>
        {
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
                desktop.Shutdown();
        };
        menu.Items.Add(toggle);
        menu.Items.Add(settings);
        menu.Items.Add(new NativeMenuItemSeparator());
        menu.Items.Add(quit);

        _tray = new TrayIcon
        {
            Icon = TrayIcons.Idle,
            ToolTipText = "VoiceToText — idle",
            Menu = menu,
            IsVisible = true,
        };
        TrayIcon.SetIcons(this, [_tray]);
    }

    private void OnStatus(DictationState state, string message)
    {
        if (_tray is not null)
        {
            _tray.Icon = state switch
            {
                DictationState.Recording => TrayIcons.Recording,
                DictationState.Transcribing => TrayIcons.Transcribing,
                _ => TrayIcons.Idle,
            };
            _tray.ToolTipText = $"VoiceToText — {message}";
        }
    }

    private void ShowSettings()
    {
        if (_settingsWindow is null || !_settingsWindow.IsVisible)
        {
            _settingsWindow = new SettingsWindow(Services!);
            _settingsWindow.Show();
        }
        else
        {
            _settingsWindow.Activate();
        }
    }
}
