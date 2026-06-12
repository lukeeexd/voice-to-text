# Linux Dashboard — Design

**Date:** 2026-06-12
**Status:** Approved (brainstorming complete; implementation deferred — plan to be
written at the start of the build session)
**Prerequisite state:** v0.9.0 shipped (Linux beta AppImage live); QEMU/KVM-in-WSL2
validation rig available (see project memory).

## Goal

Bring the Windows dashboard experience to the Linux head: one window, five pages —
Dashboard (stats), History, Text rules, Settings, About — visually cloned from the
Windows app, replacing the current lean settings window.

## Decisions made during brainstorming

| Question | Decision |
|---|---|
| Scope | Full five-page parity window; replaces the lean `SettingsWindow` |
| Visual fidelity | Clone the Windows theme (palette/typography/layout from `Theme.cs`) |
| Per-app stats on Linux | X11: real focused-app attribution; Wayland: single `"Desktop"` bucket (no general API exists) |
| Build approach | A — shared view-models in Core + code-built Avalonia views (no XAML, consistent with the existing Linux UI) |

## Architecture

### Core additions (Windows-safe refactor, battery-covered)

Move from `src/VoiceToText` to `src/VoiceToText.Core`, namespaces unchanged
(the phase-1 extraction pattern):

- `Dashboard/DashboardModel.cs` + `ChartRange` (pure view-model; already fully
  covered by `--dashtest`).
- `DiagnosticsInfo` SPLIT: the pure constructor/row-assembly/clipboard-text goes to
  Core; the Windows-only probing (`GpuInfo`, current-environment collection in
  `Current()`) stays in the head behind the existing call sites.
- Windows `SelfTest.RunDashTest` delegates to `CoreSelfTest.RunDashTest`; the Linux
  head dispatches `--dashtest` too (its battery and CI gain the flag).

### Linux UI (`src/VoiceToText.Linux/Ui/Dashboard/`)

- `ThemeTokens.cs` — the Windows `Theme.cs` palette, spacing, and typography scale
  as Avalonia brushes/values. Single source for every page.
- `DashboardWindow.cs` — left nav rail + page host, same five pages and order as
  Windows (`Dashboard`, `History`, `Text rules`, `Settings`, `About`). Replaces
  `SettingsWindow`: the tray menu item ("Dashboard…") and the IPC `settings`/`show`
  commands open it (at the Dashboard page by default; Settings page when invoked
  via a future `--settings`-style intent is NOT required — one entry point).
- Custom-drawn controls (Avalonia `Control.Render` overrides, mirroring the
  GDI+ originals): `NavButton`, `HeroPanel` (time-saved hero), `StatTile`,
  `BarChart` (+ Week/Month/All range tabs), `BreakdownBars` (top apps).
- `HistoryPage` — Windows row layout: text, meta line (time · app · words · model ·
  seconds), per-row copy, clear-all; idempotent reload (no rebuild when data is
  unchanged — the v0.8.11 no-flicker semantics).
- `TextRulesPage` — editable find→replace grid (3 columns incl. delete), spoken-
  commands toggle, live preview box (input → transformed output, real line breaks).
- `SettingsPage` — the current lean settings window's contents, restyled with
  ThemeTokens (model picker + download state, language, auto-stop + pause slider,
  sound cues + volume, hotkey section incl. GNOME auto-setup, autostart, GPU
  experimental toggle, updates: auto toggle + check-now button).
- `AboutPage` — version, acceleration/runtime row (from the Core `DiagnosticsInfo`
  with Linux-side values: loaded Whisper runtime, model, model file size, OS,
  framework), copy-diagnostics button.

### Per-app attribution

- `Platform/X11FocusTracker.cs` — on X11 sessions, at dictation START resolve the
  focused application name: `_NET_ACTIVE_WINDOW` root property → window's
  `WM_CLASS` (class part) via `XGetWindowProperty`. Feed
  `DictationController.AppNameProvider` before each recording.
- Wayland sessions: attribute to `"Desktop"` (replaces today's `"Unknown"`).
  No general Wayland API exists for a normal app to read the focused window; a
  GNOME-extension route was considered and rejected (separate component, packaging
  burden) — revisit only on user demand.
- Tracker failures degrade to `"Desktop"`; they must never delay or block dictation.

## Data flow

No new persistence. Pages read the daemon's existing `StatsService`,
`HistoryService`, and `AppSettings` instances (stats/history have been recording on
Linux since v0.9.0, so the dashboard shows real accumulated data immediately). The
window subscribes to `DictationController.Transcribed` and refreshes the Dashboard
and History pages when visible.

## Error handling

- Pages are read-only consumers; they must never throw into the dictation path
  (services already swallow their own I/O failures).
- Opening/closing the window must not affect an in-progress recording.
- All chart/format edge cases (empty data, single-day, >30-day spans) are already
  defined and tested by `DashboardModel` — the views render whatever it yields.

## Testing

- `--dashtest` (view-model) runs in BOTH platforms' batteries once the model moves
  to Core; Windows output must stay identical (battery diff).
- Linux `--uitest` extends to construct + show + page-switch through all five
  dashboard pages headlessly in CI (the `--dashwindow` equivalent).
- Windows regression: full battery + `--dashwindow` after the Core moves; no
  Windows release required unless behavior changes (none expected).
- Final visual pass: QEMU VM rig session — every page opened and compared
  side-by-side against the Windows app.

## Out of scope (explicit)

- Onboarding-wizard parity (the Linux first-run dialog stays).
- The floating recording-indicator widget.
- In-app hotkey capture UI and HoldToTalk settings toggle (separate backlog items).
- Any Windows-side visual changes.

## Effort

Comparable to the phase-2b UX layer. The dominant cost is faithfully reproducing
the five custom-drawn controls; the data/view-model layer is already done and
tested.
