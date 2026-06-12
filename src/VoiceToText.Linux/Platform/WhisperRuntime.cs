using System.Runtime.InteropServices;
using VoiceToText.Diagnostics;
using Whisper.net.LibraryLoader;

namespace VoiceToText.Linux.Platform;

/// <summary>
/// Whisper.net runtime selection. The ggml Vulkan native library ABORTS the whole
/// process at dlopen time when the host has no Vulkan loader (it does not fall back),
/// so probe for libvulkan.so.1 first and pin the CPU runtime when it's absent.
/// ForceCpu (settings) pins CPU for broken-but-present GPU stacks.
/// </summary>
public static class WhisperRuntime
{
    public static void ConfigureForHost(bool forceCpu)
    {
        if (forceCpu)
        {
            Pin("forced to CPU by settings");
            return;
        }
        if (NativeLibrary.TryLoad("libvulkan.so.1", out var handle))
        {
            NativeLibrary.Free(handle);
            return; // loader present: Vulkan-first with CPU fallback (package default)
        }
        Pin("no libvulkan.so.1 on this system — using the CPU runtime");
    }

    private static void Pin(string reason)
    {
        try
        {
            RuntimeOptions.RuntimeLibraryOrder = [RuntimeLibrary.Cpu];
            Log.Info($"Whisper runtime: {reason}.");
        }
        catch (Exception ex)
        {
            Log.Error("Could not pin the CPU runtime", ex);
        }
    }
}
