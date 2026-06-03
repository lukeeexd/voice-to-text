# History Per-Entry Model — Design Spec

**Date:** 2026-06-03
**Status:** Approved
**Ships as:** v0.8.7

Show which speech model produced each History entry, beside the v0.8.6 transcribe-time badge:
`14:32   ·   Discord   ·   23 words   ·   Large v3 Turbo   ·   0.3s`. Also harden the meta line
against running under the Copy link now that it is longer (the minor risk code review flagged
on v0.8.6).

## Changes

1. **`History/HistoryEntry.cs`** — add `public string? Model { get; set; }` (canonical engine
   name, e.g. `"LargeV3Turbo"`; null for entries recorded before v0.8.7). Store the enum *name*,
   prettify only at display time.
2. **`History/HistoryService.cs`** — `Record(string text, int words, string? app,
   double? transcribeSeconds = null, string? model = null)`; sets `Model = model`.
3. **`App/TrayApplicationContext.cs`** (`StopAndTranscribeAsync`) — capture
   `var model = _settings.ModelType.ToString();` alongside `transcribeSeconds` (before the
   `BeginInvoke` closure) and pass it to `_history.Record(...)`.
4. **`Stt/ModelOption.cs`** — add `public static string ShortLabel(string ggmlTypeName)`:
   `SmallEn → "Small (En)"`, `MediumEn → "Medium (En)"`, `LargeV3Turbo → "Large v3 Turbo"`,
   `LargeV3 → "Large v3"`, anything else → returned raw.
5. **`Dashboard/HistoryPage.cs`** —
   - `BuildRow`: meta text gains `   ·   {ModelOption.ShortLabel(m)}` when `entry.Model` is
     non-empty, placed BEFORE the seconds segment (needs `using VoiceToText.Stt;`).
   - **Overlap guard:** meta label becomes `AutoSize = false` + `AutoEllipsis = true`;
     `card.Tag` becomes the `(Label Body, Label Meta)` pair; `LayoutRow` sizes meta to
     `copy.Left - meta.Left - 8` wide (min 40) at its `PreferredHeight`, so a long line
     ellipsizes instead of ever running under Copy.
6. **`Diagnostics/SelfTest.cs`** (`RunHistoryTest`) — round-trip check also asserts
   `Model = "LargeV3Turbo"` survives; the legacy-JSON check also asserts `Model is null`.

## Compatibility / scope

- Same story as v0.8.6: nullable field, System.Text.Json safe both directions, no migration,
  no settings. Stats untouched.
- Verify: clean `--no-incremental` build 0/0; `--historytest`, `--dashwindow`, `--dashtest` green.
- Ship v0.8.7 to the feed (foreground, safe verify — never `--updatecheck` on the real folder).
