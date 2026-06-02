using System.Drawing;
using VoiceToText.App;
using VoiceToText.Audio;
using VoiceToText.Dashboard.Controls;
using VoiceToText.Hotkeys;
using VoiceToText.Settings;
using VoiceToText.Stt;

namespace VoiceToText.Dashboard;

/// <summary>
/// Settings as a page inside the dashboard window: microphone, speech model, global hotkey,
/// activation mode, auto-stop on silence, the on-screen indicator, recent-dictation history,
/// typing speed (WPM), automatic updates, and start-on-login. Save writes into the shared
/// <see cref="AppSettings"/> (and the
/// Run key) and raises <see cref="SettingsSaved"/>.
/// </summary>
internal sealed class SettingsPage : UserControl
{
    private readonly AppSettings _settings;
    private const int CardWidth = 700;

    private readonly DarkComboBox _deviceCombo = new();
    private readonly DarkComboBox _modelCombo = new();
    private readonly DarkComboBox _activationCombo = new();
    private readonly TextBox _hotkeyBox = new() { ReadOnly = true, Cursor = Cursors.Hand, TextAlign = HorizontalAlignment.Center, BorderStyle = BorderStyle.None };
    private readonly Label _hintLabel = new() { AutoSize = false, ForeColor = Theme.TextSecondary, Font = Theme.Caption };
    private readonly ToggleSwitch _autoStopCheck = new();
    private readonly DarkNumericUpDown _silenceUpDown = new() { DecimalPlaces = 1, Minimum = 0.3M, Maximum = 10.0M, Increment = 0.1M };
    private readonly ToggleSwitch _overlayCheck = new();
    private readonly ToggleSwitch _historyCheck = new();
    private readonly DarkNumericUpDown _wpmUpDown = new() { DecimalPlaces = 0, Minimum = 10, Maximum = 300, Increment = 5 };
    private readonly ToggleSwitch _autoUpdateCheck = new();
    private readonly TextBox _updateFolderBox = new() { BorderStyle = BorderStyle.None };
    private readonly ToggleSwitch _startupCheck = new();
    private readonly Button _saveButton = new() { Text = "Save", Size = new Size(96, 30), FlatStyle = FlatStyle.Flat, BackColor = Theme.Accent, ForeColor = Color.White, Enabled = false };
    private readonly Label _savedLabel = new() { AutoSize = true, ForeColor = Theme.Accent, Visible = false, Text = "Settings saved ✓" };
    private readonly Label _unsavedLabel = new() { AutoSize = true, ForeColor = Theme.Warning, Visible = false, Text = "● Unsaved changes" };
    private string _baseline = "";
    private bool _loading;
    private bool _layingOutCards;
    private HotkeyDefinition _hotkey;

    public event Action? SettingsSaved;
    public event Action? HotkeyCaptureStarted;
    public event Action? HotkeyCaptureEnded;

    public SettingsPage(AppSettings settings)
    {
        _settings = settings;
        _hotkey = settings.Hotkey;
        BackColor = Theme.WindowBg;
        ForeColor = Theme.TextPrimary;
        _loading = true; // guard dirty-tracking during construction; LoadFromSettings re-baselines + clears it
        BuildUi();
        LoadDevices();
        LoadModels();
        LoadFromSettings();
    }

    /// <summary>Re-sync the controls if settings changed elsewhere (e.g. a rejected hotkey was reverted).</summary>
    public void ReloadFromSettings()
    {
        _hotkey = _settings.Hotkey;
        LoadDevices();
        LoadModels();
        LoadFromSettings();
        _savedLabel.Visible = false;
    }

    protected override void OnVisibleChanged(EventArgs e)
    {
        base.OnVisibleChanged(e);
        if (Visible) { _savedLabel.Visible = false; UpdateDirty(); }
    }

    private void LoadFromSettings()
    {
        _loading = true;
        _hotkeyBox.Text = _hotkey.Describe();
        _startupCheck.Checked = AutoStart.IsEnabled();
        _activationCombo.SelectedIndex = _settings.HoldToTalk ? 1 : 0;
        _autoStopCheck.Checked = _settings.AutoStopEnabled;
        _silenceUpDown.Value = (decimal)Math.Clamp(_settings.AutoStopSilenceSeconds, 0.3, 10.0);
        _overlayCheck.Checked = _settings.ShowOverlay;
        _historyCheck.Checked = _settings.HistoryEnabled;
        _wpmUpDown.Value = (decimal)Math.Clamp(_settings.TypingSpeedWpm, 10, 300);
        _autoUpdateCheck.Checked = _settings.AutoUpdateEnabled;
        _updateFolderBox.Text = _settings.UpdateFeedFolder;
        UpdateAutoStopEnabled();
        UpdateHint();
        _loading = false;
        _baseline = Snapshot();
        UpdateDirty();
    }

    private void BuildUi()
    {
        // Controls that sit in rows: size them; DarkComboBox self-draws.
        _deviceCombo.Width = 300;
        _modelCombo.Width = 300;
        _activationCombo.Width = 180;
        _activationCombo.Items.AddRange(new object[] { "Press to toggle", "Hold to talk" });
        _activationCombo.SelectedIndexChanged += (_, _) => UpdateAutoStopEnabled();
        _hotkeyBox.GotFocus += (_, _) => { _hotkeyBox.Text = "Press a key or combination…"; HotkeyCaptureStarted?.Invoke(); };
        _hotkeyBox.LostFocus += (_, _) => { _hotkeyBox.Text = _hotkey.Describe(); HotkeyCaptureEnded?.Invoke(); };
        _silenceUpDown.Width = 78;
        _wpmUpDown.Width = 78;
        _autoStopCheck.CheckedChanged += (_, _) => UpdateAutoStopEnabled();

        // Composite controls (numeric + unit / textbox + browse) used as a single "control" in a row.
        var silence = RowComposite(_silenceUpDown, "seconds");
        var wpm = RowComposite(_wpmUpDown, "WPM");

        var browseButton = new Button { Text = "Browse…", Size = new Size(72, 30), FlatStyle = FlatStyle.Flat, BackColor = Theme.CardBg, ForeColor = Theme.TextPrimary };
        browseButton.FlatAppearance.BorderColor = Theme.InputBorder;
        browseButton.Click += OnBrowseUpdateFolder;
        var folderField = new DarkField(_updateFolderBox, 232);
        var folder = new Panel { BackColor = Theme.CardBg, Height = 30, Width = folderField.Width + 8 + browseButton.Width };
        folderField.Location = new Point(0, 0);
        browseButton.Location = new Point(folderField.Width + 8, 0);
        folder.Controls.Add(folderField);
        folder.Controls.Add(browseButton);
        var updateWarning = new Label { Text = "⚠ Runs an installer from this folder — only enable for a folder you trust.", ForeColor = Theme.Warning, Font = Theme.Caption };

        // --- Cards ---
        var dictation = new SectionCard("Dictation") { Width = CardWidth, Margin = new Padding(0, 0, 0, 14) };
        dictation.AddRow("Microphone", _deviceCombo);
        dictation.AddRow("Speech model", _modelCombo);
        var hotkeyField = new DarkField(_hotkeyBox, 240);
        dictation.AddRow("Dictation hotkey", hotkeyField, _hintLabel);
        dictation.AddRow("Activation", _activationCombo);
        dictation.AddRow("Auto-stop after a pause in speech", _autoStopCheck);
        dictation.AddRow("Stop after", silence);

        var feedback = new SectionCard("Feedback & privacy") { Width = CardWidth, Margin = new Padding(0, 0, 0, 14) };
        feedback.AddRow("Show on-screen indicator while dictating", _overlayCheck);
        feedback.AddRow("Save recent dictation history", _historyCheck, new Label { Text = "Kept only on this PC.", ForeColor = Theme.TextSecondary, Font = Theme.Caption });

        var general = new SectionCard("General") { Width = CardWidth, Margin = new Padding(0, 0, 0, 14) };
        general.AddRow("Typing speed", wpm, new Label { Text = "Used to estimate \"time saved\".", ForeColor = Theme.TextSecondary, Font = Theme.Caption });
        general.AddRow("Start automatically when I log in", _startupCheck);

        var updates = new SectionCard("Updates") { Width = CardWidth, Margin = new Padding(0, 0, 0, 14) };
        updates.AddRow("Check for updates on startup", _autoUpdateCheck);
        updates.AddRow("Update folder", folder, updateWarning);

        // --- Scroll area (cards) + pinned Save bar ---
        var cards = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            BackColor = Theme.WindowBg,
            Margin = Padding.Empty,
            Location = new Point(0, 0),
        };
        cards.Controls.AddRange(new Control[] { dictation, feedback, general, updates });

        var scroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = Theme.WindowBg };
        scroll.Controls.Add(cards);

        // Cards grow with the window up to a readable max and stay centered (both axes) when they
        // fit; when taller than the viewport they pin near the top and scroll. Re-runs on ClientSize
        // changes (incl. the scrollbar appearing).
        void LayoutCards()
        {
            if (_layingOutCards) return;
            _layingOutCards = true;
            try
            {
                int clientW = scroll.ClientSize.Width;
                int clientH = scroll.ClientSize.Height;
                if (clientW <= 1) return;
                int cardW = Math.Min(980, Math.Max(320, clientW - 48));
                foreach (Control c in cards.Controls) c.Width = cardW;
                cards.Left = Math.Max(16, (clientW - cardW) / 2);
                cards.Top = cards.Height + 36 <= clientH ? (clientH - cards.Height) / 2 : 18;
            }
            finally { _layingOutCards = false; }
        }
        scroll.ClientSizeChanged += (_, _) => LayoutCards();
        LayoutCards();

        _saveButton.FlatAppearance.BorderSize = 0;
        _saveButton.FlatAppearance.MouseOverBackColor = Theme.AccentLight;
        _saveButton.Click += OnSave;
        _saveButton.Location = new Point(24, 12);
        _savedLabel.Location = new Point(130, 18);
        _unsavedLabel.Location = new Point(234, 18);
        var saveBar = new Panel { Dock = DockStyle.Bottom, Height = 54, BackColor = Theme.WindowBg };
        saveBar.Controls.AddRange(new Control[] { _saveButton, _savedLabel, _unsavedLabel });

        // Fill added before Bottom so the bar reserves its edge first (matches DashboardForm).
        Controls.Add(scroll);
        Controls.Add(saveBar);

        // Dirty-tracking wiring (unchanged set of inputs).
        _deviceCombo.SelectedIndexChanged += (_, _) => UpdateDirty();
        _modelCombo.SelectedIndexChanged += (_, _) => UpdateDirty();
        _activationCombo.SelectedIndexChanged += (_, _) => UpdateDirty();
        _autoStopCheck.CheckedChanged += (_, _) => UpdateDirty();
        _silenceUpDown.ValueChanged += (_, _) => UpdateDirty();
        _overlayCheck.CheckedChanged += (_, _) => UpdateDirty();
        _historyCheck.CheckedChanged += (_, _) => UpdateDirty();
        _wpmUpDown.ValueChanged += (_, _) => UpdateDirty();
        _autoUpdateCheck.CheckedChanged += (_, _) => UpdateDirty();
        _updateFolderBox.TextChanged += (_, _) => UpdateDirty();
        _startupCheck.CheckedChanged += (_, _) => UpdateDirty();
    }

    // A small composite: a control followed by a unit label, sized to fit, for use as one row "control".
    private static Panel RowComposite(Control control, string unit)
    {
        control.Location = new Point(0, 0);
        var label = new Label { Text = unit, AutoSize = true, ForeColor = Theme.TextSecondary, Font = Theme.Caption, Location = new Point(control.Width + 6, (control.Height - 14) / 2 + 2) };
        var panel = new Panel { BackColor = Theme.CardBg, Height = Math.Max(control.Height, 20) };
        panel.Controls.Add(control);
        panel.Controls.Add(label);
        panel.Width = control.Width + 6 + label.PreferredWidth + 4;
        return panel;
    }

    // Auto-stop applies only in press-to-toggle mode; the silence spinner only when it's also checked.
    private void UpdateAutoStopEnabled()
    {
        bool hold = _activationCombo.SelectedIndex == 1;
        _autoStopCheck.Enabled = !hold;
        _silenceUpDown.Enabled = !hold && _autoStopCheck.Checked;
    }

    private void LoadDevices()
    {
        _deviceCombo.Items.Clear();
        var devices = AudioDevices.GetInputDevices();
        foreach (var device in devices)
            _deviceCombo.Items.Add(device);

        var current = devices.FirstOrDefault(d => d.Id == _settings.InputDeviceId);
        if (current is not null) _deviceCombo.SelectedItem = current;
        else if (_deviceCombo.Items.Count > 0) _deviceCombo.SelectedIndex = 0;
    }

    private void LoadModels()
    {
        _modelCombo.Items.Clear();
        foreach (var option in ModelOption.All)
            _modelCombo.Items.Add(option);

        var current = ModelOption.All.FirstOrDefault(m => m.Type == _settings.ModelType);
        if (current is null)
        {
            current = new ModelOption(_settings.ModelType.ToString(), _settings.ModelType);
            _modelCombo.Items.Add(current);
        }
        _modelCombo.SelectedItem = current;
    }

    private void OnBrowseUpdateFolder(object? sender, EventArgs e)
    {
        using var dialog = new FolderBrowserDialog { Description = "Choose the update folder", UseDescriptionForTitle = true };
        if (!string.IsNullOrWhiteSpace(_updateFolderBox.Text) && Directory.Exists(_updateFolderBox.Text))
            dialog.SelectedPath = _updateFolderBox.Text;
        if (dialog.ShowDialog(this) == DialogResult.OK)
            _updateFolderBox.Text = dialog.SelectedPath;
    }

    /// <summary>
    /// Hotkey capture, called by the host form's <c>ProcessCmdKey</c> when this page is active.
    /// Returns true when the key was consumed as a hotkey; false to let the form handle it.
    /// </summary>
    public bool TryCaptureHotkey(ref Message msg, Keys keyData)
    {
        if (!_hotkeyBox.Focused) return false;

        var key = keyData & Keys.KeyCode;
        var hasModifier = (keyData & (Keys.Control | Keys.Alt | Keys.Shift)) != 0;

        // Reserve bare Esc/Tab/Enter so the window stays navigable.
        if (!hasModifier && key is Keys.Escape or Keys.Tab or Keys.Enter)
            return false;

        var definition = HotkeyDefinition.FromKeyEvent(keyData);
        if (definition is not null)
        {
            _hotkey = definition;
            _hotkeyBox.Text = definition.Describe();
            UpdateHint();
            UpdateDirty();
        }
        return true; // swallow (captured combo, or a lone modifier being held)
    }

    private void UpdateHint()
    {
        if (_hotkey.IsRiskyBareKey())
        {
            _hintLabel.ForeColor = Theme.Warning;
            _hintLabel.Text = "⚠ This is a normal typing key — it would be intercepted everywhere. Add Ctrl/Alt/Shift, or use a dedicated key (e.g. F13).";
        }
        else
        {
            _hintLabel.ForeColor = Theme.TextSecondary;
            _hintLabel.Text = "Click the box, then press a single key (e.g. an extra/macro key or F13) or a modifier combo.";
        }
    }

    // A stable string of every value OnSave persists; baseline vs. current => dirty.
    private string Snapshot() => string.Join("|",
        (_deviceCombo.SelectedItem as AudioInputDevice)?.Id ?? "",
        (_modelCombo.SelectedItem as ModelOption)?.Type.ToString() ?? "",
        _hotkey.Describe(),
        _activationCombo.SelectedIndex,
        _autoStopCheck.Checked,
        _silenceUpDown.Value,
        _overlayCheck.Checked,
        _historyCheck.Checked,
        _wpmUpDown.Value,
        _autoUpdateCheck.Checked,
        _updateFolderBox.Text.Trim(),
        _startupCheck.Checked);

    private void UpdateDirty()
    {
        if (_loading) return;
        bool dirty = Snapshot() != _baseline;
        _unsavedLabel.Visible = dirty;
        _saveButton.Enabled = dirty;
        if (dirty) _savedLabel.Visible = false; // don't show a stale "Settings saved ✓" next to "Unsaved changes"
    }

    /// <summary>True when the controls differ from the last loaded/saved settings.</summary>
    public bool HasUnsavedChanges() => Snapshot() != _baseline;

    private void OnSave(object? sender, EventArgs e) => Save();

    /// <summary>Persist the current control values and clear the dirty state.</summary>
    public void Save()
    {
        _settings.InputDeviceId = (_deviceCombo.SelectedItem as AudioInputDevice)?.Id;
        if (_modelCombo.SelectedItem is ModelOption model)
            _settings.ModelType = model.Type;
        _settings.Hotkey = _hotkey;
        _settings.HoldToTalk = _activationCombo.SelectedIndex == 1;
        _settings.AutoStopEnabled = _autoStopCheck.Checked;
        _settings.AutoStopSilenceSeconds = (double)_silenceUpDown.Value;
        _settings.ShowOverlay = _overlayCheck.Checked;
        _settings.HistoryEnabled = _historyCheck.Checked;
        _settings.TypingSpeedWpm = (double)_wpmUpDown.Value;
        _settings.AutoUpdateEnabled = _autoUpdateCheck.Checked;
        _settings.UpdateFeedFolder = _updateFolderBox.Text.Trim();
        _settings.UpdateConsentAccepted = _autoUpdateCheck.Checked;
        AutoStart.Apply(_startupCheck.Checked);
        _savedLabel.Visible = true;
        _baseline = Snapshot();
        UpdateDirty();
        SettingsSaved?.Invoke();
    }
}
