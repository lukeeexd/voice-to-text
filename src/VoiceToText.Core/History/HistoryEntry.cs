namespace VoiceToText.History;

/// <summary>One recorded dictation kept in the opt-in history (serializable).</summary>
public sealed class HistoryEntry
{
    /// <summary>When the dictation was recorded (local time).</summary>
    public DateTime Time { get; set; }

    /// <summary>Foreground app name at dictation time; empty if unknown.</summary>
    public string App { get; set; } = "";

    /// <summary>The final, rule-processed transcription text.</summary>
    public string Text { get; set; } = "";

    /// <summary>Word count of <see cref="Text"/>.</summary>
    public int Words { get; set; }

    /// <summary>Seconds the transcription took; null for entries recorded before v0.8.6.</summary>
    public double? TranscribeSeconds { get; set; }

    /// <summary>Canonical engine model name (e.g. "LargeV3Turbo"); null for entries recorded before v0.8.7.</summary>
    public string? Model { get; set; }
}
