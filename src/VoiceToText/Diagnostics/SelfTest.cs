using System.Diagnostics;
using System.Text;
using VoiceToText.Audio;
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

    /// <summary>Deterministic check of the auto-stop (silence) logic, no mic needed.</summary>
    public static int RunVadTest(string outputPath)
    {
        const double chunk = 0.02; // 20 ms chunks
        const double quiet = 0.002, speech = 0.05;
        var log = new StringBuilder();
        var allPass = true;

        void Pass(string name, bool ok, string detail)
        {
            allPass &= ok;
            log.AppendLine($"[{(ok ? "PASS" : "FAIL")}] {name}: {detail}");
        }

        // 1) Fires ~1.0 s after speech ends.
        {
            var d = new SilenceDetector(1.0);
            for (var t = 0.0; t < 0.30; t += chunk) d.Process(quiet, chunk);   // calibrate
            for (var t = 0.0; t < 0.60; t += chunk) d.Process(speech, chunk);  // speech
            var elapsed = 0.0; var fired = false;
            for (var i = 0; i < 500; i++) { elapsed += chunk; if (d.Process(quiet, chunk)) { fired = true; break; } }
            Pass("fires after sustained silence", fired && elapsed is >= 0.9 and <= 1.2, $"fired={fired}, elapsed={elapsed:F2}s (expected ~1.0)");
        }

        // 2) Never fires if no speech occurred.
        {
            var d = new SilenceDetector(0.5);
            var fired = false;
            for (var i = 0; i < 500; i++) if (d.Process(quiet, chunk)) { fired = true; break; }
            Pass("no speech -> no auto-stop", !fired, $"fired={fired}");
        }

        // 3) Fires exactly once.
        {
            var d = new SilenceDetector(0.3);
            for (var t = 0.0; t < 0.30; t += chunk) d.Process(quiet, chunk);
            for (var t = 0.0; t < 0.20; t += chunk) d.Process(speech, chunk);
            var count = 0;
            for (var i = 0; i < 200; i++) if (d.Process(quiet, chunk)) count++;
            Pass("fires exactly once", count == 1, $"count={count}");
        }

        // 4) Resumed speech resets the silence timer.
        {
            var d = new SilenceDetector(1.0);
            for (var t = 0.0; t < 0.30; t += chunk) d.Process(quiet, chunk);
            for (var t = 0.0; t < 0.30; t += chunk) d.Process(speech, chunk);
            var early = false;
            for (var t = 0.0; t < 0.60; t += chunk) early |= d.Process(quiet, chunk); // 0.6 s < 1.0
            for (var t = 0.0; t < 0.20; t += chunk) d.Process(speech, chunk);          // resume
            var mid = false;
            for (var t = 0.0; t < 0.60; t += chunk) mid |= d.Process(quiet, chunk);    // 0.6 s < 1.0 again
            var late = false;
            for (var t = 0.0; t < 0.60; t += chunk) late |= d.Process(quiet, chunk);   // crosses 1.0
            Pass("resumed speech resets timer", !early && !mid && late, $"early={early}, mid={mid}, late={late}");
        }

        log.AppendLine(allPass ? "ALL VAD TESTS PASSED" : "SOME VAD TESTS FAILED");
        var result = log.ToString();
        File.WriteAllText(outputPath, result);
        Console.WriteLine(result);
        return allPass ? 0 : 1;
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
