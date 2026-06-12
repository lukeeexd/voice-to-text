namespace VoiceToText.Stt;

/// <summary>
/// Speech-to-text engine. Kept behind an interface so alternative backends
/// (faster-whisper, Parakeet, Voxtral, ...) can be added later without touching
/// the rest of the app.
/// </summary>
public interface ISttEngine : IDisposable
{
    /// <summary>Load the model into memory (idempotent). May be slow on first call.</summary>
    Task LoadAsync(CancellationToken cancellationToken = default);

    /// <summary>Transcribe 16 kHz mono float samples to text.</summary>
    Task<string> TranscribeAsync(float[] samples, CancellationToken cancellationToken = default);
}
