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
            Exception? readError = null;
            var levels = 0;
            source.RecordingFailed += ex => readError = ex;
            source.LevelChanged += _ => levels++;
            source.Start(null, autoStop: false, autoStopSilenceSeconds: 1.0);
            // Virtual sources (CI's null-sink monitor) deliver audio in ~1-2 s bursts
            // because null sinks are timer-scheduled; real mics clock continuously.
            // Capture long enough to span multiple bursts.
            Thread.Sleep(3000);
            var samples = source.StopAndGetSamplesAsync().GetAwaiter().GetResult();
            // 3 s at 16 kHz ≈ 48000 samples; require at least ~1.5 s of audio.
            var ok = samples.Length is > 24_000 and < 96_000;
            log.AppendLine($"[{(ok ? "PASS" : "FAIL")}] pulse capture roundtrip: {samples.Length} samples in 3s ({levels} chunks)");
            if (readError is not null)
                log.AppendLine($"read error: {readError.Message}");
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
