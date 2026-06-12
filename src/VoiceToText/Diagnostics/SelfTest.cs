using System.Text;
using VoiceToText.Audio;
using VoiceToText.Dashboard;
using VoiceToText.History;
using VoiceToText.Overlay;
using VoiceToText.Settings;
using VoiceToText.Stats;

namespace VoiceToText.Diagnostics;

/// <summary>
/// The Windows head's self-test dispatch. Portable checks live in
/// <see cref="CoreSelfTest"/> (shared with other platform heads); the methods here either
/// delegate straight through or add the WinForms/UI halves on top of the portable halves.
/// Run: VoiceToText.exe --selftest path\to\16k-mono.wav [out.txt] (and friends).
/// </summary>
internal static class SelfTest
{
    public static int Run(string wavPath, string outputPath, string? modelName = null)
        => CoreSelfTest.Run(wavPath, outputPath, modelName);

    public static int RunVadTest(string outputPath) => CoreSelfTest.RunVadTest(outputPath);

    public static int RunStatsTest(string outputPath) => CoreSelfTest.RunStatsTest(outputPath);

    public static int RunLogTest(string outputPath) => CoreSelfTest.RunLogTest(outputPath);

    public static int RunUpdateCheck(string outputPath, string? feedFolder)
        => CoreSelfTest.RunUpdateCheck(outputPath, feedFolder);

    public static int RunControllerTest(string outputPath) => CoreSelfTest.RunControllerTest(outputPath);

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

        // Sound cues: pure PCM generation only (never open an output device here — no audio on CI).
        var startCue = CueSynth.RenderCue(CueSynth.StartFreqs);
        var stopCue = CueSynth.RenderCue(CueSynth.StopFreqs);
        Pass("sound cue: start buffer non-empty", startCue.Length > 0, $"bytes={startCue.Length}");
        Pass("sound cue: stop buffer non-empty", stopCue.Length > 0, $"bytes={stopCue.Length}");
        Pass("sound cue: 16-bit PCM (even byte count)", startCue.Length % 2 == 0 && stopCue.Length % 2 == 0);
        Pass("sound cue: start and stop are a distinct pair",
            startCue.Length == stopCue.Length && !startCue.SequenceEqual(stopCue));

        // DarkSlider: pure Value clamping (0..1) — no painting, no devices.
        using (var slider = new VoiceToText.Dashboard.Controls.DarkSlider())
        {
            slider.Value = -1; Pass("slider: clamps below 0 to 0", slider.Value == 0.0, $"={slider.Value}");
            slider.Value = 2;  Pass("slider: clamps above 1 to 1", slider.Value == 1.0, $"={slider.Value}");
            slider.Value = 0.5; Pass("slider: midpoint preserved", Math.Abs(slider.Value - 0.5) < 1e-9, $"={slider.Value}");
            int raised = 0;
            slider.ValueChanged += (_, _) => raised++;
            slider.Value = 0.5; // unchanged
            slider.Value = 0.75; // changed
            Pass("slider: ValueChanged only on change", raised == 1, $"raised={raised}");
        }

        log.AppendLine(allPass ? "ALL WIDGET TESTS PASSED" : "SOME WIDGET TESTS FAILED");
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


    /// <summary>Checks the pure history store (prepend, cap at 50, clear) plus the history page UI.</summary>
    public static int RunHistoryTest(string outputPath)
    {
        var log = new StringBuilder();
        var allPass = CoreSelfTest.HistoryStoreChecks(log);
        void Pass(string name, bool ok, string detail = "")
        {
            allPass &= ok;
            log.AppendLine($"[{(ok ? "PASS" : "FAIL")}] {name}{(detail.Length > 0 ? ": " + detail : "")}");
        }

        // UI: rows must build + lay out for both a new-style and a legacy entry, regardless of this
        // machine's real settings/history (regression guard for the LayoutRow meta/Copy layout —
        // --dashwindow only exercises it when the machine happens to have history enabled + entries).
        // Seeds Data.Entries in-memory only; Record/Clear are never called, so nothing is persisted.
        try
        {
            var uiSettings = new AppSettings { HistoryEnabled = true };
            var uiHistory = new HistoryService();
            uiHistory.Data.Entries.Clear();
            uiHistory.Data.Entries.Add(new HistoryEntry { Time = DateTime.Now, App = "Test", Text = "hello world", Words = 2, TranscribeSeconds = 0.3, Model = "LargeV3Turbo" });
            uiHistory.Data.Entries.Add(new HistoryEntry { Time = DateTime.Now, App = "Test", Text = "legacy entry", Words = 2 });
            using var page = new HistoryPage(uiHistory, uiSettings) { Size = new Size(700, 500) };
            page.Reload();
            var list = page.Controls.OfType<FlowLayoutPanel>().Single();
            Pass("ui rows constructed", list.Controls.Count == 2, $"={list.Controls.Count}");
            var meta = list.Controls[0].Controls.OfType<Label>().First(l => l is not LinkLabel && l.Top == 8);
            Pass("ui meta shows model + seconds", meta.Text.Contains("Large v3 Turbo") && meta.Text.Contains("0.3s"), meta.Text);

            // Freeze guard: Reload with UNCHANGED data must NOT dispose+rebuild the rows (the v0.8.11
            // flicker/thrash fix). Same row instance after a second Reload => the guard skipped the rebuild.
            var firstRow = list.Controls[0];
            page.Reload();
            Pass("reload idempotent when unchanged (no rebuild => no flicker)",
                list.Controls.Count == 2 && ReferenceEquals(list.Controls[0], firstRow),
                $"same-instance={ReferenceEquals(list.Controls[0], firstRow)}");

            // And it DOES rebuild when the data changes (a new dictation appears on return).
            uiHistory.Data.Entries.Insert(0, new HistoryEntry { Time = DateTime.Now.AddSeconds(1), App = "Test", Text = "new one", Words = 2 });
            page.Reload();
            Pass("reload rebuilds when entries change", list.Controls.Count == 3, $"={list.Controls.Count}");
        }
        catch (Exception ex)
        {
            Pass("ui rows constructed", false, $"{ex.GetType().Name}: {ex.Message}");
        }

        log.AppendLine(allPass ? "ALL HISTORY TESTS PASSED" : "SOME HISTORY TESTS FAILED");
        var result = log.ToString();
        File.WriteAllText(outputPath, result);
        Console.WriteLine(result);
        return allPass ? 0 : 1;
    }

    /// <summary>Checks the pure text-rules engine (replacements + spoken commands) plus the page UI.</summary>
    public static int RunTextRulesTest(string outputPath)
    {
        var log = new StringBuilder();
        var allPass = CoreSelfTest.TextRulesChecks(log);
        void Pass(string name, bool ok, string detail = "")
        {
            allPass &= ok;
            log.AppendLine($"[{(ok ? "PASS" : "FAIL")}] {name}{(detail.Length > 0 ? ": " + detail : "")}");
        }
        static string Vis(string s) => s.Replace("\n", "\\n");

        // UI: the page must build, theme its grid (3 columns), and render the spoken "new line"
        // command as a real line break in the preview box (the v0.8.8 CRLF newline fix). Settings are
        // in-memory only and Save is never called, so nothing on this machine is touched.
        try
        {
            var uiSettings = new AppSettings { SpokenCommandsEnabled = true };
            using var page = new TextRulesPage(uiSettings) { Size = new Size(700, 600) };

            TextBox? FindEditableSingleLine(Control parent)
            {
                foreach (Control c in parent.Controls)
                {
                    if (c is TextBox { ReadOnly: false, Multiline: false } tb) return tb;
                    var nested = FindEditableSingleLine(c);
                    if (nested is not null) return nested;
                }
                return null;
            }

            var input = FindEditableSingleLine(page);
            Pass("ui preview input found", input is not null);
            if (input is not null) input.Text = "a new line b";

            TextBox? FindReadonlyMultiline(Control parent)
            {
                foreach (Control c in parent.Controls)
                {
                    if (c is TextBox { ReadOnly: true, Multiline: true } tb) return tb;
                    var nested = FindReadonlyMultiline(c);
                    if (nested is not null) return nested;
                }
                return null;
            }

            var output = FindReadonlyMultiline(page);
            Pass("ui preview output renders newline", output is not null && output.Text.Contains(Environment.NewLine), Vis(output?.Text ?? "null"));

            DataGridView? FindGrid(Control parent)
            {
                foreach (Control c in parent.Controls)
                {
                    if (c is DataGridView dgv) return dgv;
                    var nested = FindGrid(c);
                    if (nested is not null) return nested;
                }
                return null;
            }

            var grid = FindGrid(page);
            Pass("ui grid has 3 columns", grid is not null && grid.Columns.Count == 3, $"={grid?.Columns.Count.ToString() ?? "null"}");
        }
        catch (Exception ex)
        {
            Pass("ui preview output renders newline", false, $"{ex.GetType().Name}: {ex.Message}");
        }

        log.AppendLine(allPass ? "ALL TEXTRULES TESTS PASSED" : "SOME TEXTRULES TESTS FAILED");
        var result = log.ToString();
        File.WriteAllText(outputPath, result);
        Console.WriteLine(result);
        return allPass ? 0 : 1;
    }

    /// <summary>Smoke test: construct the dashboard window, show both pages, force a synchronous
    /// paint, and close. Catches construction/layout/OnPaint exceptions without a human. No asserts —
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
}
