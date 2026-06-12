namespace VoiceToText.Audio;

/// <summary>
/// Detects when speech has been followed by a sustained pause, so recording can
/// auto-stop. Energy-based: it calibrates to the room's background noise for a
/// short window, then treats audio above a margin over that floor as speech.
/// Once speech has occurred, it accumulates trailing silence and fires once that
/// silence reaches the configured duration.
///
/// Pure (no audio device) so it can be unit-tested with synthetic levels.
/// </summary>
public sealed class SilenceDetector
{
    private const double CalibrationSeconds = 0.25;
    private const double SpeechMargin = 3.0;   // speech must exceed noiseFloor * this
    private const double MinThreshold = 0.006;  // ~ -44 dBFS RMS
    private const double MaxThreshold = 0.05;

    private readonly double _silenceSeconds;

    private double _calibratedSeconds;
    private double _calibrationSum;
    private int _calibrationCount;
    private double _noiseFloor;
    private bool _hasSpoken;
    private double _silenceAccum;
    private bool _triggered;

    public SilenceDetector(double silenceSeconds) => _silenceSeconds = silenceSeconds;

    /// <summary>The speech threshold derived from the calibrated noise floor.</summary>
    public double Threshold => Math.Clamp(_noiseFloor * SpeechMargin, MinThreshold, MaxThreshold);

    /// <summary>
    /// Feed one audio chunk's RMS level and its duration in seconds. Returns true
    /// exactly once — when trailing silence after speech reaches the configured
    /// duration. Returns false on every other call.
    /// </summary>
    public bool Process(double rms, double chunkSeconds)
    {
        if (_triggered)
            return false;

        // Calibrate the noise floor from the first chunks (assumed near-silent).
        if (_calibratedSeconds < CalibrationSeconds)
        {
            _calibrationSum += rms;
            _calibrationCount++;
            _calibratedSeconds += chunkSeconds;
            _noiseFloor = _calibrationSum / _calibrationCount;
            return false;
        }

        if (rms >= Threshold)
        {
            _hasSpoken = true;
            _silenceAccum = 0;
            return false;
        }

        if (!_hasSpoken)
            return false; // never auto-stop before any speech is heard

        _silenceAccum += chunkSeconds;
        if (_silenceAccum >= _silenceSeconds)
        {
            _triggered = true;
            return true;
        }
        return false;
    }
}
