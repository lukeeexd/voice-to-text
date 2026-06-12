using NAudio.Wave;
using NAudio.Wave.SampleProviders;
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
    private static readonly WaveFormat Format = new(CueSynth.SampleRate, 16, 1); // 44.1 kHz mono 16-bit PCM

    private readonly byte[] _startPcm;
    private readonly byte[] _stopPcm;

    // Players currently in flight; kept alive here so they aren't GC'd mid-play.
    private readonly List<IWavePlayer> _active = new();
    private readonly object _lock = new();
    private bool _disposed;
    private float _volume = 1f;

    public SoundCues()
    {
        _startPcm = CueSynth.RenderCue(CueSynth.StartFreqs);
        _stopPcm = CueSynth.RenderCue(CueSynth.StopFreqs);
    }

    /// <summary>
    /// Output level, 0..1 (clamped). 1 = the buffers' native loudness (today's sound exactly);
    /// scales down from there via a <see cref="VolumeSampleProvider"/> — no system-volume side
    /// effects. Read/written under the play lock.
    /// </summary>
    public float Volume
    {
        get { lock (_lock) return _volume; }
        set { lock (_lock) _volume = Math.Clamp(value, 0f, 1f); }
    }

    /// <summary>Play the rising start cue. Never throws.</summary>
    public void PlayStart() => Play(_startPcm);

    /// <summary>Play the falling stop cue. Never throws.</summary>
    public void PlayStop() => Play(_stopPcm);

    private void Play(byte[] pcm)
    {
        try
        {
            IWavePlayer player;
            lock (_lock)
            {
                if (_disposed) return;
                if (_active.Count >= 4) return; // bound concurrent players if the hotkey is spammed (cues are ~120 ms)
                var stream = new RawSourceWaveStream(new MemoryStream(pcm), Format);
                // Scale the output device-independently; Volume = 1 reproduces the buffers verbatim.
                ISampleProvider src = new VolumeSampleProvider(stream.ToSampleProvider()) { Volume = _volume };
                player = new WaveOutEvent();
                player.Init(src);
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
