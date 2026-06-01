# Settings: Update Controls + Model Picker — Design Spec

**Date:** 2026-06-01
**Status:** Approved (ready for implementation plan)
**Ships as:** v0.6.3

## Overview

Two additions to the Settings page (now a `UserControl` inside the dashboard window):

1. **Update settings UI** — expose `AutoUpdateEnabled` (a checkbox) and `UpdateFeedFolder` (a text field + Browse button), which today live only in `settings.json`.
2. **Speech model picker** — a dropdown to choose the Whisper model (speed vs. accuracy), mapped to `AppSettings.ModelType`, with the engine reloading when it changes.

English-only is intentional: **no language picker**, and `Language` behaviour is unchanged (`WhisperSttEngine` keeps using `_settings.Language`, currently "auto", which transcribes English fine).

## Context (current state)

- `UpdateChecker.Decide(enabled, feedFolder, current, manifest)` is the pure gate; it uses only `AutoUpdateEnabled` + `UpdateFeedFolder`. **`UpdateConsentAccepted` is defined in `AppSettings` but read nowhere** — a dormant flag.
- `UpdateService` is constructed once with the shared `AppSettings` and reads `AutoUpdateEnabled`/`UpdateFeedFolder` live on each `CheckAsync`, so changing them needs no service recreation.
- `WhisperSttEngine` takes `(GgmlType modelType, string language)` and holds the model in a `readonly` field; `LoadAsync` lazily downloads (via `ModelManager.EnsureModelAsync`) + builds the factory. Changing the model therefore means **dispose + new engine + load**.
- `TrayApplicationContext` owns `_stt`, and `WarmUpAsync()` already: shows a "Downloading the {model} speech model…" balloon when the model is absent, calls `_stt.LoadAsync()`, and updates the tray text. `OnSettingsSaved()` already re-applies settings after a Save on the Settings page.

## 1. Model picker

### `Stt/ModelOption.cs` (pure)

```csharp
public sealed record ModelOption(string Label, GgmlType Type);
```
plus a static list (the approved ladder) and the default:

| Label | Type |
|---|---|
| "Small (English) — fastest" | `GgmlType.SmallEn` |
| "Medium (English) — faster" | `GgmlType.MediumEn` |
| "Large v3 Turbo — recommended" | `GgmlType.LargeV3Turbo` |
| "Large v3 — most accurate" | `GgmlType.LargeV3` |

Exposed as `ModelOption.All` (in the order above) and `ModelOption.Default` (`LargeV3Turbo`). Pure, no I/O — the implementer must confirm each `GgmlType` member exists in Whisper.net 1.9.0 at build time.

### UI

A dark, owner-drawn `ComboBox` (`DropDownList`) styled exactly like the existing microphone combo — **reuse one shared `OnComboDrawItem(object?, DrawItemEventArgs)`** handler for both combos (it reads `(ComboBox)sender!`), replacing the device-only `OnDeviceComboDrawItem` (a small DRY improvement). Items are the `ModelOption` rows (the combo shows `Label`).

On load: select the option whose `Type == _settings.ModelType`; if none matches (e.g. a model not in the ladder), insert a `ModelOption($"{_settings.ModelType}", _settings.ModelType)` and select it so the current choice is never lost. On Save: `_settings.ModelType = (selected ModelOption).Type`.

## 2. Update settings UI

Controls (dark-themed to match the page):

- `CheckBox` "Automatically check for updates on startup" → `AutoUpdateEnabled`.
- `Label` "Update folder:" + a dark `TextBox` (editable; accepts a local or UNC path) → `UpdateFeedFolder` + a `Button` "Browse…" that opens a `FolderBrowserDialog` and, on OK, sets the textbox to the chosen path.
- An amber note `Label` (`Theme.Warning`): "Updates run an installer from this folder — only enable this for a folder you trust."

The folder field + Browse are always enabled (you can configure a folder before enabling checks). On Save:
- `_settings.AutoUpdateEnabled = checkbox.Checked`
- `_settings.UpdateFeedFolder = textbox.Text.Trim()`
- `_settings.UpdateConsentAccepted = checkbox.Checked` — enabling auto-update *is* the consent (the inline warning is the disclosure; no separate modal).

## 3. Engine reload on model change (`TrayApplicationContext`)

- New field `private GgmlType _loadedModelType;` initialised to `_settings.ModelType` in the constructor (right where `_stt` is created).
- New field `private bool _modelReloadPending;`.
- In `OnSettingsSaved()`, after the existing hotkey/overlay re-apply: if `_settings.ModelType != _loadedModelType`, set `_modelReloadPending = true` and call `MaybeReloadModel()`. Also, if `_settings.AutoUpdateEnabled` and `UpdateFeedFolder` is non-empty, fire `_ = CheckForUpdatesAsync(userInitiated: false)` so a newly-configured feed surfaces an available update (quiet balloon, no nag).
- `MaybeReloadModel()`:
  ```
  if (!_modelReloadPending || _busy || _state != AppState.Idle) return;
  _modelReloadPending = false;
  var old = _stt;
  _loadedModelType = _settings.ModelType;
  _stt = new WhisperSttEngine(_settings.ModelType, _settings.Language);
  try { old.Dispose(); } catch { /* best effort */ }
  _ = Task.Run(WarmUpAsync);
  ```
- Call `MaybeReloadModel()` once more in `StopAndTranscribeAsync`'s `finally` (on the UI thread, after `SetState(Idle)` and `_busy = false`), so a model change made while a dictation was in flight applies the moment it finishes — we never dispose an engine that is transcribing.

`WarmUpAsync` is reused unchanged: switching to a not-yet-downloaded model shows the download balloon and loads it; switching to a cached model just reloads quickly.

## 4. Layout

`SettingsPage.BuildUi` is re-flowed top-to-bottom (absolute positions, same dark styling, left margin x=20, field width ~440). Order:

1. Microphone (label + combo)
2. **Speech model** (label + combo) — new
3. Dictation hotkey (label + box) + wrapping hint
4. Auto-stop checkbox + "Stop after [n] seconds of silence"
5. Show on-screen indicator checkbox
6. Typing speed ([n] WPM)
7. **Updates** — new: auto-update checkbox; "Update folder:" + textbox + Browse…; amber warning note
8. Start automatically when I log in
9. Save button + "Settings saved ✓"

The window is 920×620 with the page docked Fill, so there is ample vertical room. Exact Y-coordinates are pinned in the implementation plan. `OnVisibleChanged` still clears the saved-label on re-show.

## 5. Error handling

- `FolderBrowserDialog` cancel → leave the field unchanged.
- A blank/typo'd update folder is harmless: `UpdateChecker.Decide` returns `NoFeedConfigured`/`ManifestInvalid` and the tray only surfaces those when the user explicitly checks. No validation gate needed.
- Engine reload failures (e.g. download error for a new model) flow through `WarmUpAsync`'s existing catch → an error balloon; the app keeps the (now-disposed) old engine reference replaced by the new one, which will retry `LoadAsync` on the next dictation.

## 6. Testing

- **Build:** clean (`--no-incremental`) 0 warnings / 0 errors.
- **`--dashwindow` smoke:** constructs + paints both pages including the new model combo, update controls, and Browse button — catches layout/owner-draw exceptions.
- **`--updatecheck`:** still passes (the updater's pure logic is untouched).
- **Manual:**
  - Change the model → a balloon appears (download balloon if that model isn't cached yet), the engine reloads, and the next dictation uses it. Switching mid-dictation applies after the current one finishes.
  - Toggle "Automatically check for updates", Browse to a folder (and type a UNC path) → Save → `settings.json` shows the new `AutoUpdateEnabled`/`UpdateFeedFolder` with `UpdateConsentAccepted` matching; "Check for updates…" works against the chosen folder.
  - Existing settings (mic, hotkey, auto-stop, overlay, WPM, startup) still round-trip; English transcription unaffected.

`ModelOption.All`/`Default` is a trivial pure static table verified at compile time; no dedicated self-test is added (YAGNI).

## 7. Rollout

Ship to the update feed as **v0.6.3** (bump `<Version>`, `publish.ps1`, ISCC, copy the setup to `D:\ClaudeCode\VoiceToText-Releases`, write `latest.json` with the SHA-256). **The feed population must run from the foreground session** — out-of-repo writes from isolated subagents did not persist in a prior release; foreground writes do.

## Files

**Create**
- `src/VoiceToText/Stt/ModelOption.cs` — pure model-option table (label ↔ GgmlType, default).

**Modify**
- `src/VoiceToText/Dashboard/SettingsPage.cs` — model combo, update controls (checkbox/folder/Browse/note), shared combo owner-draw, OnSave field mapping, re-flowed layout.
- `src/VoiceToText/App/TrayApplicationContext.cs` — `_loadedModelType` + `_modelReloadPending` fields, `MaybeReloadModel()`, the `StopAndTranscribeAsync` finally hook, and the `OnSettingsSaved` extension (model reload + post-save quiet update check).
- `src/VoiceToText/VoiceToText.csproj` — `<Version>0.6.3</Version>` (at ship).
