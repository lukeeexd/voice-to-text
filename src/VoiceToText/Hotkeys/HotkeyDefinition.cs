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

    private static string KeyName(uint vk) => vk switch
    {
        VkSpace => "Space",
        _ => ((Keys)vk).ToString(),
    };

    /// <summary>
    /// Build a definition from a WinForms key event (used by the settings capture box).
    /// Returns null while only a modifier key is held on its own. A single key with
    /// no modifier is allowed — useful for dedicated/extra keyboard buttons (F13–F24,
    /// media keys, etc.).
    /// </summary>
    public static HotkeyDefinition? FromKeyEvent(Keys keyData)
    {
        var key = keyData & Keys.KeyCode;
        if (key is Keys.None or Keys.ControlKey or Keys.ShiftKey or Keys.Menu or Keys.LWin or Keys.RWin)
            return null;

        uint mods = 0;
        if ((keyData & Keys.Control) != 0) mods |= ModControl;
        if ((keyData & Keys.Alt) != 0) mods |= ModAlt;
        if ((keyData & Keys.Shift) != 0) mods |= ModShift;

        return new HotkeyDefinition(mods, (uint)key);
    }

    /// <summary>
    /// True if this is a bare (no-modifier) "normal typing" key — binding it globally
    /// would swallow that key everywhere, so the UI should warn before accepting it.
    /// </summary>
    public bool IsRiskyBareKey()
    {
        if (Modifiers != 0)
            return false;
        var key = (Keys)VirtualKey;
        return key is (>= Keys.A and <= Keys.Z)
            or (>= Keys.D0 and <= Keys.D9)
            or (>= Keys.NumPad0 and <= Keys.NumPad9)
            or Keys.Space or Keys.Back or Keys.Oemcomma or Keys.OemPeriod
            or Keys.OemQuestion or Keys.OemSemicolon or Keys.Oem1 or Keys.Oemplus or Keys.OemMinus;
    }
}
