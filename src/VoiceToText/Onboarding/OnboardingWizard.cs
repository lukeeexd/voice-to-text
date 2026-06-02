using System.Drawing;
using VoiceToText.Audio;
using VoiceToText.Dashboard;
using VoiceToText.Hotkeys;
using VoiceToText.Settings;
using VoiceToText.Stt;

namespace VoiceToText.Onboarding;

/// <summary>
/// First-run setup wizard: Welcome → Microphone → Hotkey → Done. Configures the mic + hotkey
/// inline and persists them on Finish/Skip. UI only — the tray shows it once (the
/// <see cref="AppSettings.OnboardingCompleted"/> flag) and re-registers the hotkey via
/// the <see cref="Completed"/> event.
/// </summary>
internal sealed class OnboardingWizard : Form
{
    private const int StepCount = 4;

    private readonly AppSettings _settings;
    private int _step;
    private HotkeyDefinition _hotkey;

    private readonly Label _title = new() { AutoSize = true, Location = new Point(24, 20), Font = Theme.Heading, ForeColor = Theme.TextPrimary };
    private readonly Label _progress = new() { AutoSize = true, Location = new Point(24, 54), Font = Theme.Caption, ForeColor = Theme.TextMuted };
    private readonly Panel _content = new() { Location = new Point(24, 86), Size = new Size(488, 240), BackColor = Theme.WindowBg };
    private readonly Panel[] _panels;

    // Step 1 — microphone
    private readonly ComboBox _micCombo = new() { DropDownStyle = ComboBoxStyle.DropDownList, FlatStyle = FlatStyle.Flat, BackColor = Theme.CardBg, ForeColor = Theme.TextPrimary, DrawMode = DrawMode.OwnerDrawFixed, DisplayMember = "Name" };
    // Step 2 — hotkey
    private readonly TextBox _hotkeyBox = new() { ReadOnly = true, Cursor = Cursors.Hand, TextAlign = HorizontalAlignment.Center, BackColor = Theme.CardBg, ForeColor = Theme.TextPrimary, BorderStyle = BorderStyle.FixedSingle };
    private readonly Label _hotkeyHint = new() { AutoSize = false, Size = new Size(470, 52), ForeColor = Theme.TextSecondary, Font = Theme.Caption };
    // Step 4 — done
    private readonly Label _doneBody = new() { AutoSize = false, Size = new Size(470, 110), ForeColor = Theme.TextSecondary, Font = Theme.NavItem };
    private readonly Label _doneStatus = new() { AutoSize = false, Size = new Size(470, 40), Font = Theme.Caption };

    private readonly Button _back = new() { Text = "Back", FlatStyle = FlatStyle.Flat, Size = new Size(90, 32), BackColor = Theme.CardBg, ForeColor = Theme.NavActiveText };
    private readonly Button _next = new() { Text = "Next", FlatStyle = FlatStyle.Flat, Size = new Size(110, 32), BackColor = Theme.Accent, ForeColor = Color.White };
    private readonly LinkLabel _skip = new() { Text = "Skip", AutoSize = true, LinkColor = Theme.TextMuted, ActiveLinkColor = Theme.AccentLight, Font = Theme.Caption };

    /// <summary>Raised after the wizard saves the chosen mic + hotkey; the host re-registers the hotkey.</summary>
    public event Action? Completed;

    public OnboardingWizard(AppSettings settings)
    {
        _settings = settings;
        _hotkey = settings.Hotkey;

        Text = "Set up Voice to Text";
        BackColor = Theme.WindowBg;
        ForeColor = Theme.TextPrimary;
        ClientSize = new Size(536, 404);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        try { Icon = Icon.ExtractAssociatedIcon(Environment.ProcessPath!); }
        catch { /* no embedded icon under the debugger host */ }

        _panels = new[] { BuildWelcome(), BuildMic(), BuildHotkey(), BuildDone() };
        foreach (var p in _panels) { p.Dock = DockStyle.Fill; p.Visible = false; _content.Controls.Add(p); }

        _back.FlatAppearance.BorderColor = Theme.CardBorder;
        _back.Location = new Point(24, 354);
        _back.Click += (_, _) => ShowStep(_step - 1);

        _next.FlatAppearance.BorderSize = 0;
        _next.FlatAppearance.MouseOverBackColor = Theme.AccentLight;
        _next.Location = new Point(422, 354);
        _next.Click += (_, _) => { if (_step < StepCount - 1) ShowStep(_step + 1); else Finish(); };

        _skip.Location = new Point(128, 362);
        _skip.LinkClicked += (_, _) => Finish();

        Controls.AddRange(new Control[] { _title, _progress, _content, _back, _next, _skip });
        ShowStep(0);
    }

    private Panel BuildWelcome()
    {
        var p = new Panel { BackColor = Theme.WindowBg };
        p.Controls.Add(new Label
        {
            AutoSize = false, Location = new Point(0, 8), Size = new Size(484, 200),
            ForeColor = Theme.TextSecondary, Font = Theme.NavItem,
            Text = "Voice to Text turns your speech into typed text in any app — fully local and offline, so nothing ever leaves your PC.\r\n\r\nThis quick setup picks your microphone and dictation hotkey. It only takes a few seconds.",
        });
        return p;
    }

    private Panel BuildMic()
    {
        var p = new Panel { BackColor = Theme.WindowBg };
        var label = new Label { Text = "Choose your microphone:", AutoSize = true, Location = new Point(0, 8), ForeColor = Theme.TextPrimary };
        _micCombo.SetBounds(0, 36, 484, 24);
        _micCombo.DrawItem += OnComboDrawItem;
        foreach (var d in AudioDevices.GetInputDevices())
            _micCombo.Items.Add(d);
        var current = _micCombo.Items.Cast<AudioInputDevice>().FirstOrDefault(d => d.Id == _settings.InputDeviceId);
        if (current is not null) _micCombo.SelectedItem = current;
        else if (_micCombo.Items.Count > 0) _micCombo.SelectedIndex = 0;
        var note = new Label { Text = "You can change this any time in Settings.", AutoSize = true, Location = new Point(0, 70), ForeColor = Theme.TextMuted, Font = Theme.Caption };
        p.Controls.AddRange(new Control[] { label, _micCombo, note });
        return p;
    }

    private Panel BuildHotkey()
    {
        var p = new Panel { BackColor = Theme.WindowBg };
        var label = new Label { Text = "Set your dictation hotkey:", AutoSize = true, Location = new Point(0, 8), ForeColor = Theme.TextPrimary };
        _hotkeyBox.SetBounds(0, 36, 240, 26);
        _hotkeyBox.Text = _hotkey.Describe();
        _hotkeyBox.GotFocus += (_, _) => _hotkeyBox.Text = "Press a key or combination…";
        _hotkeyBox.LostFocus += (_, _) => _hotkeyBox.Text = _hotkey.Describe();
        _hotkeyHint.Location = new Point(0, 74);
        UpdateHotkeyHint();
        p.Controls.AddRange(new Control[] { label, _hotkeyBox, _hotkeyHint });
        return p;
    }

    private Panel BuildDone()
    {
        var p = new Panel { BackColor = Theme.WindowBg };
        var heading = new Label { Text = "You're all set 🎉", AutoSize = true, Location = new Point(0, 8), Font = Theme.LabelBold, ForeColor = Theme.TextPrimary };
        _doneBody.Location = new Point(0, 40);
        _doneStatus.Location = new Point(0, 168);
        p.Controls.AddRange(new Control[] { heading, _doneBody, _doneStatus });
        return p;
    }

    private void ShowStep(int i)
    {
        _step = Math.Clamp(i, 0, StepCount - 1);
        for (var k = 0; k < _panels.Length; k++)
            _panels[k].Visible = k == _step;

        _title.Text = _step switch { 0 => "Welcome to Voice to Text", 1 => "Microphone", 2 => "Dictation hotkey", _ => "All set" };
        _progress.Text = $"Step {_step + 1} of {StepCount}";
        _back.Visible = _step > 0;
        _next.Text = _step == StepCount - 1 ? "Finish" : "Next";
        _skip.Visible = _step < StepCount - 1;

        if (_step == StepCount - 1)
            RefreshDoneStep();
    }

    /// <summary>Test hook: advance one step without ever finishing/saving (used by the --dashwindow smoke).</summary>
    internal void AdvanceForSmoke()
    {
        if (_step < StepCount - 1)
            ShowStep(_step + 1);
    }

    private void RefreshDoneStep()
    {
        _doneBody.Text = $"Press {_hotkey.Describe()}, speak, then stop — your words are typed into whatever app you're focused on.\r\n\r\nYou can change your microphone, hotkey, and more in Settings any time.";
        bool ready = false;
        try { ready = ModelManager.IsModelPresent(_settings.ModelType); }
        catch { /* best-effort status only */ }
        _doneStatus.ForeColor = ready ? Color.FromArgb(0x7B, 0xD8, 0x8F) : Theme.Warning;
        _doneStatus.Text = ready
            ? "Speech model ready ✓"
            : "Downloading the speech model now (one-time, ~1.5 GB). Dictation works as soon as it's ready.";
    }

    private void UpdateHotkeyHint()
    {
        if (_hotkey.IsRiskyBareKey())
        {
            _hotkeyHint.ForeColor = Theme.Warning;
            _hotkeyHint.Text = "⚠ That's a normal typing key — it would be intercepted everywhere. Add Ctrl/Alt/Shift, or use a dedicated key like F13.";
        }
        else
        {
            _hotkeyHint.ForeColor = Theme.TextSecondary;
            _hotkeyHint.Text = "Click the box, then press a single key (e.g. F13) or a combo like Ctrl + Shift + Space.";
        }
    }

    // Capture the hotkey while the box is focused (mirrors the Settings page's capture rules).
    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (_hotkeyBox.Focused)
        {
            var key = keyData & Keys.KeyCode;
            var hasModifier = (keyData & (Keys.Control | Keys.Alt | Keys.Shift)) != 0;
            if (!hasModifier && key is Keys.Escape or Keys.Tab or Keys.Enter)
                return base.ProcessCmdKey(ref msg, keyData);

            var definition = HotkeyDefinition.FromKeyEvent(keyData);
            if (definition is not null)
            {
                _hotkey = definition;
                _hotkeyBox.Text = _hotkey.Describe();
                UpdateHotkeyHint();
            }
            return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    private void OnComboDrawItem(object? sender, DrawItemEventArgs e)
    {
        var combo = (ComboBox)sender!;
        e.DrawBackground();
        if (e.Index >= 0)
        {
            var selected = (e.State & DrawItemState.Selected) != 0;
            using var back = new SolidBrush(selected ? Theme.NavActiveBg : Theme.CardBg);
            e.Graphics.FillRectangle(back, e.Bounds);
            using var text = new SolidBrush(selected ? Theme.NavActiveText : Theme.TextPrimary);
            using var fmt = new StringFormat { LineAlignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap };
            var rect = new Rectangle(e.Bounds.X + 4, e.Bounds.Y, e.Bounds.Width - 6, e.Bounds.Height);
            e.Graphics.DrawString(combo.GetItemText(combo.Items[e.Index]), e.Font ?? Font, text, rect, fmt);
        }
        e.DrawFocusRectangle();
    }

    private void Finish()
    {
        _settings.InputDeviceId = (_micCombo.SelectedItem as AudioInputDevice)?.Id;
        _settings.Hotkey = _hotkey;
        _settings.OnboardingCompleted = true;
        _settings.Save();
        Completed?.Invoke();
        Close();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        DarkTitleBar.Apply(Handle);
    }
}
