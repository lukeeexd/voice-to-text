namespace VoiceToText.Audio;

/// <summary>Plays the start/stop dictation cues. Implementations must never throw
/// into the dictation path and must honor a 0..1 volume.</summary>
public interface ICuePlayer
{
    float Volume { get; set; }
    void PlayStart();
    void PlayStop();
}
