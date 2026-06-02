# First-Run Onboarding — Design Spec

**Date:** 2026-06-02
**Status:** Approved (lean build — implemented directly, no separate plan/subagent cycle)
**Ships as:** v0.7.1
**Context:** The onboarding item deferred from Polish & Robustness (v0.7.0). Voice to Text — C#/.NET 10 WinForms, fully local.

## Decision (locked)

A **lightweight first-run welcome dialog** (chosen over a multi-step wizard or a dashboard Welcome page) — leanest, fits the solo user, reuses existing Settings for configuration.

## Components

### `Onboarding/WelcomeForm.cs` (new, dark `Form`)
- Fixed-size (~480×360), `FixedDialog`, centered, no maximize/minimize, dark title bar via the existing `Dashboard.DarkTitleBar.Apply`.
- Heading "Welcome to Voice to Text" (`Theme.Heading`).
- Body (3 short lines): what it is (local/offline, nothing leaves the PC); how to dictate ("press **{hotkey}** — read live from `settings.Hotkey.Describe()` — speak, then stop; the text is typed into the focused app"); and "pick your microphone / change the hotkey in Settings".
- A **status line** read once at open from `ModelManager.IsModelPresent(settings.ModelType)`: green "Speech model ready ✓" or amber "Downloading the speech model now (one-time, ~1.5 GB). Dictation works as soon as it's ready."
- Buttons: **Open Settings** (raises `event Action? OpenSettingsRequested;` then closes) and **Got it** (closes; `AcceptButton`). The X behaves like Got it.
- UI only — no settings writes, no business logic.

### `Settings/AppSettings.cs`
- Add `public bool OnboardingCompleted { get; set; } = false;` (persisted with the rest).

### `App/TrayApplicationContext.cs`
- At the end of the constructor, first-run trigger:
  ```csharp
  if (!_settings.OnboardingCompleted)
  {
      _settings.OnboardingCompleted = true;
      _settings.Save();      // mark + persist immediately so it never re-shows (even on crash/close)
      ShowWelcome();
  }
  ```
- `ShowWelcome()` constructs `WelcomeForm(_settings)`, subscribes `OpenSettingsRequested → ShowDashboard(DashboardPageKind.Settings)`, holds it in a `_welcome` field (cleared on `FormClosed`), and `Show()`s it non-modally (the message loop starts right after via `Application.Run`).

## Behavior

- First-ever launch (no `settings.json` → defaults → `OnboardingCompleted == false`): the welcome appears once; the flag is set+saved immediately. `WarmUpAsync` is already downloading the model, so the status line shows "Downloading…". Subsequent launches: flag is true → no welcome. Post-update launches: flag already true → no welcome.

## Testing

- Extend the `--dashwindow` smoke to construct + `Show()` + paint + `Close()` a `WelcomeForm` (catches construction/paint exceptions headlessly; no asserts).
- One clean `--no-incremental` build (0/0).
- Manual: delete `%APPDATA%\VoiceToText\settings.json`, launch → welcome appears once; relaunch → it does not; "Open Settings" opens the dashboard Settings page.

## Files

**Create:** `src/VoiceToText/Onboarding/WelcomeForm.cs`.
**Modify:** `src/VoiceToText/Settings/AppSettings.cs` (`OnboardingCompleted`); `src/VoiceToText/App/TrayApplicationContext.cs` (first-run trigger + `ShowWelcome`); `src/VoiceToText/Diagnostics/SelfTest.cs` (`RunDashWindow` smoke); `src/VoiceToText/VoiceToText.csproj` (`<Version>0.7.1</Version>` at ship).

## Out of scope

Multi-step wizard; dashboard Welcome page; live download-progress bar (a static status line is enough); a "replay onboarding" option.

## Rollout

Ship to the feed as **v0.7.1** (publish.ps1 + ISCC + copy setup to Releases + latest.json with SHA-256, from the foreground; verify the feed safely — never pass the real Releases folder to `--updatecheck`).
