# Sound-Cue Volume Control — Design Spec

**Date:** 2026-06-04
**Status:** Approved
**Ships as:** v0.8.13

Add a volume control for the start/stop sound cues (v0.8.12). A custom dark slider in
Settings → Feedback & privacy, under the sound toggle. **100% = the current loudness** (the
user likes it) and the slider scales *down* from there; greyed out when sounds are off; plays a
preview cue when released so the user can calibrate by ear.

## New control — `src/VoiceToText/Dashboard/Controls/DarkSlider.cs`

`internal sealed class DarkSlider : Control` (premium dark look; a native TrackBar is stock/ugly):
- `public double Value { get; set; }` clamped 0..1, raises `ValueChanged`; annotate
  `[DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]` (WFO1000).
- `event EventHandler? ValueChanged`.
- ctor: `SetStyle(OptimizedDoubleBuffer | AllPaintingInWmPaint | UserPaint | ResizeRedraw, true)`,
  default `Size` (e.g. 160×24) set LAST (no child fields, but keep the ctor-order rule). `Cursor = Hand`.
- OnPaint (AntiAlias): a thin rounded track (height ~4, `Theme.InputBorder`) centered vertically;
  the filled portion left-of-thumb in `Theme.Accent`; a circular thumb (~r7) in `Theme.AccentLight`
  with a subtle border. When `!Enabled`, draw everything muted (`Theme.TextMuted`/`InputBg`) and
  ignore mouse. Fill own bounds (no `g.Clear`); dispose all GDI objects.
- Mouse: OnMouseDown (left) → set Value from X + `Capture = true`; OnMouseMove while captured →
  update Value; OnMouseUp → `Capture = false` (the standard `MouseUp` event then fires — the host
  uses it for the preview). Value↔X mapping accounts for the thumb radius padding so 0 and 1 are reachable.

## Volume application — `src/VoiceToText/Audio/SoundCues.cs`

- Add `public float Volume { get; set; } = 1f;` (clamp 0..1 on set).
- In `Play`, scale output via NAudio `VolumeSampleProvider`:
  `ISampleProvider src = new VolumeSampleProvider(stream.ToSampleProvider()) { Volume = Volume };`
  then `player.Init(src);`. (Device-independent, no system-volume side effects.) Buffers stay at the
  current 0.22 amplitude, so Volume = 1 reproduces today's sound exactly. Read `Volume` under the
  existing lock when capturing it for the play.

## Setting — `src/VoiceToText/Settings/AppSettings.cs`

`public double SoundCuesVolume { get; set; } = 1.0;` (0..1; default = current loudness).

## Wiring — `src/VoiceToText/App/TrayApplicationContext.cs`

Before each `_cues.PlayStart()/PlayStop()` (or once when settings change), set
`_cues.Volume = (float)_settings.SoundCuesVolume;` so the live setting is honored without restart.
(Simplest: set it on the line above each gated PlayStart/PlayStop.)

## Settings UI — `src/VoiceToText/Dashboard/SettingsPage.cs`

- Field `private readonly DarkSlider _volumeSlider = new() { Width = 160 };` and a percent `Label`.
- A row in the Feedback & privacy card right AFTER the sound toggle: a small composite of the slider
  + a right-side `NN%` label (update the label text on `ValueChanged`). Label e.g. `"Volume"`.
- `UpdateSoundEnabled()`: `_volumeSlider.Enabled = _soundCheck.Checked;` — call it from
  `_soundCheck.CheckedChanged` (alongside `UpdateDirty`) and at the end of `LoadFromSettings`.
- `LoadFromSettings`: `_volumeSlider.Value = _settings.SoundCuesVolume;`
- `Snapshot()`: include the volume as a STABLE token to avoid float-drift dirtiness, e.g.
  `((int)Math.Round(_volumeSlider.Value * 100))`.
- `Save()`: `_settings.SoundCuesVolume = _volumeSlider.Value;`
- Dirty wiring: `_volumeSlider.ValueChanged += (_, _) => UpdateDirty();`
- **Preview on release:** SettingsPage owns `private readonly SoundCues _previewCues = new();`
  Subscribe `_volumeSlider.MouseUp += (_, _) => { if (_soundCheck.Checked) { _previewCues.Volume =
  (float)_volumeSlider.Value; _previewCues.PlayStart(); } };`. Override
  `protected override void Dispose(bool disposing)` to dispose `_previewCues` (call base).

## Tests

- If DarkSlider exposes a pure Value↔ratio mapping, assert clamping (−1→0, 2→1) and a midpoint in a
  self-test. Otherwise rely on `--dashwindow` constructing + painting the Settings page with the
  slider (no exception). Do NOT play audio in any test.
- Clean `--no-incremental` build 0/0; full smoke battery green.

## Out of scope / YAGNI

Louder-than-current (100% stays the max), keyboard/scroll-wheel on the slider, separate
start/stop volumes, a numeric entry. Just the bar + % + preview.

## Rollout

Ship v0.8.13 to BOTH feeds (GitHub canonical + local dev) via `/release`; push `main`.
