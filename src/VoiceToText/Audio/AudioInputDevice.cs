namespace VoiceToText.Audio;

/// <summary>A selectable microphone / capture endpoint.</summary>
public sealed record AudioInputDevice(string Id, string Name)
{
    public override string ToString() => Name;
}
