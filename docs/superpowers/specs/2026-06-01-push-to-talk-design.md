# Push-to-Talk — Design Spec

**Date:** 2026-06-01
**Status:** Approved (ready for implementation plan)
**Ships as:** v0.6.6
**Context:** Second of four queued features (after Text rules; then dashboard extras, polish). Voice to Text — C#/.NET 10 WinForms, fully local. English-only.

## Overview

Add a **hold-to-talk** activation mode as an alternative to the current press-to-toggle: hold the global hotkey to record, release to stop + transcribe. The user picks the mode in Settings; the default stays toggle (no behavior change for existing users).

## Decisions (locked during brainstorming)

- **Release detection:** poll `GetAsyncKeyState(virtualKey)` on a ~40 ms timer while holding (keep `RegisterHotKey` for the press). No low-level keyboard hook.
- **Mode UI:** a dropdown in Settings ("Press to toggle" / "Hold to talk").
- **Auto-stop in hold mode:** disabled (release governs the stop); the auto-stop Settings controls are greyed when Hold is selected.
- **Accidental taps:** no min-hold threshold — a too-short hold yields empty audio and the existing empty-transcription no-op covers it.
- **Stuck-key safety:** a ~2-minute backstop force-stops a hold that never sees a release.

## Current state (for reference)

- `Hotkeys/HotkeyManager.cs` registers one global hotkey via `NativeHotkeys.RegisterHotKey` (with `MOD_NOREPEAT`) on the hidden message-only window and raises `Pressed` on `WM_HOTKEY` (key-down only — no key-up, no repeat). `NativeHotkeys` uses source-gen `LibraryImport` P/Invoke.
- `App/TrayApplicationContext.cs`: `OnHotkeyPressed` toggles `StartRecording`/`StopAndTranscribeAsync` based on `AppState` (Idle/Recording/Transcribing), guarded by `_busy`. `StartRecording` calls `_audio.Start(_settings.InputDeviceId, _settings.AutoStopEnabled, _settings.AutoStopSilenceSeconds)`. Auto-stop-on-silence is handled by `Audio/SilenceDetector` and surfaced via `_audio.SilenceDetected → OnSilenceDetected`.

## 1. `HotkeyManager` — add release detection

Add (no change to the existing `Pressed`/`Register`/`Unregister` semantics):

- `public bool HoldToTalk { get; set; }` — set by the tray from settings.
- A stored `uint _vk` captured in `Register(hotkey)` (= `hotkey.VirtualKey`).
- `public event Action? Released;`
- A `System.Windows.Forms.Timer _pollTimer` (Interval ≈ 40 ms) and an `int _holdStartTick`.
- In `OnHotkeyMessage` (the `WM_HOTKEY` handler), after raising `Pressed`: if `HoldToTalk` **and** the poll timer is not already running, record `_holdStartTick = Environment.TickCount` and start `_pollTimer`.
- `_pollTimer.Tick`: if `(GetAsyncKeyState(_vk) & 0x8000) == 0` (key is up) **or** `Environment.TickCount - _holdStartTick > MaxHoldMs` (≈120000, the stuck-key backstop), stop the timer and raise `Released`.
- `GetAsyncKeyState` added to `NativeHotkeys` as a source-gen `LibraryImport("user32.dll")` returning `short`.
- `Unregister` / `Dispose` / setting `HoldToTalk = false` stop the timer; `Dispose` disposes it.
- Guard: a re-`Pressed` while the timer runs is ignored (with `MOD_NOREPEAT` this is rare anyway).

This keeps `HotkeyManager` self-contained: it owns press+release detection and exposes two events; consumers don't poll.

## 2. `TrayApplicationContext` — orchestration

- Set `_hotkeys.HoldToTalk = _settings.HoldToTalk` after constructing the hotkey manager (startup) and in `OnSettingsSaved` (so a mode change applies immediately).
- Subscribe `_hotkeys.Released += OnHotkeyReleased`.
- `OnHotkeyPressed`:
  - **Toggle mode** (`!_settings.HoldToTalk`): unchanged.
  - **Hold mode**: only `StartRecording()` when `Idle` (never toggle-stop on press).
- `OnHotkeyReleased`: no-op unless `_settings.HoldToTalk` **and** `_state == Recording` **and** `!_busy`; then `_ = StopAndTranscribeAsync()`.
- `StartRecording`: compute `bool autoStop = !_settings.HoldToTalk && _settings.AutoStopEnabled;` and call `_audio.Start(_settings.InputDeviceId, autoStop, _settings.AutoStopSilenceSeconds)`. (In hold mode, silence auto-stop is off — release stops it.)

The `_busy` guard and `AppState` transitions already serialize start/stop, so a released-after-transcription-started event harmlessly no-ops. The overlay widget (recording/transcribing) is unchanged — it follows `AppState` exactly as today.

## 3. Settings UI (`SettingsPage`)

Add an **"Activation"** label + `ComboBox` (`DropDownStyle = DropDownList`, dark-themed like the existing combos, wired to the shared `OnComboDrawItem`) with two items: `"Press to toggle"` (index 0 → `HoldToTalk = false`) and `"Hold to talk"` (index 1 → `HoldToTalk = true`). Placed just under the hotkey hint label; subsequent controls shift down to make room (re-flow the absolute Y coordinates).

- `LoadFromSettings`: select index from `_settings.HoldToTalk`; call the same enable/disable helper as below.
- A `SelectedIndexChanged` handler enables/disables `_autoStopCheck` + `_silenceUpDown` (greyed when Hold is selected, since auto-stop doesn't apply). It composes with the existing rule that the silence spinner follows the auto-stop checkbox — i.e. the silence spinner is enabled only when *not* Hold **and** auto-stop is checked.
- `OnSave`: `_settings.HoldToTalk = (_activationCombo.SelectedIndex == 1);` (alongside the existing writes).

## 4. Storage (`AppSettings`)

Add `public bool HoldToTalk { get; set; } = false;` (default preserves today's toggle behavior).

## 5. Error handling / edges

- Stuck key / missed release → the ~2-minute backstop in the poll timer force-stops; the user can also just press the hotkey again (in toggle mode) or it auto-recovers next cycle.
- Releasing after auto-transcription already began (only possible in toggle mode, where Released isn't wired) → not applicable; in hold mode auto-stop is off so the only stop is release.
- Switching mode mid-recording is not a normal flow (the Settings window has focus, not recording); `OnSettingsSaved` re-applies `HoldToTalk` and the next dictation uses the new mode.

## 6. Testing

- This is Win32 key-state polling + WinForms orchestration (not pure logic), so **no new headless self-test**.
- Clean build (`--no-incremental`) 0 warnings / 0 errors. (No new public property on a Control → no WFO1000.)
- `--dashwindow` smoke: the Settings page constructs + paints the new Activation dropdown (already part of the smoke).
- Manual: select **Hold to talk** → the auto-stop checkbox + spinner grey out; Save. Hold F13 → records only while held; release → transcribes. Quick tap → nothing pasted. Switch back to **Press to toggle** → press starts, press stops, auto-stop controls re-enable and work as before.

## 7. Rollout

Ship to the feed as **v0.6.6** (bump `<Version>`, `publish.ps1`, ISCC, copy setup to `D:\ClaudeCode\VoiceToText-Releases`, write `latest.json` with SHA-256). **Feed population runs from the foreground session** (isolated subagent out-of-repo writes didn't persist previously).

## Files

**Modify**
- `src/VoiceToText/Hotkeys/HotkeyManager.cs` — `HoldToTalk`, stored VK, `Released` event, poll timer + backstop.
- `src/VoiceToText/Hotkeys/NativeHotkeys.cs` — `GetAsyncKeyState` P/Invoke.
- `src/VoiceToText/App/TrayApplicationContext.cs` — set `HoldToTalk`, subscribe `Released`, hold-vs-toggle in `OnHotkeyPressed`, `OnHotkeyReleased`, auto-stop-off-in-hold in `StartRecording`.
- `src/VoiceToText/Dashboard/SettingsPage.cs` — Activation dropdown + greying + load/save + layout re-flow.
- `src/VoiceToText/Settings/AppSettings.cs` — `HoldToTalk`.
- `src/VoiceToText/VoiceToText.csproj` — `<Version>0.6.6</Version>` (at ship).

## Out of scope (YAGNI)

- Min-hold threshold; per-app activation mode; hold-to-talk for modifier-only hotkeys (the "released" trigger is the main virtual key); a configurable backstop duration.
