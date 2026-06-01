# Dashboard UI — Design Spec

**Date:** 2026-06-01
**Status:** Approved (ready for implementation plan)
**Ships as:** v0.6.0

## Overview

Give the Voice to Text app a real main window: a unified, dark-themed, native-WinForms
window with a left sidebar that switches between a **Dashboard** page (visualising the
usage stats) and a **Settings** page (absorbing the current Settings dialog). This is
Phase 2 of the original three-feature vision (proper UI / stats / listening widget).

Today the app exposes its UI only through the tray menu: "Settings…" is a small
`FixedDialog`, and "Stats…" is a plain text `MessageBox`. Both are replaced by pages in
the new window.

## Goal

A polished, on-brand window that opens from the tray and shows, at a glance, how much
typing time dictation has saved — plus daily activity, per-app usage, streak, and
records — and that also hosts the existing settings.

## Decisions (locked during brainstorming)

- **Scope:** Unified main window with a sidebar (Dashboard + Settings pages). The
  standalone Settings dialog and the Stats MessageBox are removed.
- **Rendering:** Native WinForms with hand-drawn GDI+ charts. No new dependencies, no
  WebView2, no charting library. Consistent with the existing overlay (which is also
  hand-drawn) and keeps the self-contained exe small.
- **Layout:** "Option A" — hero band on top, a row of stat tiles, a two-column row
  (activity chart | top-apps breakdown), then a records strip.
- **Window:** Resizable, with a sensible minimum size (~880×580).
- **Freshness:** Loads a fresh stats snapshot on show and on window re-activation. No
  live-while-focused streaming for v1.

## Architecture & components

All new code lives under `src/VoiceToText/Dashboard/` (namespace `VoiceToText.Dashboard`),
mirroring the existing folder-per-concern layout (`App`, `Audio`, `Overlay`, `Settings`,
`Stats`).

### DashboardModel (pure, testable) — `Dashboard/DashboardModel.cs`

The heart of the testability story, mirroring how `StatsData` is a pure model behind
`StatsService`. Constructed from a `StatsData`, a `DateOnly today`, and a `double wpm`;
exposes ready-to-render view data and does **no** drawing and **no** I/O.

```
public sealed class DashboardModel
{
    public DashboardModel(StatsData data, DateOnly today, double typingWpm);

    public bool HasData { get; }                 // TotalDictations > 0

    // Hero
    public string TimeSavedText { get; }         // e.g. "2.4 hrs", "37 min", "<1 min"
    public string TimeSavedSubtext { get; }      // e.g. "vs typing at 40 WPM"
    public int Streak { get; }                   // CurrentStreak(today)

    // Tiles
    public long TotalWords { get; }
    public long TotalDictations { get; }
    public int AvgWordsPerDictation { get; }     // rounded
    public int SpeakingWpm { get; }              // rounded

    // Activity chart: last 30 days ending `today`, oldest->newest, zero-filled.
    public IReadOnlyList<DayBar> DailySeries { get; }   // always 30 entries
    public long DailyMax { get; }                        // max Words across series (>=1 for scaling)

    // Top apps: top 5 by words desc; any remainder aggregated into one "Other" row.
    public IReadOnlyList<AppBar> TopApps { get; }
    // Fraction is Words / (largest displayed row's Words), so the biggest bar is full width
    // (computed after "Other" is formed, so "Other" can never overflow the track).

    // Records strip (null when no data)
    public string? BestDictationText { get; }    // "86 words"  (from MaxWordsInOneDictation)
    public string? BusiestDayText { get; }       // "May 28 (1,240 words)"
}

public readonly record struct DayBar(DateOnly Date, long Words);
public readonly record struct AppBar(string Name, long Words, double Fraction);
```

Reuses existing `StatsData` members: `EstimatedMinutesSaved(wpm)`, `CurrentStreak(today)`,
`AverageWordsPerDictation`, `SpeakingWpm`, `BusiestDay`, `MaxWordsInOneDictation`, `Days`,
`Apps`, `WordsOn`.

**Daily series:** the 30 calendar days ending at `today` (inclusive), each mapped to that
day's `Words` (0 if absent), ordered oldest→newest. `DailyMax` is the max across the
series, clamped to a minimum of 1 so the chart never divides by zero.

**Top apps:** sort `Apps` by `Words` descending; take the first 5; if more remain, sum
their words into a single `AppBar("Other", sum, …)`. Then `Fraction = Words / maxWords`
where `maxWords` is the largest `Words` among the displayed rows (computed *after* the
"Other" row is formed, so "Other" can never exceed the track even if its sum is large).

### StatsFormat (shared) — `Stats/StatsFormat.cs`

Extract the duration formatting currently private in `StatsService.FormatDuration` into a
shared static so both the (retired) summary path and `DashboardModel` use one
implementation:

```
public static class StatsFormat
{
    // <1 min => "<1 min"; <90 min => "N min"; else "N.N hrs"
    public static string Duration(double minutes);
}
```

`StatsService.Summary` is no longer wired to any menu after this change; it may be left in
place (delegating to `StatsFormat.Duration`) or removed. The plan will remove it to avoid
dead code, but doing so is non-essential.

### Theme — `Dashboard/Theme.cs`

Central palette + fonts so every control paints consistently (and so the overlay can adopt
the same colors later). Values taken from the approved mockup:

| Token            | Value     | Use                              |
|------------------|-----------|----------------------------------|
| WindowBg         | #17181C   | window background                |
| SidebarBg        | #121317   | sidebar                          |
| CardBg           | #202229   | tiles / cards                    |
| CardBorder       | #2C2E36   | card + window borders            |
| Accent           | #4C8DFF   | bars, active accents             |
| AccentDeep       | #27457E   | bar gradient bottom              |
| HeroFrom/HeroTo  | #1D2840 / #191B22 | hero band gradient       |
| HeroBorder       | #2B3550   | hero band border                 |
| NavActiveBg      | #222B3D   | active nav item background       |
| NavActiveText    | #CFE0FF   | active nav item text             |
| TextPrimary      | #E8E9ED   | headline numbers / labels        |
| TextSecondary    | #8A8C95   | captions                         |
| TextMuted        | #54565F   | axis / version                   |
| Gold             | #FFCE6B   | streak badge                     |

Fonts: Segoe UI. Hero number ~32–42px bold; tile number ~22px bold; labels 11–13px.

### Custom GDI+ controls — `Dashboard/Controls/`

Each is a small, focused `Control` with one `OnPaint`. All read colors/fonts from `Theme`.

- **HeroPanel** — gradient band; large "Time saved" value + uppercase label + subtext on
  the left; "🔥 N days" streak badge on the right.
- **StatTile** — card with a big number and a caption label. Four are arranged in a row.
- **BarChart** — vertical bars for `DailySeries`, scaled to `DailyMax`; bottom axis labels
  (leftmost date, midpoint, "Today"). Bars are a top-down blue gradient.
- **BreakdownBars** — for `TopApps`: each row is name + word count + a horizontal track
  with a blue fill at `Fraction` width.

### Pages

- **DashboardPage** — `Dashboard/DashboardPage.cs` (UserControl). Hosts HeroPanel, the
  StatTile row, the two-column row (BarChart | BreakdownBars), and the records strip.
  `Bind(DashboardModel)` pushes data into the controls and lays them out. If
  `!model.HasData`, it hides the content and shows a centered empty-state message
  ("No dictations yet — press your hotkey and start talking."). Layout reflows on resize.
- **SettingsPage** — `Dashboard/SettingsPage.cs` (UserControl). The existing
  `SettingsForm` controls (microphone, hotkey, auto-stop + seconds, overlay, typing-speed
  WPM, start-on-login) moved verbatim into a page, plus a **Save** button and an inline
  "Settings saved ✓" confirmation. See "Settings migration" below.

### DashboardForm (shell) — `Dashboard/DashboardForm.cs`

The main window. Sidebar (brand row + nav items + version footer) on the left; a content
host panel on the right that shows exactly one page. `ShowPage(DashboardPageKind)` toggles
which UserControl is visible and updates the active nav styling. Nav items are lightweight
owner-drawn clickable labels (active item uses `NavActiveBg`/`NavActiveText`).

- `MinimumSize ≈ 880×580`, default a bit larger; `StartPosition = CenterScreen`;
  resizable (`Sizable`), with maximize allowed.
- Window icon from the app exe (same `ExtractAssociatedIcon` pattern as `SettingsForm`).
- `RefreshData()` rebuilds a `DashboardModel` from `StatsService.Data` +
  `settings.TypingSpeedWpm` + today, and calls `DashboardPage.Bind(...)`. Called on
  construction, on `OnShown`, and on `OnActivated`.

## Layout (Option A)

```
+-----------+------------------------------------------------------+
|  🎙 Voice  |  ⏱ TIME SAVED                          🔥 streak    |
|   to Text  |  2.4 hrs                                  5 days    |
|            |  vs typing at 40 WPM                                 |
| ▣ Dashboard|------------------------------------------------------|
| ⚙ Settings | [12,480]   [642]      [19]        [98]               |
|            |  Words     Dictations Avg words   Speaking WPM       |
|            |------------------------------------------------------|
|            |  Activity — last 30 days     | Top apps              |
|            |  ▁▂▃▅▂▆▃█▅▄█▆▃▅▇█▄▆▂▆▄▇▃▅▇▄▆▅█▄ | Code      ████████  |
|            |  May 2     May 16   Today    | Chrome    █████      |
|            |                              | Slack     ███        |
|            |                              | Outlook   ██         |
| v0.6.0     |  🏆 Best dictation: 86 words   📅 Busiest: May 28     |
+-----------+------------------------------------------------------+
```

Approximate metrics: sidebar 172px; main padding ~20px; hero height ~96px; four tiles in a
row (gap ~10px); two-column row with the chart column wider than the breakdown (~1.7 : 1);
daily chart ~96–110px tall with 30 bars; records strip a single muted row at the bottom.

## Behavior & tray integration

`TrayApplicationContext` owns a single `DashboardForm` instance (`_dashboard`).

- **Menu** (`BuildMenu`) becomes: **Open Dashboard** (default item, bold) → opens on the
  Dashboard page · **Settings…** → opens on the Settings page · Check for updates… · Exit.
  The "Stats…" item and its `MessageBox` are removed.
- **Tray double-click** → opens on the Dashboard page.
- `ShowDashboard(DashboardPageKind page)`: if `_dashboard` is null/disposed, create it
  (wiring the `SettingsSaved` handler); else reuse it. Then `RefreshData()`, `ShowPage(page)`,
  `Show()`, restore if minimized, and `Activate()` to bring it to front (single instance).
- On the form's `FormClosed`, clear `_dashboard` so the next open builds a fresh one.

### Settings migration

The control set, `LoadDevices`, hotkey capture, hint logic, and the save mapping move from
`SettingsForm` into `SettingsPage`. `SettingsForm.cs` is deleted.

- **Hotkey capture:** the `ProcessCmdKey` override must stay at the *form* level. The
  `SettingsPage` exposes `bool TryCaptureHotkey(ref Message msg, Keys keyData)` containing
  the existing logic (only acts when its hotkey box is focused). `DashboardForm.ProcessCmdKey`
  forwards to the active Settings page first; if it returns `true`, the key is swallowed.
- **Save:** a **Save** button on the page maps controls → `AppSettings` (and applies
  `AutoStart`) exactly as `SettingsForm.OnSave` does today, then raises a
  `SettingsSaved` event and shows the inline "Settings saved ✓" confirmation.
- `TrayApplicationContext` handles `SettingsSaved` by doing whatever it currently does
  after the Settings dialog returns OK: persist `settings.json`, re-register the global
  hotkey, apply the input-device / overlay-visibility changes. (The dashboard hero will
  reflect a new WPM the next time it binds — on activate or reopen.)
- Navigating away from the Settings page without pressing Save discards unsaved edits
  (acceptable for v1; no dirty-state prompt).

## Empty state

When `model.HasData` is false (no dictations recorded yet), the Dashboard page hides the
hero/tiles/charts and shows a centered, muted message: "No dictations yet — press your
hotkey and start talking." The window and Settings page are otherwise fully usable. The
real `stats.json` already has data, so the populated path is the common case in testing.

## Visual style

Dark, flat, blue-accented — matching the overlay pill and the mockup the user approved.
Rounded cards (~9px), subtle 1px borders, blue top-down gradient bars, a gold streak
accent. No drop shadows inside the window (the window itself may keep the OS frame).

## Testing

### Headless self-test — `--dashtest` (mirrors `--statstest`)

Add `RunDashTest(outputPath)` to `Diagnostics/SelfTest.cs` and route `--dashtest` in the
entry point. Pure `DashboardModel` assertions (no UI):

- **Empty:** `new StatsData()` → `HasData == false`; `DailySeries.Count == 30`;
  `TopApps` empty; `BestDictationText == null`.
- **Daily series:** record words on `today`, `today-2`, and `today-40`; assert
  `DailySeries.Count == 30`, oldest→newest order, the `today` bucket holds the right words,
  the `today-2` bucket is populated, the `today-1` bucket is 0 (zero-fill), and the
  `today-40` entry is **excluded** (outside the 30-day window). `DailyMax` equals the
  largest in-window day and is ≥ 1.
- **Top apps + Other:** record 7 distinct apps with descending words; assert `TopApps`
  has 6 rows (top 5 + "Other"), "Other" equals the sum of apps 6–7, rows are sorted
  descending, and the top row's `Fraction == 1.0`.
- **Hero / formatting:** with a known word count and WPM, assert `TimeSavedText` matches
  `StatsFormat.Duration(EstimatedMinutesSaved(wpm))` and `TimeSavedSubtext` contains the
  WPM. Cover the three duration branches (<1 min, minutes, hours).
- **Tiles/records:** `AvgWordsPerDictation`, `SpeakingWpm` rounded as expected;
  `BestDictationText` reflects `MaxWordsInOneDictation`; `BusiestDayText` reflects the
  busiest day.
- **Streak passthrough:** equals `StatsData.CurrentStreak(today)`.

Print PASS/FAIL lines + a final "ALL DASH TESTS PASSED" / "SOME DASH TESTS FAILED" and
return 0/1, exactly like `RunStatsTest`.

### Manual checklist

- Open from the tray menu and by double-clicking the tray icon; only one window ever opens.
- Switch Dashboard ↔ Settings; active nav styling updates.
- Resize the window; the chart and columns reflow without clipping.
- Populated (real `stats.json`) vs. empty state.
- Settings round-trip: capture a hotkey (incl. an F13/extra key), toggle each option, Save,
  confirm `settings.json` updates and the global hotkey re-registers; reopen shows saved values.
- Do a dictation while the window is in the background, click back → hero/chart update.

## Out of scope (YAGNI for v1)

- Live-while-focused streaming updates (refresh-on-activate is enough).
- Selectable chart ranges (7/30/all), CSV export, per-app drill-down.
- Theming the OS title bar / custom chrome.
- Animations beyond what GDI+ paint naturally gives.

## Rollout

Per the standing rule, ship to the update feed as **v0.6.0**: bump `<Version>`, run
`publish.ps1`, build the installer with ISCC, copy the setup into
`D:\ClaudeCode\VoiceToText-Releases`, and write `latest.json` with the SHA-256.

## Files

**Create**
- `src/VoiceToText/Dashboard/DashboardModel.cs` (pure)
- `src/VoiceToText/Dashboard/DashboardForm.cs`
- `src/VoiceToText/Dashboard/DashboardPage.cs`
- `src/VoiceToText/Dashboard/SettingsPage.cs`
- `src/VoiceToText/Dashboard/Theme.cs`
- `src/VoiceToText/Dashboard/Controls/HeroPanel.cs`
- `src/VoiceToText/Dashboard/Controls/StatTile.cs`
- `src/VoiceToText/Dashboard/Controls/BarChart.cs`
- `src/VoiceToText/Dashboard/Controls/BreakdownBars.cs`
- `src/VoiceToText/Stats/StatsFormat.cs`

**Modify**
- `src/VoiceToText/App/TrayApplicationContext.cs` — menu, `ShowDashboard`, single instance,
  double-click, remove Stats MessageBox, `SettingsSaved` wiring.
- `src/VoiceToText/Diagnostics/SelfTest.cs` — `RunDashTest`.
- entry point (`Program`/`Main`) — route `--dashtest`.
- `src/VoiceToText/Stats/StatsService.cs` — delegate to `StatsFormat.Duration` (and drop
  the now-unused `Summary`, optional).
- `src/VoiceToText/VoiceToText.csproj` — `<Version>0.6.0</Version>` at ship time.

**Delete**
- `src/VoiceToText/Settings/SettingsForm.cs` — replaced by `SettingsPage`.
