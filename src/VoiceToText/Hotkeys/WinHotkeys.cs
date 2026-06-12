namespace VoiceToText.Hotkeys;

/// <summary>WinForms-specific halves of HotkeyDefinition (key naming, capture, risk check).</summary>
internal static class WinHotkeys
{
    /// <summary>Wire HotkeyDefinition.Describe() to WinForms key names. Call once at startup.</summary>
    public static void RegisterKeyNames()
        => HotkeyDefinition.KeyNameResolver = vk => ((Keys)vk).ToString();

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
        if ((keyData & Keys.Control) != 0) mods |= HotkeyDefinition.ModControl;
        if ((keyData & Keys.Alt) != 0) mods |= HotkeyDefinition.ModAlt;
        if ((keyData & Keys.Shift) != 0) mods |= HotkeyDefinition.ModShift;

        return new HotkeyDefinition(mods, (uint)key);
    }

    /// <summary>
    /// True if this is a bare (no-modifier) "normal typing" key — binding it globally
    /// would swallow that key everywhere, so the UI should warn before accepting it.
    /// </summary>
    public static bool IsRiskyBareKey(this HotkeyDefinition def)
    {
        if (def.Modifiers != 0)
            return false;
        var key = (Keys)def.VirtualKey;
        return key is (>= Keys.A and <= Keys.Z)
            or (>= Keys.D0 and <= Keys.D9)
            or (>= Keys.NumPad0 and <= Keys.NumPad9)
            or Keys.Space or Keys.Back or Keys.Oemcomma or Keys.OemPeriod
            or Keys.OemQuestion or Keys.OemSemicolon or Keys.Oem1 or Keys.Oemplus or Keys.OemMinus;
    }
}
