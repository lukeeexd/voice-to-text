using System.Diagnostics;
using System.Text;
using VoiceToText.Stt;
using Whisper.net;
using Whisper.net.Ggml;

namespace VoiceToText.Diagnostics;

/// <summary>
/// Headless one-shot transcription used to verify the Whisper + GPU pipeline
/// without a microphone. Run: VoiceToText.exe --selftest path\to\16k-mono.wav [out.txt]
/// Results (including which native runtime loaded) are written to the output file.
/// </summary>
internal static class SelfTest
{
    public static int Run(string wavPath, string outputPath, string? modelName = null)
    {
        try
        {
            var modelType = Enum.TryParse<GgmlType>(modelName, ignoreCase: true, out var parsed)
                ? parsed
                : GgmlType.LargeV3Turbo;
            return RunAsync(wavPath, outputPath, modelType).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            File.WriteAllText(outputPath, "ERROR: " + ex);
            return 1;
        }
    }

    private static async Task<int> RunAsync(string wavPath, string outputPath, GgmlType modelType)
    {
        var log = new StringBuilder();
        log.AppendLine($"Model: {modelType}");

        var sw = Stopwatch.StartNew();
        var modelPath = await ModelManager.EnsureModelAsync(
            modelType, new Progress<string>(s => log.AppendLine(s)));
        log.AppendLine($"Model path: {modelPath}");
        log.AppendLine($"Model ready in {sw.Elapsed.TotalSeconds:F1}s");

        using var factory = WhisperFactory.FromPath(modelPath);
        log.AppendLine($"Loaded native runtime: {GetLoadedRuntime()}");

        await using var processor = factory.CreateBuilder().WithLanguage("en").Build();

        sw.Restart();
        var transcript = new StringBuilder();
        await using (var audio = File.OpenRead(wavPath))
        {
            await foreach (var segment in processor.ProcessAsync(audio))
                transcript.Append(segment.Text);
        }
        log.AppendLine($"Transcribed in {sw.Elapsed.TotalSeconds:F2}s");
        log.AppendLine("TRANSCRIPT: " + transcript.ToString().Trim());

        var result = log.ToString();
        File.WriteAllText(outputPath, result);
        Console.WriteLine(result);
        return 0;
    }

    /// <summary>
    /// Read Whisper.net's loaded runtime via reflection (the enum member names
    /// aren't part of our compile surface). Tells us Vulkan vs Cpu.
    /// </summary>
    private static string GetLoadedRuntime()
    {
        try
        {
            var type = Type.GetType("Whisper.net.LibraryLoader.RuntimeOptions, Whisper.net");
            var value = type?.GetProperty("LoadedLibrary")?.GetValue(null);
            return value?.ToString() ?? "unknown";
        }
        catch
        {
            return "unknown";
        }
    }
}
