using System.Text;

namespace VoiceToText.Linux.Platform;

/// <summary>
/// `--audiotest`: a real capture roundtrip against the default source. Exits 0 with
/// SKIPPED when no pulse daemon is reachable (dev boxes); CI starts a daemon with a
/// virtual source and additionally asserts the output contains PASS, making this the
/// empirical validation of the libpulse P/Invoke layer.
/// </summary>
internal static class PulseSelfTest
{
    public static int Run(string outputPath)
    {
        var log = new StringBuilder();
        try
        {
            var source = new PulseAudioSource();
            source.Start(null, autoStop: false, autoStopSilenceSeconds: 1.0);
            Thread.Sleep(500);
            var samples = source.StopAndGetSamplesAsync().GetAwaiter().GetResult();
            // 0.5 s at 16 kHz ≈ 8000 samples; allow generous jitter from daemon buffering.
            var ok = samples.Length is > 2_000 and < 32_000;
            log.AppendLine($"[{(ok ? "PASS" : "FAIL")}] pulse capture roundtrip: {samples.Length} samples in 0.5s");
            log.AppendLine(ok ? "ALL AUDIO TESTS PASSED" : "SOME AUDIO TESTS FAILED");
            File.WriteAllText(outputPath, log.ToString());
            Console.WriteLine(log.ToString());
            return ok ? 0 : 1;
        }
        catch (Exception ex) when (ex is InvalidOperationException or DllNotFoundException)
        {
            log.AppendLine($"SKIPPED: no PulseAudio available ({ex.Message})");
            File.WriteAllText(outputPath, log.ToString());
            Console.WriteLine(log.ToString());
            return 0;
        }
    }
}
