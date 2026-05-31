namespace VoiceToText.Injection;

/// <summary>Inserts transcribed text into whatever window currently has focus.</summary>
public interface ITextInjector
{
    /// <summary>Must be called on the UI/STA thread (clipboard access requires STA).</summary>
    void Inject(string text);
}
