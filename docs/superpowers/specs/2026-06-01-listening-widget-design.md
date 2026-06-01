# Listening Widget — on-screen dictation indicator

- **Date:** 2026-06-01
- **Status:** Approved (brainstorm) — ready for an implementation plan
- **Component of:** Voice to Text (C# / .NET 10 WinForms system-tray app)

## Goal

A small always-on-top overlay that appears while you dictate, giving clear,
glanceable feedback that the app is active — **without stealing keyboard focus**
from the app you're typing into.

## User-facing design (approved)

- **Look:** a dark rounded "pill" with a blue accent (`#5FA5FA`), no text labels.
  - **Recording:** microphone icon + a wide row of ~14 vertical bars that react
    to the live microphone level.
  - **Transcribing:** a **pencil** icon + three bouncing dots. Distinct from the
    recording bars so the state change is visible; the mic icon is intentionally
    dropped here because the app is no longer listening.
- **Lifecycle:** hidden when idle. Fades in (recording) when dictation starts,
  switches to transcribing when capture stops, fades out once the transcribed
  text has been injected.
- **Placement:** defaults to **bottom-center of the primary screen**. The whole
  pill is **draggable**; the chosen position is remembered across sessions.
- **Interaction:** drag to move only. A click does **not** stop dictation — it is
  a pure indicator; the global hotkey remains the sole control.
- **Focus:** the overlay never takes keyboard focus, so dictation still lands in
  whatever app is focused.
- **Setting:** a "Show on-screen indicator" checkbox in Settings, **on by default**.

## Non-goals (v1)

- No Stop button, elapsed timer, or other controls.
- No click-to-stop.
- No partial/streaming transcription text shown in the widget.
- No always-on / idle-visible mode.
- No "follow the cursor / active window" placement — just one remembered position.

## Architecture

Each unit has one job and a clear boundary.

1. **`WasapiAudioSource` — level signal.**
   Surface the per-chunk microphone level the capture loop *already* computes (the
   RMS used by `SilenceDetector`). Add `event Action<float>? LevelChanged`, raised
   on the capture thread with a normalized `0..1` level while recording. No new
   audio processing — reuse the existing RMS. Add to the `IAudioSource` interface.

2. **`Overlay/LevelMeter` — pure, testable.**
   Turns a stream of raw levels into display data: normalization (a quiet-room
   floor and a sensible ceiling), exponential smoothing, and a mapping to an array
   of N bar heights with gentle per-bar variation so it reads as a meter. No UI,
   no threading — unit-tested headlessly exactly like `SilenceDetector`.

3. **`Overlay/ListeningOverlay` — the window.**
   A borderless, top-most, **no-activate**, layered window rendered with GDI+ and
   animated by a ~60 fps timer.
   - Window styles: `WS_EX_LAYERED | WS_EX_TOPMOST | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW`.
     Note: **not** `WS_EX_TRANSPARENT` — the pill must receive mouse input so it can
     be dragged. Override `ShowWithoutActivation` and `CreateParams` so it never
     activates and stays out of Alt-Tab.
   - Drawn to a 32-bit ARGB bitmap and pushed via `UpdateLayeredWindow` for
     anti-aliased rounded corners, a soft shadow, and translucency.
   - **Visual states:** `Hidden`, `Recording` (mic + bars from `LevelMeter`),
     `Transcribing` (pencil + animated dots). Fade in/out by ramping the layer alpha.
   - **Drag:** mouse-down + move repositions the window; the final location is saved
     to settings. A plain click does nothing.
   - The latest level is held in a `volatile` field written by `LevelChanged`
     (capture thread) and read each frame by the animation timer (UI thread) — no
     per-chunk cross-thread marshaling.

4. **`TrayApplicationContext` — wiring.**
   Owns the overlay when enabled. `SetState` drives it: `Recording` → show recording,
   `Transcribing` → show transcribing, `Idle` → fade out/hide. Subscribes the overlay
   to `IAudioSource.LevelChanged`. Honors `ShowOverlay` (creates the overlay on
   startup if enabled; creates/destroys it when the setting is toggled).

5. **`AppSettings` + `SettingsForm`.**
   Add `ShowOverlay` (bool, default `true`) and `OverlayX` / `OverlayY`
   (`int?` — remembered position; null = default bottom-center). Add the
   "Show on-screen indicator" checkbox, persisted in `OnSave`.

## Data flow

Hotkey → `StartRecording` → `SetState(Recording)` → overlay shows recording. The
capture thread raises `LevelChanged`; the overlay stores the latest level; its
animation timer maps level → bar heights via `LevelMeter` and repaints. Stop
(hotkey or VAD) → `SetState(Transcribing)` → overlay shows pencil + dots. Text
injected → `SetState(Idle)` → overlay fades out.

## Error handling

The overlay is purely cosmetic and never on the dictation path. Creating/showing
it is wrapped so any failure (display/driver edge case) is caught and ignored —
dictation continues exactly as today. When `ShowOverlay` is false, no window is
created at all.

## Testing

- **`LevelMeter`** — headless unit tests via a `--widgettest` diagnostic (mirroring
  `--vadtest` / `--updatecheck`): silence → near-zero bars; loud input → high bars;
  smoothing produces no jumps beyond a bound; fixed bar count; output stays within
  `0..1` / max bar height.
- **`ListeningOverlay`** — manual smoke: it appears on dictation without stealing
  focus (type into Notepad while the bars move), drag repositions it and the
  position survives a restart, the transcribing state shows pencil + dots, it hides
  after the paste, and the Settings toggle creates/removes it.

## Future (out of scope)

Optional click-to-stop, richer/branded animation, theming, and per-monitor
placement — revisit after the stats + dashboard work.
