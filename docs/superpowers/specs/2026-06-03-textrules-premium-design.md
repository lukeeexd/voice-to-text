# Text Rules Page Premium Redesign — Design Spec

**Date:** 2026-06-03
**Status:** Approved
**Ships as:** v0.8.8

Bring `TextRulesPage` up to the premium look of the rest of the app (cards, toggle, dark rounded
inputs, finished grid theming) and fix the preview newline-rendering bug. Behavior unchanged:
load/save, live preview, add/edit/delete rules, `--textrulestest` logic untouched.

## Page structure (mirror SettingsPage's responsive pattern)

- Scroll host: `Panel { Dock = Fill, AutoScroll = true, BackColor = WindowBg }` containing a
  non-docked `FlowLayoutPanel` (TopDown, AutoSize) of three `SectionCard`s; reuse SettingsPage's
  `LayoutCards` logic verbatim (clamp width 320–980, center horizontally, center vertically when it
  fits, `ClientSizeChanged` + reentrancy guard).
- Pinned bottom save bar exactly like SettingsPage: `Panel { Dock = Bottom, Height = 54 }` with the
  existing `DarkButton` Save at (24, 12) and the "Saved ✓" label at (130, 18). Add scroll before
  saveBar (Fill before Bottom). Delete the old absolute `DoLayout` and the
  `_replacementsLabel`/`_tryLabel` headers (cards provide headers now).

## Card 1 — "Spoken commands"

- `AddRow("Turn spoken commands into formatting", _commandsToggle, hint)` where `_commandsToggle`
  is a `ToggleSwitch` (replaces the stock CheckBox; same `Checked`/`CheckedChanged` usage) and the
  hint is the existing caption text.

## Card 2 — "Replacements"

The grid goes in as a **full-width custom row**: a `Panel` (height ~250, `Padding = (4, 2, 4, 8)`)
with the grid `Dock = Fill`, added via `card.Content.Controls.Add(panel)` — NOT `AddRow` (AddRow
right-anchors its control; Content rows are width-stretched by SectionCard on resize, which is what
we want). Grid theming finished:

- `RowHeadersVisible = false` (delete `RowHeadersWidth` + row-header styles) — removes the gutter.
- `ColumnHeadersBorderStyle = None`, `ColumnHeadersHeight = 30` (keep DisableResizing),
  header style: BackColor CardBg, ForeColor TextSecondary, Font Theme.Caption.
- `CellBorderStyle = SingleHorizontal`, `GridColor = CardBorder`, `BackgroundColor = CardBg`,
  `RowTemplate.Height = 30`, cell `Padding = (6, 0, 0, 0)` for cells + headers.
- Cells: BackColor CardBg, ForeColor TextPrimary, SelectionBackColor NavActiveBg,
  SelectionForeColor NavActiveText.
- **Delete column**: `DataGridViewButtonColumn { Text = "✕", UseColumnTextForButtonValue = true,
  FlatStyle = Flat, Width = 36, AutoSizeMode = None }` styled muted (ForeColor TextMuted, BackColor
  CardBg, selection same as back); `CellClick` on it removes the row (guard `IsNewRow` and header
  row index). The two text columns keep `Fill` sizing.
- **Dark in-place editor**: `EditingControlShowing` — when the control is a TextBox set
  BackColor InputBg, ForeColor TextPrimary, BorderStyle None (today the editor flashes white).
- Keep `AllowUserToAddRows` (the empty bottom row remains the add affordance) and the existing
  `CellEndEdit`/`RowsRemoved` preview wiring.

## Card 3 — "Try it"

- Input: `_previewInput` becomes `BorderStyle = None` hosted in a `DarkField`, full-width row
  (DarkField `Anchor = Top|Left|Right` inside a stretching row panel; DarkField re-lays its inner
  on resize already).
- Output: `_previewOutput` (readonly, multiline, keeps its green `#9BE6A8` text) in a `DarkField`
  of height ~64. **DarkField change:** when the inner control is a multiline TextBox, lay it as
  `SetBounds(10, 6, Width - 20, Height - 12)` instead of the centered single-line placement.
  GOTCHA: DarkField's ctor sets `inner.ForeColor = TextPrimary` — re-apply the green ForeColor
  after constructing the output's DarkField (or make the ctor preserve a non-default ForeColor).
- **Newline bug fix**: `UpdatePreview` becomes
  `_previewOutput.Text = TextRules.Apply(...).Replace("\r\n", "\n").Replace("\n", Environment.NewLine);`
  so "new line" renders as an actual line break in the preview box (WinForms needs CRLF).

## Tests

- `--textrulestest` logic checks unchanged. Add one UI check to `RunTextRulesTest` (pattern of the
  v0.8.7 history UI check): construct `TextRulesPage` with an in-memory
  `new AppSettings { SpokenCommandsEnabled = true }` (never call its Save), find the editable
  single-line preview TextBox by recursive descent, set its Text to "a new line b", and assert the
  readonly multiline output's Text contains `Environment.NewLine`. Also assert the page contains a
  DataGridView with 3 columns. Wrap in try/catch → FAIL like the history check.
- Clean `--no-incremental` build 0/0; `--textrulestest`, `--dashwindow`, `--dashtest` green.

## Out of scope / YAGNI

Dirty-tracking for Save (Settings-style) — not part of the visual parity ask. Grid placeholder text.
Reordering rules. Any TextRules.Apply logic change.

## Rollout

Ship v0.8.8 to the feed (foreground, safe verify — never `--updatecheck` on the real folder).
Real acceptance is visual — the user eyeballs in prod.
