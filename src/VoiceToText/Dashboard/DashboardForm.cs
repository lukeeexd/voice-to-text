using System.Drawing;
using VoiceToText.History;
using VoiceToText.Settings;
using VoiceToText.Stats;

namespace VoiceToText.Dashboard;

internal enum DashboardPageKind { Dashboard, Settings, TextRules, History }

/// <summary>
/// The app's main window: a sidebar (brand + nav + version) on the left and a content host on
/// the right that shows exactly one page. Refreshes the dashboard data on show and on activate.
/// </summary>
internal sealed class DashboardForm : Form
{
    private readonly AppSettings _settings;
    private readonly StatsService _stats;

    private readonly Panel _sidebar = new() { Dock = DockStyle.Left, Width = 172, BackColor = Theme.SidebarBg };
    private readonly Panel _content = new() { Dock = DockStyle.Fill, BackColor = Theme.WindowBg };
    private readonly NavButton _navDashboard = new("Dashboard") { Dock = DockStyle.Top };
    private readonly NavButton _navSettings = new("Settings") { Dock = DockStyle.Top };
    private readonly NavButton _navTextRules = new("Text rules") { Dock = DockStyle.Top };
    private readonly NavButton _navHistory = new("History") { Dock = DockStyle.Top };
    private readonly DashboardPage _dashboardPage = new() { Dock = DockStyle.Fill };
    private readonly SettingsPage _settingsPage;
    private readonly TextRulesPage _textRulesPage;
    private readonly HistoryPage _historyPage;
    private DashboardPageKind _active = DashboardPageKind.Dashboard;

    public event Action? SettingsSaved;
    public event Action? HotkeyCaptureStarted;
    public event Action? HotkeyCaptureEnded;

    public DashboardForm(AppSettings settings, StatsService stats, HistoryService history, string versionLabel)
    {
        _settings = settings;
        _stats = stats;
        _settingsPage = new SettingsPage(settings) { Dock = DockStyle.Fill, Visible = false };
        _settingsPage.SettingsSaved += () => SettingsSaved?.Invoke();
        _settingsPage.HotkeyCaptureStarted += () => HotkeyCaptureStarted?.Invoke();
        _settingsPage.HotkeyCaptureEnded += () => HotkeyCaptureEnded?.Invoke();
        _textRulesPage = new TextRulesPage(settings) { Dock = DockStyle.Fill, Visible = false };
        _historyPage = new HistoryPage(history, settings) { Dock = DockStyle.Fill, Visible = false };
        BuildUi(versionLabel);
    }

    private void BuildUi(string versionLabel)
    {
        Text = "Voice to Text";
        try { Icon = System.Drawing.Icon.ExtractAssociatedIcon(Environment.ProcessPath!); }
        catch { /* no embedded icon under the debugger host */ }
        BackColor = Theme.WindowBg;
        ClientSize = new Size(920, 620);
        MinimumSize = new Size(900, 620);
        StartPosition = FormStartPosition.CenterScreen;
        DoubleBuffered = true;

        _content.Controls.Add(_historyPage);
        _content.Controls.Add(_textRulesPage);
        _content.Controls.Add(_settingsPage);
        _content.Controls.Add(_dashboardPage);

        var brand = new Label
        {
            Text = "  Voice to Text",
            Dock = DockStyle.Top,
            Height = 54,
            ForeColor = Theme.TextPrimary,
            Font = Theme.Brand,
            TextAlign = ContentAlignment.MiddleLeft,
            BackColor = Theme.SidebarBg,
        };
        var version = new Label
        {
            Text = "  " + versionLabel,
            Dock = DockStyle.Bottom,
            Height = 28,
            ForeColor = Theme.TextMuted,
            Font = Theme.Caption,
            TextAlign = ContentAlignment.MiddleLeft,
            BackColor = Theme.SidebarBg,
        };

        _navDashboard.Click += (_, _) => ShowPage(DashboardPageKind.Dashboard);
        _navSettings.Click += (_, _) => ShowPage(DashboardPageKind.Settings);
        _navTextRules.Click += (_, _) => ShowPage(DashboardPageKind.TextRules);
        _navHistory.Click += (_, _) => ShowPage(DashboardPageKind.History);

        // Dock.Top stacks in reverse add-order, so the last added sits highest.
        // Added first => lowest, giving visual order: brand, Dashboard, Settings, Text rules, History.
        _sidebar.Controls.Add(_navHistory);
        _sidebar.Controls.Add(_navTextRules);
        _sidebar.Controls.Add(_navSettings);
        _sidebar.Controls.Add(_navDashboard);
        _sidebar.Controls.Add(brand);
        _sidebar.Controls.Add(version);

        // Add the Fill host before the Left sidebar so the sidebar reserves its edge first.
        Controls.Add(_content);
        Controls.Add(_sidebar);

        SetActiveStyles();
    }

    public void ShowPage(DashboardPageKind page)
    {
        _active = page;
        _dashboardPage.Visible = page == DashboardPageKind.Dashboard;
        _settingsPage.Visible = page == DashboardPageKind.Settings;
        _textRulesPage.Visible = page == DashboardPageKind.TextRules;
        _historyPage.Visible = page == DashboardPageKind.History;
        SetActiveStyles();
    }

    private void SetActiveStyles()
    {
        _navDashboard.Active = _active == DashboardPageKind.Dashboard;
        _navSettings.Active = _active == DashboardPageKind.Settings;
        _navTextRules.Active = _active == DashboardPageKind.TextRules;
        _navHistory.Active = _active == DashboardPageKind.History;
    }

    /// <summary>Rebuild the view-model from the current stats snapshot and bind the dashboard page.</summary>
    public void RefreshData()
    {
        var model = new DashboardModel(_stats.Data, DateOnly.FromDateTime(DateTime.Now), _settings.TypingSpeedWpm);
        _dashboardPage.Bind(model);
    }

    /// <summary>Re-sync the Settings page (e.g. after a rejected hotkey was reverted).</summary>
    public void ReloadSettings() => _settingsPage.ReloadFromSettings();

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        DarkTitleBar.Apply(Handle);
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        RefreshData();
    }

    protected override void OnActivated(EventArgs e)
    {
        base.OnActivated(e);
        RefreshData();
        // The History page reloads on show; also refresh it on activation so a dictation made
        // while it was already the visible page appears when the user returns to the window.
        if (_active == DashboardPageKind.History) _historyPage.Reload();
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (_active == DashboardPageKind.Settings && _settingsPage.TryCaptureHotkey(ref msg, keyData))
            return true;
        return base.ProcessCmdKey(ref msg, keyData);
    }
}
