using VoiceToText.Audio;
using VoiceToText.Diagnostics;
using VoiceToText.History;
using VoiceToText.Injection;
using VoiceToText.Settings;
using VoiceToText.Stats;
using VoiceToText.Stt;
using VoiceToText.TextProcessing;

namespace VoiceToText.App;

public enum DictationState { Idle, Recording, Transcribing }

/// <summary>
/// Portable dictation orchestrator: toggle → record (auto-stop on silence) →
/// transcribe → text rules → inject → stats/history. Mirrors the Windows head's
/// proven flow (TrayApplicationContext.StopAndTranscribeAsync) without UI concerns;
/// the Windows head keeps its own path, new heads build on this. Errors never
/// escape — they surface via <see cref="StatusChanged"/> and the log.
/// </summary>
public sealed class DictationController(
    IAudioSource audio,
    ISttEngine stt,
    ITextInjector injector,
    ICuePlayer? cues,
    AppSettings settings,
    StatsService stats,
    HistoryService history)
{
    private readonly object _gate = new(); // serializes the Idle→Recording transition
    private int _busy; // 1 while transcribing (toggle ignored), else 0

    public DictationState State { get; private set; } = DictationState.Idle;

    /// <summary>UI hook: state changes + user-facing status text ("Recording", errors).</summary>
    public event Action<DictationState, string>? StatusChanged;

    /// <summary>Raised with the final injected text (after rules).</summary>
    public event Action<string>? Transcribed;

    /// <summary>Name recorded in per-app stats. Heads that can resolve the focused
    /// app set this before each dictation; defaults to "Unknown".</summary>
    public string AppNameProvider { get; set; } = "Unknown";

    public async Task ToggleAsync()
    {
        if (Interlocked.CompareExchange(ref _busy, 0, 0) == 1)
            return; // mid-transcription: ignore, exactly like the Windows head

        // The hotkey thread, the IPC thread pool, and the tray UI can all toggle
        // concurrently; the gate makes Idle→Recording atomic so a double-tap can
        // never double-start the audio source.
        var started = false;
        lock (_gate)
        {
            if (State == DictationState.Idle)
            {
                StartLocked();
                started = true;
            }
        }
        if (!started)
            await StopAndTranscribeAsync().ConfigureAwait(false);
    }

    private void StartLocked()
    {
        try
        {
            audio.SilenceDetected += OnSilence;
            audio.RecordingFailed += OnRecordingFailed;
            audio.Start(settings.InputDeviceId, settings.AutoStopEnabled, settings.AutoStopSilenceSeconds);
            State = DictationState.Recording;
            if (settings.SoundCuesEnabled && cues is not null)
            {
                cues.Volume = (float)settings.SoundCuesVolume;
                cues.PlayStart();
            }
            StatusChanged?.Invoke(State, "Recording");
        }
        catch (Exception ex)
        {
            audio.SilenceDetected -= OnSilence;
            audio.RecordingFailed -= OnRecordingFailed;
            State = DictationState.Idle;
            Log.Error("Could not start recording", ex);
            StatusChanged?.Invoke(State, $"Could not start recording: {ex.Message}");
        }
    }

    private void OnSilence() => _ = StopAndTranscribeAsync();

    private void OnRecordingFailed(Exception ex)
    {
        // Device lost mid-dictation (mic unplugged, audio server restart): salvage
        // what was captured and surface the error instead of recording into the void.
        Log.Error("Recording failed mid-dictation", ex);
        StatusChanged?.Invoke(State, $"Recording failed: {ex.Message}");
        _ = StopAndTranscribeAsync();
    }

    private async Task StopAndTranscribeAsync()
    {
        if (Interlocked.Exchange(ref _busy, 1) == 1)
            return;
        try
        {
            audio.SilenceDetected -= OnSilence;
            audio.RecordingFailed -= OnRecordingFailed;
            State = DictationState.Transcribing;
            if (settings.SoundCuesEnabled && cues is not null)
            {
                cues.Volume = (float)settings.SoundCuesVolume;
                cues.PlayStop();
            }
            StatusChanged?.Invoke(State, "Transcribing");

            var samples = await audio.StopAndGetSamplesAsync().ConfigureAwait(false);
            var seconds = samples.Length / 16_000.0;

            var sw = System.Diagnostics.Stopwatch.StartNew();
            var text = await stt.TranscribeAsync(samples).ConfigureAwait(false);
            var transcribeSeconds = sw.Elapsed.TotalSeconds;

            text = TextRules.Apply(text, settings.Replacements, settings.SpokenCommandsEnabled);

            if (!string.IsNullOrWhiteSpace(text))
            {
                injector.Inject(text);
                var words = StatsData.CountWords(text);
                var app = AppNameProvider;
                stats.Record(words, seconds, app);
                if (settings.HistoryEnabled)
                    history.Record(text, words, app, transcribeSeconds, settings.ModelType.ToString());
                Transcribed?.Invoke(text);
            }

            State = DictationState.Idle;
            StatusChanged?.Invoke(State, "Done");
        }
        catch (Exception ex)
        {
            State = DictationState.Idle;
            Log.Error("Dictation failed", ex);
            StatusChanged?.Invoke(State, $"Dictation failed: {ex.Message}");
        }
        finally
        {
            Interlocked.Exchange(ref _busy, 0);
        }
    }
}
