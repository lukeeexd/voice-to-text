using System.Drawing;
using VoiceToText.App;
using VoiceToText.Audio;
using VoiceToText.Hotkeys;
using VoiceToText.Settings;

namespace VoiceToText.Dashboard;

/// <summary>
/// Settings as a page inside the dashboard window: microphone, global hotkey, auto-stop on
/// silence, the on-screen indicator, typing speed (WPM), and start-on-login. Save writes into
/// the shared <see cref="AppSettings"/> (and the Run key) and raises <see cref="SettingsSaved"/>.
/// </summary>
internal sealed class SettingsPage : UserControl
{
    private readonly AppSettings _settings;
    private readonly ComboBox _deviceCombo = new() { DropDownStyle = ComboBoxStyle.DropDownList, FlatStyle = FlatStyle.Flat, BackColor = Theme.CardBg, ForeColor = Theme.TextPrimary, DrawMode = DrawMode.OwnerDrawFixed };
    private readonly TextBox _hotkeyBox = new() { ReadOnly = true, Cursor = Cursors.Hand, TextAlign = HorizontalAlignment.Center, BackColor = Theme.CardBg, ForeColor = Theme.TextPrimary, BorderStyle = BorderStyle.FixedSingle };
    private readonly Label _hintLabel = new() { AutoSize = true, ForeColor = Theme.TextSecondary, Location = new Point(20, 130), MaximumSize = new Size(440, 0) };
    private readonly CheckBox _autoStopCheck = new() { Text = "Auto-stop after a pause in speech", AutoSize = true, Location = new Point(20, 168), ForeColor = Theme.TextPrimary };
    private readonly NumericUpDown _silenceUpDown = new() { DecimalPlaces = 1, Minimum = 0.3M, Maximum = 10.0M, Increment = 0.1M, BackColor = Theme.CardBg, ForeColor = Theme.TextPrimary, BorderStyle = BorderStyle.FixedSingle };
    private readonly CheckBox _overlayCheck = new() { Text = "Show on-screen indicator while dictating", AutoSize = true, Location = new Point(20, 228), ForeColor = Theme.TextPrimary };
    private readonly NumericUpDown _wpmUpDown = new() { DecimalPlaces = 0, Minimum = 10, Maximum = 300, Increment = 5, BackColor = Theme.CardBg, ForeColor = Theme.TextPrimary, BorderStyle = BorderStyle.FixedSingle };
    private readonly CheckBox _startupCheck = new() { Text = "Start automatically when I log in", AutoSize = true, Location = new Point(20, 296), ForeColor = Theme.TextPrimary };
    private readonly Label _savedLabel = new() { AutoSize = true, ForeColor = Theme.Accent, Visible = false, Text = "Settings saved ✓", Location = new Point(128, 336) };
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
        BuildUi();
        LoadDevices();
        LoadFromSettings();
    }

    /// <summary>Re-sync the controls if settings changed elsewhere (e.g. a rejected hotkey was reverted).</summary>
    public void ReloadFromSettings()
    {
        _hotkey = _settings.Hotkey;
        LoadDevices();
        LoadFromSettings();
        _savedLabel.Visible = false;
    }

    protected override void OnVisibleChanged(EventArgs e)
    {
        base.OnVisibleChanged(e);
        if (Visible) _savedLabel.Visible = false;
    }

    private void LoadFromSettings()
    {
        _hotkeyBox.Text = _hotkey.Describe();
        _startupCheck.Checked = AutoStart.IsEnabled();
        _autoStopCheck.Checked = _settings.AutoStopEnabled;
        _silenceUpDown.Value = (decimal)Math.Clamp(_settings.AutoStopSilenceSeconds, 0.3, 10.0);
        _silenceUpDown.Enabled = _autoStopCheck.Checked;
        _overlayCheck.Checked = _settings.ShowOverlay;
        _wpmUpDown.Value = (decimal)Math.Clamp(_settings.TypingSpeedWpm, 10, 300);
        UpdateHint();
    }

    private void BuildUi()
    {
        var deviceLabel = new Label { Text = "Microphone:", Location = new Point(20, 20), AutoSize = true, ForeColor = Theme.TextPrimary };
        _deviceCombo.SetBounds(20, 42, 440, 24);
        _deviceCombo.DrawItem += OnDeviceComboDrawItem;

        var hotkeyLabel = new Label { Text = "Dictation hotkey:", Location = new Point(20, 78), AutoSize = true, ForeColor = Theme.TextPrimary };
        _hotkeyBox.SetBounds(20, 100, 440, 26);
        _hotkeyBox.GotFocus += (_, _) => { _hotkeyBox.Text = "Press a key or combination…"; HotkeyCaptureStarted?.Invoke(); };
        _hotkeyBox.LostFocus += (_, _) => { _hotkeyBox.Text = _hotkey.Describe(); HotkeyCaptureEnded?.Invoke(); };

        _autoStopCheck.CheckedChanged += (_, _) => _silenceUpDown.Enabled = _autoStopCheck.Checked;
        var stopAfterLabel = new Label { Text = "Stop after", Location = new Point(40, 196), AutoSize = true, ForeColor = Theme.TextPrimary };
        _silenceUpDown.SetBounds(116, 194, 56, 24);
        var secondsLabel = new Label { Text = "seconds of silence", Location = new Point(178, 196), AutoSize = true, ForeColor = Theme.TextPrimary };

        var wpmLabel = new Label { Text = "Typing speed:", Location = new Point(20, 262), AutoSize = true, ForeColor = Theme.TextPrimary };
        _wpmUpDown.SetBounds(108, 260, 60, 24);
        var wpmSuffix = new Label { Text = "WPM  (used to estimate \"time saved\")", Location = new Point(176, 262), AutoSize = true, ForeColor = Theme.TextSecondary };

        var saveButton = new Button { Text = "Save", Location = new Point(20, 330), Size = new Size(96, 30), FlatStyle = FlatStyle.Flat, BackColor = Theme.Accent, ForeColor = Color.White };
        saveButton.FlatAppearance.BorderSize = 0;
        saveButton.FlatAppearance.MouseOverBackColor = Theme.AccentLight;
        saveButton.Click += OnSave;

        Controls.AddRange(new Control[]
        {
            deviceLabel, _deviceCombo, hotkeyLabel, _hotkeyBox, _hintLabel,
            _autoStopCheck, stopAfterLabel, _silenceUpDown, secondsLabel,
            _overlayCheck, wpmLabel, _wpmUpDown, wpmSuffix,
            _startupCheck, saveButton, _savedLabel,
        });
    }

    private void OnDeviceComboDrawItem(object? sender, DrawItemEventArgs e)
    {
        e.DrawBackground();
        if (e.Index >= 0)
        {
            var selected = (e.State & DrawItemState.Selected) != 0;
            using var backBrush = new SolidBrush(selected ? Theme.NavActiveBg : Theme.CardBg);
            e.Graphics.FillRectangle(backBrush, e.Bounds);
            using var textBrush = new SolidBrush(selected ? Theme.NavActiveText : Theme.TextPrimary);
            using var format = new StringFormat { LineAlignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap };
            var textRect = new Rectangle(e.Bounds.X + 4, e.Bounds.Y, e.Bounds.Width - 6, e.Bounds.Height);
            e.Graphics.DrawString(_deviceCombo.GetItemText(_deviceCombo.Items[e.Index]), e.Font ?? Font, textBrush, textRect, format);
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

    private void OnSave(object? sender, EventArgs e)
    {
        _settings.InputDeviceId = (_deviceCombo.SelectedItem as AudioInputDevice)?.Id;
        _settings.Hotkey = _hotkey;
        _settings.AutoStopEnabled = _autoStopCheck.Checked;
        _settings.AutoStopSilenceSeconds = (double)_silenceUpDown.Value;
        _settings.ShowOverlay = _overlayCheck.Checked;
        _settings.TypingSpeedWpm = (double)_wpmUpDown.Value;
        AutoStart.Apply(_startupCheck.Checked);
        _savedLabel.Visible = true;
        SettingsSaved?.Invoke();
    }
}
