namespace VoiceToText.App;

/// <summary>Current dictation state, reflected by the tray icon colour.</summary>
internal enum AppState
{
    Idle,
    Recording,
    Transcribing
}
