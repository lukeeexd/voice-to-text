namespace VoiceToText.Audio;

/// <summary>
/// Pure PCM synthesis for the start/stop dictation cues (44.1 kHz mono 16-bit).
/// Deterministic and device-free — playback lives in each platform head.
/// </summary>
public static class CueSynth
{
    public const int SampleRate = 44_100;
    private const double Amplitude = 0.22;
    private const double NoteSeconds = 0.060; // ~60 ms per note
    private const double FadeSeconds = 0.006; // ~6 ms linear fade in/out per note to avoid clicks

    public static readonly double[] StartFreqs = { 660.0, 880.0 }; // rising
    public static readonly double[] StopFreqs = { 880.0, 660.0 };  // falling mirror

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
}
