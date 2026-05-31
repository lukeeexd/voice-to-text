using System.Drawing;
using VoiceToText.App;
using VoiceToText.Audio;
using VoiceToText.Hotkeys;

namespace VoiceToText.Settings;

/// <summary>
/// Settings UI: input microphone, global hotkey, auto-stop on silence, and
/// start-on-login. On OK, changes are written into the supplied
/// <see cref="AppSettings"/> (and the Run registry key is updated).
/// </summary>
public sealed class SettingsForm : Form
{
    private readonly AppSettings _settings;
    private readonly ComboBox _deviceCombo = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly TextBox _hotkeyBox = new() { ReadOnly = true, Cursor = Cursors.Hand, TextAlign = HorizontalAlignment.Center };
    private readonly Label _hintLabel = new() { AutoSize = true, ForeColor = SystemColors.GrayText, Location = new Point(16, 126), MaximumSize = new Size(388, 0) };
    private readonly CheckBox _autoStopCheck = new() { Text = "Auto-stop after a pause in speech", AutoSize = true, Location = new Point(16, 162) };
    private readonly NumericUpDown _silenceUpDown = new() { DecimalPlaces = 1, Minimum = 0.3M, Maximum = 10.0M, Increment = 0.1M };
    private readonly CheckBox _startupCheck = new() { Text = "Start automatically when I log in", AutoSize = true, Location = new Point(16, 222) };
    private HotkeyDefinition _hotkey;

    public SettingsForm(AppSettings settings)
    {
        _settings = settings;
        _hotkey = settings.Hotkey;
        BuildUi();
        LoadDevices();
        _hotkeyBox.Text = _hotkey.Describe();
        _startupCheck.Checked = AutoStart.IsEnabled();
        _autoStopCheck.Checked = settings.AutoStopEnabled;
        _silenceUpDown.Value = (decimal)Math.Clamp(settings.AutoStopSilenceSeconds, 0.3, 10.0);
        _silenceUpDown.Enabled = _autoStopCheck.Checked;
        UpdateHint();
    }

    private void BuildUi()
    {
        Text = "Voice to Text — Settings";
        try { Icon = System.Drawing.Icon.ExtractAssociatedIcon(Environment.ProcessPath!); }
        catch { /* no embedded icon (e.g. running under the debugger host) */ }
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(420, 300);

        var deviceLabel = new Label { Text = "Microphone:", Location = new Point(16, 16), AutoSize = true };
        _deviceCombo.SetBounds(16, 38, 388, 24);
        _deviceCombo.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;

        var hotkeyLabel = new Label { Text = "Dictation hotkey:", Location = new Point(16, 74), AutoSize = true };
        _hotkeyBox.SetBounds(16, 96, 388, 26);
        _hotkeyBox.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        _hotkeyBox.GotFocus += (_, _) => _hotkeyBox.Text = "Press a key or combination…";
        _hotkeyBox.LostFocus += (_, _) => _hotkeyBox.Text = _hotkey.Describe();

        _autoStopCheck.CheckedChanged += (_, _) => _silenceUpDown.Enabled = _autoStopCheck.Checked;
        var stopAfterLabel = new Label { Text = "Stop after", Location = new Point(36, 190), AutoSize = true };
        _silenceUpDown.SetBounds(112, 188, 56, 24);
        var secondsLabel = new Label { Text = "seconds of silence", Location = new Point(174, 190), AutoSize = true };

        var okButton = new Button { Text = "Save", DialogResult = DialogResult.OK };
        okButton.SetBounds(228, 258, 84, 30);
        okButton.Click += OnSave;

        var cancelButton = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel };
        cancelButton.SetBounds(320, 258, 84, 30);

        Controls.AddRange(
            deviceLabel, _deviceCombo, hotkeyLabel, _hotkeyBox, _hintLabel,
            _autoStopCheck, stopAfterLabel, _silenceUpDown, secondsLabel,
            _startupCheck, okButton, cancelButton);
        AcceptButton = okButton;
        CancelButton = cancelButton;
    }

    private void LoadDevices()
    {
        _deviceCombo.Items.Clear();
        var devices = AudioDevices.GetInputDevices();
        foreach (var device in devices)
            _deviceCombo.Items.Add(device);

        var current = devices.FirstOrDefault(d => d.Id == _settings.InputDeviceId);
        if (current is not null)
            _deviceCombo.SelectedItem = current;
        else if (_deviceCombo.Items.Count > 0)
            _deviceCombo.SelectedIndex = 0;
    }

    /// <summary>
    /// Capture the hotkey here rather than via TextBox.KeyDown: ProcessCmdKey sees
    /// every key (including Alt/WM_SYSKEYDOWN, function keys, and extra keys that
    /// report a virtual-key code) before dialogs consume them for navigation.
    /// A single key with no modifier is allowed.
    /// </summary>
    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (_hotkeyBox.Focused)
        {
            var key = keyData & Keys.KeyCode;
            var hasModifier = (keyData & (Keys.Control | Keys.Alt | Keys.Shift)) != 0;

            // Reserve bare Esc/Tab/Enter so the dialog stays navigable.
            if (!hasModifier && key is Keys.Escape or Keys.Tab or Keys.Enter)
                return base.ProcessCmdKey(ref msg, keyData);

            var definition = HotkeyDefinition.FromKeyEvent(keyData);
            if (definition is not null)
            {
                _hotkey = definition;
                _hotkeyBox.Text = definition.Describe();
                UpdateHint();
            }
            return true; // swallow (captured combo, or a lone modifier being held)
        }

        return base.ProcessCmdKey(ref msg, keyData);
    }

    private void UpdateHint()
    {
        if (_hotkey.IsRiskyBareKey())
        {
            _hintLabel.ForeColor = Color.FromArgb(200, 80, 0);
            _hintLabel.Text = "⚠ This is a normal typing key — it would be intercepted everywhere. Add Ctrl/Alt/Shift, or use a dedicated key (e.g. F13).";
        }
        else
        {
            _hintLabel.ForeColor = SystemColors.GrayText;
            _hintLabel.Text = "Click the box, then press a single key (e.g. an extra/macro key or F13) or a modifier combo.";
        }
    }

    private void OnSave(object? sender, EventArgs e)
    {
        _settings.InputDeviceId = (_deviceCombo.SelectedItem as AudioInputDevice)?.Id;
        _settings.Hotkey = _hotkey;
        _settings.AutoStopEnabled = _autoStopCheck.Checked;
        _settings.AutoStopSilenceSeconds = (double)_silenceUpDown.Value;
        AutoStart.Apply(_startupCheck.Checked);
    }
}
