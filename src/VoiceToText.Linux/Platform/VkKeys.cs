using VoiceToText.Hotkeys;

namespace VoiceToText.Linux.Platform;

/// <summary>
/// Maps the Win32 virtual-key codes stored in settings to X11 keysyms, and to GNOME
/// accelerator names. Covers the keys the app supports as hotkeys: A–Z, 0–9, F1–F24,
/// Space and a few navigation keys.
/// </summary>
internal static class VkKeys
{
    public static nuint? ToKeysym(uint vk) => vk switch
    {
        >= 0x41 and <= 0x5A => 0x61 + (vk - 0x41),      // XK_a..XK_z (lowercase)
        >= 0x30 and <= 0x39 => vk,                       // XK_0..XK_9 (same codes)
        >= 0x70 and <= 0x87 => 0xFFBE + (vk - 0x70),    // XK_F1..XK_F24
        0x20 => 0x0020,                                  // XK_space
        0x0D => 0xFF0D,                                  // XK_Return
        0x09 => 0xFF09,                                  // XK_Tab
        0x08 => 0xFF08,                                  // XK_BackSpace
        0x2E => 0xFFFF,                                  // XK_Delete
        0x24 => 0xFF50,                                  // XK_Home
        0x23 => 0xFF57,                                  // XK_End
        _ => null,
    };

    /// <summary>GNOME accelerator string, e.g. "&lt;Control&gt;&lt;Shift&gt;space".</summary>
    public static string? ToGnomeBinding(HotkeyDefinition hotkey)
    {
        var key = hotkey.VirtualKey switch
        {
            >= 0x41 and <= 0x5A => ((char)(hotkey.VirtualKey + 32)).ToString(), // a-z
            >= 0x30 and <= 0x39 => ((char)hotkey.VirtualKey).ToString(),
            >= 0x70 and <= 0x87 => $"F{hotkey.VirtualKey - 0x6F}",
            0x20 => "space",
            0x0D => "Return",
            0x09 => "Tab",
            _ => null,
        };
        if (key is null) return null;

        var mods = "";
        if ((hotkey.Modifiers & HotkeyDefinition.ModControl) != 0) mods += "<Control>";
        if ((hotkey.Modifiers & HotkeyDefinition.ModShift) != 0) mods += "<Shift>";
        if ((hotkey.Modifiers & HotkeyDefinition.ModAlt) != 0) mods += "<Alt>";
        if ((hotkey.Modifiers & HotkeyDefinition.ModWin) != 0) mods += "<Super>";
        return mods + key;
    }

    public static uint ToX11Modifiers(uint vkModifiers)
    {
        uint m = 0;
        if ((vkModifiers & HotkeyDefinition.ModControl) != 0) m |= X11Native.ControlMask;
        if ((vkModifiers & HotkeyDefinition.ModShift) != 0) m |= X11Native.ShiftMask;
        if ((vkModifiers & HotkeyDefinition.ModAlt) != 0) m |= X11Native.Mod1Mask;
        if ((vkModifiers & HotkeyDefinition.ModWin) != 0) m |= X11Native.Mod4Mask;
        return m;
    }
}
