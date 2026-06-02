namespace VoiceToText.Diagnostics;

/// <summary>
/// Reads Whisper.net's loaded native runtime (Vulkan vs Cpu) via reflection — the enum
/// member names are not part of our compile surface. Returns "Unknown" before a model
/// has loaded or if the internals move.
/// </summary>
public static class RuntimeProbe
{
    public static string LoadedRuntime()
    {
        try
        {
            var type = Type.GetType("Whisper.net.LibraryLoader.RuntimeOptions, Whisper.net");
            var value = type?.GetProperty("LoadedLibrary")?.GetValue(null);
            return value?.ToString() ?? "Unknown";
        }
        catch
        {
            return "Unknown";
        }
    }
}
