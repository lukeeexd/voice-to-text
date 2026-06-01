namespace VoiceToText.Overlay;

/// <summary>
/// Pure, testable mapping from raw mic RMS to smoothed bar heights (0..1) for the
/// listening widget. No UI, no threading. Call <see cref="Update"/> once per render frame.
/// </summary>
internal sealed class LevelMeter
{
    private const float Floor = 0.006f;    // RMS at/below this reads as silence
    private const float Ceil = 0.13f;      // RMS at/above this reads as full scale
    private const float Smoothing = 0.35f; // fraction toward the new level each frame
    private const float MinBar = 0.10f;    // bars never fully vanish
    private const float Variation = 0.22f; // per-bar shimmer depth

    private readonly int _barCount;
    private readonly float[] _bars;
    private float _smoothed;
    private int _frame;

    public int BarCount => _barCount;

    public LevelMeter(int barCount = 14)
    {
        _barCount = barCount;
        _bars = new float[barCount];
    }

    public float[] Update(float rawRms)
    {
        var norm = Normalize(rawRms);
        _smoothed += (norm - _smoothed) * Smoothing;
        _frame++;

        for (var i = 0; i < _barCount; i++)
        {
            var t = _barCount == 1 ? 0.5f : (float)i / (_barCount - 1);
            var shape = 0.55f + 0.45f * (float)Math.Sin(Math.PI * t);          // center-weighted
            var shimmer = 1f - Variation * (0.5f + 0.5f * (float)Math.Sin(_frame * 0.5 + i * 1.3));
            var h = _smoothed * shape * shimmer;
            _bars[i] = Math.Clamp(MinBar + h * (1f - MinBar), 0f, 1f);
        }
        return _bars;
    }

    public void Reset()
    {
        _smoothed = 0f;
        _frame = 0;
        Array.Clear(_bars);
    }

    private static float Normalize(float rms)
    {
        if (rms <= Floor) return 0f;
        if (rms >= Ceil) return 1f;
        return (rms - Floor) / (Ceil - Floor);
    }
}
