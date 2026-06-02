# Settings Page Redesign Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans. Steps use checkbox (`- [ ]`) syntax. (This plan will be executed via the parallel Workflow tool: Tasks 1 & 2 concurrently, then Task 3, with reviews fanned out.)

**Goal:** Rebuild the Settings page content as readable, grouped, card-based sections (no functional change), shipped as v0.8.0.

**Architecture:** Two new reusable controls — `ToggleSwitch` (replaces checkboxes) and `SectionCard` (rounded header panel with an `AddRow` helper) — let `SettingsPage` compose four fixed-width section cards in a scrollable area with a pinned Save bar, instead of hand-positioned pixel coordinates. All existing behavior (dirty-tracking, hotkey capture, dark combos, auto-stop greying, Save + leave-prompt) is preserved.

**Tech Stack:** C#/.NET 10 WinForms, GDI+ custom controls. Per-user SDK at `C:\Users\Luke\.dotnet`.

---

## Build & test environment

**Build** (always `--no-incremental` — incremental hides WFO1000 analyzer warnings):
```powershell
& "$env:USERPROFILE\.dotnet\dotnet.exe" build VoiceToText.slnx -c Debug --no-incremental
```
Success = `0 Warning(s)` / `0 Error(s)`.

**Smoke** (paints all dashboard pages incl. Settings; no asserts — catches construction/paint exceptions):
```powershell
$env:DOTNET_ROOT = "$env:USERPROFILE\.dotnet"
& "src\VoiceToText\bin\Debug\net10.0-windows\VoiceToText.exe" --dashwindow
Get-Content .\dashwindow-output.txt   # expect: DASH WINDOW OK
```

**WFO1000:** the analyzer errors on a *new public property of a `Control` subclass*. `ToggleSwitch.Checked` is exactly that → it MUST carry `[DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]`. `SectionCard.Content` is a public property too → annotate it the same way (or expose rows only via the `AddRow` method). Events are exempt.

**Visual caveat:** the `--dashwindow` smoke only proves it doesn't throw, not that it *looks* right. A background agent can't see the GUI, so after the build the exact spacing/sizing may need one visual-tweak pass once the user screenshots v0.8.0.

---

## File structure

**Create**
- `src/VoiceToText/Dashboard/Controls/ToggleSwitch.cs` — owner-drawn on/off switch.
- `src/VoiceToText/Dashboard/Controls/SectionCard.cs` — rounded card with header + `AddRow`.

**Modify**
- `src/VoiceToText/Dashboard/SettingsPage.cs` — swap the 5 checkboxes for `ToggleSwitch` (field type only — names kept so the logic methods are untouched), and rewrite `BuildUi` to compose cards/rows + a scrollable area + a pinned Save bar.
- `src/VoiceToText/VoiceToText.csproj` — `<Version>0.8.0</Version>` (Task 4).

---

## Task 1: `ToggleSwitch` control  (independent — parallel with Task 2)

**Files:** Create `src/VoiceToText/Dashboard/Controls/ToggleSwitch.cs`.

UI control; no unit test (consistent with `BarChart`/`NavButton`); verified by build + the `--dashwindow` smoke once wired in Task 3.

- [ ] **Step 1: Create the control**
```csharp
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace VoiceToText.Dashboard.Controls;

/// <summary>A small on/off toggle switch used in place of a checkbox: accent track when on,
/// muted when off, greyed when disabled. Click or Space toggles it.</summary>
internal sealed class ToggleSwitch : Control
{
    private static readonly Color OffTrack = Color.FromArgb(0x3A, 0x3D, 0x47);
    private bool _checked;

    public ToggleSwitch()
    {
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint
               | ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
        Size = new Size(44, 24);
        Cursor = Cursors.Hand;
        TabStop = true;
        BackColor = Theme.CardBg; // toggles live inside cards; host may override
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool Checked
    {
        get => _checked;
        set
        {
            if (_checked == value) return;
            _checked = value;
            Invalidate();
            CheckedChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public event EventHandler? CheckedChanged;

    protected override void OnClick(EventArgs e)
    {
        base.OnClick(e);
        if (Enabled) { Focus(); Checked = !Checked; }
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (Focused && Enabled && keyData == Keys.Space)
        {
            Checked = !Checked;
            return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(BackColor);

        int h = Math.Min(Height, 22);
        int top = (Height - h) / 2;
        var track = new Rectangle(0, top, Math.Max(2, Width - 1), h - 1);

        Color trackColor = !Enabled ? Theme.CardBorder : _checked ? Theme.Accent : OffTrack;
        using (var path = Theme.RoundedRect(track, (h - 1) / 2))
        using (var fill = new SolidBrush(trackColor))
            g.FillPath(fill, path);

        int knob = h - 6;
        int kx = _checked ? track.Right - knob - 3 : track.Left + 3;
        using var knobBrush = new SolidBrush(Enabled ? Color.White : Theme.TextMuted);
        g.FillEllipse(knobBrush, kx, top + 3, knob, knob);
    }
}
```

- [ ] **Step 2: Build**

Run the build. Expected: `0 Warning(s)`, `0 Error(s)`. (If WFO1000 fires on `Checked`, the `[DesignerSerializationVisibility(...)]` attribute is missing.)

- [ ] **Step 3: Commit**
```bash
git add src/VoiceToText/Dashboard/Controls/ToggleSwitch.cs
git commit -m "feat(ui): ToggleSwitch control (owner-drawn on/off switch)"
```

---

## Task 2: `SectionCard` control  (independent — parallel with Task 1)

**Files:** Create `src/VoiceToText/Dashboard/Controls/SectionCard.cs`.

UI control; verified by build + the `--dashwindow` smoke once wired in Task 3.

- [ ] **Step 1: Create the control**
```csharp
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;

namespace VoiceToText.Dashboard.Controls;

/// <summary>
/// A rounded dark section card with an accent header. The host sets a fixed <see cref="Control.Width"/>
/// then calls <see cref="AddRow"/> for each setting; the card auto-sizes its height to its rows.
/// Rows lay out as: name label (left) + control (right) + optional hint (full-width, below).
/// </summary>
internal sealed class SectionCard : Control
{
    private const int Radius = 9;
    private const int HeaderH = 34;
    private const int PadX = 14;
    private const int PadBottom = 12;

    private readonly string _header;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public FlowLayoutPanel Content { get; }

    public SectionCard(string header)
    {
        _header = header.ToUpperInvariant();
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint
               | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        BackColor = Theme.WindowBg;

        Content = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            BackColor = Theme.CardBg,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
        };
        Content.SizeChanged += (_, _) => AdjustHeight();
        Controls.Add(Content);
    }

    /// <summary>Add one setting row: name on the left, control right-aligned, optional hint below.</summary>
    public void AddRow(string name, Control control, Label? hint = null)
    {
        const int topH = 38;
        int rowW = Math.Max(40, Content.ClientSize.Width);
        int rowH = topH + (hint is null ? 0 : 22);
        var row = new Panel { BackColor = Theme.CardBg, Width = rowW, Height = rowH, Margin = Padding.Empty };

        var label = new Label { Text = name, AutoSize = true, ForeColor = Theme.TextPrimary, Font = Theme.NavItem };
        label.Location = new Point(4, (topH - label.PreferredHeight) / 2);
        row.Controls.Add(label);

        control.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        control.Location = new Point(rowW - control.Width - 4, (topH - control.Height) / 2);
        row.Controls.Add(control);

        if (hint is not null)
        {
            hint.AutoSize = false;
            hint.Location = new Point(4, topH - 2);
            hint.Size = new Size(rowW - 8, 22);
            hint.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            row.Controls.Add(hint);
        }

        Content.Controls.Add(row);
    }

    private void AdjustHeight()
    {
        Content.SetBounds(PadX, HeaderH, Math.Max(0, Width - PadX * 2), Content.Height);
        Height = HeaderH + Content.Height + PadBottom;
    }

    protected override void OnSizeChanged(EventArgs e)
    {
        base.OnSizeChanged(e);
        Content.Width = Math.Max(0, Width - PadX * 2);
        AdjustHeight();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
        g.Clear(Theme.WindowBg);

        var r = new Rectangle(0, 0, Width - 1, Height - 1);
        using (var path = Theme.RoundedRect(r, Radius))
        using (var fill = new SolidBrush(Theme.CardBg))
        using (var pen = new Pen(Theme.CardBorder))
        {
            g.FillPath(fill, path);
            g.DrawPath(pen, path);
        }

        using var brush = new SolidBrush(Theme.Accent);
        g.DrawString(_header, Theme.Caption, brush, PadX, 11);
    }
}
```

- [ ] **Step 2: Build**

Run the build. Expected: `0 Warning(s)`, `0 Error(s)`.

- [ ] **Step 3: Commit**
```bash
git add src/VoiceToText/Dashboard/Controls/SectionCard.cs
git commit -m "feat(ui): SectionCard control (rounded header card + AddRow)"
```

---

## Task 3: Rebuild `SettingsPage` on cards + rows  (depends on Tasks 1 & 2)

**Files:** Modify `src/VoiceToText/Dashboard/SettingsPage.cs`.

This swaps the five `CheckBox` fields for `ToggleSwitch` (type only — **field names stay** so the logic methods don't change) and replaces the whole absolute-positioned `BuildUi` with a card/row composition inside a scroll area + pinned Save bar.

- [ ] **Step 1: Add the using + swap the field block**

At the top, add: `using VoiceToText.Dashboard.Controls;`

Replace the field declarations (the block from `_deviceCombo` through `_unsavedLabel`, i.e. the current lines 20–35) with:
```csharp
    private const int CardWidth = 700;

    private readonly ComboBox _deviceCombo = new() { DropDownStyle = ComboBoxStyle.DropDownList, FlatStyle = FlatStyle.Flat, BackColor = Theme.CardBg, ForeColor = Theme.TextPrimary, DrawMode = DrawMode.OwnerDrawFixed };
    private readonly ComboBox _modelCombo = new() { DropDownStyle = ComboBoxStyle.DropDownList, FlatStyle = FlatStyle.Flat, BackColor = Theme.CardBg, ForeColor = Theme.TextPrimary, DrawMode = DrawMode.OwnerDrawFixed };
    private readonly ComboBox _activationCombo = new() { DropDownStyle = ComboBoxStyle.DropDownList, FlatStyle = FlatStyle.Flat, BackColor = Theme.CardBg, ForeColor = Theme.TextPrimary, DrawMode = DrawMode.OwnerDrawFixed };
    private readonly TextBox _hotkeyBox = new() { ReadOnly = true, Cursor = Cursors.Hand, TextAlign = HorizontalAlignment.Center, BackColor = Theme.CardBg, ForeColor = Theme.TextPrimary, BorderStyle = BorderStyle.FixedSingle };
    private readonly Label _hintLabel = new() { AutoSize = false, ForeColor = Theme.TextSecondary, Font = Theme.Caption };
    private readonly ToggleSwitch _autoStopCheck = new();
    private readonly NumericUpDown _silenceUpDown = new() { DecimalPlaces = 1, Minimum = 0.3M, Maximum = 10.0M, Increment = 0.1M, BackColor = Theme.CardBg, ForeColor = Theme.TextPrimary, BorderStyle = BorderStyle.FixedSingle };
    private readonly ToggleSwitch _overlayCheck = new();
    private readonly ToggleSwitch _historyCheck = new();
    private readonly NumericUpDown _wpmUpDown = new() { DecimalPlaces = 0, Minimum = 10, Maximum = 300, Increment = 5, BackColor = Theme.CardBg, ForeColor = Theme.TextPrimary, BorderStyle = BorderStyle.FixedSingle };
    private readonly ToggleSwitch _autoUpdateCheck = new();
    private readonly TextBox _updateFolderBox = new() { BackColor = Theme.CardBg, ForeColor = Theme.TextPrimary, BorderStyle = BorderStyle.FixedSingle };
    private readonly ToggleSwitch _startupCheck = new();
    private readonly Button _saveButton = new() { Text = "Save", Size = new Size(96, 30), FlatStyle = FlatStyle.Flat, BackColor = Theme.Accent, ForeColor = Color.White, Enabled = false };
    private readonly Label _savedLabel = new() { AutoSize = true, ForeColor = Theme.Accent, Visible = false, Text = "Settings saved ✓" };
    private readonly Label _unsavedLabel = new() { AutoSize = true, ForeColor = Theme.Warning, Visible = false, Text = "● Unsaved changes" };
```
(Note: the `ToggleSwitch` fields keep their `_…Check` names so `Snapshot`, `LoadFromSettings`, `UpdateAutoStopEnabled`, `Save`, and the change-event wiring — which read `.Checked` / `.Enabled` / `.CheckedChanged` — compile and work **unchanged**. `ToggleSwitch` exposes all three.)

- [ ] **Step 2: Replace `BuildUi` entirely**

Replace the whole `BuildUi` method (current lines ~93–156) with:
```csharp
    private void BuildUi()
    {
        // Controls that sit in rows: size them; combos keep the dark owner-draw.
        _deviceCombo.Width = 300; _deviceCombo.DrawItem += OnComboDrawItem;
        _modelCombo.Width = 300; _modelCombo.DrawItem += OnComboDrawItem;
        _activationCombo.Width = 180; _activationCombo.DrawItem += OnComboDrawItem;
        _activationCombo.Items.AddRange(new object[] { "Press to toggle", "Hold to talk" });
        _activationCombo.SelectedIndexChanged += (_, _) => UpdateAutoStopEnabled();
        _hotkeyBox.Size = new Size(240, 26);
        _hotkeyBox.GotFocus += (_, _) => { _hotkeyBox.Text = "Press a key or combination…"; HotkeyCaptureStarted?.Invoke(); };
        _hotkeyBox.LostFocus += (_, _) => { _hotkeyBox.Text = _hotkey.Describe(); HotkeyCaptureEnded?.Invoke(); };
        _silenceUpDown.Width = 56;
        _wpmUpDown.Width = 64;
        _autoStopCheck.CheckedChanged += (_, _) => UpdateAutoStopEnabled();

        // Composite controls (numeric + unit / textbox + browse) used as a single "control" in a row.
        var silence = RowComposite(_silenceUpDown, "seconds");
        var wpm = RowComposite(_wpmUpDown, "WPM");

        var browseButton = new Button { Text = "Browse…", Size = new Size(72, 26), FlatStyle = FlatStyle.Flat, BackColor = Theme.CardBg, ForeColor = Theme.TextPrimary };
        browseButton.FlatAppearance.BorderColor = Theme.CardBorder;
        browseButton.Click += OnBrowseUpdateFolder;
        _updateFolderBox.Size = new Size(232, 24);
        var folder = new Panel { BackColor = Theme.CardBg, Height = 26, Width = _updateFolderBox.Width + 8 + browseButton.Width };
        _updateFolderBox.Location = new Point(0, 1);
        browseButton.Location = new Point(_updateFolderBox.Width + 8, 0);
        folder.Controls.Add(_updateFolderBox);
        folder.Controls.Add(browseButton);
        var updateWarning = new Label { Text = "⚠ Runs an installer from this folder — only enable for a folder you trust.", ForeColor = Theme.Warning, Font = Theme.Caption };

        // --- Cards ---
        var dictation = new SectionCard("Dictation") { Width = CardWidth, Margin = new Padding(0, 0, 0, 14) };
        dictation.AddRow("Microphone", _deviceCombo);
        dictation.AddRow("Speech model", _modelCombo);
        dictation.AddRow("Dictation hotkey", _hotkeyBox, _hintLabel);
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

        var scroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = Theme.WindowBg, Padding = new Padding(24, 18, 24, 8) };
        scroll.Controls.Add(cards);

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
        panel.Width = control.Width + 6 + 70;
        return panel;
    }
```

- [ ] **Step 3: Confirm the logic methods are unchanged**

Do NOT modify `LoadFromSettings`, `LoadDevices`, `LoadModels`, `OnComboDrawItem`, `UpdateAutoStopEnabled`, `OnBrowseUpdateFolder`, `TryCaptureHotkey`, `UpdateHint`, `Snapshot`, `UpdateDirty`, `HasUnsavedChanges`, `OnSave`, `Save`, `OnVisibleChanged`, `ReloadFromSettings`, or the constructor. They reference the controls by name and use `.Checked` / `.Enabled` / `.CheckedChanged` / `.Text` / `.SelectedItem` / `.Value` — all still valid (the five toggles are now `ToggleSwitch`, which provides `Checked`/`CheckedChanged`/`Enabled`; combos/numerics/textbox are unchanged).

`UpdateHint` writes `_hintLabel.Text`/`.ForeColor` — `_hintLabel` is now the hint Label passed into the "Dictation hotkey" row, so the dynamic risky-key warning still appears under that row.

- [ ] **Step 4: Build + smoke**
```powershell
& "$env:USERPROFILE\.dotnet\dotnet.exe" build VoiceToText.slnx -c Debug --no-incremental
$env:DOTNET_ROOT = "$env:USERPROFILE\.dotnet"
& "src\VoiceToText\bin\Debug\net10.0-windows\VoiceToText.exe" --dashwindow ; Get-Content .\dashwindow-output.txt
& "src\VoiceToText\bin\Debug\net10.0-windows\VoiceToText.exe" --dashtest    ; Get-Content .\dashtest-output.txt
```
Expected: `0 Warning(s)`, `0 Error(s)`; `DASH WINDOW OK`; `ALL DASH TESTS PASSED` (the dash model is untouched, but confirm nothing regressed).

- [ ] **Step 5: Commit**
```bash
git add src/VoiceToText/Dashboard/SettingsPage.cs
git commit -m "feat(settings): rebuild Settings content as grouped card sections + toggles"
```

---

## Task 4: Ship v0.8.0 to the feed — FOREGROUND ONLY

**Files:** Modify `src/VoiceToText/VoiceToText.csproj`; out-of-repo `D:\ClaudeCode\VoiceToText-Releases\`.

> **Foreground only.** **Manual visual check first** — a background agent can't see the GUI, so confirm the redesigned page looks right (and tune spacing if needed) before publishing.

- [ ] **Step 1: Manual visual check**

Launch `src\VoiceToText\bin\Debug\net10.0-windows\VoiceToText.exe`, open Settings, and confirm: four cards (Dictation / Feedback & privacy / General / Updates); one setting per row with right-aligned controls; toggles flip and persist; the dictation-hotkey hint + the update warning show; auto-stop toggle + seconds grey out when Activation = Hold-to-talk; the area scrolls and the Save bar stays pinned; dirty indicator + Save-enable still work. Tune row heights / widths / card width if anything looks off, then rebuild.

- [ ] **Step 2: Bump the version**

In `src/VoiceToText/VoiceToText.csproj`: `<Version>0.8.0</Version>`.

- [ ] **Step 3: Publish, package, populate the feed**
```powershell
.\publish.ps1
& "C:\Users\Luke\.claude\jobs\f39a9536\tmp\innosetup\tools\ISCC.exe" installer\VoiceToText.iss
Copy-Item installer\Output\VoiceToText-Setup.exe "D:\ClaudeCode\VoiceToText-Releases\VoiceToText-Setup-0.8.0.exe" -Force
```
Write `D:\ClaudeCode\VoiceToText-Releases\latest.json` (4-space indent): `Version` `0.8.0`; `SetupFileName` `VoiceToText-Setup-0.8.0.exe`; `Sha256` = `(Get-FileHash …0.8.0.exe -Algorithm SHA256).Hash.ToLower()`; `ReleaseNotes` "Redesigned Settings: grouped into clear card sections with toggles and consistent spacing — much easier to read."; `Mandatory` false; `ReleasedUtc` = current UTC.

- [ ] **Step 4: Commit the bump**
```bash
git add src/VoiceToText/VoiceToText.csproj
git commit -m "v0.8.0: redesigned Settings page (card sections + toggles)"
```

- [ ] **Step 5: Verify the feed SAFELY**

**Never** pass the real Releases folder to `--updatecheck` (it writes test files then `Directory.Delete()`s it). Instead:
```powershell
$env:DOTNET_ROOT = "$env:USERPROFILE\.dotnet"
& "src\VoiceToText\bin\Debug\net10.0-windows\VoiceToText.exe" --updatecheck ; Get-Content .\updatecheck-output.txt   # temp-feed self-test
$feed = "D:\ClaudeCode\VoiceToText-Releases"
$m = Get-Content "$feed\latest.json" -Raw | ConvertFrom-Json
$onDisk = (Get-FileHash "$feed\$($m.SetupFileName)" -Algorithm SHA256).Hash.ToLower()
"version=$($m.Version)  sha matches=$($onDisk -eq $m.Sha256)  present=$(Test-Path "$feed\$($m.SetupFileName)")"
```
Expected: `ALL UPDATE-CHECK TESTS PASSED`; `version=0.8.0 sha matches=True present=True`; historical setups intact.

---

## Notes on testing

- No new unit tests: the two controls are GDI+ paint controls (like `BarChart`/`NavButton`) and the page is layout — the `--dashwindow` smoke (construct + paint every page) is the automated guard; `--dashtest` confirms the dashboard model didn't regress.
- The real acceptance is **visual** (the whole point is readability) and must be eyeballed by the user on v0.8.0.
