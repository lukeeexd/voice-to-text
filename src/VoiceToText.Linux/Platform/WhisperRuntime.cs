using VoiceToText.Diagnostics;
using Whisper.net.LibraryLoader;

namespace VoiceToText.Linux.Platform;

/// <summary>Whisper.net runtime selection. By default Vulkan is tried first with CPU
/// fallback (package order); ForceCpu pins the CPU runtime for broken GPU drivers.</summary>
public static class WhisperRuntime
{
    public static void ForceCpu()
    {
        try
        {
            RuntimeOptions.RuntimeLibraryOrder = [RuntimeLibrary.Cpu];
            Log.Info("Whisper runtime forced to CPU by settings.");
        }
        catch (Exception ex)
        {
            Log.Error("Could not force the CPU runtime", ex);
        }
    }
}
