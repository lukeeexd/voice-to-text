using System.Runtime.InteropServices;
using VoiceToText.Audio;
using VoiceToText.Diagnostics;

namespace VoiceToText.Linux.Platform;

/// <summary>
/// Plays the start/stop cues through a short-lived pulse playback stream on a
/// background thread. A missing or broken audio device must never break dictation:
/// every failure is swallowed into the log.
/// </summary>
public sealed class PulseCuePlayer : ICuePlayer
{
    private readonly byte[] _startPcm = CueSynth.RenderCue(CueSynth.StartFreqs);
    private readonly byte[] _stopPcm = CueSynth.RenderCue(CueSynth.StopFreqs);
    private float _volume = 1f;

    public float Volume
    {
        get => _volume;
        set => _volume = Math.Clamp(value, 0f, 1f);
    }

    public void PlayStart() => Play(_startPcm);
    public void PlayStop() => Play(_stopPcm);

    private void Play(byte[] pcm)
    {
        var vol = _volume;
        new Thread(() =>
        {
            try
            {
                var scaled = vol >= 0.999f ? pcm : Scale(pcm, vol);
                var spec = new PulseNative.pa_sample_spec
                {
                    format = PulseNative.PA_SAMPLE_S16LE,
                    rate = CueSynth.SampleRate,
                    channels = 1,
                };
                var s = PulseNative.pa_simple_new(
                    null, "VoiceToText", PulseNative.PA_STREAM_PLAYBACK, null, "cue",
                    in spec, IntPtr.Zero, IntPtr.Zero, out var err);
                if (s == IntPtr.Zero)
                {
                    Log.Error($"Cue playback unavailable: {PulseNative.ErrorText(err)}");
                    return;
                }
                try
                {
                    var h = GCHandle.Alloc(scaled, GCHandleType.Pinned);
                    try { PulseNative.pa_simple_write(s, h.AddrOfPinnedObject(), (nuint)scaled.Length, out _); }
                    finally { h.Free(); }
                    PulseNative.pa_simple_drain(s, out _);
                }
                finally
                {
                    PulseNative.pa_simple_free(s);
                }
            }
            catch (Exception ex)
            {
                Log.Error("Sound cue playback failed", ex);
            }
        }) { IsBackground = true, Name = "pulse-cue" }.Start();
    }

    private static byte[] Scale(byte[] pcm, float vol)
    {
        var scaled = new byte[pcm.Length];
        for (var i = 0; i < pcm.Length; i += 2)
        {
            var s = (short)(pcm[i] | (pcm[i + 1] << 8));
            s = (short)(s * vol);
            scaled[i] = (byte)(s & 0xFF);
            scaled[i + 1] = (byte)((s >> 8) & 0xFF);
        }
        return scaled;
    }
}
