# Listening Widget Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.
>
> **Commits:** end every commit message with `Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>`.
>
> **Build/run note:** the .NET 10 SDK is per-user at `C:\Users\Luke\.dotnet`. Build with `& "$env:USERPROFILE\.dotnet\dotnet.exe" build VoiceToText.slnx -c Debug`. To run the built exe directly, first set `$env:DOTNET_ROOT="C:\Users\Luke\.dotnet"`.

**Goal:** A draggable, focus-less, top-most overlay pill that appears while dictating — microphone + live level bars while recording, pencil + bouncing dots while transcribing.

**Architecture:** A pure `LevelMeter` maps the per-chunk RMS (already computed in `WasapiAudioSource`) to smoothed bar heights. A layered, no-activate `ListeningOverlay` window renders the pill with GDI+ on a ~30 fps timer; position is persisted in settings. `TrayApplicationContext` owns the overlay, feeds it the level, and drives its state from `SetState`.

**Tech Stack:** C# / .NET 10, WinForms, GDI+, Win32 `UpdateLayeredWindow` (per-pixel alpha). No new NuGet packages.

---

## File Structure
- Create `src/VoiceToText/Overlay/OverlayState.cs` — visual-state enum.
- Create `src/VoiceToText/Overlay/LevelMeter.cs` — pure RMS→bar-heights (testable).
- Create `src/VoiceToText/Overlay/NativeOverlay.cs` — `UpdateLayeredWindow` P/Invoke + `SetBitmap` helper.
- Create `src/VoiceToText/Overlay/ListeningOverlay.cs` — the layered overlay `Form`.
- Modify `src/VoiceToText/Audio/IAudioSource.cs` — add `LevelChanged` event.
- Modify `src/VoiceToText/Audio/WasapiAudioSource.cs` — raise `LevelChanged` each chunk.
- Modify `src/VoiceToText/Settings/AppSettings.cs` — `ShowOverlay`, `OverlayX`, `OverlayY`.
- Modify `src/VoiceToText/Settings/SettingsForm.cs` — "Show on-screen indicator" checkbox.
- Modify `src/VoiceToText/App/TrayApplicationContext.cs` — own + drive the overlay.
- Modify `src/VoiceToText/Diagnostics/SelfTest.cs` — `RunWidgetTest` (LevelMeter assertions).
- Modify `src/VoiceToText/Program.cs` — `--widgettest` dispatch.

---

## Task 1: Overlay settings (AppSettings.cs)
Add after `AutoStopSilenceSeconds`:
```csharp
    /// <summary>Show the on-screen "listening" indicator while dictating.</summary>
    public bool ShowOverlay { get; set; } = true;
    /// <summary>Remembered overlay top-left position; null = default bottom-center of the primary screen.</summary>
    public int? OverlayX { get; set; }
    public int? OverlayY { get; set; }
```
Build → commit "Add overlay settings (ShowOverlay, OverlayX/Y)".

## Task 2: Mic level signal
`IAudioSource.cs` — add below `SilenceDetected`:
```csharp
    /// <summary>Raised on the capture thread per chunk while recording, with raw RMS (~0..0.3).</summary>
    event Action<float>? LevelChanged;
```
`WasapiAudioSource.cs` — add field `public event Action<float>? LevelChanged;` and replace `OnDataAvailable`:
```csharp
    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        lock (_lock)
        {
            if (_buffer is not null && _buffer.Length < _maxBytes)
                _buffer.Write(e.Buffer, 0, e.BytesRecorded);
        }
        if (_format is null || e.BytesRecorded <= 0) return;

        var rms = ComputeRms(e.Buffer, e.BytesRecorded, _format);
        LevelChanged?.Invoke((float)rms);

        var detector = _silenceDetector;
        if (detector is null || _silenceSignaled) return;
        var chunkSeconds = (double)e.BytesRecorded / _format.AverageBytesPerSecond;
        if (detector.Process(rms, chunkSeconds)) { _silenceSignaled = true; SilenceDetected?.Invoke(); }
    }
```
Build → commit "Expose per-chunk mic level (LevelChanged)".

## Task 3: LevelMeter (pure) + --widgettest (red→green)
Create stub `Overlay/LevelMeter.cs` (returns zeros), add `SelfTest.RunWidgetTest` (assertions: bar count 14; bounds 0..1; silence→max≤0.15; loud(0.25)→center≥0.7; first loud frame center<0.6), dispatch `--widgettest` in `Program.cs`. Build+run → FAIL on "loud -> center bar high". Then implement real `LevelMeter` (Floor 0.006, Ceil 0.13, Smoothing 0.35, MinBar 0.10, center-weighted shape + frame shimmer). Run → PASS. Commit "Add pure LevelMeter + --widgettest".

(Full code for all tasks is in the conversation; see TrayApplicationContext / SettingsForm current contents already read.)

## Task 4: NativeOverlay.cs — UpdateLayeredWindow P/Invoke + SetBitmap helper + WS_EX_* constants. Build → commit.

## Task 5: OverlayState enum + ListeningOverlay.cs — layered no-activate Form, CreateParams ex-styles, ShowWithoutActivation, ~30fps timer, GDI+ pill (shadow + body + border), DrawMic/DrawPencil/DrawBars(from LevelMeter)/DrawDots, fade alpha, WM_NCHITTEST→HTCAPTION drag, WM_EXITSIZEMOVE→PositionChanged. Build → commit.

## Task 6: Wire-in — TrayApplicationContext (field `_overlay`, CreateOverlay/ApplyOverlaySetting, subscribe `_audio.LevelChanged`→SetLevel, drive from SetState, ApplyOverlaySetting in ShowSettings, dispose) + SettingsForm ("Show on-screen indicator" checkbox at y=252, ClientSize 420x330, buttons y=288, load/save). Build → manual smoke (dictate into Notepad: pill appears, bars move, focus stays, pencil+dots on stop, fades out; drag persists; toggle works) → commit.

## Task 7 (optional): Release v0.4.0 to the feed (bump Version, publish.ps1, iscc, copy setup + write latest.json, dogfood auto-update).
