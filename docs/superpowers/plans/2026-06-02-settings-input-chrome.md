# Settings Input-Chrome Polish Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: superpowers:subagent-driven-development or executing-plans. Executed via the parallel Workflow (one implementer for Tasks 1–2, then spec + code-quality reviews in parallel). Steps use `- [ ]`.

**Goal:** Restyle the Settings input controls (combos, numerics, hotkey/folder boxes) to a consistent dark, rounded, premium look, shipped as v0.8.1.

**Architecture:** Add two theme colors + a shared rounded-field paint helper, and three controls — `DarkComboBox` (subclass that repaints the closed combo dark), `DarkNumericUpDown` (custom dark field + ▲▼ stepper with the NumericUpDown API surface), and `DarkField` (rounded dark wrapper for borderless textboxes). `SettingsPage` swaps field types + wraps the two textboxes; all logic is unchanged.

**Tech Stack:** C#/.NET 10 WinForms, GDI+ custom controls. Per-user SDK at `C:\Users\Luke\.dotnet`.

---

## Build & test environment

Build (always `--no-incremental`): `& "$env:USERPROFILE\.dotnet\dotnet.exe" build VoiceToText.slnx -c Debug --no-incremental` → `0 Warning(s)` / `0 Error(s)`.
Smoke: `$env:DOTNET_ROOT="$env:USERPROFILE\.dotnet"; & "src\VoiceToText\bin\Debug\net10.0-windows\VoiceToText.exe" --dashwindow; Get-Content .\dashwindow-output.txt` → `DASH WINDOW OK`.
**WFO1000:** new public properties on a `Control` subclass need `[DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]` — applies to every public property on `DarkNumericUpDown`.
**Visual caveat:** the smoke only proves no exceptions, not the look. The combo repaint especially can't be verified headlessly — the user eyeballs v0.8.1.

---

## File structure

**Create:** `src/VoiceToText/Dashboard/Controls/DarkComboBox.cs`, `DarkNumericUpDown.cs`, `DarkField.cs`.
**Modify:** `src/VoiceToText/Dashboard/Theme.cs` (colors + `PaintField`), `src/VoiceToText/Dashboard/SettingsPage.cs` (field swaps + textbox wrapping; remove the now-unused `OnComboDrawItem`), `src/VoiceToText/VoiceToText.csproj` (`<Version>0.8.1</Version>` at ship).

---

## Task 1: Theme additions + the three controls

**Files:** Modify `Theme.cs`; Create the three control files.

- [ ] **Step 1: Theme — colors + `PaintField`**

In `src/VoiceToText/Dashboard/Theme.cs`, add to the color block:
```csharp
    public static readonly Color InputBg     = Color.FromArgb(0x2A, 0x2C, 0x34);
    public static readonly Color InputBorder = Color.FromArgb(0x3A, 0x3D, 0x47);
```
And add this method (the file already `using System.Drawing.Drawing2D;`):
```csharp
    /// <summary>Paint a rounded dark input field: clear to the parent bg, fill InputBg, stroke InputBorder.</summary>
    public static void PaintField(Graphics g, Rectangle bounds, Color parentBg, int radius = 6)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(parentBg);
        var r = new Rectangle(bounds.X, bounds.Y, bounds.Width - 1, bounds.Height - 1);
        using var path = RoundedRect(r, radius);
        using var fill = new SolidBrush(InputBg);
        using var pen = new Pen(InputBorder);
        g.FillPath(fill, path);
        g.DrawPath(pen, path);
    }
```

- [ ] **Step 2: Create `DarkComboBox.cs`**
```csharp
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;

namespace VoiceToText.Dashboard.Controls;

/// <summary>
/// A dark, flat, rounded combo box. Keeps all ComboBox behavior; self-draws the dropdown items
/// dark (OnDrawItem) and repaints the closed control's border + dropdown button + chevron dark
/// (WM_PAINT), covering the light system chrome. Rounded via a clipping Region.
/// </summary>
internal sealed class DarkComboBox : ComboBox
{
    private const int Radius = 6;
    private const int ButtonW = 22;

    public DarkComboBox()
    {
        DropDownStyle = ComboBoxStyle.DropDownList;
        FlatStyle = FlatStyle.Flat;
        DrawMode = DrawMode.OwnerDrawFixed;
        BackColor = Theme.InputBg;
        ForeColor = Theme.TextPrimary;
        ItemHeight = 22;
        Height = 30;
    }

    protected override void OnSizeChanged(EventArgs e)
    {
        base.OnSizeChanged(e);
        using var path = Theme.RoundedRect(new Rectangle(0, 0, Width, Height), Radius);
        Region = new Region(path);
    }

    protected override void OnDrawItem(DrawItemEventArgs e)
    {
        if (e.Index < 0) { e.DrawBackground(); return; }
        bool selected = (e.State & DrawItemState.Selected) != 0;
        using var back = new SolidBrush(selected ? Theme.NavActiveBg : Theme.InputBg);
        e.Graphics.FillRectangle(back, e.Bounds);
        using var text = new SolidBrush(selected ? Theme.NavActiveText : Theme.TextPrimary);
        using var fmt = new StringFormat { LineAlignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap };
        var rect = new Rectangle(e.Bounds.X + 6, e.Bounds.Y, e.Bounds.Width - 8, e.Bounds.Height);
        e.Graphics.DrawString(GetItemText(Items[e.Index]), e.Font ?? Font, text, rect, fmt);
    }

    protected override void WndProc(ref Message m)
    {
        base.WndProc(ref m);
        const int WM_PAINT = 0x000F;
        if (m.Msg == WM_PAINT)
            PaintChrome();
    }

    // Draw the rounded border + dark button strip + chevron over the system chrome.
    // (OnDrawItem already painted the dark text/background for the closed selected item.)
    private void PaintChrome()
    {
        using var g = CreateGraphics();
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

        // Cover the native dropdown button strip with InputBg.
        using (var fill = new SolidBrush(Theme.InputBg))
            g.FillRectangle(fill, new Rectangle(Width - ButtonW, 1, ButtonW - 1, Height - 2));

        // Rounded border.
        using (var path = Theme.RoundedRect(new Rectangle(0, 0, Width - 1, Height - 1), Radius))
        using (var pen = new Pen(Theme.InputBorder))
            g.DrawPath(pen, path);

        // Chevron.
        using (var cb = new SolidBrush(Theme.TextSecondary))
        using (var cf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
            g.DrawString("▾", Font, cb, new Rectangle(Width - ButtonW, 0, ButtonW, Height), cf);
    }
}
```

- [ ] **Step 3: Create `DarkNumericUpDown.cs`**
```csharp
using System.ComponentModel;
using System.Drawing;
using System.Globalization;

namespace VoiceToText.Dashboard.Controls;

/// <summary>
/// A dark rounded numeric field with a ▲▼ stepper — a drop-in for the NumericUpDown surface
/// SettingsPage uses (Value / Minimum / Maximum / Increment / DecimalPlaces / ValueChanged).
/// </summary>
internal sealed class DarkNumericUpDown : Control
{
    private const int Radius = 6;
    private const int StepperW = 22;

    private readonly TextBox _text;
    private decimal _value;
    private decimal _min, _max = 100m, _increment = 1m;
    private int _decimals;

    public DarkNumericUpDown()
    {
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint
               | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        BackColor = Theme.CardBg;
        Size = new Size(96, 30);

        _text = new TextBox
        {
            BorderStyle = BorderStyle.None,
            BackColor = Theme.InputBg,
            ForeColor = Theme.TextPrimary,
        };
        _text.Leave += (_, _) => CommitText();
        _text.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter) { CommitText(); e.SuppressKeyPress = true; } };
        Controls.Add(_text);
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public decimal Value { get => _value; set => SetValue(value, raise: true); }
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public decimal Minimum { get => _min; set { _min = value; SetValue(_value, raise: false); } }
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public decimal Maximum { get => _max; set { _max = value; SetValue(_value, raise: false); } }
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public decimal Increment { get => _increment; set => _increment = value; }
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int DecimalPlaces { get => _decimals; set { _decimals = value; UpdateText(); } }

    public event EventHandler? ValueChanged;

    private void SetValue(decimal v, bool raise)
    {
        if (_max < _min) _max = _min;
        v = Math.Clamp(v, _min, _max);
        bool changed = v != _value;
        _value = v;
        UpdateText();
        if (changed && raise) ValueChanged?.Invoke(this, EventArgs.Empty);
    }

    private void UpdateText()
    {
        var s = _value.ToString("F" + _decimals, CultureInfo.CurrentCulture);
        if (_text.Text != s) _text.Text = s;
    }

    private void CommitText()
    {
        if (decimal.TryParse(_text.Text, NumberStyles.Any, CultureInfo.CurrentCulture, out var v))
            SetValue(v, raise: true);
        else
            UpdateText();
    }

    protected override void OnLayout(LayoutEventArgs e)
    {
        base.OnLayout(e);
        int ih = _text.PreferredHeight;
        _text.SetBounds(10, Math.Max(1, (Height - ih) / 2), Math.Max(10, Width - StepperW - 14), ih);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (Enabled && e.X >= Width - StepperW)
            SetValue(_value + (e.Y < Height / 2 ? _increment : -_increment), raise: true);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        Theme.PaintField(g, ClientRectangle, BackColor, Radius);

        int sx = Width - StepperW;
        using (var pen = new Pen(Theme.InputBorder))
            g.DrawLine(pen, sx, 4, sx, Height - 5);
        using var arrow = new SolidBrush(Enabled ? Theme.TextSecondary : Theme.TextMuted);
        using var fmt = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        g.DrawString("▲", Theme.Caption, arrow, new Rectangle(sx, 1, StepperW, Height / 2 - 1), fmt);
        g.DrawString("▼", Theme.Caption, arrow, new Rectangle(sx, Height / 2, StepperW, Height / 2 - 1), fmt);
    }
}
```

- [ ] **Step 4: Create `DarkField.cs`**
```csharp
using System.Drawing;

namespace VoiceToText.Dashboard.Controls;

/// <summary>A rounded dark input field that wraps one borderless child control (e.g. a TextBox).</summary>
internal sealed class DarkField : Panel
{
    private const int Radius = 6;
    private readonly Control _inner;

    public DarkField(Control inner, int width, int height = 30)
    {
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint
               | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        BackColor = Theme.CardBg;
        Size = new Size(width, height);
        _inner = inner;
        _inner.BackColor = Theme.InputBg;
        _inner.ForeColor = Theme.TextPrimary;
        Controls.Add(_inner);
    }

    protected override void OnLayout(LayoutEventArgs e)
    {
        base.OnLayout(e);
        int ih = _inner.PreferredSize.Height > 0 ? _inner.PreferredSize.Height : _inner.Height;
        _inner.SetBounds(10, Math.Max(1, (Height - ih) / 2), Math.Max(10, Width - 20), ih);
    }

    protected override void OnPaint(PaintEventArgs e) => Theme.PaintField(e.Graphics, ClientRectangle, BackColor, Radius);
}
```

- [ ] **Step 5: Build** — `0 Warning(s)`, `0 Error(s)` (these controls compile standalone; the page wiring is Task 2).

- [ ] **Step 6: Commit**
```bash
git add src/VoiceToText/Dashboard/Theme.cs src/VoiceToText/Dashboard/Controls/DarkComboBox.cs src/VoiceToText/Dashboard/Controls/DarkNumericUpDown.cs src/VoiceToText/Dashboard/Controls/DarkField.cs
git commit -m "feat(ui): dark rounded input controls (DarkComboBox, DarkNumericUpDown, DarkField) + Theme.PaintField"
```

---

## Task 2: Rewire `SettingsPage` to the dark inputs

**Files:** Modify `src/VoiceToText/Dashboard/SettingsPage.cs`. (`using VoiceToText.Dashboard.Controls;` is already present from v0.8.0.)

- [ ] **Step 1: Swap the field types**

Change these field declarations (keep names; the combos/numerics/textboxes self-style now):
- `_deviceCombo`, `_modelCombo`, `_activationCombo` → `private readonly DarkComboBox _deviceCombo = new();` (and `_modelCombo`, `_activationCombo` the same — drop the old `{ DropDownStyle=…, FlatStyle=…, BackColor=…, DrawMode=… }` initializer; `DarkComboBox` sets all that).
- `_silenceUpDown` → `private readonly DarkNumericUpDown _silenceUpDown = new() { DecimalPlaces = 1, Minimum = 0.3M, Maximum = 10.0M, Increment = 0.1M };`
- `_wpmUpDown` → `private readonly DarkNumericUpDown _wpmUpDown = new() { DecimalPlaces = 0, Minimum = 10, Maximum = 300, Increment = 5 };`
- `_hotkeyBox` → `private readonly TextBox _hotkeyBox = new() { ReadOnly = true, Cursor = Cursors.Hand, TextAlign = HorizontalAlignment.Center, BorderStyle = BorderStyle.None };`
- `_updateFolderBox` → `private readonly TextBox _updateFolderBox = new() { BorderStyle = BorderStyle.None };`

(Unchanged: `_hintLabel`, the five `ToggleSwitch` fields, `_saveButton`, `_savedLabel`, `_unsavedLabel`, `_baseline`, `_loading`, `_hotkey`, `CardWidth`.)

- [ ] **Step 2: Update `BuildUi`**

a) Remove the three combo owner-draw wirings (the lines `_deviceCombo.DrawItem += OnComboDrawItem;`, `_modelCombo.DrawItem += OnComboDrawItem;`, `_activationCombo.DrawItem += OnComboDrawItem;`) — `DarkComboBox` self-draws items. Keep the `.Width = 300 / 300 / 180` lines and the `_activationCombo.Items.AddRange(...)` + `_activationCombo.SelectedIndexChanged += (_, _) => UpdateAutoStopEnabled();` lines.

b) The numeric widths: change `_silenceUpDown.Width = 56;` → `_silenceUpDown.Width = 78;` and `_wpmUpDown.Width = 64;` → `_wpmUpDown.Width = 78;` (room for the value + the 22px stepper).

c) The hotkey box: remove `_hotkeyBox.Size = new Size(240, 26);`. Keep its `GotFocus`/`LostFocus` handlers. Where the Dictation card adds the hotkey row, wrap it:
```csharp
        var hotkeyField = new DarkField(_hotkeyBox, 240);
        dictation.AddRow("Dictation hotkey", hotkeyField, _hintLabel);
```
(replacing `dictation.AddRow("Dictation hotkey", _hotkeyBox, _hintLabel);`).

d) The update-folder composite: change so the textbox is wrapped in a `DarkField` and the Browse button sits beside it. Replace the current folder-composite block:
```csharp
        var browseButton = new Button { Text = "Browse…", Size = new Size(72, 26), FlatStyle = FlatStyle.Flat, BackColor = Theme.CardBg, ForeColor = Theme.TextPrimary };
        browseButton.FlatAppearance.BorderColor = Theme.CardBorder;
        browseButton.Click += OnBrowseUpdateFolder;
        _updateFolderBox.Size = new Size(232, 24);
        var folder = new Panel { BackColor = Theme.CardBg, Height = 26, Width = _updateFolderBox.Width + 8 + browseButton.Width };
        _updateFolderBox.Location = new Point(0, 1);
        browseButton.Location = new Point(_updateFolderBox.Width + 8, 0);
        folder.Controls.Add(_updateFolderBox);
        folder.Controls.Add(browseButton);
```
with:
```csharp
        var browseButton = new Button { Text = "Browse…", Size = new Size(72, 30), FlatStyle = FlatStyle.Flat, BackColor = Theme.CardBg, ForeColor = Theme.TextPrimary };
        browseButton.FlatAppearance.BorderColor = Theme.InputBorder;
        browseButton.Click += OnBrowseUpdateFolder;
        var folderField = new DarkField(_updateFolderBox, 232);
        var folder = new Panel { BackColor = Theme.CardBg, Height = 30, Width = folderField.Width + 8 + browseButton.Width };
        folderField.Location = new Point(0, 0);
        browseButton.Location = new Point(folderField.Width + 8, 0);
        folder.Controls.Add(folderField);
        folder.Controls.Add(browseButton);
```
(The `updates.AddRow("Update folder", folder, updateWarning);` line is unchanged.)

- [ ] **Step 3: Remove the now-unused `OnComboDrawItem`**

Delete the entire `private void OnComboDrawItem(object? sender, DrawItemEventArgs e) { … }` method from `SettingsPage` (the combos draw themselves now). Confirm there are no remaining references (`grep OnComboDrawItem src/VoiceToText/Dashboard/SettingsPage.cs` → none).

- [ ] **Step 4: Confirm logic methods unchanged**

`LoadFromSettings`, `LoadDevices`, `LoadModels`, `UpdateAutoStopEnabled`, `OnBrowseUpdateFolder`, `TryCaptureHotkey`, `UpdateHint`, `Snapshot`, `UpdateDirty`, `HasUnsavedChanges`, `OnSave`, `Save`, `OnVisibleChanged`, `ReloadFromSettings`, ctor — all unchanged. They use `.Items`/`.SelectedItem`/`.SelectedIndex`/`.Value`/`.Text`/`.Checked`/`.Enabled`/`.ValueChanged`/`.SelectedIndexChanged`/`.TextChanged`, all present on the new types (`DarkComboBox` is a `ComboBox`; `DarkNumericUpDown` exposes the `Value`/etc. surface; the textboxes are still `TextBox`).

- [ ] **Step 5: Build + smoke + dashtest**
```powershell
& "$env:USERPROFILE\.dotnet\dotnet.exe" build VoiceToText.slnx -c Debug --no-incremental
$env:DOTNET_ROOT = "$env:USERPROFILE\.dotnet"
& "src\VoiceToText\bin\Debug\net10.0-windows\VoiceToText.exe" --dashwindow ; Get-Content .\dashwindow-output.txt
& "src\VoiceToText\bin\Debug\net10.0-windows\VoiceToText.exe" --dashtest    ; Get-Content .\dashtest-output.txt
```
Expected: `0 Warning(s)`, `0 Error(s)`; `DASH WINDOW OK`; `ALL DASH TESTS PASSED`.

- [ ] **Step 6: Commit**
```bash
git add src/VoiceToText/Dashboard/SettingsPage.cs
git commit -m "feat(settings): use dark rounded combos/numerics/fields for the input chrome"
```

---

## Task 3: Ship v0.8.1 — FOREGROUND ONLY

**Files:** Modify `src/VoiceToText/VoiceToText.csproj`; out-of-repo `D:\ClaudeCode\VoiceToText-Releases\`.

> Foreground only. **Visual check first** — especially the combos (the riskiest repaint).

- [ ] **Step 1: Manual visual check** — open Settings: combos show a dark rounded field + chevron (no white border/button); the numerics show a dark rounded field + ▲▼ that step/clamp/persist and grey when auto-stop is off / Hold-to-talk; the hotkey + folder boxes have dark rounded borders; capture, dropdowns, dirty-tracking + Save all still work. Tune if needed, rebuild.
- [ ] **Step 2:** set `<Version>0.8.1</Version>` in the csproj.
- [ ] **Step 3:** `.\publish.ps1` ; ISCC `installer\VoiceToText.iss` ; copy `installer\Output\VoiceToText-Setup.exe` → `D:\ClaudeCode\VoiceToText-Releases\VoiceToText-Setup-0.8.1.exe`; write `latest.json` (Version 0.8.1, SetupFileName, lowercase SHA-256, ReleaseNotes "Settings inputs restyled: dark, rounded combos / numeric steppers / fields that match the cards — no more stock-Windows boxes.", Mandatory false, ReleasedUtc now).
- [ ] **Step 4:** commit the bump (`v0.8.1: dark rounded Settings input controls`).
- [ ] **Step 5:** verify the feed SAFELY — no-arg `--updatecheck` (temp feed) + read `latest.json` + `Get-FileHash` compare; **never** pass the real Releases folder.

---

## Notes on testing

No unit tests (GDI+ paint controls + a custom numeric whose value/clamp logic is simple); the `--dashwindow` smoke is the build-time guard, `--dashtest` confirms no dashboard regression. The **real acceptance is visual** — eyeball on v0.8.1, combos especially.
