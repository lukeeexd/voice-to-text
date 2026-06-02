using System.Drawing;
using VoiceToText.App;
using VoiceToText.Audio;
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
    private readonly ComboBox _deviceCombo = new() { DropDownStyle = ComboBoxStyle.DropDownList, FlatStyle = FlatStyle.Flat, BackColor = Theme.CardBg, ForeColor = Theme.TextPrimary, DrawMode = DrawMode.OwnerDrawFixed };
    private readonly ComboBox _modelCombo = new() { DropDownStyle = ComboBoxStyle.DropDownList, FlatStyle = FlatStyle.Flat, BackColor = Theme.CardBg, ForeColor = Theme.TextPrimary, DrawMode = DrawMode.OwnerDrawFixed };
    private readonly ComboBox _activationCombo = new() { DropDownStyle = ComboBoxStyle.DropDownList, FlatStyle = FlatStyle.Flat, BackColor = Theme.CardBg, ForeColor = Theme.TextPrimary, DrawMode = DrawMode.OwnerDrawFixed };
    private readonly TextBox _hotkeyBox = new() { ReadOnly = true, Cursor = Cursors.Hand, TextAlign = HorizontalAlignment.Center, BackColor = Theme.CardBg, ForeColor = Theme.TextPrimary, BorderStyle = BorderStyle.FixedSingle };
    private readonly Label _hintLabel = new() { AutoSize = true, ForeColor = Theme.TextSecondary, Location = new Point(20, 184), MaximumSize = new Size(440, 0) };
    private readonly CheckBox _autoStopCheck = new() { Text = "Auto-stop after a pause in speech", AutoSize = true, Location = new Point(20, 282), ForeColor = Theme.TextPrimary };
    private readonly NumericUpDown _silenceUpDown = new() { DecimalPlaces = 1, Minimum = 0.3M, Maximum = 10.0M, Increment = 0.1M, BackColor = Theme.CardBg, ForeColor = Theme.TextPrimary, BorderStyle = BorderStyle.FixedSingle };
    private readonly CheckBox _overlayCheck = new() { Text = "Show on-screen indicator while dictating", AutoSize = true, Location = new Point(20, 342), ForeColor = Theme.TextPrimary };
    private readonly CheckBox _historyCheck = new() { Text = "Save recent dictation history (kept only on this PC)", AutoSize = true, Location = new Point(20, 376), ForeColor = Theme.TextPrimary };
    private readonly NumericUpDown _wpmUpDown = new() { DecimalPlaces = 0, Minimum = 10, Maximum = 300, Increment = 5, BackColor = Theme.CardBg, ForeColor = Theme.TextPrimary, BorderStyle = BorderStyle.FixedSingle };
    private readonly CheckBox _autoUpdateCheck = new() { Text = "Automatically check for updates on startup", AutoSize = true, Location = new Point(20, 444), ForeColor = Theme.TextPrimary };
    private readonly TextBox _updateFolderBox = new() { BackColor = Theme.CardBg, ForeColor = Theme.TextPrimary, BorderStyle = BorderStyle.FixedSingle };
    private readonly CheckBox _startupCheck = new() { Text = "Start automatically when I log in", AutoSize = true, Location = new Point(20, 544), ForeColor = Theme.TextPrimary };
    private readonly Button _saveButton = new() { Text = "Save", Location = new Point(20, 584), Size = new Size(96, 30), FlatStyle = FlatStyle.Flat, BackColor = Theme.Accent, ForeColor = Color.White, Enabled = false };
    private readonly Label _savedLabel = new() { AutoSize = true, ForeColor = Theme.Accent, Visible = false, Text = "Settings saved ✓", Location = new Point(126, 590) };
    private readonly Label _unsavedLabel = new() { AutoSize = true, ForeColor = Theme.Warning, Visible = false, Text = "● Unsaved changes", Location = new Point(230, 590) };
    private string _baseline = "";
    private bool _loading;
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
        var deviceLabel = new Label { Text = "Microphone:", Location = new Point(20, 18), AutoSize = true, ForeColor = Theme.TextPrimary };
        _deviceCombo.SetBounds(20, 40, 440, 24);
        _deviceCombo.DrawItem += OnComboDrawItem;

        var modelLabel = new Label { Text = "Speech model:", Location = new Point(20, 74), AutoSize = true, ForeColor = Theme.TextPrimary };
        _modelCombo.SetBounds(20, 96, 440, 24);
        _modelCombo.DrawItem += OnComboDrawItem;

        var hotkeyLabel = new Label { Text = "Dictation hotkey:", Location = new Point(20, 132), AutoSize = true, ForeColor = Theme.TextPrimary };
        _hotkeyBox.SetBounds(20, 154, 440, 26);
        _hotkeyBox.GotFocus += (_, _) => { _hotkeyBox.Text = "Press a key or combination…"; HotkeyCaptureStarted?.Invoke(); };
        _hotkeyBox.LostFocus += (_, _) => { _hotkeyBox.Text = _hotkey.Describe(); HotkeyCaptureEnded?.Invoke(); };

        var activationLabel = new Label { Text = "Activation:", Location = new Point(20, 220), AutoSize = true, ForeColor = Theme.TextPrimary };
        _activationCombo.SetBounds(20, 242, 200, 24);
        _activationCombo.DrawItem += OnComboDrawItem;
        _activationCombo.Items.AddRange(new object[] { "Press to toggle", "Hold to talk" });
        _activationCombo.SelectedIndexChanged += (_, _) => UpdateAutoStopEnabled();

        _autoStopCheck.CheckedChanged += (_, _) => UpdateAutoStopEnabled();
        var stopAfterLabel = new Label { Text = "Stop after", Location = new Point(40, 308), AutoSize = true, ForeColor = Theme.TextPrimary };
        _silenceUpDown.SetBounds(116, 306, 56, 24);
        var secondsLabel = new Label { Text = "seconds of silence", Location = new Point(178, 308), AutoSize = true, ForeColor = Theme.TextPrimary };

        var wpmLabel = new Label { Text = "Typing speed:", Location = new Point(20, 408), AutoSize = true, ForeColor = Theme.TextPrimary };
        _wpmUpDown.SetBounds(108, 406, 60, 24);
        var wpmSuffix = new Label { Text = "WPM  (used to estimate \"time saved\")", Location = new Point(176, 408), AutoSize = true, ForeColor = Theme.TextSecondary };

        var updateFolderLabel = new Label { Text = "Update folder:", Location = new Point(20, 478), AutoSize = true, ForeColor = Theme.TextPrimary };
        _updateFolderBox.SetBounds(110, 476, 280, 24);
        var browseButton = new Button { Text = "Browse…", Location = new Point(396, 475), Size = new Size(64, 26), FlatStyle = FlatStyle.Flat, BackColor = Theme.CardBg, ForeColor = Theme.TextPrimary };
        browseButton.FlatAppearance.BorderColor = Theme.CardBorder;
        browseButton.Click += OnBrowseUpdateFolder;
        var updateNote = new Label { Text = "Updates run an installer from this folder — only enable this for a folder you trust.", Location = new Point(20, 506), AutoSize = true, ForeColor = Theme.Warning, MaximumSize = new Size(440, 0) };

        _saveButton.FlatAppearance.BorderSize = 0;
        _saveButton.FlatAppearance.MouseOverBackColor = Theme.AccentLight;
        _saveButton.Click += OnSave;

        Controls.AddRange(new Control[]
        {
            deviceLabel, _deviceCombo, modelLabel, _modelCombo,
            hotkeyLabel, _hotkeyBox, _hintLabel,
            activationLabel, _activationCombo,
            _autoStopCheck, stopAfterLabel, _silenceUpDown, secondsLabel,
            _overlayCheck, _historyCheck, wpmLabel, _wpmUpDown, wpmSuffix,
            _autoUpdateCheck, updateFolderLabel, _updateFolderBox, browseButton, updateNote,
            _startupCheck, _saveButton, _savedLabel, _unsavedLabel,
        });

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

    // Auto-stop applies only in press-to-toggle mode; the silence spinner only when it's also checked.
    private void UpdateAutoStopEnabled()
    {
        bool hold = _activationCombo.SelectedIndex == 1;
        _autoStopCheck.Enabled = !hold;
        _silenceUpDown.Enabled = !hold && _autoStopCheck.Checked;
    }

    private void OnComboDrawItem(object? sender, DrawItemEventArgs e)
    {
        var combo = (ComboBox)sender!;
        e.DrawBackground();
        if (e.Index >= 0)
        {
            var selected = (e.State & DrawItemState.Selected) != 0;
            using var backBrush = new SolidBrush(selected ? Theme.NavActiveBg : Theme.CardBg);
            e.Graphics.FillRectangle(backBrush, e.Bounds);
            using var textBrush = new SolidBrush(selected ? Theme.NavActiveText : Theme.TextPrimary);
            using var format = new StringFormat { LineAlignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap };
            var textRect = new Rectangle(e.Bounds.X + 4, e.Bounds.Y, e.Bounds.Width - 6, e.Bounds.Height);
            e.Graphics.DrawString(combo.GetItemText(combo.Items[e.Index]), e.Font ?? Font, textBrush, textRect, format);
        }
        e.DrawFocusRectangle();
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
