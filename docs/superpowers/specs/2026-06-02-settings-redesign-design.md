# Settings Page Redesign — Design Spec

**Date:** 2026-06-02
**Status:** Approved (ready for implementation plan)
**Ships as:** v0.8.0
**Context:** Voice to Text — C#/.NET 10 WinForms, fully local. The user finds the current Settings page hard to read (a flat, ungrouped, absolutely-positioned form-dump). This overhauls only the **content inside the Settings tab** — the sidebar/nav and the rest of the window are unchanged.

## Decision (locked during brainstorming)

**Card-based sections** (option B): the Settings content becomes a vertical, scrollable stack of rounded dark **section cards**, each with an accent header, holding one setting per row (name left, control right-aligned). Bare checkboxes become small **toggle switches**. A **pinned Save bar** stays visible at the bottom while the cards scroll.

## Scope

- **In scope:** the layout/readability of `Dashboard/SettingsPage.cs` content; two small reusable controls.
- **Out of scope:** the sidebar/nav, the window shell, and the other pages (Dashboard/Text rules/History/About). All existing Settings *behavior* is preserved exactly.

## Layout

- The page becomes: a **scroll area** (`Panel`, `AutoScroll`, `Dock = Fill`) stacking the section cards top-to-bottom, plus a **Save bar** (`Panel`, `Dock = Bottom`) holding the Save button + the "Settings saved ✓" / "● Unsaved changes" labels — so Save is always visible regardless of scroll.
- Cards fill the content width (with margins) and auto-size their height to their rows.

### Section cards (grouping)
1. **Dictation** — Microphone (combo), Speech model (combo), Dictation hotkey (capture box + hint), Activation (combo), Auto-stop after a pause (toggle) + a "Stop after [N] seconds of silence" sub-row.
2. **Feedback & privacy** — Show on-screen indicator (toggle), Save recent dictation history (toggle).
3. **General** — Typing speed ([N] WPM), Start automatically when I log in (toggle).
4. **Updates** — Check for updates on startup (toggle), Update folder (textbox + Browse) with the trust warning as sub-text.

### Within a card
One setting per **row**: a left-aligned **name label** (`TextPrimary`) and a **right-aligned control**; any hint/warning is muted sub-text (`TextSecondary` / `Warning`) under the row. Combos are width-constrained (~300px) rather than full width. Implemented with a per-card `TableLayoutPanel` (label column + right-aligned control column, hints as a full-width sub-row) so there are **no hand-positioned pixel coordinates** — the long-standing maintenance pain.

## New components

### `Dashboard/Controls/ToggleSwitch.cs`
A small owner-drawn on/off switch (~40×22): rounded track + knob, `Theme.Accent` when on / a muted track when off, honoring `Enabled` (greyed). Public `bool Checked` (with `CheckedChanged` event); click and Space toggle it.
- **WFO1000:** `Checked` is a public property on a `Control` subclass — annotate it `[DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]`. (`CheckedChanged` is an event → exempt.)

### `Dashboard/Controls/SectionCard.cs`
A rounded dark panel (`Theme.CardBg` fill + `Theme.CardBorder`, via `Theme.RoundedRect`) with an accent uppercase **header** drawn at top and a content region below for its `TableLayoutPanel`.
- Header text is passed via the **constructor** (no public settable property) to avoid WFO1000; exposes a method/content panel to add its rows.

(Optionally a tiny `SettingsLayout` helper to build a labeled, right-aligned row — kept in `SettingsPage` or alongside the card.)

## Behavior preserved (no functional change)

- **Dirty tracking:** `Snapshot()` reads the same 12 persisted values — now from the toggles' `Checked`, combos, hotkey, numerics, and the update-folder textbox; the change-event wiring moves from `CheckBox.CheckedChanged` to `ToggleSwitch.CheckedChanged` (combos/numerics/textbox unchanged). `UpdateDirty`/`HasUnsavedChanges`/`Save`/baseline-on-load all unchanged in logic.
- **Hotkey capture:** still routed from `DashboardForm.ProcessCmdKey` → `SettingsPage.TryCaptureHotkey` (the capture box just lives inside a card row now; the `_hotkeyBox.Focused` check is unchanged).
- **Dark combos:** keep the existing owner-draw `OnComboDrawItem`.
- **Auto-stop greying:** `UpdateAutoStopEnabled` now greys the auto-stop toggle + the seconds numeric when Activation = Hold-to-talk (same rule).
- **Save + leave-prompt:** `Save()` and the `DashboardForm` Save/Discard/Cancel prompt are unchanged.

## Testing

- **`--dashwindow`** smoke already shows + paints the Settings page; it now exercises the rebuilt card layout + toggles + scroll area (construct/paint without exceptions).
- Manual: dirty indicator + Save-enabled-only-when-dirty still work; hotkey capture works; auto-stop toggle + seconds grey out when Hold-to-talk; the content scrolls and the Save bar stays pinned; toggles flip and persist; combos still dark.
- Clean `--no-incremental` build, 0/0 (watch WFO1000 on `ToggleSwitch.Checked`).

## Files

**Create**
- `src/VoiceToText/Dashboard/Controls/ToggleSwitch.cs`
- `src/VoiceToText/Dashboard/Controls/SectionCard.cs`

**Modify**
- `src/VoiceToText/Dashboard/SettingsPage.cs` — rebuild `BuildUi`/layout to compose section cards + rows + the pinned Save bar; swap the five checkboxes for `ToggleSwitch`; keep all logic methods (`LoadFromSettings`, `Snapshot`, `UpdateDirty`, `HasUnsavedChanges`, `Save`, `TryCaptureHotkey`, `UpdateHint`, `OnComboDrawItem`, `UpdateAutoStopEnabled`, `LoadDevices`, `LoadModels`, `OnBrowseUpdateFolder`).
- `src/VoiceToText/Diagnostics/SelfTest.cs` — no new test needed; the existing `--dashwindow` smoke covers construction/paint (confirm it still passes).
- `src/VoiceToText/VoiceToText.csproj` — `<Version>0.8.0</Version>` (at ship).

## Out of scope / YAGNI

- Redesigning other pages, the sidebar, or the window chrome.
- A search/filter box in Settings; collapsible cards; per-setting reset.
- Animated toggle transitions (a simple two-state paint is enough).

## Rollout

Ship to the feed as **v0.8.0** (publish.ps1 + ISCC + copy setup to Releases + latest.json with SHA-256, from the foreground; verify the feed safely — never pass the real Releases folder to `--updatecheck`).

## Execution note

Implement via the **parallel Workflow tool**: build the two independent controls (`ToggleSwitch`, `SectionCard`) concurrently, then rebuild `SettingsPage` on top of them, with the spec-compliance + code-quality reviews fanned out in parallel (not sequential one-at-a-time Agent dispatches). Incremental builds while iterating; one clean `--no-incremental` build at the end.
