namespace VoiceToText.Audio;

/// <summary>
/// Captures microphone audio and returns it as 16 kHz mono float samples,
/// the format Whisper expects. Implementations buffer between Start and Stop.
/// </summary>
public interface IAudioSource
{
    bool IsRecording { get; }

    /// <summary>
    /// Raised (on the capture thread) when auto-stop is enabled and a sustained
    /// pause after speech is detected. The host should marshal to the UI thread
    /// and stop the recording.
    /// </summary>
    event Action? SilenceDetected;

    /// <summary>Begin capturing from the given device id (null = system default).</summary>
    /// <param name="autoStop">If true, watch for trailing silence and raise <see cref="SilenceDetected"/>.</param>
    /// <param name="autoStopSilenceSeconds">Seconds of silence after speech before auto-stopping.</param>
    void Start(string? deviceId, bool autoStop, double autoStopSilenceSeconds);

    /// <summary>Stop capturing and return the recorded audio as 16 kHz mono float[].</summary>
    Task<float[]> StopAndGetSamplesAsync();
}
