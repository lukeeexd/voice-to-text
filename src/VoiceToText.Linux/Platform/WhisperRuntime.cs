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
    public static void ConfigureForHost(bool useGpu)
    {
        if (!useGpu)
        {
            PinCpu("GPU not enabled (settings) — using the CPU runtime");
            return;
        }
        if (!NativeLibrary.TryLoad("libvulkan.so.1", out var handle))
        {
            PinCpu("GPU requested but no libvulkan.so.1 on this system — using the CPU runtime");
            return;
        }
        NativeLibrary.Free(handle);

        // A loader alone is not enough: ggml-vulkan's static initializer can abort()
        // on broken driver stacks (observed under WSL). Probe the actual library in a
        // sacrificial child process; only a clean child gets the Vulkan pin.
        if (ChildProbeSucceeds())
        {
            try
            {
                RuntimeOptions.RuntimeLibraryOrder = [RuntimeLibrary.Vulkan];
                Preload(Path.Combine("runtimes", "vulkan", "linux-x64"));
                Log.Info("Whisper runtime: Vulkan (GPU) — probe succeeded.");
            }
            catch (Exception ex)
            {
                Log.Error("Could not pin the Vulkan runtime", ex);
            }
        }
        else
        {
            PinCpu("GPU requested but the Vulkan runtime fails on this driver stack — using the CPU runtime");
        }
    }

    // Handles are kept for the process lifetime ON PURPOSE — see Preload.
    private static readonly List<IntPtr> PinnedLibs = [];

    /// <summary>
    /// Load the runtime's native chain ourselves and KEEP the handles. Whisper.net's
    /// loader load-checks-unloads libraries; reloading libggml-base re-runs its static
    /// initializer, whose set_terminate guard then sees its own stale handler and
    /// abort()s the process (observed on stock Ubuntu 24.04/26.04 with 1.9.0/1.9.1).
    /// With our references held, any later dlclose only decrements a refcount and the
    /// initializer can never run twice.
    /// </summary>
    private static void Preload(string relativeDir)
    {
        var dir = Path.Combine(AppContext.BaseDirectory, relativeDir);
        foreach (var name in (string[])
                 ["libggml-base-whisper.so", "libggml-cpu-whisper.so",
                  "libggml-vulkan-whisper.so", "libggml-whisper.so", "libwhisper.so"])
        {
            var path = Path.Combine(dir, name);
            if (File.Exists(path) && NativeLibrary.TryLoad(path, out var handle))
                PinnedLibs.Add(handle);
        }
    }

    /// <summary>Runs in the CHILD (--vulkanprobe): dlopen the ggml-vulkan library; an
    /// abort in its static init kills this process, which is exactly the signal.</summary>
    public static int ProbeVulkanInChild()
    {
        try
        {
            var lib = Path.Combine(AppContext.BaseDirectory, "runtimes", "vulkan", "linux-x64", "libggml-vulkan-whisper.so");
            return File.Exists(lib) && NativeLibrary.TryLoad(lib, out _) ? 0 : 3;
        }
        catch
        {
            return 3;
        }
    }

    private static bool ChildProbeSucceeds()
    {
        try
        {
            var self = Environment.ProcessPath;
            if (string.IsNullOrEmpty(self))
                return false;
            var psi = new System.Diagnostics.ProcessStartInfo(self, "--vulkanprobe")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            using var p = System.Diagnostics.Process.Start(psi);
            if (p is null)
                return false;
            if (!p.WaitForExit(10_000))
            {
                try { p.Kill(entireProcessTree: true); } catch { }
                return false;
            }
            return p.ExitCode == 0;
        }
        catch (Exception ex)
        {
            Log.Error("Vulkan child probe failed to run", ex);
            return false;
        }
    }

    private static void PinCpu(string reason)
    {
        try
        {
            RuntimeOptions.RuntimeLibraryOrder = [RuntimeLibrary.Cpu];
            Preload(Path.Combine("runtimes", "linux-x64"));
            Log.Info($"Whisper runtime: {reason}.");
        }
        catch (Exception ex)
        {
            Log.Error("Could not pin the CPU runtime", ex);
        }
    }
}
