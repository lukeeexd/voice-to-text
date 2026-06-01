# Dashboard Extras — Design Spec

**Date:** 2026-06-01
**Status:** Approved (ready for implementation plan)
**Ships as:** v0.6.8
**Context:** Third of four queued features (after text rules + push-to-talk; then polish). Voice to Text — C#/.NET 10 WinForms, fully local. English-only.

## Overview

Two additions to the dashboard:

1. **Activity-chart range toggle** — a `7 / 30 / All` switch on the activity chart.
2. **Recent dictation history** — an **opt-in** (off by default) log of recent transcriptions, shown on a new History page, with copy + clear-all.

## Decisions (locked during brainstorming)

- Chart toggle: a segmented `7 | 30 | All` control on the chart card; default 30. "All" = earliest recorded day → today, one bar per day (no weekly/monthly bucketing in v1).
- History: **opt-in, off by default** (a Settings checkbox); keeps the **last 50** entries; stored locally only.
- History UI: a **new "History" sidebar page** (Dashboard / Settings / Text rules / History), not a dashboard panel.
- Each entry shows time · app · word-count + the text, with a per-entry **Copy** and a **Clear all**.

## Part 1 — Chart range toggle

### `DashboardModel` (Dashboard/DashboardModel.cs)

Add a range enum and a pure series builder; the model retains the snapshot inputs it already reads.

```csharp
public enum ChartRange { Week, Month, All }
public readonly record struct ActivitySeries(IReadOnlyList<DayBar> Bars, long Max);
```

- Store `_data` (StatsData) and `_today` (already available in the ctor).
- `public ActivitySeries Activity(ChartRange range)` — pure:
  - **Week**: `today-6 .. today` (7 bars), zero-filled.
  - **Month**: `today-29 .. today` (30 bars), zero-filled.
  - **All**: from the **earliest day present in `_data.Days`** (parse the min `yyyy-MM-dd` key) to `today`, one zero-filled bar per day. If `_data.Days` is empty (no recorded days), fall back to the Month window so the chart is never degenerate.
  - `Max` = the largest `Words` across the window, clamped to ≥ 1.
- The existing `DailySeries`/`DailyMax` remain (computed as `Activity(ChartRange.Month)`) so the current `--dashtest` assertions keep passing; the chart now binds from `Activity(range)`.

### UI (Dashboard/DashboardPage.cs + Controls/BarChart.cs)

- `DashboardPage` holds the current `ChartRange _range = ChartRange.Month` and adds a small **segmented toggle** (three flat, dark, owner-styled buttons `7` / `30` / `All`) positioned at the **chart card's top-right** (laid out over the chart area in `DoLayout`). Clicking a segment sets `_range`, re-binds the chart via `_chart.SetData(model.Activity(_range)...)`, updates the active-segment styling, and refreshes the chart title.
- `BarChart.SetData` gains a **title** parameter (replacing the hard-coded "Activity — last 30 days"); the page passes "Activity — last 7 days" / "last 30 days" / "all time". Axis labels are already derived from the series' first/mid dates + "Today", so no other `BarChart` change is needed.
- `DashboardPage.Bind` keeps the most recent `DashboardModel` so a range click can rebuild the series without a full refresh; `RefreshData` re-applies the current `_range`.

## Part 2 — Dictation history

### Storage

- `AppSettings.HistoryEnabled` (bool, default **false**).
- New `History/` folder (mirrors `Stats/`):
  - **`HistoryEntry`** (serializable): `DateTime Time`, `string App`, `string Text`, `int Words`.
  - **`HistoryStore`** (pure, testable): `const int MaxEntries = 50`; `List<HistoryEntry> Entries` (**newest first**); `Add(HistoryEntry)` inserts at index 0 and trims the tail beyond 50; `Clear()` empties.
  - **`HistoryService`**: loads/saves `%APPDATA%\VoiceToText\history.json` (defensive, like `StatsService`); `Record(string text, int words, string app)` → builds an entry with `DateTime.Now`, `Add`, `Save`; `Clear()` → `Clear` + `Save`. Exposes the current `Entries` for the page.

### Integration (App/TrayApplicationContext.cs)

In `StopAndTranscribeAsync`, where it already computes `words`/`app` and calls `_stats.Record(...)` on the UI thread, also:
```csharp
if (_settings.HistoryEnabled)
    _history.Record(text, words, app);
```
(`text` is the final, rule-processed text; same `words`/`app` as stats. The enabled check lives in the tray, so `HistoryService.Record` is only called when on.)

### History page (Dashboard/HistoryPage.cs, new UserControl)

A 4th sidebar page (`DashboardPageKind.History`). Dark-themed:
- Header: "History" + subtitle ("Your last 50 dictations, kept only on this PC.").
- A **Clear all** flat button (top-right) → `HistoryService.Clear()` + refresh.
- A scrollable list (newest first) — a dark `FlowLayoutPanel` (TopDown, AutoScroll) of entry panels; each entry panel (CardBg) shows a meta line `time · app · N words` and the wrapped text, plus a small flat **Copy** button that puts the entry's text on the clipboard. Entry width tracks the panel width; height auto-sizes to the wrapped text.
- **Off/empty state**: when `HistoryEnabled` is false (or there are no entries), show a centered muted message — "History is off — enable it in Settings to keep your recent dictations." (when off) or "No dictations recorded yet." (when on but empty).
- `Reload()` rebuilds the list from `HistoryService.Entries` + the enabled flag; called on show (`OnVisibleChanged` / when the page is shown).

### Settings (Dashboard/SettingsPage.cs)

Add a checkbox **"Save recent dictation history (kept only on this PC)"** bound to `AppSettings.HistoryEnabled`, with the existing dark styling, re-flowing the controls below it as needed. Turning it **off** stops new entries but leaves existing ones until Clear all. Saved/loaded with the other settings.

### Wiring (Dashboard/DashboardForm.cs)

- Extend `enum DashboardPageKind` with `History`.
- Ctor gains a `HistoryService` parameter; construct `HistoryPage(historyService, settings)`; add a "History" `NavButton` (sidebar order: Dashboard / Settings / Text rules / History) + the page to the content host; `ShowPage`/`SetActiveStyles` handle all four; when showing History, call `Reload()`.
- `TrayApplicationContext` owns a `HistoryService _history = new();` and passes it when constructing `DashboardForm`.

## Testing

- **`--historytest`** (new, pure `HistoryStore`, mirrors `--statstest`): `Add` puts newest first; adding 60 entries trims to 50 keeping the newest; `Clear` empties.
- **Extend `--dashtest`**: `Activity(Week)` → 7 bars ending today; `Activity(Month)` → 30; `Activity(All)` with data on `today-40` → 41 bars starting at `today-40`; `Activity(All)` with no data → 30-bar fallback; `Max` correct and ≥ 1.
- **Extend `--dashwindow`** smoke to `ShowPage(History)` + `Refresh()` so the new page (and its `FlowLayoutPanel`) is construct/paint-tested.
- Clean build 0/0 (`--no-incremental`).
- Manual: toggle 7/30/All; enable history in Settings, dictate, see entries newest-first; Copy an entry; Clear all; toggle history off (new entries stop).

## Files

**Create**
- `src/VoiceToText/History/HistoryEntry.cs`, `History/HistoryStore.cs` (pure), `History/HistoryService.cs` (I/O).
- `src/VoiceToText/Dashboard/HistoryPage.cs`.

**Modify**
- `src/VoiceToText/Dashboard/DashboardModel.cs` — `ChartRange` + `Activity`.
- `src/VoiceToText/Dashboard/Controls/BarChart.cs` — title parameter.
- `src/VoiceToText/Dashboard/DashboardPage.cs` — range toggle + bind via `Activity`.
- `src/VoiceToText/Dashboard/DashboardForm.cs` — History page kind + nav + ctor `HistoryService`.
- `src/VoiceToText/Dashboard/SettingsPage.cs` — `HistoryEnabled` checkbox.
- `src/VoiceToText/Settings/AppSettings.cs` — `HistoryEnabled`.
- `src/VoiceToText/App/TrayApplicationContext.cs` — `HistoryService`; record in `StopAndTranscribeAsync`; pass to `DashboardForm`.
- `src/VoiceToText/Diagnostics/SelfTest.cs` — `RunHistoryTest`; extend `RunDashTest` + `RunDashWindow`.
- `src/VoiceToText/Program.cs` — `--historytest`.
- `src/VoiceToText/VoiceToText.csproj` — `<Version>0.6.8</Version>` (at ship).

## Out of scope (YAGNI for v1)

- Weekly/monthly bucketing of the "All" chart; search/filter or per-app filtering of history; history export; editing entries; a configurable retention count (fixed at 50).

## Rollout

Ship to the feed as **v0.6.8** (bump `<Version>`, `publish.ps1`, ISCC, copy setup to `D:\ClaudeCode\VoiceToText-Releases`, write `latest.json` with SHA-256). **Feed population runs from the foreground session.**
