# Settings Input-Chrome Polish — Design Spec

**Date:** 2026-06-02
**Status:** Approved (ready for implementation plan)
**Ships as:** v0.8.1
**Context:** v0.8.0 grouped Settings into dark cards (kept). The input controls inside still render with stock-WinForms chrome — light combo borders + native dropdown buttons, and light numeric borders + native spinner buttons — which look unpolished against the dark cards. This restyles those inputs to a consistent dark, premium look. No behavior changes.

## Decision (locked during brainstorming)

Dark, **rounded (6px)** inputs (option A) with a **▲▼** numeric stepper. Every input: fill `#2A2C34`, 1px `#3A3D47` definition border, 6px corners; combos get a dark chevron (▾), numerics a dark ▲▼ stepper, text fields a plain dark rounded border.

## Theme additions (`Theme.cs`)

- `public static readonly Color InputBg = Color.FromArgb(0x2A, 0x2C, 0x34);`
- `public static readonly Color InputBorder = Color.FromArgb(0x3A, 0x3D, 0x47);`
- A shared paint helper to DRY the rounded field chrome: `public static void PaintField(Graphics g, Rectangle bounds, int radius = 6)` — clears to the parent bg, fills a rounded `InputBg`, strokes a 1px `InputBorder`. Used by all three controls below.

## Components (new — `Dashboard/Controls/`)

### `DarkComboBox : ComboBox`
Keeps all ComboBox behavior and the existing dark item owner-draw; restyles only the **closed** control.
- Ctor sets `DropDownStyle = DropDownList`, `FlatStyle = Flat`, `DrawMode = OwnerDrawFixed`, `BackColor = Theme.InputBg`, `ForeColor = Theme.TextPrimary`, height ~30.
- A rounded `Region` (6px) clips the closed box; `WndProc` handles `WM_PAINT` (after base) to fill `InputBg`, draw the rounded `InputBorder` border, draw the selected text on the left, and draw a dark chevron (▾, `TextSecondary`) in a ~22px area on the right — covering the light system border + native button.
- The dropdown **list** keeps the host's `OnComboDrawItem` (already dark); its transient system popup border is acceptable.
- Drop-in for the three combos: the field type changes to `DarkComboBox`, but `.Items` / `.SelectedItem` / `.SelectedIndexChanged` / `.DrawItem` all still work (it *is* a ComboBox).

### `DarkNumericUpDown : Control`
A small custom control replacing `NumericUpDown` so the spinner can be themed. A borderless dark `TextBox` (numeric entry) on the left + a painted **▲▼** stepper (~22px, divided, `TextSecondary` arrows) on the right, inside a rounded `InputBg`/`InputBorder` field.
- **Public API (mirrors the NumericUpDown surface SettingsPage uses):** `decimal Value` (clamps to range, formats text to `DecimalPlaces`, raises `ValueChanged`), `decimal Minimum`, `decimal Maximum`, `decimal Increment`, `int DecimalPlaces`, `event EventHandler? ValueChanged`. Clicking ▲/▼ steps by `Increment` (clamped); typing in the box parses + clamps on validate/leave. Honors `Enabled` (greyed).
- **WFO1000:** `Value`/`Minimum`/`Maximum`/`Increment`/`DecimalPlaces` are public properties on a Control → annotate each `[DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]`. (`ValueChanged` is an event → exempt.)
- Drop-in for `_silenceUpDown` (0.3–10.0, inc 0.1, 1 dp) and `_wpmUpDown` (10–300, inc 5, 0 dp): set via the field/object initializer exactly as the NumericUpDowns are today.

### `DarkField : Panel`
A rounded dark field wrapper for plain text inputs: custom-paints `Theme.PaintField` and hosts one borderless child control with a few px padding.
- Used to wrap the hotkey box (`_hotkeyBox`, read-only, centered) and the update-folder box (`_updateFolderBox`). The textboxes become `BorderStyle = None`, `BackColor = Theme.InputBg`; `DarkField` provides the rounded border.

## `SettingsPage` rewiring (no behavior change)

- Field types: `_deviceCombo`/`_modelCombo`/`_activationCombo` → `DarkComboBox`; `_silenceUpDown`/`_wpmUpDown` → `DarkNumericUpDown`. `_hotkeyBox`/`_updateFolderBox` stay `TextBox` but become borderless.
- In `BuildUi`: wrap `_hotkeyBox` and `_updateFolderBox` in a `DarkField` before adding them to their rows/composite.
- Everything else is unchanged: `OnComboDrawItem` still wires to the combos' `DrawItem`; `TryCaptureHotkey` still checks `_hotkeyBox.Focused`; `Snapshot` still reads `.Value` / `.SelectedItem` / `.Text` / `.Checked`; `Save`, `LoadFromSettings`, `UpdateAutoStopEnabled` (`.Enabled`), and the dirty-tracking wiring (`.ValueChanged` / `.SelectedIndexChanged` / `.TextChanged`) all compile and behave as before.

## Testing

- `--dashwindow` smoke (constructs + paints the Settings page incl. the new controls) must stay green — but note it only catches exceptions, **not the look**.
- Manual: dictate-hotkey capture still works; the numerics still step/clamp/persist; combos still open + select; auto-stop numeric greys when Hold-to-talk; dirty-tracking + Save unchanged.
- Clean `--no-incremental` build, 0/0 (watch WFO1000 on the `DarkNumericUpDown` properties).
- **Real acceptance is visual** and must be eyeballed by the user on v0.8.1.

## Risk / caveat

WinForms ComboBox theming — especially the rounded corners — is the fiddly part, and a background agent can't see the result. The numerics and text fields are fully custom-drawn (low risk). The **combo may need one tweak after the user screenshots v0.8.1**; fallbacks if rounding is stubborn are a flat-dark combo (still no white outline) or a fully custom dropdown control.

## Files

**Create:** `src/VoiceToText/Dashboard/Controls/DarkComboBox.cs`, `DarkNumericUpDown.cs`, `DarkField.cs`.
**Modify:** `src/VoiceToText/Dashboard/Theme.cs` (InputBg/InputBorder + `PaintField`); `src/VoiceToText/Dashboard/SettingsPage.cs` (field type swaps + wrap the two textboxes); `src/VoiceToText/VoiceToText.csproj` (`<Version>0.8.1</Version>` at ship).

## Out of scope / YAGNI

Re-theming combos/numerics elsewhere (only Settings has them); animated transitions; mouse-wheel on the numeric (click + type is enough); rounding the transient combo dropdown-list popup.

## Rollout

Ship to the feed as **v0.8.1** (publish.ps1 + ISCC + copy setup to Releases + latest.json with SHA-256, from the foreground; verify the feed safely — never pass the real Releases folder to `--updatecheck`).

## Execution

Build via the **parallel Workflow**: the three controls are independent — but git commits/builds must not run concurrently, so one implementer creates all three controls + rewires SettingsPage + builds + commits, then the spec-compliance and code-quality reviewers run **in parallel**. Incremental builds while iterating; one clean `--no-incremental` build at the end.
