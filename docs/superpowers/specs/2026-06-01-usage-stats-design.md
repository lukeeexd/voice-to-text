# Usage Statistics — tracking engine

- **Date:** 2026-06-01
- **Status:** Approved (brainstorm) — functionality first; dashboard UI is a separate later effort
- **Component of:** Voice to Text (C# / .NET 10 WinForms tray app)

## Goal

Start capturing dictation usage stats **now** so real data accrues immediately, persisted locally, with a small stopgap viewer — ahead of a future "proper UI" dashboard.

## Metrics

**Stored** (in `stats.json`):
- Lifetime totals: total words, total dictations, total seconds spoken.
- Per-day buckets `{yyyy-MM-dd → words, dictations, seconds}` — powers today/this-week, streak, charts, busiest-day.
- Per-app buckets `{appName → words, dictations}` — the breakdown.
- `MaxWordsInOneDictation` — the "longest dictation" record (everything else derives from the buckets).

**Derived (computed, never stored):**
- Time saved = `TotalWords ÷ TypingSpeedWpm` (gross — "time you'd have typed"); `TypingSpeedWpm` is a setting, default 40.
- Average words per dictation; speaking WPM (`words ÷ minutesSpoken`); current streak; today / last-N-days; busiest day.

## Privacy
Only the focused window's **process name** is stored (e.g. "Outlook", "Chrome") — never window titles or any dictated content.

## Architecture
- `Stats/StatsData.cs` — **pure, serializable** model: `Record(day, words, seconds, app)`, `CountWords`, and derived getters (`EstimatedMinutesSaved`, `AverageWordsPerDictation`, `SpeakingWpm`, `CurrentStreak`, `WordsInLastDays`, `BusiestDay`). Plus `DayStat`, `AppStat`. No I/O, no threading → unit-testable.
- `Stats/StatsService.cs` — loads/saves `%APPDATA%\VoiceToText\stats.json` (defensive, like AppSettings), `Record(words, seconds, app)` (UI thread → updates data + saves), and a `Summary(wpm)` line for the stopgap view.
- `Stats/NativeForeground.cs` — `GetForegroundWindow` + `GetWindowThreadProcessId` P/Invoke → focused process name (prettified); returns "Unknown" on any failure. Never reads window titles.
- `AppSettings.TypingSpeedWpm` (double, default 40).
- Wiring in `TrayApplicationContext.StopAndTranscribeAsync`: when transcribed text is non-empty, `words = StatsData.CountWords(text)`, `seconds = samples.Length / 16000.0`, and on the UI thread capture the foreground app then `_stats.Record(words, seconds, app)` alongside the existing paste.
- Stopgap: tray menu item **"Stats…"** → `MessageBox` with the summary.

## Data flow
Hotkey → record → transcribe → (text non-empty) compute words + seconds on the worker thread → marshal to UI thread → capture foreground app → inject text → `_stats.Record(...)` → persist.

## Error handling
- `Record` no-ops when words ≤ 0 (empty/blank transcription).
- Foreground-app capture failure → "Unknown".
- Stats load corrupt → fresh; save failure swallowed (stats are non-critical; never throw into the dictation path).

## Testing
`--statstest` diagnostic exercising the pure model headlessly: word counting, totals, zero-word ignored, max record, per-app split, time-saved math, averages, speaking WPM, streak (consecutive / gap / alive-via-yesterday / idle), last-7-days window, busiest day.

## Non-goals (this phase)
The dashboard UI, charts, CSV export, net-time-saved display (raw data is kept so it's possible later), and a Settings control for `TypingSpeedWpm` (settable via stats/settings file for now).
