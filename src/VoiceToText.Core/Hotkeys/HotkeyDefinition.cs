namespace VoiceToText.Hotkeys;

/// <summary>
/// A global hotkey combination: Win32 modifier flags + a virtual-key code.
/// </summary>
public sealed record HotkeyDefinition(uint Modifiers, uint VirtualKey)
{
    public const uint ModAlt = 0x0001;
    public const uint ModControl = 0x0002;
    public const uint ModShift = 0x0004;
    public const uint ModWin = 0x0008;
    public const uint ModNoRepeat = 0x4000;

    private const uint VkSpace = 0x20;

    /// <summary>Default: Ctrl + Shift + Space (toggle dictation).</summary>
    public static HotkeyDefinition Default { get; } = new(ModControl | ModShift, VkSpace);

    public string Describe()
    {
        var parts = new List<string>();
        if ((Modifiers & ModControl) != 0) parts.Add("Ctrl");
        if ((Modifiers & ModAlt) != 0) parts.Add("Alt");
        if ((Modifiers & ModShift) != 0) parts.Add("Shift");
        if ((Modifiers & ModWin) != 0) parts.Add("Win");
        parts.Add(KeyName(VirtualKey));
        return string.Join(" + ", parts);
    }

    /// <summary>
    /// Platform hook for naming a virtual-key code (e.g. WinForms Keys on Windows).
    /// Set once at startup by each head; the fallback is a raw hex name.
    /// </summary>
    public static Func<uint, string>? KeyNameResolver { get; set; }

    private static string KeyName(uint vk) => vk switch
    {
        VkSpace => "Space",
        _ => KeyNameResolver?.Invoke(vk) ?? $"Key 0x{vk:X2}",
    };
}
