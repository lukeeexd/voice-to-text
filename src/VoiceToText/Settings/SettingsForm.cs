using System.Drawing;
using VoiceToText.Audio;
using VoiceToText.Hotkeys;

namespace VoiceToText.Settings;

/// <summary>
/// Minimal settings UI: choose the input microphone and the global hotkey.
/// On OK, the changes are written into the supplied <see cref="AppSettings"/>.
/// </summary>
public sealed class SettingsForm : Form
{
    private readonly AppSettings _settings;
    private readonly ComboBox _deviceCombo = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly TextBox _hotkeyBox = new() { ReadOnly = true, Cursor = Cursors.Hand, TextAlign = HorizontalAlignment.Center };
    private readonly Label _hintLabel = new() { AutoSize = true, ForeColor = SystemColors.GrayText, Location = new Point(16, 130), MaximumSize = new Size(388, 0) };
    private HotkeyDefinition _hotkey;

    public SettingsForm(AppSettings settings)
    {
        _settings = settings;
        _hotkey = settings.Hotkey;
        BuildUi();
        LoadDevices();
        _hotkeyBox.Text = _hotkey.Describe();
        UpdateHint();
    }

    private void BuildUi()
    {
        Text = "Voice to Text — Settings";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(420, 210);

        var deviceLabel = new Label { Text = "Microphone:", Location = new Point(16, 16), AutoSize = true };
        _deviceCombo.SetBounds(16, 38, 388, 24);
        _deviceCombo.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;

        var hotkeyLabel = new Label { Text = "Dictation hotkey:", Location = new Point(16, 78), AutoSize = true };
        _hotkeyBox.SetBounds(16, 100, 388, 26);
        _hotkeyBox.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        _hotkeyBox.GotFocus += (_, _) => _hotkeyBox.Text = "Press a key or combination…";
        _hotkeyBox.LostFocus += (_, _) => _hotkeyBox.Text = _hotkey.Describe();

        var okButton = new Button { Text = "Save", DialogResult = DialogResult.OK };
        okButton.SetBounds(228, 164, 84, 30);
        okButton.Click += OnSave;

        var cancelButton = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel };
        cancelButton.SetBounds(320, 164, 84, 30);

        Controls.AddRange(deviceLabel, _deviceCombo, hotkeyLabel, _hotkeyBox, _hintLabel, okButton, cancelButton);
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
            _hintLabel.Text = $"⚠ \"{_hotkey.Describe()}\" is a normal typing key — binding it will block that key everywhere. Use a dedicated/extra key (e.g. F13) or add Ctrl/Alt/Shift.";
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
    }
}
