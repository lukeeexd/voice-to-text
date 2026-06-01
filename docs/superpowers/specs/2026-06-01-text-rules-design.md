# Text Rules — Design Spec

**Date:** 2026-06-01
**Status:** Approved (ready for implementation plan)
**Ships as:** v0.6.5
**Context:** First of four queued features (then push-to-talk, dashboard extras, polish). Voice to Text — C#/.NET 10 WinForms, fully local/offline, Whisper large-v3-turbo via Vulkan. English-only.

## Overview

A post-transcription "text rules" layer that cleans up Whisper's output before it's pasted, entirely on-device. Two parts:

1. **Custom replacements** — a user-editable list of find→replace rules to fix names/jargon/code terms (e.g. `github` → `GitHub`).
2. **Spoken formatting commands** — turn spoken `new line`/`new paragraph` into actual line breaks instead of literal words.

Managed on a new **Text rules** page in the dashboard sidebar.

## Decisions (locked during brainstorming)

- **Editor location:** a new "Text rules" sidebar page (3rd item: Dashboard / Settings / Text rules). The Settings page is already full; a grid editor needs room.
- **Replacement matching:** case-insensitive, whole-word; replacement text inserted verbatim.
- **Spoken commands:** `new line` (and one-word `newline`) → `\n`; `new paragraph` → `\n\n`. Toggleable, **on by default**.
- **Storage:** in the existing `settings.json` (not a separate file).
- **Live preview:** a "Try it" box that updates as you type, against the live (unsaved) grid state. No Run button.
- **Scope:** global (not per-app).

## 1. Engine — `TextProcessing/TextRules.cs` (pure, testable)

```csharp
public static string Apply(string text, IReadOnlyList<ReplacementRule> rules, bool spokenCommands)
```

No I/O, no UI — fully unit-testable (mirrors `StatsData`/`DashboardModel`). Steps, in order:

1. **Spoken commands** (only if `spokenCommands` is true), applied to the whole string, case-insensitive, global:
   - `new paragraph` → `"\n\n"` — applied **before** the line rule.
   - `new line` or `newline` → `"\n"`.
   - Each phrase is matched as whole words and **absorbs surrounding whitespace + optional trailing punctuation** so no double spaces or stray periods remain around the break. Concretely:
     - paragraph: `\s*\bnew\s+paragraph\b[.,!?;:]*\s*`  → `"\n\n"`
     - line: `\s*\b(?:new\s+line|newline)\b[.,!?;:]*\s*` → `"\n"`
     - `RegexOptions.IgnoreCase`. (So "First line. New line. Second" and "first line new line second" both collapse correctly.)
   - Not handled (out of scope, documented): re-capitalising the word after a break (Whisper may have lower-cased it mid-sentence).
2. **Replacements**, applied in the user's listed order. For each rule whose `Find` is non-blank:
   - Pattern: `(?<!\w){Regex.Escape(Find)}(?!\w)` with `RegexOptions.IgnoreCase` — "whole-word" via lookarounds (works even when `Find` starts/ends with a non-word char like `c#`, where `\b` would misbehave).
   - Replacement is inserted **verbatim** via a `MatchEvaluator` returning `rule.Replace` (so `$`, `#`, `\` in the replacement are literal — no regex substitution).
   - Blank/whitespace `Find` rows are skipped.
3. **Trim** the final result (cleans edges left by command replacements).

`Apply` on empty/whitespace input returns it unchanged (after the existing engine already trims).

### `TextProcessing/ReplacementRule.cs`

```csharp
public sealed class ReplacementRule
{
    public string Find { get; set; } = "";
    public string Replace { get; set; } = "";
}
```

Plain serializable type (mutable for `System.Text.Json` + the grid).

## 2. Storage — `Settings/AppSettings.cs`

Add:
```csharp
public List<ReplacementRule> Replacements { get; set; } = new();
public bool SpokenCommandsEnabled { get; set; } = true;
```
(`AppSettings` gains `using VoiceToText.TextProcessing;`.) Serialized into `settings.json` alongside everything else. Existing `settings.json` files without these keys deserialize to the defaults (empty list, commands on).

## 3. Integration — `App/TrayApplicationContext.cs`

In `StopAndTranscribeAsync`, right after `var text = await _stt.TranscribeAsync(samples)...`:
```csharp
text = TextRules.Apply(text, _settings.Replacements, _settings.SpokenCommandsEnabled);
```
This runs **before** the `string.IsNullOrWhiteSpace(text)` check, the word count, the inject, and the stats record — so the pasted text and the stats both reflect the final, rule-processed output. `_settings` is read live, so saved rules take effect on the next dictation with no reload.

## 4. UI — `Dashboard/TextRulesPage.cs` (UserControl)

Dark-themed to match the dashboard. Added as a third sidebar page.

- **Spoken commands**: a `CheckBox` "Turn spoken commands into formatting" (bound to `SpokenCommandsEnabled`) + a hint label ("Say 'new line' or 'new paragraph' while dictating.").
- **Replacements grid**: a dark-themed `DataGridView` with two text columns — *Find (heard)* and *Replace with* — `AllowUserToAddRows = true` (blank entry row at the bottom) and row deletion enabled (Delete key / a small remove affordance). Dark styling via `BackgroundColor`, `DefaultCellStyle`, `ColumnHeadersDefaultCellStyle`, `GridColor`, `EnableHeadersVisualStyles = false`, `BorderStyle = None`.
- **Try it** preview: a single-line input `TextBox` and a read-only multiline output `TextBox`. The output re-runs `TextRules.Apply(input, <current grid rows>, <checkbox>)` on every input `TextChanged`, every grid `CellEndEdit`, and the checkbox `CheckedChanged` — i.e. against the **live, unsaved** state.
- **Save**: a flat accent button. On click: gather the grid rows (skipping the blank/incomplete trailing row) into `_settings.Replacements`, set `_settings.SpokenCommandsEnabled`, call `_settings.Save()`, and show a "Saved ✓" label. (No tray round-trip needed — text rules don't touch the hotkey/overlay/model. `OnVisibleChanged` clears the saved label on re-show, like `SettingsPage`.)
- **Load**: on construction and when shown, repopulate the grid + checkbox from `_settings`.

### `Dashboard/DashboardForm.cs`

- Extend `enum DashboardPageKind { Dashboard, Settings, TextRules }`.
- Add a `NavButton "Text rules"` to the sidebar (after Settings) and a `TextRulesPage` to the content host; `ShowPage` toggles all three pages' visibility and the active-nav styling.
- The page does not capture a global hotkey, so no `ProcessCmdKey` forwarding is needed for it.

(The tray menu is unchanged — Text rules is reached via Open Dashboard → Text rules. No new tray item.)

## 5. Testing

- **`--textrulestest`** (new headless self-test, mirrors `--dashtest`/`--statstest`):
  - Replacement: `github`/`Github`/`GITHUB` → `GitHub`; `githubbing` is **untouched** (whole-word); replacement inserted verbatim incl. a rule whose Replace contains `$` and `#`; two rules apply in listed order; blank-Find rule skipped.
  - Spoken commands: `"a new line b"` → `"a\nb"`; `"a new paragraph b"` → `"a\n\nb"`; case + punctuation tolerance (`"a. New line. b"` → `"a.\nb"`); one-word `newline`; with `spokenCommands=false` the literal words are preserved.
  - Ordering: commands run before replacements (a rule targeting `line` doesn't eat `new line` before the command fires).
  - Empty/whitespace input → unchanged; empty rule list + commands off → unchanged.
  - Prints `[PASS]/[FAIL]` lines + `ALL TEXTRULES TESTS PASSED`, returns 0/1; routed via `--textrulestest` in `Program.cs`.
- **`--dashwindow` smoke**: extend `RunDashWindow` to also `ShowPage(DashboardPageKind.TextRules)` + `Refresh()` so the new page's construction/paint (incl. the DataGridView) is exercised.
- **Clean build**: 0 warnings / 0 errors (`--no-incremental`).
- **Manual**: add a rule, watch the live preview transform sample text, Save, dictate "testing github new line done", confirm the paste reads "testing GitHub⏎done".

## 6. Rollout

Ship to the feed as **v0.6.5** (bump `<Version>`, `publish.ps1`, ISCC, copy setup to `D:\ClaudeCode\VoiceToText-Releases`, write `latest.json` with SHA-256). **Feed population runs from the foreground session** (out-of-repo writes from isolated subagents didn't persist previously).

## Files

**Create**
- `src/VoiceToText/TextProcessing/ReplacementRule.cs` — serializable find/replace rule.
- `src/VoiceToText/TextProcessing/TextRules.cs` — pure `Apply` engine.
- `src/VoiceToText/Dashboard/TextRulesPage.cs` — the editor page.

**Modify**
- `src/VoiceToText/Settings/AppSettings.cs` — `Replacements` + `SpokenCommandsEnabled`.
- `src/VoiceToText/App/TrayApplicationContext.cs` — apply `TextRules` in `StopAndTranscribeAsync`.
- `src/VoiceToText/Dashboard/DashboardForm.cs` — `TextRules` page kind + sidebar nav + page host.
- `src/VoiceToText/Diagnostics/SelfTest.cs` — `RunTextRulesTest`; extend `RunDashWindow` to paint the new page.
- `src/VoiceToText/Program.cs` — route `--textrulestest`.
- `src/VoiceToText/VoiceToText.csproj` — `<Version>0.6.5</Version>` (at ship).

## Out of scope (YAGNI for v1)

- Per-app rules; regex rules; import/export; re-capitalisation after a line break; spoken punctuation commands (Whisper already punctuates); reordering rows via drag (list order = grid order, edited by hand).
