# Push-to-Talk Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a hold-to-talk activation mode (hold the hotkey to record, release to transcribe) selectable in Settings, alongside the existing press-to-toggle.

**Architecture:** Keep `RegisterHotKey` for the key-down press; detect release by polling `GetAsyncKeyState` on a ~40 ms WinForms timer in `HotkeyManager`, which raises a new `Released` event. The tray wires press→start / release→stop in hold mode and disables silence-auto-stop. A Settings dropdown picks the mode.

**Tech Stack:** C# / .NET 10 (`net10.0-windows`), WinForms, Win32 (`user32` `GetAsyncKeyState`).

---

## Conventions (read first)

**Background-isolation guard: `Write`/`Edit` tools cannot modify repo files.** Create/replace a repo file by `Write`-ing to `C:\Users\Luke\.claude\jobs\f39a9536\tmp\stage\<filename>` then Bash `cp "C:/Users/Luke/.claude/jobs/f39a9536/tmp/stage/<filename>" "<repo path>"`. For surgical one-line edits use Bash `perl -0pi -e`. For a multi-line block replacement, use the **literal-replace helper** (no regex escaping) — Write the exact OLD block to `stage/old.txt` and the NEW block to `stage/new.txt`, then:
```bash
perl -e 'local $/; open my $fh,"<:raw",$ARGV[0]; my $c=<$fh>; close $fh; open my $oh,"<:raw",$ARGV[1]; my $old=<$oh>; close $oh; open my $nh,"<:raw",$ARGV[2]; my $new=<$nh>; close $nh; my $i=index($c,$old); die "OLD NOT FOUND\n" if $i<0; substr($c,$i,length($old))=$new; open my $wh,">:raw",$ARGV[0]; print $wh $c; close $wh; print "replaced at $i\n";' "<repo file>" "C:/Users/Luke/.claude/jobs/f39a9536/tmp/stage/old.txt" "C:/Users/Luke/.claude/jobs/f39a9536/tmp/stage/new.txt"
```
Commits via Bash `git` on `main`.

**Build (PowerShell tool) — clean (`--no-incremental`):**
```powershell
& "$env:USERPROFILE/.dotnet/dotnet.exe" build VoiceToText.slnx -c Debug --no-incremental
```
Expected `0 Warning(s)`, `0 Error(s)`. (`HotkeyManager` is a plain class, not a `Control`, so its public `HoldToTalk` property does NOT trigger WFO1000.)

**Smoke (WinExe writes a file), repo root `D:\ClaudeCode\voice-to-text`:**
```powershell
$env:DOTNET_ROOT = "$env:USERPROFILE/.dotnet"
Start-Process "src/VoiceToText/bin/Debug/net10.0-windows/VoiceToText.exe" -ArgumentList "--dashwindow" -WorkingDirectory $PWD -Wait ; Get-Content dashwindow-output.txt
```

---

## File Structure

**Modify**
- `src/VoiceToText/Hotkeys/NativeHotkeys.cs` — add `GetAsyncKeyState`.
- `src/VoiceToText/Hotkeys/HotkeyManager.cs` — `HoldToTalk`, stored VK, `Released`, poll timer + backstop (full rewrite).
- `src/VoiceToText/Settings/AppSettings.cs` — `HoldToTalk`.
- `src/VoiceToText/App/TrayApplicationContext.cs` — wire mode + release.
- `src/VoiceToText/Dashboard/SettingsPage.cs` — Activation dropdown + re-flow + greying (full rewrite).
- `src/VoiceToText/VoiceToText.csproj` — `<Version>0.6.6</Version>` (ship).

---

## Task 1: Hotkey release detection (`NativeHotkeys` + `HotkeyManager`)

**Files:**
- Modify (full replace): `src/VoiceToText/Hotkeys/NativeHotkeys.cs`, `src/VoiceToText/Hotkeys/HotkeyManager.cs`

- [ ] **Step 1: Replace `NativeHotkeys.cs`**

Stage → `cp` to `src/VoiceToText/Hotkeys/NativeHotkeys.cs`:

```csharp
using System.Runtime.InteropServices;

namespace VoiceToText.Hotkeys;

internal static partial class NativeHotkeys
{
    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool UnregisterHotKey(IntPtr hWnd, int id);

    // High bit of the result is set while the key is down. Used to detect hold-to-talk release.
    [LibraryImport("user32.dll")]
    public static partial short GetAsyncKeyState(int vKey);
}
```

- [ ] **Step 2: Replace `HotkeyManager.cs`**

Stage → `cp` to `src/VoiceToText/Hotkeys/HotkeyManager.cs`:

```csharp
using VoiceToText.App;

namespace VoiceToText.Hotkeys;

/// <summary>
/// Registers a single global hotkey via RegisterHotKey on the hidden window and raises
/// <see cref="Pressed"/> on key-down. In hold-to-talk mode it polls the key with
/// GetAsyncKeyState (RegisterHotKey gives no key-up) and raises <see cref="Released"/> when it
/// goes up — or after a safety backstop. No admin rights, no global keyboard hook.
/// </summary>
public sealed class HotkeyManager : IDisposable
{
    private const int HotkeyId = 0xB1A5;
    private const int PollIntervalMs = 40;
    private const int MaxHoldMs = 120_000; // stuck-key / missed-release backstop

    private readonly HiddenWindow _window;
    private readonly System.Windows.Forms.Timer _pollTimer;
    private bool _registered;
    private uint _vk;
    private int _holdStartTick;
    private bool _holdToTalk;

    /// <summary>Raised on the UI thread when the hotkey is pressed (key-down).</summary>
    public event Action? Pressed;

    /// <summary>Raised on the UI thread when a held hotkey is released (hold-to-talk only).</summary>
    public event Action? Released;

    /// <summary>When true, a press starts polling for release and raises <see cref="Released"/>.</summary>
    public bool HoldToTalk
    {
        get => _holdToTalk;
        set
        {
            _holdToTalk = value;
            if (!value) _pollTimer.Stop();
        }
    }

    internal HotkeyManager(HiddenWindow window)
    {
        _window = window;
        _window.HotkeyMessageReceived += OnHotkeyMessage;
        _pollTimer = new System.Windows.Forms.Timer { Interval = PollIntervalMs };
        _pollTimer.Tick += OnPollTick;
    }

    private void OnHotkeyMessage(int id)
    {
        if (id != HotkeyId)
            return;

        Pressed?.Invoke();

        if (_holdToTalk && !_pollTimer.Enabled)
        {
            _holdStartTick = Environment.TickCount;
            _pollTimer.Start();
        }
    }

    private void OnPollTick(object? sender, EventArgs e)
    {
        var keyUp = (NativeHotkeys.GetAsyncKeyState((int)_vk) & 0x8000) == 0;
        var timedOut = Environment.TickCount - _holdStartTick > MaxHoldMs;
        if (keyUp || timedOut)
        {
            _pollTimer.Stop();
            Released?.Invoke();
        }
    }

    /// <summary>
    /// Register (replacing any previous registration). Returns false if the OS rejected the
    /// combo — usually because another app already owns it.
    /// </summary>
    public bool Register(HotkeyDefinition hotkey)
    {
        Unregister();
        _vk = hotkey.VirtualKey;
        var modifiers = hotkey.Modifiers | HotkeyDefinition.ModNoRepeat;
        _registered = NativeHotkeys.RegisterHotKey(_window.Handle, HotkeyId, modifiers, hotkey.VirtualKey);
        return _registered;
    }

    public void Unregister()
    {
        _pollTimer.Stop();
        if (!_registered)
            return;
        NativeHotkeys.UnregisterHotKey(_window.Handle, HotkeyId);
        _registered = false;
    }

    public void Dispose()
    {
        _pollTimer.Stop();
        _pollTimer.Dispose();
        Unregister();
        _window.HotkeyMessageReceived -= OnHotkeyMessage;
    }
}
```

- [ ] **Step 3: Build**

```powershell
& "$env:USERPROFILE/.dotnet/dotnet.exe" build VoiceToText.slnx -c Debug --no-incremental
```
Expected: `Build succeeded`, 0/0. (Existing toggle behavior is unchanged — `HoldToTalk` defaults false, so the poll timer never starts.)

- [ ] **Step 4: Commit**

```bash
cd "D:/ClaudeCode/voice-to-text" && git add -A && git commit -m "HotkeyManager: poll-based release detection for hold-to-talk" -m "Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

## Task 2: `AppSettings.HoldToTalk`

**Files:**
- Modify: `src/VoiceToText/Settings/AppSettings.cs`

- [ ] **Step 1: Add the field after `SpokenCommandsEnabled`**

```bash
cd "D:/ClaudeCode/voice-to-text" && perl -0pi -e 's{    public bool SpokenCommandsEnabled \{ get; set; \} = true;\n}{    public bool SpokenCommandsEnabled { get; set; } = true;\n\n    /// <summary>Hold the hotkey to record (release to stop) instead of press-to-toggle.</summary>\n    public bool HoldToTalk { get; set; } = false;\n}' src/VoiceToText/Settings/AppSettings.cs && grep -n "HoldToTalk" src/VoiceToText/Settings/AppSettings.cs
```
Expected: prints the new `HoldToTalk` property line. (If `SpokenCommandsEnabled` isn't found, the text-rules feature's field differs — open the file and add `public bool HoldToTalk { get; set; } = false;` among the other auto-properties.)

- [ ] **Step 2: Build**

```powershell
& "$env:USERPROFILE/.dotnet/dotnet.exe" build VoiceToText.slnx -c Debug --no-incremental
```
Expected: 0/0.

- [ ] **Step 3: Commit**

```bash
cd "D:/ClaudeCode/voice-to-text" && git add -A && git commit -m "AppSettings: HoldToTalk activation flag" -m "Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

## Task 3: Tray orchestration

**Files:**
- Modify: `src/VoiceToText/App/TrayApplicationContext.cs`

Read the file first to confirm each anchor. Four edits:

- [ ] **Step 1 (Edit A — ctor): wire Released + set HoldToTalk**

```bash
cd "D:/ClaudeCode/voice-to-text" && perl -0pi -e 's{        _hotkeys\.Pressed \+= OnHotkeyPressed;\n        RegisterHotkey\(\);\n}{        _hotkeys.Pressed += OnHotkeyPressed;\n        _hotkeys.Released += OnHotkeyReleased;\n        RegisterHotkey();\n        _hotkeys.HoldToTalk = _settings.HoldToTalk;\n}' src/VoiceToText/App/TrayApplicationContext.cs && grep -n "_hotkeys.Released += OnHotkeyReleased;\|_hotkeys.HoldToTalk = _settings.HoldToTalk;" src/VoiceToText/App/TrayApplicationContext.cs
```
Expected: prints both new lines (the ctor one; the second grep match in OnSettingsSaved comes in Edit D).

- [ ] **Step 2 (Edit B — replace `OnHotkeyPressed` + add `OnHotkeyReleased`)**

Write the exact CURRENT method to `stage/old.txt`:
```csharp
    private void OnHotkeyPressed()
    {
        if (_busy)
            return;

        switch (_state)
        {
            case AppState.Idle:
                StartRecording();
                break;
            case AppState.Recording:
                _ = StopAndTranscribeAsync();
                break;
        }
    }
```
Write the replacement to `stage/new.txt`:
```csharp
    private void OnHotkeyPressed()
    {
        if (_busy)
            return;

        // Hold-to-talk: a press only starts; release (OnHotkeyReleased) stops.
        if (_settings.HoldToTalk)
        {
            if (_state == AppState.Idle)
                StartRecording();
            return;
        }

        // Press-to-toggle.
        switch (_state)
        {
            case AppState.Idle:
                StartRecording();
                break;
            case AppState.Recording:
                _ = StopAndTranscribeAsync();
                break;
        }
    }

    private void OnHotkeyReleased()
    {
        if (_settings.HoldToTalk && _state == AppState.Recording && !_busy)
            _ = StopAndTranscribeAsync();
    }
```
Then run the literal-replace helper (from Conventions) against `src/VoiceToText/App/TrayApplicationContext.cs`. Expected: `replaced at <n>`. Verify: `grep -n "private void OnHotkeyReleased" src/VoiceToText/App/TrayApplicationContext.cs`.

- [ ] **Step 3 (Edit C — `StartRecording` auto-stop)**

```bash
cd "D:/ClaudeCode/voice-to-text" && perl -0pi -e 's{            _audio\.Start\(_settings\.InputDeviceId, _settings\.AutoStopEnabled, _settings\.AutoStopSilenceSeconds\);}{            var autoStop = !_settings.HoldToTalk && _settings.AutoStopEnabled;\n            _audio.Start(_settings.InputDeviceId, autoStop, _settings.AutoStopSilenceSeconds);}' src/VoiceToText/App/TrayApplicationContext.cs && grep -n "var autoStop = !_settings.HoldToTalk" src/VoiceToText/App/TrayApplicationContext.cs
```
Expected: prints the new `var autoStop` line. (In hold mode, silence auto-stop is off — release stops.)

- [ ] **Step 4 (Edit D — `OnSettingsSaved` re-applies HoldToTalk)**

```bash
cd "D:/ClaudeCode/voice-to-text" && perl -0pi -e 's{        ApplyOverlaySetting\(\);\n}{        ApplyOverlaySetting();\n        _hotkeys.HoldToTalk = _settings.HoldToTalk;\n}' src/VoiceToText/App/TrayApplicationContext.cs && grep -nc "_hotkeys.HoldToTalk = _settings.HoldToTalk;" src/VoiceToText/App/TrayApplicationContext.cs
```
Expected: count `2` (the ctor line from Edit A + this OnSettingsSaved line). `ApplyOverlaySetting();` occurs once (in `OnSettingsSaved`), so this matches uniquely.

- [ ] **Step 5: Build + smoke (no regression)**

```powershell
& "$env:USERPROFILE/.dotnet/dotnet.exe" build VoiceToText.slnx -c Debug --no-incremental
$env:DOTNET_ROOT = "$env:USERPROFILE/.dotnet"
Start-Process "src/VoiceToText/bin/Debug/net10.0-windows/VoiceToText.exe" -ArgumentList "--dashwindow" -WorkingDirectory $PWD -Wait ; Get-Content dashwindow-output.txt
```
Expected: `Build succeeded` 0/0; `DASH WINDOW OK ...`.

- [ ] **Step 6: Commit**

```bash
cd "D:/ClaudeCode/voice-to-text" && git add -A && git commit -m "Tray: hold-to-talk press/release orchestration; auto-stop off in hold" -m "Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

## Task 4: Settings "Activation" dropdown (full `SettingsPage.cs` rewrite)

**Files:**
- Modify (full replace): `src/VoiceToText/Dashboard/SettingsPage.cs`

Adds the `_activationCombo` (dark, owner-drawn), an "Activation" label under the hotkey hint, re-flows every control from auto-stop down by +60 px, replaces the auto-stop enable wiring with a shared `UpdateAutoStopEnabled()` helper (greys auto-stop + silence when Hold is selected), and reads/writes `HoldToTalk`.

- [ ] **Step 1: Replace the file**

Stage → `cp` to `src/VoiceToText/Dashboard/SettingsPage.cs`, exact content:

```csharp
using System.Drawing;
using VoiceToText.App;
using VoiceToText.Audio;
using VoiceToText.Hotkeys;
using VoiceToText.Settings;
using VoiceToText.Stt;

namespace VoiceToText.Dashboard;

/// <summary>
/// Settings as a page inside the dashboard window: microphone, speech model, global hotkey,
/// activation mode, auto-stop on silence, the on-screen indicator, typing speed (WPM), automatic
/// updates, and start-on-login. Save writes into the shared <see cref="AppSettings"/> (and the
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
    private readonly NumericUpDown _wpmUpDown = new() { DecimalPlaces = 0, Minimum = 10, Maximum = 300, Increment = 5, BackColor = Theme.CardBg, ForeColor = Theme.TextPrimary, BorderStyle = BorderStyle.FixedSingle };
    private readonly CheckBox _autoUpdateCheck = new() { Text = "Automatically check for updates on startup", AutoSize = true, Location = new Point(20, 412), ForeColor = Theme.TextPrimary };
    private readonly TextBox _updateFolderBox = new() { BackColor = Theme.CardBg, ForeColor = Theme.TextPrimary, BorderStyle = BorderStyle.FixedSingle };
    private readonly CheckBox _startupCheck = new() { Text = "Start automatically when I log in", AutoSize = true, Location = new Point(20, 512), ForeColor = Theme.TextPrimary };
    private readonly Label _savedLabel = new() { AutoSize = true, ForeColor = Theme.Accent, Visible = false, Text = "Settings saved ✓", Location = new Point(126, 558) };
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
        if (Visible) _savedLabel.Visible = false;
    }

    private void LoadFromSettings()
    {
        _hotkeyBox.Text = _hotkey.Describe();
        _startupCheck.Checked = AutoStart.IsEnabled();
        _activationCombo.SelectedIndex = _settings.HoldToTalk ? 1 : 0;
        _autoStopCheck.Checked = _settings.AutoStopEnabled;
        _silenceUpDown.Value = (decimal)Math.Clamp(_settings.AutoStopSilenceSeconds, 0.3, 10.0);
        _overlayCheck.Checked = _settings.ShowOverlay;
        _wpmUpDown.Value = (decimal)Math.Clamp(_settings.TypingSpeedWpm, 10, 300);
        _autoUpdateCheck.Checked = _settings.AutoUpdateEnabled;
        _updateFolderBox.Text = _settings.UpdateFeedFolder;
        UpdateAutoStopEnabled();
        UpdateHint();
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

        var wpmLabel = new Label { Text = "Typing speed:", Location = new Point(20, 376), AutoSize = true, ForeColor = Theme.TextPrimary };
        _wpmUpDown.SetBounds(108, 374, 60, 24);
        var wpmSuffix = new Label { Text = "WPM  (used to estimate \"time saved\")", Location = new Point(176, 376), AutoSize = true, ForeColor = Theme.TextSecondary };

        var updateFolderLabel = new Label { Text = "Update folder:", Location = new Point(20, 446), AutoSize = true, ForeColor = Theme.TextPrimary };
        _updateFolderBox.SetBounds(110, 444, 280, 24);
        var browseButton = new Button { Text = "Browse…", Location = new Point(396, 443), Size = new Size(64, 26), FlatStyle = FlatStyle.Flat, BackColor = Theme.CardBg, ForeColor = Theme.TextPrimary };
        browseButton.FlatAppearance.BorderColor = Theme.CardBorder;
        browseButton.Click += OnBrowseUpdateFolder;
        var updateNote = new Label { Text = "Updates run an installer from this folder — only enable this for a folder you trust.", Location = new Point(20, 474), AutoSize = true, ForeColor = Theme.Warning, MaximumSize = new Size(440, 0) };

        var saveButton = new Button { Text = "Save", Location = new Point(20, 552), Size = new Size(96, 30), FlatStyle = FlatStyle.Flat, BackColor = Theme.Accent, ForeColor = Color.White };
        saveButton.FlatAppearance.BorderSize = 0;
        saveButton.FlatAppearance.MouseOverBackColor = Theme.AccentLight;
        saveButton.Click += OnSave;

        Controls.AddRange(new Control[]
        {
            deviceLabel, _deviceCombo, modelLabel, _modelCombo,
            hotkeyLabel, _hotkeyBox, _hintLabel,
            activationLabel, _activationCombo,
            _autoStopCheck, stopAfterLabel, _silenceUpDown, secondsLabel,
            _overlayCheck, wpmLabel, _wpmUpDown, wpmSuffix,
            _autoUpdateCheck, updateFolderLabel, _updateFolderBox, browseButton, updateNote,
            _startupCheck, saveButton, _savedLabel,
        });
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
        if (_modelCombo.SelectedItem is ModelOption model)
            _settings.ModelType = model.Type;
        _settings.Hotkey = _hotkey;
        _settings.HoldToTalk = _activationCombo.SelectedIndex == 1;
        _settings.AutoStopEnabled = _autoStopCheck.Checked;
        _settings.AutoStopSilenceSeconds = (double)_silenceUpDown.Value;
        _settings.ShowOverlay = _overlayCheck.Checked;
        _settings.TypingSpeedWpm = (double)_wpmUpDown.Value;
        _settings.AutoUpdateEnabled = _autoUpdateCheck.Checked;
        _settings.UpdateFeedFolder = _updateFolderBox.Text.Trim();
        _settings.UpdateConsentAccepted = _autoUpdateCheck.Checked;
        AutoStart.Apply(_startupCheck.Checked);
        _savedLabel.Visible = true;
        SettingsSaved?.Invoke();
    }
}
```

- [ ] **Step 2: Clean build**

```powershell
& "$env:USERPROFILE/.dotnet/dotnet.exe" build VoiceToText.slnx -c Debug --no-incremental
```
Expected: `Build succeeded`, 0/0.

- [ ] **Step 3: Smoke (paints the Settings page incl. the new dropdown)**

```powershell
$env:DOTNET_ROOT = "$env:USERPROFILE/.dotnet"
Start-Process "src/VoiceToText/bin/Debug/net10.0-windows/VoiceToText.exe" -ArgumentList "--dashwindow" -WorkingDirectory $PWD -Wait
Get-Content dashwindow-output.txt
```
Expected: `DASH WINDOW OK ...`. If `ERROR: ...`, fix before committing.

- [ ] **Step 4: Commit**

```bash
cd "D:/ClaudeCode/voice-to-text" && git add -A && git commit -m "Settings: Activation dropdown (press-to-toggle / hold-to-talk)" -m "Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

## Task 5: Manual verification

**Files:** none.

- [ ] **Step 1: Build + launch**

```powershell
& "$env:USERPROFILE/.dotnet/dotnet.exe" build VoiceToText.slnx -c Debug --no-incremental
$env:DOTNET_ROOT = "$env:USERPROFILE/.dotnet"
Start-Process "src/VoiceToText/bin/Debug/net10.0-windows/VoiceToText.exe"
```

- [ ] **Step 2: Checklist**
  - Settings page shows an **Activation** dropdown under the hotkey hint; the rest of the page (auto-stop, overlay, typing speed, updates, startup, Save) is shifted down and nothing overlaps; Save sits fully on the page.
  - Select **Hold to talk** → the auto-stop checkbox + silence spinner **grey out**. Save.
  - **Hold** F13 → recording starts (overlay shows recording) and continues *while held*, not auto-stopping on a pause; **release** → it stops and transcribes/pastes. A quick tap → nothing pasted.
  - Select **Press to toggle**, Save → press starts, press stops (as before); auto-stop controls re-enable and the silence spinner follows the checkbox.
  - Hotkey capture still works (click the box, press a key) and dictation re-registers after Save.

- [ ] **Step 3: Record result.** Fix any issue (stage→cp / perl), clean-build, re-verify, commit.

---

## Task 6: Ship v0.6.6

**Feed population MUST run in the foreground session.**

- [ ] **Step 1: Bump version**

```bash
cd "D:/ClaudeCode/voice-to-text" && perl -0pi -e 's{<Version>0\.6\.5</Version>}{<Version>0.6.6</Version>}' src/VoiceToText/VoiceToText.csproj && grep -n "<Version>" src/VoiceToText/VoiceToText.csproj
```
Expected: `<Version>0.6.6</Version>`.

- [ ] **Step 2: Publish + installer**

```powershell
& "D:/ClaudeCode/voice-to-text/publish.ps1" | Select-Object -Last 1
(Get-Item "D:/ClaudeCode/voice-to-text/publish/VoiceToText.exe").VersionInfo.ProductVersion   # expect 0.6.6
& "C:/Users/Luke/.claude/jobs/f39a9536/tmp/innosetup/tools/ISCC.exe" "D:/ClaudeCode/voice-to-text/installer/VoiceToText.iss" | Select-Object -Last 2
Test-Path "D:/ClaudeCode/voice-to-text/installer/Output/VoiceToText-Setup.exe"   # expect True
```

- [ ] **Step 3: Copy to feed + write latest.json (Bash, foreground)**

```bash
cd "D:/ClaudeCode/voice-to-text"
FEED="D:/ClaudeCode/VoiceToText-Releases"
cp "installer/Output/VoiceToText-Setup.exe" "$FEED/VoiceToText-Setup-0.6.6.exe"
SHA=$(sha256sum "$FEED/VoiceToText-Setup-0.6.6.exe" | cut -d' ' -f1)
TS=$(date -u +"%Y-%m-%dT%H:%M:%SZ")
cat > "$FEED/latest.json" <<EOF
{
    "Version": "0.6.6",
    "SetupFileName": "VoiceToText-Setup-0.6.6.exe",
    "Sha256": "$SHA",
    "ReleaseNotes": "New activation mode: hold-to-talk (hold the hotkey to record, release to transcribe), selectable in Settings alongside press-to-toggle.",
    "Mandatory": false,
    "ReleasedUtc": "$TS"
}
EOF
cat "$FEED/latest.json"; echo "verify:"; sha256sum "$FEED/VoiceToText-Setup-0.6.6.exe" | cut -d' ' -f1
```
Expected: `latest.json` shows `"Version": "0.6.6"` and the printed SHA matches its `Sha256`.

- [ ] **Step 4: Commit**

```bash
cd "D:/ClaudeCode/voice-to-text" && git add -A && git commit -m "v0.6.6: push-to-talk (hold-to-talk) activation mode" -m "Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

## Self-Review

**1. Spec coverage:**
- `GetAsyncKeyState` P/Invoke → Task 1. ✓
- `HotkeyManager`: `HoldToTalk`, stored VK, `Released`, poll timer + 120s backstop, timer stop on Unregister/Dispose/HoldToTalk=false → Task 1. ✓
- `AppSettings.HoldToTalk = false` → Task 2. ✓
- Tray: set HoldToTalk at startup + OnSettingsSaved, subscribe Released, hold-vs-toggle press, OnHotkeyReleased, auto-stop off in hold → Task 3. ✓
- Settings: Activation dropdown (shared owner-draw), greying of auto-stop+silence in hold, load/save, re-flow → Task 4. ✓
- No new headless test (Win32 polling + UI); build + `--dashwindow` smoke + manual → Tasks 3-5. ✓
- Ship v0.6.6 from foreground → Task 6. ✓

**2. Placeholder scan:** No TBD/TODO; every step has full code/commands + expected output.

**3. Type consistency:** `HotkeyManager.HoldToTalk` (bool prop), `Released` (event Action?), `NativeHotkeys.GetAsyncKeyState(int)`, `OnHotkeyReleased`, `_settings.HoldToTalk`, `_activationCombo` (index 1 = hold), `UpdateAutoStopEnabled()` are used consistently across Tasks 1–4 and match the current code read from the repo (HotkeyManager, NativeHotkeys, SettingsPage, TrayApplicationContext ctor/OnHotkeyPressed/StartRecording/OnSettingsSaved). The SettingsPage rewrite preserves every existing field/method (model picker, update controls, hotkey capture, OnComboDrawItem) and only adds the activation control + shifts Y-coordinates +60 from auto-stop down.
