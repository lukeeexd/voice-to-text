# Settings: Update Controls + Model Picker — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a Whisper model picker and update-folder/auto-update controls to the Settings page, reloading the STT engine when the model changes.

**Architecture:** A new pure `ModelOption` ladder feeds a dark owner-drawn combo on `SettingsPage` (which also gains update-settings controls); `TrayApplicationContext` detects a `ModelType` change on save and swaps the `WhisperSttEngine` when idle.

**Tech Stack:** C# / .NET 10 (`net10.0-windows`), WinForms, Whisper.net 1.9.0.

---

## Conventions (read first — they apply to every task)

**Background-isolation guard: the `Write`/`Edit` tools cannot modify repo files.** To create/replace a repo file: `Write` the full content to `C:\Users\Luke\.claude\jobs\f39a9536\tmp\stage\<filename>`, then Bash `cp "C:/Users/Luke/.claude/jobs/f39a9536/tmp/stage/<filename>" "<repo path>"`. For surgical edits to an existing file, use Bash `perl -0pi -e '...'`. Commits via Bash `git` on `main`. Do NOT call EnterWorktree.

**Build (PowerShell tool) — always use a CLEAN build to surface warnings (incremental builds hide them):**
```powershell
& "$env:USERPROFILE/.dotnet/dotnet.exe" build VoiceToText.slnx -c Debug --no-incremental
```
Expected: `0 Warning(s)` and `0 Error(s)`. (Note: .NET 10's WinForms analyzer **WFO1000** errors on any *new public property* declared on a `Control` subclass — not relevant here since we add no such property, but keep it in mind.)

**Run a headless self-test** (WinExe writes results to a file), from the repo root `D:\ClaudeCode\voice-to-text`:
```powershell
$env:DOTNET_ROOT = "$env:USERPROFILE/.dotnet"
$exe = "src/VoiceToText/bin/Debug/net10.0-windows/VoiceToText.exe"
Start-Process $exe -ArgumentList "--dashwindow" -WorkingDirectory $PWD -Wait ; Get-Content dashwindow-output.txt
Start-Process $exe -ArgumentList "--updatecheck" -WorkingDirectory $PWD -Wait ; Get-Content updatecheck-output.txt -Tail 1
```

---

## File Structure

**Create**
- `src/VoiceToText/Stt/ModelOption.cs` — pure label↔`GgmlType` ladder + default.

**Modify**
- `src/VoiceToText/Dashboard/SettingsPage.cs` — model combo, update controls, shared combo owner-draw, re-flowed layout, OnSave mapping.
- `src/VoiceToText/App/TrayApplicationContext.cs` — `_loadedModelType`/`_modelReloadPending`, `MaybeReloadModel()`, the `StopAndTranscribeAsync` finally hook, the `OnSettingsSaved` extension.
- `src/VoiceToText/VoiceToText.csproj` — `<Version>0.6.3</Version>` (ship task).

---

## Task 1: Pure `ModelOption` ladder

**Files:**
- Create: `src/VoiceToText/Stt/ModelOption.cs`

- [ ] **Step 1: Create the file**

Stage to `stage/ModelOption.cs`, then `cp` to `src/VoiceToText/Stt/ModelOption.cs`:

```csharp
using Whisper.net.Ggml;

namespace VoiceToText.Stt;

/// <summary>One selectable speech model: a friendly label paired with its ggml type.
/// Pure data — the speed↔accuracy ladder shown in Settings.</summary>
public sealed record ModelOption(string Label, GgmlType Type)
{
    /// <summary>The offered models, fastest → most accurate.</summary>
    public static IReadOnlyList<ModelOption> All { get; } = new[]
    {
        new ModelOption("Small (English) — fastest", GgmlType.SmallEn),
        new ModelOption("Medium (English) — faster", GgmlType.MediumEn),
        new ModelOption("Large v3 Turbo — recommended", GgmlType.LargeV3Turbo),
        new ModelOption("Large v3 — most accurate", GgmlType.LargeV3),
    };

    /// <summary>Matches AppSettings.ModelType's default (LargeV3Turbo).</summary>
    public static ModelOption Default { get; } = All[2];

    // Combos display this via GetItemText/ToString.
    public override string ToString() => Label;
}
```

- [ ] **Step 2: Build (verifies the GgmlType members exist)**

```powershell
& "$env:USERPROFILE/.dotnet/dotnet.exe" build VoiceToText.slnx -c Debug --no-incremental
```
Expected: `Build succeeded`, `0 Warning(s)`, `0 Error(s)`. If a member like `GgmlType.SmallEn` doesn't resolve (CS0117), open the enum (it ships with Whisper.net 1.9.0) and use the correct member name, keeping the same fastest→accurate order, then rebuild.

- [ ] **Step 3: Commit**

```bash
cd "D:/ClaudeCode/voice-to-text" && git add -A && git commit -m "Add pure ModelOption ladder (label <-> GgmlType)" -m "Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

## Task 2: SettingsPage — model picker, update controls, re-flowed layout

**Files:**
- Modify (full replace): `src/VoiceToText/Dashboard/SettingsPage.cs`

This replaces the whole file: adds `_modelCombo`, `_autoUpdateCheck`, `_updateFolderBox`; renames the combo draw handler to the shared `OnComboDrawItem` (wired to both combos); adds `LoadModels()`, `OnBrowseUpdateFolder()`; extends `LoadFromSettings()` + `OnSave()`; and re-flows `BuildUi()` to the new top-to-bottom layout. Positions/logic of existing controls are preserved, only re-laid-out.

- [ ] **Step 1: Replace the file**

Stage to `stage/SettingsPage.cs`, then `cp` to `src/VoiceToText/Dashboard/SettingsPage.cs`, with this exact content:

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
/// auto-stop on silence, the on-screen indicator, typing speed (WPM), automatic updates, and
/// start-on-login. Save writes into the shared <see cref="AppSettings"/> (and the Run key) and
/// raises <see cref="SettingsSaved"/>.
/// </summary>
internal sealed class SettingsPage : UserControl
{
    private readonly AppSettings _settings;
    private readonly ComboBox _deviceCombo = new() { DropDownStyle = ComboBoxStyle.DropDownList, FlatStyle = FlatStyle.Flat, BackColor = Theme.CardBg, ForeColor = Theme.TextPrimary, DrawMode = DrawMode.OwnerDrawFixed };
    private readonly ComboBox _modelCombo = new() { DropDownStyle = ComboBoxStyle.DropDownList, FlatStyle = FlatStyle.Flat, BackColor = Theme.CardBg, ForeColor = Theme.TextPrimary, DrawMode = DrawMode.OwnerDrawFixed };
    private readonly TextBox _hotkeyBox = new() { ReadOnly = true, Cursor = Cursors.Hand, TextAlign = HorizontalAlignment.Center, BackColor = Theme.CardBg, ForeColor = Theme.TextPrimary, BorderStyle = BorderStyle.FixedSingle };
    private readonly Label _hintLabel = new() { AutoSize = true, ForeColor = Theme.TextSecondary, Location = new Point(20, 184), MaximumSize = new Size(440, 0) };
    private readonly CheckBox _autoStopCheck = new() { Text = "Auto-stop after a pause in speech", AutoSize = true, Location = new Point(20, 222), ForeColor = Theme.TextPrimary };
    private readonly NumericUpDown _silenceUpDown = new() { DecimalPlaces = 1, Minimum = 0.3M, Maximum = 10.0M, Increment = 0.1M, BackColor = Theme.CardBg, ForeColor = Theme.TextPrimary, BorderStyle = BorderStyle.FixedSingle };
    private readonly CheckBox _overlayCheck = new() { Text = "Show on-screen indicator while dictating", AutoSize = true, Location = new Point(20, 282), ForeColor = Theme.TextPrimary };
    private readonly NumericUpDown _wpmUpDown = new() { DecimalPlaces = 0, Minimum = 10, Maximum = 300, Increment = 5, BackColor = Theme.CardBg, ForeColor = Theme.TextPrimary, BorderStyle = BorderStyle.FixedSingle };
    private readonly CheckBox _autoUpdateCheck = new() { Text = "Automatically check for updates on startup", AutoSize = true, Location = new Point(20, 352), ForeColor = Theme.TextPrimary };
    private readonly TextBox _updateFolderBox = new() { BackColor = Theme.CardBg, ForeColor = Theme.TextPrimary, BorderStyle = BorderStyle.FixedSingle };
    private readonly CheckBox _startupCheck = new() { Text = "Start automatically when I log in", AutoSize = true, Location = new Point(20, 452), ForeColor = Theme.TextPrimary };
    private readonly Label _savedLabel = new() { AutoSize = true, ForeColor = Theme.Accent, Visible = false, Text = "Settings saved ✓", Location = new Point(126, 498) };
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
        _autoStopCheck.Checked = _settings.AutoStopEnabled;
        _silenceUpDown.Value = (decimal)Math.Clamp(_settings.AutoStopSilenceSeconds, 0.3, 10.0);
        _silenceUpDown.Enabled = _autoStopCheck.Checked;
        _overlayCheck.Checked = _settings.ShowOverlay;
        _wpmUpDown.Value = (decimal)Math.Clamp(_settings.TypingSpeedWpm, 10, 300);
        _autoUpdateCheck.Checked = _settings.AutoUpdateEnabled;
        _updateFolderBox.Text = _settings.UpdateFeedFolder;
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

        _autoStopCheck.CheckedChanged += (_, _) => _silenceUpDown.Enabled = _autoStopCheck.Checked;
        var stopAfterLabel = new Label { Text = "Stop after", Location = new Point(40, 248), AutoSize = true, ForeColor = Theme.TextPrimary };
        _silenceUpDown.SetBounds(116, 246, 56, 24);
        var secondsLabel = new Label { Text = "seconds of silence", Location = new Point(178, 248), AutoSize = true, ForeColor = Theme.TextPrimary };

        var wpmLabel = new Label { Text = "Typing speed:", Location = new Point(20, 316), AutoSize = true, ForeColor = Theme.TextPrimary };
        _wpmUpDown.SetBounds(108, 314, 60, 24);
        var wpmSuffix = new Label { Text = "WPM  (used to estimate \"time saved\")", Location = new Point(176, 316), AutoSize = true, ForeColor = Theme.TextSecondary };

        var updateFolderLabel = new Label { Text = "Update folder:", Location = new Point(20, 386), AutoSize = true, ForeColor = Theme.TextPrimary };
        _updateFolderBox.SetBounds(110, 384, 280, 24);
        var browseButton = new Button { Text = "Browse…", Location = new Point(396, 383), Size = new Size(64, 26), FlatStyle = FlatStyle.Flat, BackColor = Theme.CardBg, ForeColor = Theme.TextPrimary };
        browseButton.FlatAppearance.BorderColor = Theme.CardBorder;
        browseButton.Click += OnBrowseUpdateFolder;
        var updateNote = new Label { Text = "Updates run an installer from this folder — only enable this for a folder you trust.", Location = new Point(20, 414), AutoSize = true, ForeColor = Theme.Warning, MaximumSize = new Size(440, 0) };

        var saveButton = new Button { Text = "Save", Location = new Point(20, 492), Size = new Size(96, 30), FlatStyle = FlatStyle.Flat, BackColor = Theme.Accent, ForeColor = Color.White };
        saveButton.FlatAppearance.BorderSize = 0;
        saveButton.FlatAppearance.MouseOverBackColor = Theme.AccentLight;
        saveButton.Click += OnSave;

        Controls.AddRange(new Control[]
        {
            deviceLabel, _deviceCombo, modelLabel, _modelCombo,
            hotkeyLabel, _hotkeyBox, _hintLabel,
            _autoStopCheck, stopAfterLabel, _silenceUpDown, secondsLabel,
            _overlayCheck, wpmLabel, _wpmUpDown, wpmSuffix,
            _autoUpdateCheck, updateFolderLabel, _updateFolderBox, browseButton, updateNote,
            _startupCheck, saveButton, _savedLabel,
        });
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
Expected: `Build succeeded`, `0 Warning(s)`, `0 Error(s)`.

- [ ] **Step 3: Smoke-test the window (constructs + paints both pages incl. the new controls)**

```powershell
$env:DOTNET_ROOT = "$env:USERPROFILE/.dotnet"
Start-Process "src/VoiceToText/bin/Debug/net10.0-windows/VoiceToText.exe" -ArgumentList "--dashwindow" -WorkingDirectory $PWD -Wait
Get-Content dashwindow-output.txt
```
Expected: `DASH WINDOW OK (constructed, both pages shown + painted, closed)`. If it prints `ERROR: ...`, that's a real layout/owner-draw bug — fix before committing.

- [ ] **Step 4: Commit**

```bash
cd "D:/ClaudeCode/voice-to-text" && git add -A && git commit -m "Settings: model picker + update-folder/auto-update controls" -m "Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

## Task 3: Tray wiring — reload the engine on model change + post-save update check

**Files:**
- Modify: `src/VoiceToText/App/TrayApplicationContext.cs`

Apply these surgical edits (read the file first so each anchor matches exactly).

- [ ] **Step 1: Add the `Whisper.net.Ggml` using**

The using block currently ends with `using VoiceToText.Update;`. Add `using Whisper.net.Ggml;` after it:

```bash
cd "D:/ClaudeCode/voice-to-text" && perl -0pi -e 's/using VoiceToText\.Update;\n/using VoiceToText.Update;\nusing Whisper.net.Ggml;\n/' src/VoiceToText/App/TrayApplicationContext.cs && grep -n "using Whisper.net.Ggml;" src/VoiceToText/App/TrayApplicationContext.cs
```
Expected: prints the new using line.

- [ ] **Step 2: Add the two fields**

Anchor on the existing `_busy`/`_updateInProgress` field lines:

```bash
cd "D:/ClaudeCode/voice-to-text" && perl -0pi -e 's/    private bool _busy;\n    private int _updateInProgress;/    private bool _busy;\n    private GgmlType _loadedModelType;\n    private bool _modelReloadPending;\n    private int _updateInProgress;/' src/VoiceToText/App/TrayApplicationContext.cs && grep -n "_loadedModelType;\|_modelReloadPending;" src/VoiceToText/App/TrayApplicationContext.cs
```
Expected: prints the two new field lines.

- [ ] **Step 3: Initialise `_loadedModelType` in the constructor**

Anchor on the `_stt = new WhisperSttEngine(...)` line:

```bash
cd "D:/ClaudeCode/voice-to-text" && perl -0pi -e 's/        _stt = new WhisperSttEngine\(_settings\.ModelType, _settings\.Language\);\n/        _stt = new WhisperSttEngine(_settings.ModelType, _settings.Language);\n        _loadedModelType = _settings.ModelType;\n/' src/VoiceToText/App/TrayApplicationContext.cs && grep -n "_loadedModelType = _settings.ModelType;" src/VoiceToText/App/TrayApplicationContext.cs
```
Expected: prints the new init line (there should be exactly one such line at this point).

- [ ] **Step 4: Hook the reload into `StopAndTranscribeAsync`'s finally**

The finally block sets `SetState(AppState.Idle); _busy = false;` inside a `BeginInvoke`. Add `MaybeReloadModel();` after `_busy = false;`:

```bash
cd "D:/ClaudeCode/voice-to-text" && perl -0pi -e 's/                SetState\(AppState\.Idle\);\n                _busy = false;\n/                SetState(AppState.Idle);\n                _busy = false;\n                MaybeReloadModel();\n/' src/VoiceToText/App/TrayApplicationContext.cs && grep -n "MaybeReloadModel();" src/VoiceToText/App/TrayApplicationContext.cs
```
Expected: prints one `MaybeReloadModel();` line (the finally hook).

- [ ] **Step 5: Add the `MaybeReloadModel` method**

Insert it right before the `OnSettingsSaved` method. Anchor on `    private void OnSettingsSaved()`:

```bash
cd "D:/ClaudeCode/voice-to-text" && perl -0pi -e 's/    private void OnSettingsSaved\(\)/    \/\/\/ <summary>Swap the STT engine to the newly-selected model once idle (never mid-dictation).<\/summary>\n    private void MaybeReloadModel()\n    {\n        if (!_modelReloadPending || _busy || _state != AppState.Idle)\n            return;\n        _modelReloadPending = false;\n        var old = _stt;\n        _loadedModelType = _settings.ModelType;\n        _stt = new WhisperSttEngine(_settings.ModelType, _settings.Language);\n        try { old.Dispose(); } catch { \/* best effort *\/ }\n        _ = Task.Run(WarmUpAsync);\n    }\n\n    private void OnSettingsSaved()/' src/VoiceToText/App/TrayApplicationContext.cs && grep -n "private void MaybeReloadModel()" src/VoiceToText/App/TrayApplicationContext.cs
```
Expected: prints the `MaybeReloadModel` method line.

- [ ] **Step 6: Extend `OnSettingsSaved` (detect model change + post-save update check)**

`OnSettingsSaved` ends with `ApplyOverlaySetting();` then `    }`. Append the new logic after that call:

```bash
cd "D:/ClaudeCode/voice-to-text" && perl -0pi -e 's/        ApplyOverlaySetting\(\);\n    }/        ApplyOverlaySetting();\n\n        if (_settings.ModelType != _loadedModelType)\n        {\n            _modelReloadPending = true;\n            MaybeReloadModel();\n        }\n\n        if (_settings.AutoUpdateEnabled \&\& !string.IsNullOrWhiteSpace(_settings.UpdateFeedFolder))\n            _ = CheckForUpdatesAsync(userInitiated: false);\n    }/' src/VoiceToText/App/TrayApplicationContext.cs && grep -n "_modelReloadPending = true;" src/VoiceToText/App/TrayApplicationContext.cs
```
Expected: prints the `_modelReloadPending = true;` line (inside `OnSettingsSaved`). Note: `ApplyOverlaySetting();` followed by `    }` appears only once (at the end of `OnSettingsSaved`), so this matches uniquely.

- [ ] **Step 7: Clean build**

```powershell
& "$env:USERPROFILE/.dotnet/dotnet.exe" build VoiceToText.slnx -c Debug --no-incremental
```
Expected: `Build succeeded`, `0 Warning(s)`, `0 Error(s)`. If `MaybeReloadModel` or a field shows a duplicate-definition error, an anchor matched twice — inspect and fix.

- [ ] **Step 8: Smoke + updater regression**

```powershell
$env:DOTNET_ROOT = "$env:USERPROFILE/.dotnet"
$exe = "src/VoiceToText/bin/Debug/net10.0-windows/VoiceToText.exe"
Start-Process $exe -ArgumentList "--dashwindow" -WorkingDirectory $PWD -Wait ; Get-Content dashwindow-output.txt
Start-Process $exe -ArgumentList "--updatecheck" -WorkingDirectory $PWD -Wait ; Get-Content updatecheck-output.txt -Tail 1
```
Expected: `DASH WINDOW OK ...` and `ALL UPDATE-CHECK TESTS PASSED`.

- [ ] **Step 9: Commit**

```bash
cd "D:/ClaudeCode/voice-to-text" && git add -A && git commit -m "Tray: reload STT engine on model change; re-check updates after save" -m "Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

## Task 4: Manual verification

**Files:** none (build + run + observe).

- [ ] **Step 1: Build and launch**

```powershell
& "$env:USERPROFILE/.dotnet/dotnet.exe" build VoiceToText.slnx -c Debug --no-incremental
$env:DOTNET_ROOT = "$env:USERPROFILE/.dotnet"
Start-Process "src/VoiceToText/bin/Debug/net10.0-windows/VoiceToText.exe"
```

- [ ] **Step 2: Walk the checklist**
  - Settings page shows, top→bottom: Microphone, **Speech model**, Dictation hotkey (+hint), Auto-stop (+seconds), Show indicator, Typing speed, **Auto-update checkbox + Update folder + Browse + amber note**, Start-on-login, Save. All dark-themed; no white inputs.
  - The model dropdown lists the four options and shows the current model selected; opening it shows dark rows.
  - **Browse…** opens a folder picker; choosing a folder fills the field. Typing a UNC path also works.
  - Toggle auto-update + set/clear the folder, change the model, **Save** → "Settings saved ✓"; `%APPDATA%\VoiceToText\settings.json` reflects `ModelType`, `AutoUpdateEnabled`, `UpdateFeedFolder`, and `UpdateConsentAccepted` matching the checkbox.
  - After saving a **different model**: a balloon appears (a "Downloading the … model" balloon if it isn't cached yet), and the next dictation uses the new model. Changing the model while a dictation is mid-flight applies right after it finishes.
  - With auto-update enabled + a valid feed folder, saving triggers a quiet background check (a balloon only if an update is actually available).
  - Existing settings still round-trip; English dictation unaffected.

- [ ] **Step 3: Record the result.** If a fix is needed, fix it in the relevant task's file (stage→cp / perl), clean-build, re-verify, and commit with a clear message.

---

## Task 5: Ship v0.6.3 to the update feed

**Files:**
- Modify: `src/VoiceToText/VoiceToText.csproj` (`<Version>`)
- Write (outside the repo): `D:\ClaudeCode\VoiceToText-Releases\latest.json` + the setup exe

**IMPORTANT:** the feed lives outside the repo; **populate it from the foreground session**, not an isolated subagent (a prior subagent's out-of-repo write did not persist).

- [ ] **Step 1: Bump the version**

```bash
cd "D:/ClaudeCode/voice-to-text" && perl -0pi -e 's{<Version>0\.6\.2</Version>}{<Version>0.6.3</Version>}' src/VoiceToText/VoiceToText.csproj && grep -n "<Version>" src/VoiceToText/VoiceToText.csproj
```
Expected: `<Version>0.6.3</Version>`.

- [ ] **Step 2: Publish + build the installer**

```powershell
& "D:/ClaudeCode/voice-to-text/publish.ps1" | Select-Object -Last 2
(Get-Item "D:/ClaudeCode/voice-to-text/publish/VoiceToText.exe").VersionInfo.ProductVersion   # expect 0.6.3
& "C:/Users/Luke/.claude/jobs/f39a9536/tmp/innosetup/tools/ISCC.exe" "D:/ClaudeCode/voice-to-text/installer/VoiceToText.iss" | Select-Object -Last 2
Test-Path "D:/ClaudeCode/voice-to-text/installer/Output/VoiceToText-Setup.exe"   # expect True
```

- [ ] **Step 3: Copy to feed + write latest.json (Bash, foreground)**

```bash
cd "D:/ClaudeCode/voice-to-text"
FEED="D:/ClaudeCode/VoiceToText-Releases"
mkdir -p "$FEED"
cp "installer/Output/VoiceToText-Setup.exe" "$FEED/VoiceToText-Setup-0.6.3.exe"
SHA=$(sha256sum "$FEED/VoiceToText-Setup-0.6.3.exe" | cut -d' ' -f1)
TS=$(date -u +"%Y-%m-%dT%H:%M:%SZ")
cat > "$FEED/latest.json" <<EOF
{
    "Version": "0.6.3",
    "SetupFileName": "VoiceToText-Setup-0.6.3.exe",
    "Sha256": "$SHA",
    "ReleaseNotes": "Settings now has a speech-model picker and update controls (auto-update toggle + update folder).",
    "Mandatory": false,
    "ReleasedUtc": "$TS"
}
EOF
cat "$FEED/latest.json"; echo "verify:"; sha256sum "$FEED/VoiceToText-Setup-0.6.3.exe" | cut -d' ' -f1
```
Expected: `latest.json` shows `"Version": "0.6.3"`, and the printed SHA matches the `Sha256` field.

- [ ] **Step 4: Commit the version bump**

```bash
cd "D:/ClaudeCode/voice-to-text" && git add -A && git commit -m "v0.6.3: Settings model picker + update controls" -m "Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

## Self-Review

**1. Spec coverage:**
- Model picker (pure ladder, dark combo, preserve unknown model) → Task 1 + Task 2 (`ModelOption`, `_modelCombo`, `LoadModels`). ✓
- Shared owner-draw for both combos → Task 2 (`OnComboDrawItem`). ✓
- Update controls (checkbox, folder + Browse, amber note) + consent-on-save → Task 2 (`_autoUpdateCheck`, `_updateFolderBox`, `OnBrowseUpdateFolder`, `OnSave` sets `UpdateConsentAccepted = AutoUpdateEnabled`). ✓
- Engine reload, deferred if busy → Task 3 (`_loadedModelType`, `_modelReloadPending`, `MaybeReloadModel`, finally hook, `OnSettingsSaved`). ✓
- Post-save quiet update check → Task 3 (`OnSettingsSaved`). ✓
- English-only / no language picker → no Language control added; `WhisperSttEngine` still constructed with `_settings.Language`. ✓
- Re-flowed layout with exact coordinates → Task 2 `BuildUi`. ✓
- Testing (`--dashwindow`, `--updatecheck`, manual) → Tasks 2-4. ✓
- Ship v0.6.3 from foreground → Task 5. ✓

**2. Placeholder scan:** No TBD/TODO; every code/edit step has full content and exact commands.

**3. Type consistency:** `ModelOption(string Label, GgmlType Type)`, `ModelOption.All`/`Default`, `_modelCombo`, `OnComboDrawItem`, `_loadedModelType` (GgmlType), `_modelReloadPending` (bool), `MaybeReloadModel()`, and the `AppSettings` members (`ModelType`, `AutoUpdateEnabled`, `UpdateFeedFolder`, `UpdateConsentAccepted`) are used consistently across tasks and match the existing code read from the repo.
