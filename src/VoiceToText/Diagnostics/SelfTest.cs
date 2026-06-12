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

    public static int RunDashTest(string outputPath) => CoreSelfTest.RunDashTest(outputPath);

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

    public static int RunAboutTest(string outputPath) => CoreSelfTest.RunAboutTest(outputPath);

}
