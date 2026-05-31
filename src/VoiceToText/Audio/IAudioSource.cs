namespace VoiceToText.Audio;

/// <summary>
/// Captures microphone audio and returns it as 16 kHz mono float samples,
/// the format Whisper expects. Implementations buffer between Start and Stop.
/// </summary>
public interface IAudioSource
{
    bool IsRecording { get; }

    /// <summary>Begin capturing from the given device id (null = system default).</summary>
    void Start(string? deviceId);

    /// <summary>Stop capturing and return the recorded audio as 16 kHz mono float[].</summary>
    Task<float[]> StopAndGetSamplesAsync();
}
