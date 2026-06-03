using System.Diagnostics;
using System.Text;
using System.Text.Json;
using VoiceToText.Audio;
using VoiceToText.Dashboard;
using VoiceToText.History;
using VoiceToText.Overlay;
using VoiceToText.Settings;
using VoiceToText.Stats;
using VoiceToText.Stt;
using VoiceToText.TextProcessing;
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

    /// <summary>Checks the pure usage-stats model (recording + derived metrics). No UI, no I/O.</summary>
    public static int RunStatsTest(string outputPath)
    {
        var log = new StringBuilder();
        var allPass = true;
        void Pass(string name, bool ok, string detail = "")
        {
            allPass &= ok;
            log.AppendLine($"[{(ok ? "PASS" : "FAIL")}] {name}{(detail.Length > 0 ? ": " + detail : "")}");
        }

        Pass("word count splits on whitespace", StatsData.CountWords("  hello   world\tfoo\n") == 3);
        Pass("word count blank => 0", StatsData.CountWords("   ") == 0);

        var s = new StatsData();
        var d0 = new DateOnly(2026, 6, 1);
        s.Record(d0, 10, 30.0, "Outlook");
        s.Record(d0, 5, 15.0, "Chrome");
        s.Record(d0, 0, 5.0, "Chrome"); // zero words ignored
        Pass("totals", s.TotalWords == 15 && s.TotalDictations == 2, $"words={s.TotalWords}, dict={s.TotalDictations}");
        Pass("max single dictation", s.MaxWordsInOneDictation == 10);
        Pass("per-app split", s.Apps["Outlook"].Words == 10 && s.Apps["Chrome"].Words == 5 && s.Apps["Chrome"].Dictations == 1);
        Pass("today words", s.WordsOn(d0) == 15);
        Pass("time saved @40wpm", Math.Abs(s.EstimatedMinutesSaved(40) - 15 / 40.0) < 1e-9);
        Pass("avg words/dictation", Math.Abs(s.AverageWordsPerDictation - 7.5) < 1e-9);
        Pass("speaking wpm", Math.Abs(s.SpeakingWpm - 15 / (45.0 / 60.0)) < 1e-6, $"{s.SpeakingWpm:F1}");

        var today = new DateOnly(2026, 6, 10);
        var st = new StatsData();
        st.Record(today, 1, 1, "A");
        st.Record(today.AddDays(-1), 1, 1, "A");
        st.Record(today.AddDays(-2), 1, 1, "A");
        Pass("streak counts consecutive (3)", st.CurrentStreak(today) == 3, $"={st.CurrentStreak(today)}");

        var sg = new StatsData();
        sg.Record(today, 1, 1, "A");
        sg.Record(today.AddDays(-2), 1, 1, "A"); // gap on -1
        Pass("streak stops at a gap (1)", sg.CurrentStreak(today) == 1, $"={sg.CurrentStreak(today)}");

        var sy = new StatsData();
        sy.Record(today.AddDays(-1), 1, 1, "A");
        sy.Record(today.AddDays(-2), 1, 1, "A");
        Pass("streak alive via yesterday (2)", sy.CurrentStreak(today) == 2, $"={sy.CurrentStreak(today)}");

        Pass("streak 0 when idle", new StatsData().CurrentStreak(today) == 0);

        var sw = new StatsData();
        sw.Record(today, 3, 1, "A");
        sw.Record(today.AddDays(-6), 4, 1, "A");
        sw.Record(today.AddDays(-7), 99, 1, "A"); // outside the 7-day window
        Pass("words in last 7 days", sw.WordsInLastDays(today, 7) == 7, $"={sw.WordsInLastDays(today, 7)}");

        var sb = new StatsData();
        sb.Record(today, 2, 1, "A");
        sb.Record(today.AddDays(-1), 9, 1, "A");
        Pass("busiest day", sb.BusiestDay == today.AddDays(-1).ToString("yyyy-MM-dd"), sb.BusiestDay ?? "null");

        log.AppendLine(allPass ? "ALL STATS TESTS PASSED" : "SOME STATS TESTS FAILED");
        var result = log.ToString();
        File.WriteAllText(outputPath, result);
        Console.WriteLine(result);
        return allPass ? 0 : 1;
    }

    /// <summary>Checks the pure dashboard view-model (series, top-apps, hero formatting). No UI.</summary>
    public static int RunDashTest(string outputPath)
    {
        var log = new StringBuilder();
        var allPass = true;
        void Pass(string name, bool ok, string detail = "")
        {
            allPass &= ok;
            log.AppendLine($"[{(ok ? "PASS" : "FAIL")}] {name}{(detail.Length > 0 ? ": " + detail : "")}");
        }

        var today = new DateOnly(2026, 6, 10);

        // --- Empty state ---
        var empty = new DashboardModel(new StatsData(), today, 40);
        Pass("empty: HasData false", !empty.HasData);
        Pass("empty: 30-day series", empty.DailySeries.Count == 30, $"={empty.DailySeries.Count}");
        Pass("empty: no apps", empty.TopApps.Count == 0);
        Pass("empty: no best", empty.BestDictationText is null);
        Pass("empty: dailyMax >= 1", empty.DailyMax >= 1, $"={empty.DailyMax}");

        // --- Daily series window + zero-fill ---
        var sd = new StatsData();
        sd.Record(today, 10, 20, "Code");
        sd.Record(today.AddDays(-2), 4, 8, "Code");
        sd.Record(today.AddDays(-40), 999, 100, "Code"); // outside the 30-day window
        var dm = new DashboardModel(sd, today, 40);
        Pass("series count 30", dm.DailySeries.Count == 30);
        Pass("series oldest->newest", dm.DailySeries[0].Date == today.AddDays(-29) && dm.DailySeries[29].Date == today);
        Pass("today bucket", dm.DailySeries[29].Words == 10, $"={dm.DailySeries[29].Words}");
        Pass("t-2 bucket", dm.DailySeries[27].Words == 4, $"={dm.DailySeries[27].Words}");
        Pass("t-1 zero-filled", dm.DailySeries[28].Words == 0);
        Pass("t-40 excluded => max 10", dm.DailyMax == 10, $"={dm.DailyMax}");

        // --- Activity(range) windows ---
        var act = new DashboardModel(sd, today, 40); // sd: today=10, t-2=4, t-40=999
        var wk = act.Activity(ChartRange.Week);
        Pass("Activity Week = 7 bars ending today", wk.Bars.Count == 7 && wk.Bars[6].Date == today, $"={wk.Bars.Count}");
        Pass("Activity Week max = 10 (t-40 outside)", wk.Max == 10, $"={wk.Max}");
        Pass("Activity Month = 30 bars", act.Activity(ChartRange.Month).Bars.Count == 30, $"={act.Activity(ChartRange.Month).Bars.Count}");
        var all = act.Activity(ChartRange.All);
        Pass("Activity All spans t-40..today = 41 bars", all.Bars.Count == 41 && all.Bars[0].Date == today.AddDays(-40) && all.Bars[40].Date == today, $"={all.Bars.Count}");
        Pass("Activity All max includes t-40 (999)", all.Max == 999, $"={all.Max}");
        var allEmpty = new DashboardModel(new StatsData(), today, 40).Activity(ChartRange.All);
        Pass("Activity All with no data => 30-bar fallback", allEmpty.Bars.Count == 30, $"={allEmpty.Bars.Count}");

        // --- Top apps + Other ---
        var sa = new StatsData();
        sa.Record(today, 70, 10, "A");
        sa.Record(today, 60, 10, "B");
        sa.Record(today, 50, 10, "C");
        sa.Record(today, 40, 10, "D");
        sa.Record(today, 30, 10, "E");
        sa.Record(today, 20, 10, "F");
        sa.Record(today, 10, 10, "G");
        var am = new DashboardModel(sa, today, 40);
        Pass("top apps = 5 + Other = 6 rows", am.TopApps.Count == 6, $"={am.TopApps.Count}");
        Pass("Other aggregates F+G (30)", am.TopApps[5].Name == "Other" && am.TopApps[5].Words == 30, $"={am.TopApps[5].Words}");
        Pass("apps sorted desc", am.TopApps[0].Words == 70 && am.TopApps[0].Name == "A");
        Pass("largest fraction == 1.0", Math.Abs(am.TopApps[0].Fraction - 1.0) < 1e-9, $"={am.TopApps[0].Fraction:F3}");

        // --- Duration formatting (three branches) ---
        Pass("duration <1 min", StatsFormat.Duration(0.5) == "<1 min", StatsFormat.Duration(0.5));
        Pass("duration minutes", StatsFormat.Duration(37) == "37 min", StatsFormat.Duration(37));
        Pass("duration hours", StatsFormat.Duration(150) == "2.5 hrs", StatsFormat.Duration(150));

        // --- Hero / tiles / records / streak ---
        var t = new StatsData();
        t.Record(today, 19, 12, "Code");
        t.Record(today.AddDays(-1), 5, 4, "Chrome");
        var tm = new DashboardModel(t, today, 40);
        Pass("hero time saved matches format", tm.TimeSavedText == StatsFormat.Duration(t.EstimatedMinutesSaved(40)), tm.TimeSavedText);
        Pass("hero subtext has WPM", tm.TimeSavedSubtext.Contains("40"), tm.TimeSavedSubtext);
        Pass("avg words rounded (12)", tm.AvgWordsPerDictation == 12, $"={tm.AvgWordsPerDictation}");
        var ar = new StatsData();
        ar.Record(today, 10, 5, "X");
        ar.Record(today, 11, 5, "X");
        ar.Record(today, 11, 5, "X");
        var arm = new DashboardModel(ar, today, 40);
        Pass("avg rounds 32/3 -> 11", arm.AvgWordsPerDictation == 11, $"={arm.AvgWordsPerDictation}");
        Pass("speaking wpm rounded", tm.SpeakingWpm == (int)Math.Round(t.SpeakingWpm), $"={tm.SpeakingWpm}");
        Pass("speaking time text", tm.SpeakingTimeText == StatsFormat.Duration(t.TotalSeconds / 60.0), tm.SpeakingTimeText);
        Pass("best dictation text", tm.BestDictationText == "19 words", tm.BestDictationText ?? "null");
        Pass("busiest day text", tm.BusiestDayText == "Jun 10 (19 words)", tm.BusiestDayText ?? "null");
        Pass("streak passthrough", tm.Streak == t.CurrentStreak(today), $"={tm.Streak}");

        log.AppendLine(allPass ? "ALL DASH TESTS PASSED" : "SOME DASH TESTS FAILED");
        var result = log.ToString();
        File.WriteAllText(outputPath, result);
        Console.WriteLine(result);
        return allPass ? 0 : 1;
    }


    /// <summary>Checks the pure history store (prepend, cap at 50, clear). No UI, no I/O.</summary>
    public static int RunHistoryTest(string outputPath)
    {
        var log = new StringBuilder();
        var allPass = true;
        void Pass(string name, bool ok, string detail = "")
        {
            allPass &= ok;
            log.AppendLine($"[{(ok ? "PASS" : "FAIL")}] {name}{(detail.Length > 0 ? ": " + detail : "")}");
        }

        var store = new HistoryStore();
        store.Add(new HistoryEntry { Text = "first", Words = 1 });
        store.Add(new HistoryEntry { Text = "second", Words = 1 });
        Pass("newest first", store.Entries[0].Text == "second" && store.Entries[1].Text == "first", store.Entries[0].Text);

        var capped = new HistoryStore();
        for (var i = 0; i < 60; i++) capped.Add(new HistoryEntry { Text = $"e{i}", Words = 1 });
        Pass("capped to 50", capped.Entries.Count == HistoryStore.MaxEntries, $"={capped.Entries.Count}");
        Pass("cap keeps newest", capped.Entries[0].Text == "e59" && capped.Entries[49].Text == "e10", $"{capped.Entries[0].Text}..{capped.Entries[49].Text}");

        capped.Clear();
        Pass("clear empties", capped.Entries.Count == 0, $"={capped.Entries.Count}");

        // Round-trip: TranscribeSeconds + Model survive serialize -> deserialize.
        var rtStore = new HistoryStore();
        rtStore.Add(new HistoryEntry { Text = "rt", Words = 1, TranscribeSeconds = 0.34, Model = "LargeV3Turbo" });
        var json = JsonSerializer.Serialize(rtStore);
        var rtBack = JsonSerializer.Deserialize<HistoryStore>(json);
        var rtSeconds = rtBack?.Entries[0].TranscribeSeconds;
        Pass("transcribe seconds round-trip", rtSeconds is double rs && Math.Abs(rs - 0.34) < 1e-9, $"={rtSeconds?.ToString() ?? "null"}");
        var rtModel = rtBack?.Entries[0].Model;
        Pass("model round-trip", rtModel == "LargeV3Turbo", $"={rtModel ?? "null"}");

        // Legacy JSON: an entry with no TranscribeSeconds/Model field loads as null.
        const string legacyJson = "{\"Entries\":[{\"Time\":\"2026-06-03T14:32:00\",\"App\":\"Discord\",\"Text\":\"hello\",\"Words\":1}]}";
        var legacyStore = JsonSerializer.Deserialize<HistoryStore>(legacyJson);
        Pass("legacy entry has null seconds", legacyStore?.Entries[0].TranscribeSeconds is null);
        Pass("legacy entry has null model", legacyStore?.Entries[0].Model is null);

        log.AppendLine(allPass ? "ALL HISTORY TESTS PASSED" : "SOME HISTORY TESTS FAILED");
        var result = log.ToString();
        File.WriteAllText(outputPath, result);
        Console.WriteLine(result);
        return allPass ? 0 : 1;
    }

    /// <summary>Checks the rolling LogWriter (rotation at the cap, never throws). No app state.</summary>
    public static int RunLogTest(string outputPath)
    {
        var log = new StringBuilder();
        var allPass = true;
        void Pass(string name, bool ok, string detail = "")
        {
            allPass &= ok;
            log.AppendLine($"[{(ok ? "PASS" : "FAIL")}] {name}{(detail.Length > 0 ? ": " + detail : "")}");
        }

        var dir = Path.Combine(Path.GetTempPath(), "vtt-logtest");
        try { if (Directory.Exists(dir)) Directory.Delete(dir, true); } catch { }
        var file = Path.Combine(dir, "t.log");

        var w = new LogWriter(file, maxBytes: 1000);
        w.Write("INFO", "first line", null);
        Pass("writes + creates file", File.Exists(file));
        Pass("no rollover under cap", !File.Exists(file + ".1"));

        for (var i = 0; i < 200; i++) w.Write("INFO", $"padding line {i} ........................", null);
        Pass("rolled over past cap", File.Exists(file + ".1"), "expected t.log.1");
        Pass("main file reset below cap", new FileInfo(file).Length <= 1000 + 512, $"={new FileInfo(file).Length}");

        var threw = false;
        try { w.Write("ERROR", "boom", new InvalidOperationException("x")); } catch { threw = true; }
        Pass("error write does not throw", !threw);

        var threw2 = false;
        try { new LogWriter(@"Z:\nope\does\not\exist\t.log").Write("INFO", "x", null); } catch { threw2 = true; }
        Pass("unwritable path does not throw", !threw2);

        try { Directory.Delete(dir, true); } catch { }

        log.AppendLine(allPass ? "ALL LOG TESTS PASSED" : "SOME LOG TESTS FAILED");
        var result = log.ToString();
        File.WriteAllText(outputPath, result);
        Console.WriteLine(result);
        return allPass ? 0 : 1;
    }

    /// <summary>Checks the pure DiagnosticsInfo row assembly + acceleration flag. No live probing.</summary>
    public static int RunAboutTest(string outputPath)
    {
        var log = new StringBuilder();
        var allPass = true;
        void Pass(string name, bool ok, string detail = "")
        {
            allPass &= ok;
            log.AppendLine($"[{(ok ? "PASS" : "FAIL")}] {name}{(detail.Length > 0 ? ": " + detail : "")}");
        }

        var gpu = new DiagnosticsInfo(
            version: "0.7.0", runtime: "Vulkan", gpu: "AMD Radeon RX 7900 XT",
            model: "Large V3 Turbo", modelPath: @"C:\x\ggml-LargeV3Turbo.bin",
            modelSizeBytes: 1_610_612_736, os: "Windows 11", framework: ".NET 10.0", arch: "X64");

        Pass("vulkan => gpu accelerated", gpu.IsGpuAccelerated);
        var rows = gpu.Rows;
        bool Has(string label, string valueContains) =>
            rows.Any(r => r.Label == label && r.Value.Contains(valueContains, StringComparison.OrdinalIgnoreCase));
        Pass("version row", Has("Version", "0.7.0"));
        Pass("acceleration row green text", Has("Acceleration", "Vulkan (GPU)"));
        Pass("gpu row", Has("GPU", "7900 XT"));
        Pass("model row", Has("Speech model", "Large V3 Turbo"));
        Pass("model file row has size", Has("Model file", "1.5 GB"));
        Pass("system row", Has("System", "Windows 11") && Has("System", ".NET 10.0"));

        var cpu = new DiagnosticsInfo(
            version: "0.7.0", runtime: "Cpu", gpu: "Unknown",
            model: "Large V3 Turbo", modelPath: "x", modelSizeBytes: 0,
            os: "Windows 11", framework: ".NET 10.0", arch: "X64");
        Pass("cpu => not gpu accelerated", !cpu.IsGpuAccelerated);
        Pass("cpu acceleration text", cpu.Rows.Any(r => r.Label == "Acceleration" && r.Value.Contains("CPU")));

        var unknown = new DiagnosticsInfo(
            version: "0.7.0", runtime: "Unknown", gpu: "Unknown",
            model: "Large V3 Turbo", modelPath: "x", modelSizeBytes: 0,
            os: "Windows 11", framework: ".NET 10.0", arch: "X64");
        Pass("unknown => not gpu accelerated", !unknown.IsGpuAccelerated);
        Pass("unknown acceleration shows raw", unknown.Rows.Any(r => r.Label == "Acceleration" && r.Value == "Unknown"));

        var text = gpu.ToClipboardText();
        Pass("clipboard text has key fields", text.Contains("0.7.0") && text.Contains("Vulkan") && text.Contains("7900 XT"));

        log.AppendLine(allPass ? "ALL ABOUT TESTS PASSED" : "SOME ABOUT TESTS FAILED");
        var result = log.ToString();
        File.WriteAllText(outputPath, result);
        Console.WriteLine(result);
        return allPass ? 0 : 1;
    }

    /// <summary>Checks the pure text-rules engine (replacements + spoken commands). No UI.</summary>
    public static int RunTextRulesTest(string outputPath)
    {
        var log = new StringBuilder();
        var allPass = true;
        void Pass(string name, bool ok, string detail = "")
        {
            allPass &= ok;
            log.AppendLine($"[{(ok ? "PASS" : "FAIL")}] {name}{(detail.Length > 0 ? ": " + detail : "")}");
        }
        static List<ReplacementRule> R(params (string f, string r)[] rs) =>
            rs.Select(x => new ReplacementRule { Find = x.f, Replace = x.r }).ToList();
        var none = new List<ReplacementRule>();
        static string Vis(string s) => s.Replace("\n", "\\n");

        // Replacements
        Pass("ci whole-word replace", TextRules.Apply("love github and GitHub and GITHUB", R(("github", "GitHub")), false) == "love GitHub and GitHub and GitHub");
        Pass("whole-word leaves githubbing", TextRules.Apply("githubbing", R(("github", "GitHub")), false) == "githubbing");
        Pass("verbatim replace ($ #)", TextRules.Apply("price code", R(("price", "$5"), ("code", "C#")), false) == "$5 C#");
        Pass("rules apply in order", TextRules.Apply("a", R(("a", "b"), ("b", "c")), false) == "c");
        Pass("blank find skipped", TextRules.Apply("hello", R(("", "X")), false) == "hello");
        Pass("verbatim $& stays literal (MatchEvaluator)", TextRules.Apply("foo", R(("foo", "$&")), false) == "$&");
        Pass("replaces all occurrences", TextRules.Apply("github github", R(("github", "GitHub")), false) == "GitHub GitHub");
        Pass("multi-word find", TextRules.Apply("visual studio daily", R(("visual studio", "VS")), false) == "VS daily");

        // Spoken commands
        Pass("new line", TextRules.Apply("a new line b", none, true) == "a\nb", Vis(TextRules.Apply("a new line b", none, true)));
        Pass("new paragraph", TextRules.Apply("a new paragraph b", none, true) == "a\n\nb", Vis(TextRules.Apply("a new paragraph b", none, true)));
        Pass("case + punctuation tolerant", TextRules.Apply("a. New line. b", none, true) == "a.\nb", Vis(TextRules.Apply("a. New line. b", none, true)));
        Pass("one-word newline", TextRules.Apply("a newline b", none, true) == "a\nb");
        Pass("commands off => literal", TextRules.Apply("a new line b", none, false) == "a new line b");
        Pass("commands run before replacements", TextRules.Apply("a new line b", R(("line", "LINE")), true) == "a\nb", Vis(TextRules.Apply("a new line b", R(("line", "LINE")), true)));
        Pass("adjacent commands stack breaks", TextRules.Apply("a new paragraph new line b", none, true) == "a\n\n\nb", Vis(TextRules.Apply("a new paragraph new line b", none, true)));

        // Edges
        Pass("empty unchanged", TextRules.Apply("", none, true) == "");
        Pass("trims output", TextRules.Apply("  hello world  ", none, false) == "hello world");

        log.AppendLine(allPass ? "ALL TEXTRULES TESTS PASSED" : "SOME TEXTRULES TESTS FAILED");
        var result = log.ToString();
        File.WriteAllText(outputPath, result);
        Console.WriteLine(result);
        return allPass ? 0 : 1;
    }

    /// <summary>Smoke test: construct the dashboard window, show both pages, force a synchronous
    /// paint, and close. Catches construction/layout/OnPaint exceptions without a human. No asserts â€”
    /// returns 0 if nothing threw, 1 otherwise.</summary>
    public static int RunDashWindow(string outputPath)
    {
        try
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            var settings = AppSettings.Load();
            var stats = new StatsService();
            var history = new HistoryService();
            using var form = new DashboardForm(settings, stats, history, "v-smoketest");

            form.ShowPage(DashboardPageKind.Settings);
            form.Show();
            Application.DoEvents();
            form.Refresh();           // synchronous WM_PAINT for the Settings page
            Application.DoEvents();

            form.ShowPage(DashboardPageKind.Dashboard);
            Application.DoEvents();
            form.Refresh();           // synchronous WM_PAINT for the Dashboard page (hero/tiles/chart/apps)
            Application.DoEvents();

            form.ShowPage(DashboardPageKind.TextRules);
            Application.DoEvents();
            form.Refresh();           // synchronous WM_PAINT for the Text rules page (grid + preview)
            Application.DoEvents();

            form.ShowPage(DashboardPageKind.History);
            Application.DoEvents();
            form.Refresh();           // synchronous WM_PAINT for the History page (list + empty state)
            Application.DoEvents();

            form.ShowPage(DashboardPageKind.About);
            Application.DoEvents();
            form.Refresh();           // synchronous WM_PAINT for the About page (card + actions)
            Application.DoEvents();

            form.Close();

            using (var wizard = new VoiceToText.Onboarding.OnboardingWizard(settings))
            {
                wizard.Show();
                Application.DoEvents();
                wizard.Refresh();
                for (var s = 0; s < 3; s++)   // paint each of the 4 wizard steps (never finishes → no save)
                {
                    wizard.AdvanceForSmoke();
                    Application.DoEvents();
                    wizard.Refresh();
                }
                wizard.Close();
            }

            File.WriteAllText(outputPath, "DASH WINDOW OK (constructed, all pages shown + painted, closed)");
            Console.WriteLine("DASH WINDOW OK");
            return 0;
        }
        catch (Exception ex)
        {
            File.WriteAllText(outputPath, "ERROR: " + ex);
            Console.WriteLine("ERROR: " + ex);
            return 1;
        }
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

    private static string GetLoadedRuntime() => RuntimeProbe.LoadedRuntime();
}
