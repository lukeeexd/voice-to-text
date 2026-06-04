using NAudio.Wave;
using VoiceToText.Diagnostics;

namespace VoiceToText.Audio;

/// <summary>
/// Short synthesized audio cues for dictation start/stop, so the user knows the recording
/// state without watching the on-screen widget. No audio files: two PCM buffers are rendered
/// once at construction (a rising chirp for start, its falling mirror for stop) and played
/// non-blocking on the default output device.
///
/// Playback must NEVER throw into the dictation/hotkey path — every play is wrapped in
/// try/catch + <see cref="Log"/>, so a missing or locked output device can never break
/// dictation. Active players are tracked so they aren't GC'd mid-play and are disposed on
/// <see cref="IWavePlayer.PlaybackStopped"/>.
/// </summary>
internal sealed class SoundCues : IDisposable
{
    private const int SampleRate = 44_100;
    private const double Amplitude = 0.22;
    private const double NoteSeconds = 0.060; // ~60 ms per note
    private const double FadeSeconds = 0.006; // ~6 ms linear fade in/out per note to avoid clicks

    private static readonly WaveFormat Format = new(SampleRate, 16, 1); // 44.1 kHz mono 16-bit PCM

    private readonly byte[] _startPcm;
    private readonly byte[] _stopPcm;

    // Players currently in flight; kept alive here so they aren't GC'd mid-play.
    private readonly List<IWavePlayer> _active = new();
    private readonly object _lock = new();
    private bool _disposed;

    public SoundCues()
    {
        _startPcm = RenderCue(new[] { 660.0, 880.0 }); // rising
        _stopPcm = RenderCue(new[] { 880.0, 660.0 });  // falling mirror
    }

    /// <summary>Play the rising start cue. Never throws.</summary>
    public void PlayStart() => Play(_startPcm);

    /// <summary>Play the falling stop cue. Never throws.</summary>
    public void PlayStop() => Play(_stopPcm);

    /// <summary>
    /// Render a sequence of notes into a 44.1 kHz mono 16-bit PCM buffer. Pure and deterministic
    /// (no audio devices) — this is the unit-tested part. Each frequency is one note of
    /// <see cref="NoteSeconds"/> with a short linear fade in/out to avoid clicks.
    /// </summary>
    public static byte[] RenderCue(double[] freqs)
    {
        int samplesPerNote = (int)(SampleRate * NoteSeconds);
        int fadeSamples = Math.Max(1, (int)(SampleRate * FadeSeconds));
        int total = samplesPerNote * freqs.Length;
        var pcm = new byte[total * 2]; // 16-bit => 2 bytes/sample

        int pos = 0;
        foreach (var freq in freqs)
        {
            for (var i = 0; i < samplesPerNote; i++)
            {
                double t = (double)i / SampleRate;
                double sample = Math.Sin(2.0 * Math.PI * freq * t) * Amplitude;

                // Linear fade in at the note start and out at the note end.
                double env = 1.0;
                if (i < fadeSamples) env = (double)i / fadeSamples;
                else if (i >= samplesPerNote - fadeSamples) env = (double)(samplesPerNote - i) / fadeSamples;
                sample *= env;

                short s = (short)(sample * short.MaxValue);
                pcm[pos++] = (byte)(s & 0xFF);
                pcm[pos++] = (byte)((s >> 8) & 0xFF);
            }
        }

        return pcm;
    }

    private void Play(byte[] pcm)
    {
        try
        {
            IWavePlayer player;
            lock (_lock)
            {
                if (_disposed) return;
                var stream = new RawSourceWaveStream(new MemoryStream(pcm), Format);
                player = new WaveOutEvent();
                player.Init(stream);
                player.PlaybackStopped += (_, _) =>
                {
                    lock (_lock) _active.Remove(player);
                    try { player.Dispose(); } catch { /* best effort */ }
                    try { stream.Dispose(); } catch { /* best effort */ }
                };
                _active.Add(player);
            }
            player.Play(); // non-blocking
        }
        catch (Exception ex)
        {
            // A missing/locked output device must never break dictation.
            Log.Error("Sound cue playback failed", ex);
        }
    }

    public void Dispose()
    {
        IWavePlayer[] players;
        lock (_lock)
        {
            if (_disposed) return;
            _disposed = true;
            players = _active.ToArray();
            _active.Clear();
        }

        foreach (var player in players)
        {
            try { player.Stop(); } catch { /* best effort */ }
            try { player.Dispose(); } catch { /* best effort */ }
        }
    }
}
