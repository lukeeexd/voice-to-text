namespace VoiceToText.TextProcessing;

/// <summary>One find→replace rule, applied to transcribed text before pasting.
/// Mutable for JSON (de)serialization and the editor grid.</summary>
public sealed class ReplacementRule
{
    public string Find { get; set; } = "";
    public string Replace { get; set; } = "";
}
