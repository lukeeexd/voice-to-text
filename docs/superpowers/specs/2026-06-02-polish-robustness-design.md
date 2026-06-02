# Polish & Robustness — Design Spec

**Date:** 2026-06-02
**Status:** Approved (ready for implementation plan)
**Ships as:** v0.7.0
**Context:** Fourth and final queued feature (after text rules, push-to-talk, dashboard extras). Voice to Text — C#/.NET 10 WinForms, fully local, English-only. **Onboarding is explicitly deferred** to a later release (single user for now).

## Overview

Five loosely-coupled polish/robustness items shipped as one release, built in dependency order:

1. **Logging** (foundation) — a tiny rolling log file.
2. **Global crash handler** — log + friendly balloon instead of silent death.
3. **Friendlier error surfacing** — visible, logged handling of mic/model failures.
4. **Dark title bar** — caption matches the dark theme.
5. **Unsaved-changes affordance** in Settings — indicator + confirm-on-leave.
6. **About / diagnostics page** — confirms the GPU/Vulkan path; log + diagnostics access.

(About consumes the log path + runtime info, so it lands last.)

## Decisions (locked during brainstorming)

- **Unsaved changes:** dirty indicator + Save enabled only when dirty + **confirm-on-leave** prompt (Save / Discard / Cancel) when switching pages or closing the window with unsaved edits.
- **About:** a **new sidebar page** with the **full** diagnostics card including the **GPU name** (via `EnumDisplayDevices`, dependency-free).
- **Logging never records transcribed text** — metadata/timings/errors only — preserving the privacy posture even with logging always on.
- Ships as **v0.7.0**.

## 1. Logging

New `src/VoiceToText/Diagnostics/Log.cs`:
- An instance `LogWriter` (one clear unit, testable) that owns a log file path and does the writing + rotation; a static `Log` facade wrapping a default `LogWriter` pointed at `%APPDATA%\VoiceToText\logs\voicetotext.log`.
- API: `Log.Info(string message)`, `Log.Error(string message, Exception? ex = null)`, and `Log.LogFolder` (the logs directory, for the About page).
- Line format: `yyyy-MM-dd HH:mm:ss.fff [INFO|ERROR] message` (+ exception `ToString()` on the next lines for errors).
- **Rotation:** before writing, if the file exceeds ~512 KB, move it to `voicetotext.log.1` (replacing any existing `.1`) and start a fresh file. Keep exactly the current + one previous.
- **Thread-safe** (a lock) and **never throws** — all I/O wrapped in try/catch (logging must not break dictation).
- **Privacy:** callers pass word counts, timings, app names, runtime, and error text — **never** the transcribed text.
- Initialized early in `Program.cs` (`Log.Info` of app start + version); `WhisperSttEngine`/warm-up logs the loaded runtime; `StopAndTranscribeAsync` logs transcription timings (e.g. `transcribed N words in 0.31s`).

## 2. Global crash handler

- `Program.cs`: `Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException)` before `Application.Run`.
- `TrayApplicationContext` (owns the tray icon) subscribes in its ctor and unsubscribes in `Dispose`:
  - `Application.ThreadException` → `Log.Error("UI thread exception", e.Exception)` + a friendly balloon ("Something went wrong — see the log via About → Open log folder.").
  - `AppDomain.CurrentDomain.UnhandledException` → `Log.Error(...)` (best-effort; the process may be terminating, so logging is the priority over the balloon).

## 3. Friendlier error surfacing

Each case logs (`Log.Error`) and shows a tray balloon (existing `ShowError`/`ShowBalloonTip`); no silent failures:
- **No microphone at record start** — in `StartRecording`, detect no input device (or a thrown start) and show "No microphone found — connect one and try again." instead of a generic message; stay Idle.
- **Mic removed mid-recording** — `WasapiAudioSource` surfaces a device-lost error (NAudio's `RecordingStopped` carries an exception); `StopAndTranscribeAsync` shows "Microphone disconnected — recording stopped." and returns to Idle.
- **Whisper load / transcription failure** — already caught; add logging + a clear message ("Speech model failed to load/transcribe — see the log.").
- **Model download failure / offline** — `ModelManager.EnsureModelAsync` failure surfaces as "Couldn't download the speech model — check your connection." + log.

## 4. Dark title bar

- A small `src/VoiceToText/Dashboard/DarkTitleBar.cs` helper with a `DwmSetWindowAttribute` `LibraryImport` (matching the app's existing source-gen P/Invoke style).
- Applied from `DashboardForm.OnHandleCreated`: set `DWMWA_USE_IMMERSIVE_DARK_MODE` (attribute **20**, falling back to **19** on older Win10 builds), value `TRUE`. Wrapped best-effort so it no-ops on unsupported Windows.

## 5. Unsaved-changes affordance (Settings)

`SettingsPage` (`src/VoiceToText/Dashboard/SettingsPage.cs`):
- After `LoadFromSettings`/`ReloadFromSettings`, capture a **baseline snapshot** of the values `OnSave` writes (device, model, hotkey, HoldToTalk, AutoStopEnabled, silence seconds, ShowOverlay, HistoryEnabled, TypingSpeedWpm, AutoUpdateEnabled, update folder, start-on-login).
- Wire change events on every input; a private `IsDirty()` compares current control values to the baseline.
- A "● Unsaved changes" `Label` (near Save, amber/accent) is visible only when dirty; the **Save button is enabled only when dirty**.
- On Save: perform the write, re-snapshot the baseline, clear dirty (hide the label, disable Save). Expose a public **method** `bool HasUnsavedChanges()` and a public `void Save()` (performs the existing save) — methods, not properties, to avoid WFO1000.

`DashboardForm`:
- On any transition away from Settings — centralized in `ShowPage` so it covers nav-button clicks **and** programmatic navigation (e.g. the tray's "Open Dashboard"/"Settings…") — **and** on `FormClosing`, if `_settingsPage.HasUnsavedChanges()` → prompt **Save / Discard / Cancel**:
  - **Save** → `_settingsPage.Save()`, then proceed (switch page / close).
  - **Discard** → `_settingsPage.ReloadFromSettings()`, then proceed.
  - **Cancel** → abort (stay on Settings / cancel the close).

## 6. About / diagnostics page

- New `DashboardPageKind.About` + an "About" `NavButton` (sidebar order: Dashboard / Settings / Text rules / History / About — About added first so it sits at the bottom).
- A **pure** `src/VoiceToText/Diagnostics/DiagnosticsInfo.cs` built from injected inputs (so it is unit-testable): `Version`, `Runtime` (string, e.g. "Vulkan"/"Cpu"), `IsGpuAccelerated` (true unless Runtime is CPU), `Gpu`, `Model`, `ModelPath`, `ModelSizeBytes`, `Os`, `Framework`, `Arch`. Exposes an ordered list of `(Label, Value)` rows and `string ToClipboardText()` (the rows as plain text — **no transcripts**).
- `src/VoiceToText/Dashboard/AboutPage.cs` (UserControl, dark): renders the rows in a card (Acceleration row green when `IsGpuAccelerated`, amber otherwise), a footer ("Fully local — your audio and transcripts never leave this PC."), and three flat buttons:
  - **Check for updates** → raises `event Action? CheckForUpdatesRequested;` (bubbled by `DashboardForm` to `TrayApplicationContext.CheckForUpdatesAsync(userInitiated: true)`).
  - **Open log folder** → `Process.Start("explorer.exe", Log.LogFolder)`.
  - **Copy diagnostics** → `Clipboard.SetText(info.ToClipboardText())`.
  - Reloads its info on show (`OnVisibleChanged`).
- **Live inputs:** Runtime via the existing reflection probe (`Whisper.net.LibraryLoader.RuntimeOptions.LoadedLibrary`) — extracted into a reusable `RuntimeProbe.LoadedRuntime()` shared by `SelfTest` and About (shows "Loading…" until the engine has loaded once). Model type from `AppSettings.ModelType`; model path/size from `ModelManager` + `FileInfo`. GPU name via `EnumDisplayDevices` (`src/VoiceToText/Diagnostics/GpuInfo.cs`, P/Invoke, "Unknown" on failure). OS/Framework/Arch from `RuntimeInformation`/`Environment`.
- `AboutPage(AppSettings settings)`; `DashboardForm` constructs it, wires the nav + page + `CheckForUpdatesRequested` bubble.

## Testing

- **`--logtest`** (new): a `LogWriter` pointed at a temp dir — writing under the cap keeps one file; exceeding the cap creates `voicetotext.log.1` and resets the main file; `Error(msg, ex)` and a write to an unwritable path do not throw.
- **`--abouttest`** (new): `DiagnosticsInfo` from injected inputs produces the expected ordered rows; `IsGpuAccelerated` is true for "Vulkan" and false for "Cpu"; `ToClipboardText()` contains version/runtime/gpu/model and never any transcript text.
- **Extend `--dashwindow`** to `ShowPage(About)` + `Refresh()` (paint the About page).
- Clean build 0/0 (`--no-incremental`).
- **Manual:** dark title bar renders dark; Settings shows the "Unsaved changes" indicator + Save enables only when dirty + leaving/closing dirty prompts Save/Discard/Cancel; unplug the mic mid-dictation → "Microphone disconnected" balloon + log line; About shows "Vulkan (GPU)" + the GPU name, and the three actions work.

## Files

**Create**
- `src/VoiceToText/Diagnostics/Log.cs` — `LogWriter` (rotation) + static `Log` facade.
- `src/VoiceToText/Diagnostics/DiagnosticsInfo.cs` — pure diagnostics row model.
- `src/VoiceToText/Diagnostics/GpuInfo.cs` — `EnumDisplayDevices` P/Invoke (GPU name, best-effort).
- `src/VoiceToText/Diagnostics/RuntimeProbe.cs` — shared loaded-runtime reflection probe (reused by `SelfTest`).
- `src/VoiceToText/Dashboard/AboutPage.cs` — the About page UserControl.
- `src/VoiceToText/Dashboard/DarkTitleBar.cs` — `DwmSetWindowAttribute` helper.

**Modify**
- `src/VoiceToText/Program.cs` — early `Log` init, `SetUnhandledExceptionMode`, route `--logtest`/`--abouttest`.
- `src/VoiceToText/App/TrayApplicationContext.cs` — crash-handler subscriptions; error surfacing in `StartRecording`/`StopAndTranscribeAsync`/`WarmUpAsync`; transcription-timing logging; bubble About's `CheckForUpdatesRequested`.
- `src/VoiceToText/Audio/WasapiAudioSource.cs` — surface device-lost error from `RecordingStopped`.
- `src/VoiceToText/Stt/WhisperSttEngine.cs` and/or `Stt/ModelManager.cs` — log load/download errors with friendly messages; use `RuntimeProbe`.
- `src/VoiceToText/Dashboard/SettingsPage.cs` — dirty tracking, indicator, Save-enable, `HasUnsavedChanges()`, `Save()`.
- `src/VoiceToText/Dashboard/DashboardForm.cs` — About page kind/nav/page; dark title bar; unsaved-changes leave/close prompts.
- `src/VoiceToText/Diagnostics/SelfTest.cs` — `RunLogTest`, `RunAboutTest`, extend `RunDashWindow`; reuse `RuntimeProbe`.
- `src/VoiceToText/VoiceToText.csproj` — `<Version>0.7.0</Version>` (at ship).

## Out of scope (YAGNI / deferred)

- **First-run onboarding** (explicitly deferred to a later release).
- Paste-timing rework in `ClipboardTextInjector` (no observed failure).
- `uiAccess`/elevation strategy for dictating over admin apps (separate decision; the Discord case was root-caused + workaround accepted).
- Configurable log size/retention; structured/JSON logs; in-app log viewer (Open-folder is enough).
- `DWMWA_CAPTION_COLOR` exact-match tinting (immersive dark mode is enough).

## Rollout

Ship to the feed as **v0.7.0** (bump `<Version>`, `publish.ps1`, ISCC, copy setup to `D:\ClaudeCode\VoiceToText-Releases\VoiceToText-Setup-0.7.0.exe`, write `latest.json` with lowercase SHA-256). **Feed population runs from the foreground session.** **Verify the feed SAFELY** — never pass the real Releases folder to `--updatecheck` (it writes test files then `Directory.Delete()`s the folder); use the no-arg self-test plus a manual SHA/JSON integrity check.
