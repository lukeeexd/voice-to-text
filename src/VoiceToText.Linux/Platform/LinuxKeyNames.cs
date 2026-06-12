namespace VoiceToText.Linux.Platform;

/// <summary>
/// Names the Win32 virtual-key codes stored in settings (the hotkey format is shared
/// across OSes). The X11 keysym mapping arrives with the hotkey backends in phase 2b.
/// </summary>
internal static class LinuxKeyNames
{
    public static string Resolve(uint vk) => vk switch
    {
        >= 0x41 and <= 0x5A => ((char)vk).ToString(),          // A–Z
        >= 0x30 and <= 0x39 => ((char)vk).ToString(),          // 0–9
        >= 0x70 and <= 0x87 => $"F{vk - 0x6F}",                // F1–F24
        0x20 => "Space",
        0x0D => "Enter",
        0x09 => "Tab",
        0x1B => "Esc",
        0x08 => "Backspace",
        0x2E => "Delete",
        0x24 => "Home",
        0x23 => "End",
        _ => $"Key 0x{vk:X2}",
    };
}
