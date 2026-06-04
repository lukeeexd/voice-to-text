# Start/Stop Sound Cues — Design Spec

**Date:** 2026-06-04
**Status:** Approved
**Ships as:** v0.8.12

Play a short audible cue when dictation starts and a mirrored cue when it stops, so the user
knows recording state without the on-screen widget. Works in both hold-to-talk and press-to-toggle
modes, independent of the overlay setting, with an on/off toggle (default ON). Synthesized tones —
no audio files (keeps the repo asset-free and license-clean), fully local.

## Sounds (synthesized, no files)

- **Start** = a gentle *rising* two-note chirp (e.g. ~660 Hz → ~880 Hz). **Stop** = the mirror,
  *falling* (~880 Hz → ~660 Hz). Same envelope/volume so they read as a pair.
- Each note ~60 ms, total cue ~120–140 ms, 44.1 kHz mono 16-bit PCM, **low amplitude (~0.22)**,
  with a short (~6 ms) linear fade-in/out per note to avoid clicks.
- Rendered ONCE into two `byte[]` PCM buffers at construction (pure, deterministic).

## New: `src/VoiceToText/Audio/SoundCues.cs`

`internal sealed class SoundCues : IDisposable`:
- ctor renders the two buffers via a pure `static byte[] RenderCue(double[] freqs, ...)` (the part
  that is unit-tested; takes no devices).
- `void PlayStart()` / `void PlayStop()` — play the matching buffer NON-BLOCKING via NAudio
  (`WaveOutEvent` + `RawSourceWaveStream` over the prebuilt bytes; default output device). Manage
  lifetime so the player isn't GC'd mid-play and is disposed on `PlaybackStopped` (track active
  players in a list; remove on stop). **Must never throw** — wrap play in try/catch + `Log` so a
  missing/locked output device can never break dictation. `Dispose` stops/disposes any active players.
- The caller decides whether sound is enabled; SoundCues itself just plays.

## Wiring — `App/TrayApplicationContext.cs`

- Field: `private readonly SoundCues _cues = new();` Dispose it where the other disposables are torn down.
- `StartRecording()`: after `_audio.Start(...)` succeeds + `SetState(AppState.Recording)`, add
  `if (_settings.SoundCuesEnabled) _cues.PlayStart();`. (Covers both modes — both call StartRecording.)
- `StopAndTranscribeAsync()`: at the very top (before `_audio.StopAndGetSamplesAsync`), add
  `if (_settings.SoundCuesEnabled) _cues.PlayStop();` so the cue is immediate on release/stop and
  never bleeds into the captured audio. (Covers hold-release + toggle-stop — both call this.)
- Audio-bleed note: the START cue overlaps the first ~130 ms of capture. It is short, quiet, and
  precedes speech, so Whisper is unaffected — accepted; do NOT delay capture (would add dictation latency).

## New setting — `Settings/AppSettings.cs`

`public bool SoundCuesEnabled { get; set; } = true;` (doc: play start/stop sounds; default on).

## Settings UI — `Dashboard/SettingsPage.cs`

- Add `private readonly ToggleSwitch _soundCheck = new();` and a row in the **Feedback & privacy**
  card (after the overlay row): `feedback.AddRow("Play a sound when dictation starts and stops", _soundCheck);`
- Wire it exactly like `_overlayCheck`: set in `LoadFromSettings` (`_soundCheck.Checked =
  _settings.SoundCuesEnabled;`), include in `Snapshot()`, persist in `Save()`
  (`_settings.SoundCuesEnabled = _soundCheck.Checked;`), and add `_soundCheck.CheckedChanged += (_, _) => UpdateDirty();`.

## Tests

- New headless check (extend an existing self-test, e.g. `RunWidgetTest` or `RunStatsTest`, OR add
  a tiny block): assert `SoundCues.RenderCue` produces a non-empty buffer and that the start vs stop
  buffers DIFFER (so they're a distinct pair). Pure generation only — never open an output device in
  a test. Do NOT call PlayStart/PlayStop in the smoke (no audio device on CI).
- `--dashwindow` constructs the Settings page incl. the new toggle (no exception).
- Clean `--no-incremental` build 0/0; full smoke battery green.

## Out of scope / YAGNI

Volume slider, custom sound files, per-mode sounds, output-device selection. Just one on/off toggle.

## Rollout

Ship v0.8.12 to BOTH feeds (GitHub canonical + local dev) via `/release`; push `main`.
