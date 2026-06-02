using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace VoiceToText.Audio;

/// <summary>
/// WASAPI shared-mode capture. Buffers raw audio in the device's native format
/// while recording, then on stop downmixes to mono and resamples to 16 kHz float[]
/// (the format Whisper requires) using NAudio's managed WDL resampler.
///
/// Each incoming chunk's RMS level is published via <see cref="LevelChanged"/> (for
/// the level meter) and, when auto-stop is enabled, fed to a <see cref="SilenceDetector"/>
/// to raise <see cref="SilenceDetected"/> after a sustained pause.
/// </summary>
public sealed class WasapiAudioSource : IAudioSource
{
    private const int TargetSampleRate = 16_000;
    // Safety cap so a forgotten "recording" session can't grow unbounded in RAM.
    private static readonly TimeSpan MaxDuration = TimeSpan.FromMinutes(5);

    private readonly object _lock = new();
    private WasapiCapture? _capture;
    private MemoryStream? _buffer;
    private WaveFormat? _format;
    private TaskCompletionSource<bool>? _stopped;
    private long _maxBytes;

    private SilenceDetector? _silenceDetector;
    private bool _silenceSignaled;
    private volatile bool _captureStopped; // set once RecordingStopped has fired (device error or normal stop)

    public bool IsRecording => _capture is not null;

    public event Action? SilenceDetected;
    public event Action<float>? LevelChanged;
    public event Action<Exception>? RecordingFailed;

    public void Start(string? deviceId, bool autoStop, double autoStopSilenceSeconds)
    {
        if (IsRecording)
            throw new InvalidOperationException("Already recording.");

        var enumerator = new MMDeviceEnumerator();
        MMDevice device = deviceId is not null
            ? enumerator.GetDevice(deviceId)
            : enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Communications);

        var capture = new WasapiCapture(device);
        _format = capture.WaveFormat;
        _maxBytes = (long)(_format.AverageBytesPerSecond * MaxDuration.TotalSeconds);
        _buffer = new MemoryStream();
        _stopped = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _silenceDetector = autoStop ? new SilenceDetector(autoStopSilenceSeconds) : null;
        _silenceSignaled = false;
        _captureStopped = false;

        capture.DataAvailable += OnDataAvailable;
        capture.RecordingStopped += OnRecordingStopped;
        _capture = capture;
        capture.StartRecording();
    }

    public async Task<float[]> StopAndGetSamplesAsync()
    {
        var capture = _capture;
        if (capture is null)
            return [];

        if (!_captureStopped) capture.StopRecording(); // a device-loss stop already fired; avoid a second StopRecording
        await (_stopped?.Task ?? Task.CompletedTask).ConfigureAwait(false);

        capture.DataAvailable -= OnDataAvailable;
        capture.RecordingStopped -= OnRecordingStopped;

        byte[] raw;
        WaveFormat format;
        lock (_lock)
        {
            raw = _buffer?.ToArray() ?? [];
            format = _format!;
        }

        capture.Dispose();
        _capture = null;
        _buffer?.Dispose();
        _buffer = null;
        _silenceDetector = null;

        return Resample(raw, format);
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        lock (_lock)
        {
            if (_buffer is not null && _buffer.Length < _maxBytes)
                _buffer.Write(e.Buffer, 0, e.BytesRecorded);
        }

        if (_format is null || e.BytesRecorded <= 0)
            return;

        var rms = ComputeRms(e.Buffer, e.BytesRecorded, _format);
        LevelChanged?.Invoke((float)rms);

        var detector = _silenceDetector;
        if (detector is null || _silenceSignaled)
            return;

        var chunkSeconds = (double)e.BytesRecorded / _format.AverageBytesPerSecond;
        if (detector.Process(rms, chunkSeconds))
        {
            _silenceSignaled = true;
            SilenceDetected?.Invoke();
        }
    }

    private void OnRecordingStopped(object? sender, StoppedEventArgs e)
    {
        _captureStopped = true;
        // Notify the device-loss consumer while the capture is still live, THEN release any
        // StopAndGetSamplesAsync awaiter (whose continuation disposes the capture).
        if (e.Exception is not null)
            RecordingFailed?.Invoke(e.Exception);
        _stopped?.TrySetResult(true);
    }

    /// <summary>RMS level (0..~1) of a raw capture buffer. Handles 32-bit float and 16-bit PCM.</summary>
    private static double ComputeRms(byte[] buffer, int bytes, WaveFormat format)
    {
        switch (format.BitsPerSample)
        {
            case 32:
            {
                var count = bytes / 4;
                if (count == 0) return 0;
                double sum = 0;
                for (var i = 0; i < count; i++)
                {
                    var s = BitConverter.ToSingle(buffer, i * 4);
                    sum += s * s;
                }
                return Math.Sqrt(sum / count);
            }
            case 16:
            {
                var count = bytes / 2;
                if (count == 0) return 0;
                double sum = 0;
                for (var i = 0; i < count; i++)
                {
                    double s = BitConverter.ToInt16(buffer, i * 2) / 32768.0;
                    sum += s * s;
                }
                return Math.Sqrt(sum / count);
            }
            default:
                // Unknown sample format — report "loud" so we never auto-stop wrongly.
                return double.MaxValue;
        }
    }

    private static float[] Resample(byte[] raw, WaveFormat sourceFormat)
    {
        if (raw.Length == 0)
            return [];

        using var ms = new MemoryStream(raw);
        var rawStream = new RawSourceWaveStream(ms, sourceFormat);

        ISampleProvider sampleProvider = rawStream.ToSampleProvider();
        if (sourceFormat.Channels > 1)
            sampleProvider = sampleProvider.ToMono();

        var resampler = new WdlResamplingSampleProvider(sampleProvider, TargetSampleRate);

        var samples = new List<float>(TargetSampleRate * 8);
        var chunk = new float[TargetSampleRate]; // 1 second per read
        int read;
        while ((read = resampler.Read(chunk, 0, chunk.Length)) > 0)
        {
            for (var i = 0; i < read; i++)
                samples.Add(chunk[i]);
        }
        return samples.ToArray();
    }
}
