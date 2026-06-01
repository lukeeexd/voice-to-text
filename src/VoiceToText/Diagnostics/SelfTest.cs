using System.Diagnostics;
using System.Text;
using VoiceToText.Audio;
using VoiceToText.Overlay;
using VoiceToText.Settings;
using VoiceToText.Stt;
using VoiceToText.Update;
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

    /// <summary>Checks the pure LevelMeter mapping (RMS -> bar heights). No UI, no mic.</summary>
    public static int RunWidgetTest(string outputPath)
    {
        var log = new StringBuilder();
        var allPass = true;
        void Pass(string name, bool ok, string detail = "")
        {
            allPass &= ok;
            log.AppendLine($"[{(ok ? "PASS" : "FAIL")}] {name}{(detail.Length > 0 ? ": " + detail : "")}");
        }

        var meter = new LevelMeter(14);
        Pass("bar count is 14", meter.BarCount == 14);

        var withinBounds = true;
        meter.Reset();
        foreach (var lvl in new[] { 0f, 0.02f, 0.2f, 0.05f, 0.5f, 0f })
            for (var i = 0; i < 10; i++)
                foreach (var h in meter.Update(lvl))
                    if (h is < 0f or > 1f) withinBounds = false;
        Pass("bar heights stay within 0..1", withinBounds);

        meter.Reset();
        float[] quiet = meter.Update(0f);
        for (var i = 0; i < 40; i++) quiet = meter.Update(0f);
        Pass("silence -> bars near baseline", quiet.Max() <= 0.15f, $"max={quiet.Max():F2}");

        meter.Reset();
        float[] loud = meter.Update(0.25f);
        for (var i = 0; i < 40; i++) loud = meter.Update(0.25f);
        var center = loud[loud.Length / 2];
        Pass("loud -> center bar high", center >= 0.7f, $"center={center:F2}");

        meter.Reset();
        var firstCenter = meter.Update(0.25f)[7];
        Pass("smoothing: first frame not maxed", firstCenter < 0.6f, $"first={firstCenter:F2}");

        log.AppendLine(allPass ? "ALL WIDGET TESTS PASSED" : "SOME WIDGET TESTS FAILED");
        var result = log.ToString();
        File.WriteAllText(outputPath, result);
        Console.WriteLine(result);
        return allPass ? 0 : 1;
    }

    /// <summary>Checks the update decision logic (pure) and a simulated feed folder (I/O). No real install.</summary>
    public static int RunUpdateCheck(string outputPath, string? feedFolder)
    {
        var log = new StringBuilder();
        var allPass = true;
        void Pass(string name, bool ok, string detail = "")
        {
            allPass &= ok;
            log.AppendLine($"[{(ok ? "PASS" : "FAIL")}] {name}{(detail.Length > 0 ? ": " + detail : "")}");
        }

        var cur = new Version(1, 0, 0, 0);
        static UpdateManifest M(string? ver, string? setup = "VoiceToText-Setup.exe") => new() { Version = ver, SetupFileName = setup };

        // --- Pure UpdateChecker.Decide cases ---
        Pass("disabled", UpdateChecker.Decide(false, "x", cur, M("2.0.0")).Decision == UpdateDecision.Disabled);
        Pass("version unknown", UpdateChecker.Decide(true, "x", null, M("2.0.0")).Decision == UpdateDecision.VersionUnknown);
        Pass("no feed", UpdateChecker.Decide(true, "", cur, M("2.0.0")).Decision == UpdateDecision.NoFeedConfigured);
        Pass("null manifest", UpdateChecker.Decide(true, "x", cur, null).Decision == UpdateDecision.ManifestInvalid);
        Pass("empty version", UpdateChecker.Decide(true, "x", cur, M("")).Decision == UpdateDecision.ManifestInvalid);
        Pass("rooted setup name", UpdateChecker.Decide(true, "x", cur, M("2.0.0", @"C:\evil.exe")).Decision == UpdateDecision.ManifestInvalid);
        Pass("traversal setup name", UpdateChecker.Decide(true, "x", cur, M("2.0.0", @"..\x.exe")).Decision == UpdateDecision.ManifestInvalid);
        Pass("subdir setup name", UpdateChecker.Decide(true, "x", cur, M("2.0.0", @"sub\x.exe")).Decision == UpdateDecision.ManifestInvalid);
        Pass("equal => up to date", UpdateChecker.Decide(true, "x", cur, M("1.0.0")).Decision == UpdateDecision.UpToDate);
        Pass("lower => up to date (no downgrade)", UpdateChecker.Decide(true, "x", cur, M("0.9.0")).Decision == UpdateDecision.UpToDate);
        var numeric = UpdateChecker.Decide(true, "x", new Version(0, 9, 0, 0), M("0.10.0"));
        Pass("0.10.0 > 0.9.0 (numeric, not string)", numeric.Decision == UpdateDecision.UpdateAvailable && numeric.AvailableVersion == new Version(0, 10, 0, 0));
        Pass("unparseable version", UpdateChecker.Decide(true, "x", cur, M("not-a-version")).Decision == UpdateDecision.ManifestInvalid);
        var higher = UpdateChecker.Decide(true, "x", cur, M("1.0.1"));
        Pass("higher => update available", higher.Decision == UpdateDecision.UpdateAvailable && higher.AvailableVersion == new Version(1, 0, 1, 0));

        // --- VersionParsing ---
        Pass("normalize 1.2", VersionParsing.TryNormalize("1.2") == new Version(1, 2, 0, 0));
        Pass("normalize 1.2.3", VersionParsing.TryNormalize("1.2.3") == new Version(1, 2, 3, 0));
        Pass("normalize 1.2.3.4", VersionParsing.TryNormalize("1.2.3.4") == new Version(1, 2, 3, 4));
        Pass("normalize 1.2.3+sha", VersionParsing.TryNormalize("1.2.3+abc123") == new Version(1, 2, 3, 0));
        Pass("normalize 1.2.3-beta", VersionParsing.TryNormalize("1.2.3-beta") == new Version(1, 2, 3, 0));
        Pass("normalize garbage => null", VersionParsing.TryNormalize("x.y") is null);

        // --- Simulated feed folder (real I/O) ---
        try
        {
            var feed = feedFolder ?? Path.Combine(Path.GetTempPath(), "vtt-updatetest-feed");
            Directory.CreateDirectory(feed);
            var svc = new UpdateService(new AppSettings { AutoUpdateEnabled = true, UpdateFeedFolder = feed });
            var running = svc.CurrentVersion ?? new Version(0, 0, 0, 0);
            var newer = new Version(running.Major, running.Minor + 1, 0, 0);

            const string setupName = "VoiceToText-Setup.exe";
            var setupPath = Path.Combine(feed, setupName);
            File.WriteAllText(setupPath, "dummy-installer-bytes");
            var sha = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(setupPath))).ToLowerInvariant();
            File.WriteAllText(Path.Combine(feed, UpdateManifest.ManifestFileName),
                $"{{\"Version\":\"{newer}\",\"SetupFileName\":\"{setupName}\",\"Sha256\":\"{sha}\"}}");

            var check = svc.CheckAsync().GetAwaiter().GetResult();
            Pass("feed: update available", check.Decision == UpdateDecision.UpdateAvailable && check.AvailableVersion == newer, check.Decision.ToString());

            var staged = svc.StageInstallerAsync(check.Manifest!).GetAwaiter().GetResult();
            Pass("feed: setup staged + SHA ok", File.Exists(staged));

            File.WriteAllText(Path.Combine(feed, UpdateManifest.ManifestFileName),
                $"{{\"Version\":\"{newer}\",\"SetupFileName\":\"{setupName}\",\"Sha256\":\"deadbeef\"}}");
            var tampered = svc.CheckAsync().GetAwaiter().GetResult();
            var refused = false;
            try { svc.StageInstallerAsync(tampered.Manifest!).GetAwaiter().GetResult(); }
            catch (InvalidOperationException) { refused = true; }
            Pass("feed: SHA mismatch refused", refused);

            var missing = new UpdateService(new AppSettings { AutoUpdateEnabled = true, UpdateFeedFolder = Path.Combine(Path.GetTempPath(), "vtt-nonexistent-zzz") });
            Pass("feed: missing folder => ManifestInvalid (no throw)", missing.CheckAsync().GetAwaiter().GetResult().Decision == UpdateDecision.ManifestInvalid);

            try { Directory.Delete(feed, true); } catch { /* best effort */ }
            UpdateService.CleanStaging();
        }
        catch (Exception ex)
        {
            Pass("feed simulation ran without throwing", false, ex.Message);
        }

        log.AppendLine(allPass ? "ALL UPDATE-CHECK TESTS PASSED" : "SOME UPDATE-CHECK TESTS FAILED");
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
