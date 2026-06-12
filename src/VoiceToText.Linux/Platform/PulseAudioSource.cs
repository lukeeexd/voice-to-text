using System.Runtime.InteropServices;
using VoiceToText.Audio;

namespace VoiceToText.Linux.Platform;

/// <summary>
/// PulseAudio capture over the blocking simple API on a dedicated thread. Requests
/// 16 kHz mono float32 directly (the daemon resamples), so no resampler is needed.
/// Mirrors WasapiAudioSource semantics: 5-minute cap, RMS → LevelChanged,
/// SilenceDetector → SilenceDetected once, RecordingFailed on read errors.
/// </summary>
public sealed class PulseAudioSource : IAudioSource
{
    private const int SampleRate = 16_000;
    private const int ChunkSamples = 1_600; // 100 ms reads
    private const int MaxSamples = SampleRate * 60 * 5; // 5-minute safety cap

    private readonly object _lock = new();
    private List<float>? _samples;
    private Thread? _thread;
    private volatile bool _stopRequested;
    private TaskCompletionSource<bool>? _stopped;
    private SilenceDetector? _silenceDetector;
    private bool _silenceSignaled;

    public bool IsRecording { get; private set; }

    public event Action? SilenceDetected;
    public event Action<float>? LevelChanged;
    public event Action<Exception>? RecordingFailed;

    public void Start(string? deviceId, bool autoStop, double autoStopSilenceSeconds)
    {
        if (IsRecording)
            throw new InvalidOperationException("Already recording.");

        var spec = new PulseNative.pa_sample_spec
        {
            format = PulseNative.PA_SAMPLE_FLOAT32LE,
            rate = SampleRate,
            channels = 1,
        };
        var stream = PulseNative.pa_simple_new(
            null, "VoiceToText", PulseNative.PA_STREAM_RECORD, deviceId, "dictation",
            in spec, IntPtr.Zero, IntPtr.Zero, out var err);
        if (stream == IntPtr.Zero)
            throw new InvalidOperationException($"PulseAudio: {PulseNative.ErrorText(err)}");

        _samples = new List<float>(SampleRate * 30);
        _silenceDetector = autoStop ? new SilenceDetector(autoStopSilenceSeconds) : null;
        _silenceSignaled = false;
        _stopRequested = false;
        _stopped = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        IsRecording = true;

        _thread = new Thread(() => ReadLoop(stream)) { IsBackground = true, Name = "pulse-capture" };
        _thread.Start();
    }

    private void ReadLoop(IntPtr stream)
    {
        var buf = new float[ChunkSamples];
        var handle = GCHandle.Alloc(buf, GCHandleType.Pinned);
        try
        {
            while (!_stopRequested)
            {
                if (PulseNative.pa_simple_read(stream, handle.AddrOfPinnedObject(),
                        (nuint)(ChunkSamples * sizeof(float)), out var err) < 0)
                {
                    if (!_stopRequested)
                        RecordingFailed?.Invoke(new IOException($"PulseAudio read: {PulseNative.ErrorText(err)}"));
                    break;
                }

                lock (_lock)
                {
                    if (_samples is not null && _samples.Count < MaxSamples)
                        _samples.AddRange(buf);
                }

                double sum = 0;
                for (var i = 0; i < buf.Length; i++) sum += buf[i] * buf[i];
                var rms = Math.Sqrt(sum / buf.Length);
                LevelChanged?.Invoke((float)rms);

                var detector = _silenceDetector;
                if (detector is not null && !_silenceSignaled
                    && detector.Process(rms, (double)ChunkSamples / SampleRate))
                {
                    _silenceSignaled = true;
                    SilenceDetected?.Invoke();
                }
            }
        }
        finally
        {
            handle.Free();
            PulseNative.pa_simple_free(stream);
            _stopped?.TrySetResult(true);
        }
    }

    public async Task<float[]> StopAndGetSamplesAsync()
    {
        if (!IsRecording)
            return [];
        _stopRequested = true;
        await (_stopped?.Task ?? Task.CompletedTask).ConfigureAwait(false);

        float[] result;
        lock (_lock)
        {
            result = _samples?.ToArray() ?? [];
            _samples = null;
        }
        _silenceDetector = null;
        _thread = null;
        IsRecording = false;
        return result;
    }
}
