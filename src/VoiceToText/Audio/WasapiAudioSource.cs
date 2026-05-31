using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace VoiceToText.Audio;

/// <summary>
/// WASAPI shared-mode capture. Buffers raw audio in the device's native format
/// while recording, then on stop downmixes to mono and resamples to 16 kHz float[]
/// (the format Whisper requires) using NAudio's managed WDL resampler.
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

    public bool IsRecording => _capture is not null;

    public void Start(string? deviceId)
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

        capture.StopRecording();
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

        return Resample(raw, format);
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        lock (_lock)
        {
            if (_buffer is null) return;
            if (_buffer.Length >= _maxBytes) return; // hit safety cap; drop further audio
            _buffer.Write(e.Buffer, 0, e.BytesRecorded);
        }
    }

    private void OnRecordingStopped(object? sender, StoppedEventArgs e)
        => _stopped?.TrySetResult(true);

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
