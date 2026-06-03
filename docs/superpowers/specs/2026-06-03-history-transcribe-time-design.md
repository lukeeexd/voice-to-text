# History Per-Entry Transcribe Time — Design Spec

**Date:** 2026-06-03
**Status:** Approved
**Ships as:** v0.8.6

Show how long each dictation took to transcribe, beside each History entry. The duration is
already measured (`Stopwatch` around `TranscribeAsync` in `TrayApplicationContext`, logged only);
it just isn't stored in history.

## Changes

1. **`History/HistoryEntry.cs`** — add `public double? TranscribeSeconds { get; set; }`.
   Nullable: legacy `history.json` entries (no field) deserialize to `null` and show nothing.
2. **`History/HistoryService.cs`** — `Record(string text, int words, string? app,
   double? transcribeSeconds = null)`; sets the field on the new entry.
3. **`App/TrayApplicationContext.cs`** (`StopAndTranscribeAsync`) — capture
   `var transcribeSeconds = sw.Elapsed.TotalSeconds;` alongside `words` (NOT the existing
   `seconds` local — that is the *audio* duration used by stats) and pass it to
   `_history.Record(text, words, app, transcribeSeconds)`.
4. **`Dashboard/HistoryPage.cs`** (`BuildRow`) — append to the muted meta line when present:
   `14:32   ·   Discord   ·   23 words   ·   0.3s` (format `{s:0.0}s`); absent for legacy entries.
5. **`Diagnostics/SelfTest.cs`** (`RunHistoryTest`) — add: JSON round-trip preserves
   `TranscribeSeconds`; legacy JSON without the field loads as `null`.

## Compatibility / scope

- System.Text.Json: missing field → null (old files load); unknown field ignored (old app reads
  new files). No migration, no settings, no Stats changes (YAGNI).
- Verify: clean `--no-incremental` build 0/0; `--historytest`, `--dashwindow`, `--dashtest` green.
- Ship v0.8.6 to the feed (foreground, safe verify — never `--updatecheck` on the real folder).
