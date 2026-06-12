using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using VoiceToText.Dashboard;

namespace VoiceToText.Linux.Ui.Dashboard;

internal enum LinuxPageKind { Dashboard, Settings, TextRules, History, About }

/// <summary>
/// The Linux dashboard: a 172px sidebar (brand + nav + version) and a content host
/// showing one of five pages — the Windows DashboardForm experience on Avalonia.
/// Refreshes data on open/activate and live on each transcription. (No unsaved-changes
/// prompt: unlike Windows, the Linux settings save on change.)
/// </summary>
internal sealed class DashboardWindow : Window
{
    private readonly AppServices _services;
    private readonly LinuxDashboardPage _dashboardPage;
    private readonly LinuxSettingsPage _settingsPage;
    private readonly LinuxTextRulesPage _textRulesPage;
    private readonly LinuxHistoryPage _historyPage;
    private readonly LinuxAboutPage _aboutPage;
    private readonly Dictionary<LinuxPageKind, (VttNavButton Nav, Control Page)> _pages = new();
    private LinuxPageKind _active = LinuxPageKind.Dashboard;
    private readonly Action<string> _onTranscribed;

    public DashboardWindow(AppServices services)
    {
        _services = services;
        Title = "Voice to Text";
        Width = 920;
        Height = 620;
        MinWidth = 900;
        MinHeight = 620;
        Background = ThemeTokens.WindowBgBrush;

        _dashboardPage = new LinuxDashboardPage();
        _settingsPage = new LinuxSettingsPage(services);
        _textRulesPage = new LinuxTextRulesPage(services.Settings);
        _historyPage = new LinuxHistoryPage(services.History, services.Settings);
        _aboutPage = new LinuxAboutPage(services.Settings);

        // Sidebar: brand (54) on top, nav items, version footer (28).
        var sidebar = new DockPanel { Width = 172, Background = ThemeTokens.SidebarBgBrush };
        var brand = new TextBlock
        {
            Text = "Voice to Text",
            FontSize = ThemeTokens.BrandSize,
            FontWeight = FontWeight.Bold,
            Foreground = ThemeTokens.TextPrimaryBrush,
            Padding = new Thickness(16, 0, 0, 0),
            Height = 54,
            VerticalAlignment = VerticalAlignment.Top,
            RenderTransform = null,
        };
        var brandHost = new Border { Height = 54, Child = brand, Padding = new Thickness(0, 18, 0, 0) };
        var version = new TextBlock
        {
            Text = "v" + (typeof(DashboardWindow).Assembly.GetName().Version?.ToString(3) ?? "?"),
            FontSize = ThemeTokens.CaptionSize,
            Foreground = ThemeTokens.TextMutedBrush,
            Padding = new Thickness(16, 6, 0, 0),
            Height = 28,
        };
        DockPanel.SetDock(brandHost, Avalonia.Controls.Dock.Top);
        DockPanel.SetDock(version, Avalonia.Controls.Dock.Bottom);

        var navStack = new StackPanel();
        sidebar.Children.Add(brandHost);
        sidebar.Children.Add(version);
        sidebar.Children.Add(navStack);

        // Content host.
        var host = new Panel { Background = ThemeTokens.WindowBgBrush };
        foreach (var (kind, page, label) in new (LinuxPageKind, Control, string)[]
        {
            (LinuxPageKind.Dashboard, _dashboardPage, "Dashboard"),
            (LinuxPageKind.Settings, _settingsPage, "Settings"),
            (LinuxPageKind.TextRules, _textRulesPage, "Text rules"),
            (LinuxPageKind.History, _historyPage, "History"),
            (LinuxPageKind.About, _aboutPage, "About"),
        })
        {
            var nav = new VttNavButton(label);
            var k = kind;
            nav.Clicked += () => ShowPage(k);
            navStack.Children.Add(nav);
            page.IsVisible = false;
            host.Children.Add(page);
            _pages[kind] = (nav, page);
        }

        var root = new DockPanel();
        DockPanel.SetDock(sidebar, Avalonia.Controls.Dock.Left);
        root.Children.Add(sidebar);
        root.Children.Add(host);
        Content = root;

        ShowPage(LinuxPageKind.Dashboard);

        _onTranscribed = _ => Dispatcher.UIThread.Post(() =>
        {
            RefreshData();
            if (_active == LinuxPageKind.History) _historyPage.Reload();
        });
        _services.Controller.Transcribed += _onTranscribed;
        Closed += (_, _) => _services.Controller.Transcribed -= _onTranscribed;
        Opened += (_, _) => RefreshData();
        Activated += (_, _) =>
        {
            RefreshData();
            if (_active == LinuxPageKind.History) _historyPage.Reload();
        };
    }

    public void ShowPage(LinuxPageKind kind)
    {
        _active = kind;
        foreach (var (k, entry) in _pages)
        {
            entry.Page.IsVisible = k == kind;
            entry.Nav.Active = k == kind;
        }
        if (kind == LinuxPageKind.History) _historyPage.Reload();
        if (kind == LinuxPageKind.Dashboard) RefreshData();
    }

    /// <summary>Rebuild the view-model from the current stats snapshot and bind the dashboard page.</summary>
    public void RefreshData()
    {
        var model = new DashboardModel(
            _services.Stats.Data, DateOnly.FromDateTime(DateTime.Now), _services.Settings.TypingSpeedWpm);
        _dashboardPage.Bind(model);
    }
}
