# Dashboard UI Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Give the Voice to Text tray app a real main window — a dark, native-WinForms `DashboardForm` with a sidebar that switches between a **Dashboard** page (visualising usage stats) and a **Settings** page (absorbing the old Settings dialog).

**Architecture:** A pure `DashboardModel` turns a `StatsData` snapshot into ready-to-draw rows; four hand-drawn GDI+ controls (`HeroPanel`, `StatTile`, `BarChart`, `BreakdownBars`) render them on `DashboardPage`. `SettingsPage` is the migrated Settings dialog. `DashboardForm` hosts both pages behind sidebar nav. `TrayApplicationContext` opens a single instance and re-applies settings on save.

**Tech Stack:** C# / .NET 10 (`net10.0-windows`), WinForms, GDI+ (`System.Drawing`). No new NuGet packages.

---

## Conventions (read first — they apply to every task)

**This session is under a background-isolation guard, so the `Write` tool cannot write into the repo.** For every "create/modify a repo file" step:

1. `Write` the full file content to `C:\Users\Luke\.claude\jobs\f39a9536\tmp\stage\<filename>`.
2. Copy it into place with Bash: `cp "C:\Users\Luke\.claude\jobs\f39a9536\tmp\stage\<filename>" "<repo path>"`.

For small edits to existing files you may instead use `perl -0pi -e` via Bash. Commits are made via Bash `git` on the `main` branch.

**Build** (PowerShell tool):
```powershell
& "$env:USERPROFILE\.dotnet\dotnet.exe" build VoiceToText.slnx -c Debug
```
Expected: `Build succeeded`. (New `.cs` files under `src\VoiceToText\` are auto-included by the SDK; no csproj edit needed to add them.)

**Run a headless self-test** (WinExe writes results to a file *and* stdout; capture the file). PowerShell tool, from the repo root `D:\ClaudeCode\voice-to-text`:
```powershell
$env:DOTNET_ROOT = "$env:USERPROFILE\.dotnet"
$exe = "src\VoiceToText\bin\Debug\net10.0-windows\VoiceToText.exe"
Start-Process $exe -ArgumentList "--dashtest" -WorkingDirectory $PWD -Wait
Get-Content dashtest-output.txt
```

**Commit** (Bash tool):
```bash
cd "D:/ClaudeCode/voice-to-text" && git add -A && git commit -m "<message>

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```
(`.gitignore` already excludes `bin/`, `obj/`, `publish/`, `installer/Output/`, `.superpowers/`, and `*-output.txt`, so `git add -A` is safe.)

---

## File Structure

**Create**
- `src/VoiceToText/Stats/StatsFormat.cs` — shared duration formatting (one impl for dashboard + summary).
- `src/VoiceToText/Dashboard/DashboardModel.cs` — pure view-model (`StatsData` → view rows). Testable.
- `src/VoiceToText/Dashboard/Theme.cs` — palette, fonts, rounded-rect helper.
- `src/VoiceToText/Dashboard/Controls/HeroPanel.cs` — time-saved hero band.
- `src/VoiceToText/Dashboard/Controls/StatTile.cs` — number + caption card.
- `src/VoiceToText/Dashboard/Controls/BarChart.cs` — 30-day activity bars.
- `src/VoiceToText/Dashboard/Controls/BreakdownBars.cs` — top-apps horizontal bars.
- `src/VoiceToText/Dashboard/NavButton.cs` — sidebar nav item (owner-drawn).
- `src/VoiceToText/Dashboard/DashboardPage.cs` — composes the dashboard layout + empty state.
- `src/VoiceToText/Dashboard/SettingsPage.cs` — migrated Settings UI as a page.
- `src/VoiceToText/Dashboard/DashboardForm.cs` — the window shell (sidebar + page host).

**Modify**
- `src/VoiceToText/Stats/StatsService.cs` — delegate to `StatsFormat.Duration`.
- `src/VoiceToText/Diagnostics/SelfTest.cs` — add `RunDashTest`.
- `src/VoiceToText/Program.cs` — route `--dashtest`.
- `src/VoiceToText/App/TrayApplicationContext.cs` — menu, double-click, single-instance `ShowDashboard`, settings re-apply, remove Stats popup.
- `src/VoiceToText/VoiceToText.csproj` — bump `<Version>` to `0.6.0` (ship task).

**Delete**
- `src/VoiceToText/Settings/SettingsForm.cs` — replaced by `SettingsPage`.

---

## Task 1: Shared duration formatting (`StatsFormat`)

**Files:**
- Create: `src/VoiceToText/Stats/StatsFormat.cs`
- Modify: `src/VoiceToText/Stats/StatsService.cs` (the `Summary`/`FormatDuration` region, lines ~61-78)

- [ ] **Step 1: Create `StatsFormat.cs`**

Stage to `stage\StatsFormat.cs`, then `cp` to `src/VoiceToText/Stats/StatsFormat.cs`:

```csharp
namespace VoiceToText.Stats;

/// <summary>
/// Shared formatting for usage stats so the dashboard and any text summary render
/// durations identically. Pure; no state.
/// </summary>
public static class StatsFormat
{
    /// <summary>Human duration: "&lt;1 min" / "N min" / "N.N hrs".</summary>
    public static string Duration(double minutes)
    {
        if (minutes < 1) return "<1 min";
        if (minutes < 90) return $"{minutes:N0} min";
        return $"{minutes / 60.0:N1} hrs";
    }
}
```

- [ ] **Step 2: Point `StatsService` at it (remove the duplicate)**

In `src/VoiceToText/Stats/StatsService.cs`, change the `Summary` body to call `StatsFormat.Duration(...)` and delete the private `FormatDuration` method. The edited region becomes:

```csharp
    /// <summary>Multi-line summary (legacy text view; no longer shown in the UI but kept for reuse).</summary>
    public string Summary(double typingWpm)
    {
        if (Data.TotalDictations == 0)
            return "No dictations yet — press your hotkey and start talking.";

        var saved = StatsFormat.Duration(Data.EstimatedMinutesSaved(typingWpm));
        var streak = Data.CurrentStreak(DateOnly.FromDateTime(DateTime.Now));
        return $"{Data.TotalWords:N0} words across {Data.TotalDictations:N0} dictations\n" +
               $"~{saved} of typing saved (at {typingWpm:N0} WPM)\n" +
               $"{streak}-day streak  ·  ~{Data.SpeakingWpm:N0} words/min speaking";
    }
}
```

Delete the old `private static string FormatDuration(double minutes) { ... }` method entirely (it was the last member before the closing brace).

- [ ] **Step 3: Build**

```powershell
& "$env:USERPROFILE\.dotnet\dotnet.exe" build VoiceToText.slnx -c Debug
```
Expected: `Build succeeded`.

- [ ] **Step 4: Regression — stats self-test still passes**

```powershell
$env:DOTNET_ROOT = "$env:USERPROFILE\.dotnet"
$exe = "src\VoiceToText\bin\Debug\net10.0-windows\VoiceToText.exe"
Start-Process $exe -ArgumentList "--statstest" -WorkingDirectory $PWD -Wait
Get-Content statstest-output.txt
```
Expected: ends with `ALL STATS TESTS PASSED`.

- [ ] **Step 5: Commit**

```bash
cd "D:/ClaudeCode/voice-to-text" && git add -A && git commit -m "Extract StatsFormat.Duration (shared with dashboard)

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

## Task 2: Pure view-model (`DashboardModel`) + `--dashtest`

**Files:**
- Create: `src/VoiceToText/Dashboard/DashboardModel.cs`
- Modify: `src/VoiceToText/Diagnostics/SelfTest.cs` (add `RunDashTest`; add `using VoiceToText.Dashboard;`)
- Modify: `src/VoiceToText/Program.cs` (route `--dashtest`)

- [ ] **Step 1: Write the failing test (`RunDashTest`) + wire the arg**

Add `using VoiceToText.Dashboard;` to the `using` block at the top of `src/VoiceToText/Diagnostics/SelfTest.cs`, then add this method inside the `SelfTest` class (e.g. right after `RunStatsTest`):

```csharp
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
        Pass("speaking wpm rounded", tm.SpeakingWpm == (int)Math.Round(t.SpeakingWpm), $"={tm.SpeakingWpm}");
        Pass("best dictation text", tm.BestDictationText == "19 words", tm.BestDictationText ?? "null");
        Pass("busiest day text", tm.BusiestDayText != null && tm.BusiestDayText.StartsWith("Jun 10"), tm.BusiestDayText ?? "null");
        Pass("streak passthrough", tm.Streak == t.CurrentStreak(today), $"={tm.Streak}");

        log.AppendLine(allPass ? "ALL DASH TESTS PASSED" : "SOME DASH TESTS FAILED");
        var result = log.ToString();
        File.WriteAllText(outputPath, result);
        Console.WriteLine(result);
        return allPass ? 0 : 1;
    }
```

Then in `src/VoiceToText/Program.cs`, add the route after the `--statstest` block (line ~34) and update the doc comment list to include `--dashtest`:

```csharp
        if (args.Length > 0 && args[0].Equals("--statstest", StringComparison.OrdinalIgnoreCase))
            return SelfTest.RunStatsTest("statstest-output.txt");

        if (args.Length > 0 && args[0].Equals("--dashtest", StringComparison.OrdinalIgnoreCase))
            return SelfTest.RunDashTest("dashtest-output.txt");
```

- [ ] **Step 2: Build to verify it fails (type missing)**

```powershell
& "$env:USERPROFILE\.dotnet\dotnet.exe" build VoiceToText.slnx -c Debug
```
Expected: FAILS — `error CS0246: The type or namespace name 'DashboardModel' could not be found` (and `DayBar`/`AppBar`). This confirms the test references the not-yet-built model.

- [ ] **Step 3: Implement `DashboardModel`**

Stage to `stage\DashboardModel.cs`, then `cp` to `src/VoiceToText/Dashboard/DashboardModel.cs`:

```csharp
using VoiceToText.Stats;

namespace VoiceToText.Dashboard;

/// <summary>One bar of the daily-activity chart.</summary>
public readonly record struct DayBar(DateOnly Date, long Words);

/// <summary>One row of the top-apps breakdown. Fraction is 0..1 of the widest row.</summary>
public readonly record struct AppBar(string Name, long Words, double Fraction);

/// <summary>
/// Pure view-model: turns a <see cref="StatsData"/> snapshot into ready-to-draw rows
/// (hero strings, tile values, a zero-filled 30-day series, the top-apps breakdown,
/// and records). No I/O, no UI, fully unit-testable via --dashtest.
/// </summary>
public sealed class DashboardModel
{
    public const int SeriesDays = 30;
    public const int MaxApps = 5;

    public bool HasData { get; }
    public string TimeSavedText { get; }
    public string TimeSavedSubtext { get; }
    public int Streak { get; }
    public long TotalWords { get; }
    public long TotalDictations { get; }
    public int AvgWordsPerDictation { get; }
    public int SpeakingWpm { get; }
    public IReadOnlyList<DayBar> DailySeries { get; }
    public long DailyMax { get; }
    public IReadOnlyList<AppBar> TopApps { get; }
    public string? BestDictationText { get; }
    public string? BusiestDayText { get; }

    public DashboardModel(StatsData data, DateOnly today, double typingWpm)
    {
        HasData = data.TotalDictations > 0;
        TotalWords = data.TotalWords;
        TotalDictations = data.TotalDictations;
        Streak = data.CurrentStreak(today);
        AvgWordsPerDictation = (int)Math.Round(data.AverageWordsPerDictation);
        SpeakingWpm = (int)Math.Round(data.SpeakingWpm);

        TimeSavedText = StatsFormat.Duration(data.EstimatedMinutesSaved(typingWpm));
        TimeSavedSubtext = $"vs typing at {typingWpm:N0} WPM";

        // 30-day series, oldest -> newest, zero-filled for missing days.
        var series = new List<DayBar>(SeriesDays);
        long max = 0;
        for (var i = SeriesDays - 1; i >= 0; i--)
        {
            var day = today.AddDays(-i);
            long words = data.WordsOn(day);
            if (words > max) max = words;
            series.Add(new DayBar(day, words));
        }
        DailySeries = series;
        DailyMax = Math.Max(1, max);

        // Top apps by words desc; remainder folded into a single "Other" row.
        var ordered = data.Apps
            .Select(kv => (Name: kv.Key, Words: (long)kv.Value.Words))
            .OrderByDescending(a => a.Words)
            .ToList();
        var rows = ordered.Take(MaxApps).ToList();
        if (ordered.Count > MaxApps)
        {
            long other = ordered.Skip(MaxApps).Sum(a => a.Words);
            if (other > 0) rows.Add((Name: "Other", Words: other));
        }
        // Fraction relative to the widest displayed row (computed AFTER "Other" exists,
        // so "Other" can never overflow the track).
        long maxWords = rows.Count > 0 ? rows.Max(r => r.Words) : 1;
        TopApps = rows
            .Select(r => new AppBar(r.Name, r.Words, maxWords <= 0 ? 0 : (double)r.Words / maxWords))
            .ToList();

        BestDictationText = data.MaxWordsInOneDictation > 0 ? $"{data.MaxWordsInOneDictation:N0} words" : null;
        BusiestDayText = FormatBusiestDay(data);
    }

    private static string? FormatBusiestDay(StatsData data)
    {
        var key = data.BusiestDay;
        if (key is null || !DateOnly.TryParse(key, out var day)) return null;
        return $"{day:MMM d} ({data.WordsOn(day):N0} words)";
    }
}
```

- [ ] **Step 4: Build to verify it compiles**

```powershell
& "$env:USERPROFILE\.dotnet\dotnet.exe" build VoiceToText.slnx -c Debug
```
Expected: `Build succeeded`.

- [ ] **Step 5: Run `--dashtest` to verify it passes**

```powershell
$env:DOTNET_ROOT = "$env:USERPROFILE\.dotnet"
$exe = "src\VoiceToText\bin\Debug\net10.0-windows\VoiceToText.exe"
Start-Process $exe -ArgumentList "--dashtest" -WorkingDirectory $PWD -Wait
Get-Content dashtest-output.txt
```
Expected: every line `[PASS] …` and a final `ALL DASH TESTS PASSED`.

- [ ] **Step 6: Commit**

```bash
cd "D:/ClaudeCode/voice-to-text" && git add -A && git commit -m "Add pure DashboardModel + --dashtest self-test

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

## Task 3: Theme (palette + fonts + rounded-rect helper)

**Files:**
- Create: `src/VoiceToText/Dashboard/Theme.cs`

- [ ] **Step 1: Create `Theme.cs`**

Stage to `stage\Theme.cs`, then `cp` to `src/VoiceToText/Dashboard/Theme.cs`:

```csharp
using System.Drawing;
using System.Drawing.Drawing2D;

namespace VoiceToText.Dashboard;

/// <summary>Central dark/blue palette, fonts, and a rounded-rect helper for the dashboard.
/// Fonts live for the process lifetime (never disposed) — intentional for a shared theme.</summary>
internal static class Theme
{
    public static readonly Color WindowBg      = Color.FromArgb(0x17, 0x18, 0x1C);
    public static readonly Color SidebarBg     = Color.FromArgb(0x12, 0x13, 0x17);
    public static readonly Color CardBg        = Color.FromArgb(0x20, 0x22, 0x29);
    public static readonly Color CardBorder    = Color.FromArgb(0x2C, 0x2E, 0x36);
    public static readonly Color Accent        = Color.FromArgb(0x4C, 0x8D, 0xFF);
    public static readonly Color AccentLight   = Color.FromArgb(0x6A, 0xA0, 0xFF);
    public static readonly Color AccentDeep    = Color.FromArgb(0x27, 0x45, 0x7E);
    public static readonly Color HeroFrom      = Color.FromArgb(0x1D, 0x28, 0x40);
    public static readonly Color HeroTo        = Color.FromArgb(0x19, 0x1B, 0x22);
    public static readonly Color HeroBorder    = Color.FromArgb(0x2B, 0x35, 0x50);
    public static readonly Color NavActiveBg   = Color.FromArgb(0x22, 0x2B, 0x3D);
    public static readonly Color NavHoverBg    = Color.FromArgb(0x1B, 0x1C, 0x22);
    public static readonly Color NavActiveText = Color.FromArgb(0xCF, 0xE0, 0xFF);
    public static readonly Color TextPrimary   = Color.FromArgb(0xE8, 0xE9, 0xED);
    public static readonly Color TextSecondary = Color.FromArgb(0x8A, 0x8C, 0x95);
    public static readonly Color TextMuted     = Color.FromArgb(0x54, 0x56, 0x5F);
    public static readonly Color Gold          = Color.FromArgb(0xFF, 0xCE, 0x6B);
    public static readonly Color Warning       = Color.FromArgb(0xE0, 0x9A, 0x3A);

    public static readonly Font HeroNumber = new("Segoe UI", 30f, FontStyle.Bold);
    public static readonly Font TileNumber = new("Segoe UI", 18f, FontStyle.Bold);
    public static readonly Font LabelBold  = new("Segoe UI", 10f, FontStyle.Bold);
    public static readonly Font Caption    = new("Segoe UI", 8.5f, FontStyle.Regular);
    public static readonly Font NavItem    = new("Segoe UI", 10.5f, FontStyle.Regular);
    public static readonly Font Brand      = new("Segoe UI", 11.5f, FontStyle.Bold);
    public static readonly Font Empty      = new("Segoe UI", 11f, FontStyle.Regular);

    /// <summary>A rounded-rectangle path. Caller disposes (use `using`).</summary>
    public static GraphicsPath RoundedRect(Rectangle r, int radius)
    {
        var path = new GraphicsPath();
        int d = radius * 2;
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }
}
```

- [ ] **Step 2: Build**

```powershell
& "$env:USERPROFILE\.dotnet\dotnet.exe" build VoiceToText.slnx -c Debug
```
Expected: `Build succeeded`.

- [ ] **Step 3: Commit**

```bash
cd "D:/ClaudeCode/voice-to-text" && git add -A && git commit -m "Add dashboard Theme (palette, fonts, rounded-rect helper)

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

## Task 4: `HeroPanel` control

**Files:**
- Create: `src/VoiceToText/Dashboard/Controls/HeroPanel.cs`

- [ ] **Step 1: Create `HeroPanel.cs`**

Stage to `stage\HeroPanel.cs`, then `cp` to `src/VoiceToText/Dashboard/Controls/HeroPanel.cs`:

```csharp
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;

namespace VoiceToText.Dashboard.Controls;

/// <summary>The "time saved" hero band: big value + label + subtext, with a streak on the right.</summary>
internal sealed class HeroPanel : Control
{
    private string _value = "—";
    private string _subtext = "";
    private int _streak;

    public HeroPanel()
    {
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint
               | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        BackColor = Theme.WindowBg;
    }

    public void SetData(string value, string subtext, int streak)
    {
        _value = value;
        _subtext = subtext;
        _streak = streak;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Theme.WindowBg);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

        var r = new Rectangle(0, 0, Width - 1, Height - 1);
        using (var path = Theme.RoundedRect(r, 10))
        using (var brush = new LinearGradientBrush(r, Theme.HeroFrom, Theme.HeroTo, LinearGradientMode.Horizontal))
        using (var pen = new Pen(Theme.HeroBorder))
        {
            g.FillPath(brush, path);
            g.DrawPath(pen, path);
        }

        using var accent = new SolidBrush(Theme.Accent);
        using var primary = new SolidBrush(Theme.TextPrimary);
        using var secondary = new SolidBrush(Theme.TextSecondary);
        using var gold = new SolidBrush(Theme.Gold);

        g.DrawString("TIME SAVED", Theme.Caption, accent, 20, 16);
        g.DrawString(_value, Theme.HeroNumber, primary, 18, 32);
        g.DrawString(_subtext, Theme.Caption, secondary, 20, Height - 26);

        var streak = $"{_streak}-day streak";
        var size = g.MeasureString(streak, Theme.LabelBold);
        g.DrawString(streak, Theme.LabelBold, gold, Width - size.Width - 24, (Height - size.Height) / 2f);
    }
}
```

- [ ] **Step 2: Build**

```powershell
& "$env:USERPROFILE\.dotnet\dotnet.exe" build VoiceToText.slnx -c Debug
```
Expected: `Build succeeded`.

- [ ] **Step 3: Commit**

```bash
cd "D:/ClaudeCode/voice-to-text" && git add -A && git commit -m "Add HeroPanel control

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

## Task 5: `StatTile` control

**Files:**
- Create: `src/VoiceToText/Dashboard/Controls/StatTile.cs`

- [ ] **Step 1: Create `StatTile.cs`**

Stage to `stage\StatTile.cs`, then `cp` to `src/VoiceToText/Dashboard/Controls/StatTile.cs`:

```csharp
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;

namespace VoiceToText.Dashboard.Controls;

/// <summary>A card showing a big number and a caption beneath it.</summary>
internal sealed class StatTile : Control
{
    private string _number = "—";
    private string _caption = "";

    public StatTile()
    {
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint
               | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        BackColor = Theme.WindowBg;
    }

    public void SetData(string number, string caption)
    {
        _number = number;
        _caption = caption;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Theme.WindowBg);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

        var r = new Rectangle(0, 0, Width - 1, Height - 1);
        using (var path = Theme.RoundedRect(r, 9))
        using (var fill = new SolidBrush(Theme.CardBg))
        using (var pen = new Pen(Theme.CardBorder))
        {
            g.FillPath(fill, path);
            g.DrawPath(pen, path);
        }

        using var numBrush = new SolidBrush(Theme.TextPrimary);
        using var capBrush = new SolidBrush(Theme.TextSecondary);
        g.DrawString(_number, Theme.TileNumber, numBrush, 13, 10);
        g.DrawString(_caption, Theme.Caption, capBrush, 14, 42);
    }
}
```

- [ ] **Step 2: Build**

```powershell
& "$env:USERPROFILE\.dotnet\dotnet.exe" build VoiceToText.slnx -c Debug
```
Expected: `Build succeeded`.

- [ ] **Step 3: Commit**

```bash
cd "D:/ClaudeCode/voice-to-text" && git add -A && git commit -m "Add StatTile control

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

## Task 6: `BarChart` control (30-day activity)

**Files:**
- Create: `src/VoiceToText/Dashboard/Controls/BarChart.cs`

- [ ] **Step 1: Create `BarChart.cs`**

Stage to `stage\BarChart.cs`, then `cp` to `src/VoiceToText/Dashboard/Controls/BarChart.cs`:

```csharp
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;

namespace VoiceToText.Dashboard.Controls;

/// <summary>Vertical bar chart of the daily-activity series, scaled to its max, on a card.</summary>
internal sealed class BarChart : Control
{
    private IReadOnlyList<DayBar> _series = Array.Empty<DayBar>();
    private long _max = 1;

    public BarChart()
    {
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint
               | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        BackColor = Theme.WindowBg;
    }

    public void SetData(IReadOnlyList<DayBar> series, long max)
    {
        _series = series;
        _max = Math.Max(1, max);
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Theme.WindowBg);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

        var r = new Rectangle(0, 0, Width - 1, Height - 1);
        using (var path = Theme.RoundedRect(r, 9))
        using (var fill = new SolidBrush(Theme.CardBg))
        using (var pen = new Pen(Theme.CardBorder))
        {
            g.FillPath(fill, path);
            g.DrawPath(pen, path);
        }

        using var titleBrush = new SolidBrush(Theme.TextSecondary);
        g.DrawString("Activity — last 30 days", Theme.Caption, titleBrush, 14, 12);

        if (_series.Count == 0) return;

        const int padL = 14, padR = 14, padTop = 36, padBottom = 26;
        var plot = new Rectangle(padL, padTop, Width - padL - padR, Height - padTop - padBottom);
        if (plot.Width <= 4 || plot.Height <= 4) return;

        int n = _series.Count;
        float slot = (float)plot.Width / n;
        float barW = Math.Max(2f, slot - 2f);

        using (var barBrush = new LinearGradientBrush(
            new Rectangle(0, plot.Top, Math.Max(1, Width), plot.Height),
            Theme.Accent, Theme.AccentDeep, LinearGradientMode.Vertical))
        {
            for (int i = 0; i < n; i++)
            {
                long words = _series[i].Words;
                float h = (float)words / _max * plot.Height;
                if (words > 0 && h < 2f) h = 2f; // keep tiny non-zero days visible
                if (h <= 0f) continue;
                float x = plot.Left + i * slot + (slot - barW) / 2f;
                float y = plot.Bottom - h;
                g.FillRectangle(barBrush, x, y, barW, h);
            }
        }

        using var axisBrush = new SolidBrush(Theme.TextMuted);
        var left = _series[0].Date.ToString("MMM d");
        var mid = _series[n / 2].Date.ToString("MMM d");
        g.DrawString(left, Theme.Caption, axisBrush, plot.Left, plot.Bottom + 6);
        var midSize = g.MeasureString(mid, Theme.Caption);
        g.DrawString(mid, Theme.Caption, axisBrush, plot.Left + plot.Width / 2f - midSize.Width / 2f, plot.Bottom + 6);
        var todaySize = g.MeasureString("Today", Theme.Caption);
        g.DrawString("Today", Theme.Caption, axisBrush, plot.Right - todaySize.Width, plot.Bottom + 6);
    }
}
```

- [ ] **Step 2: Build**

```powershell
& "$env:USERPROFILE\.dotnet\dotnet.exe" build VoiceToText.slnx -c Debug
```
Expected: `Build succeeded`.

- [ ] **Step 3: Commit**

```bash
cd "D:/ClaudeCode/voice-to-text" && git add -A && git commit -m "Add BarChart control (30-day activity)

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

## Task 7: `BreakdownBars` control (top apps)

**Files:**
- Create: `src/VoiceToText/Dashboard/Controls/BreakdownBars.cs`

- [ ] **Step 1: Create `BreakdownBars.cs`**

Stage to `stage\BreakdownBars.cs`, then `cp` to `src/VoiceToText/Dashboard/Controls/BreakdownBars.cs`:

```csharp
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;

namespace VoiceToText.Dashboard.Controls;

/// <summary>Horizontal "top apps" breakdown: name + word count over a filled track.</summary>
internal sealed class BreakdownBars : Control
{
    private IReadOnlyList<AppBar> _apps = Array.Empty<AppBar>();

    public BreakdownBars()
    {
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint
               | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        BackColor = Theme.WindowBg;
    }

    public void SetData(IReadOnlyList<AppBar> apps)
    {
        _apps = apps;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Theme.WindowBg);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

        var r = new Rectangle(0, 0, Width - 1, Height - 1);
        using (var path = Theme.RoundedRect(r, 9))
        using (var fill = new SolidBrush(Theme.CardBg))
        using (var pen = new Pen(Theme.CardBorder))
        {
            g.FillPath(fill, path);
            g.DrawPath(pen, path);
        }

        using var titleBrush = new SolidBrush(Theme.TextSecondary);
        g.DrawString("Top apps", Theme.Caption, titleBrush, 14, 12);

        if (_apps.Count == 0) return;

        using var nameBrush = new SolidBrush(Theme.TextPrimary);
        using var countBrush = new SolidBrush(Theme.TextSecondary);
        using var trackBrush = new SolidBrush(Theme.CardBorder);
        using var fillBrush = new LinearGradientBrush(
            new Rectangle(0, 0, Math.Max(1, Width), 10), Theme.Accent, Theme.AccentLight, LinearGradientMode.Horizontal);

        const int x = 14, top = 40, rowH = 30, trackH = 7, rightPad = 14;
        int trackW = Width - x - rightPad;
        if (trackW <= 0) return;

        for (int i = 0; i < _apps.Count; i++)
        {
            int y = top + i * rowH;
            if (y + rowH > Height) break;
            var a = _apps[i];
            g.DrawString(a.Name, Theme.Caption, nameBrush, x, y);
            var countStr = a.Words.ToString("N0");
            var cs = g.MeasureString(countStr, Theme.Caption);
            g.DrawString(countStr, Theme.Caption, countBrush, x + trackW - cs.Width, y);

            int ty = y + 18;
            g.FillRectangle(trackBrush, x, ty, trackW, trackH);
            int fillW = (int)Math.Round(trackW * Math.Clamp(a.Fraction, 0, 1));
            if (fillW > 0) g.FillRectangle(fillBrush, x, ty, fillW, trackH);
        }
    }
}
```

- [ ] **Step 2: Build**

```powershell
& "$env:USERPROFILE\.dotnet\dotnet.exe" build VoiceToText.slnx -c Debug
```
Expected: `Build succeeded`.

- [ ] **Step 3: Commit**

```bash
cd "D:/ClaudeCode/voice-to-text" && git add -A && git commit -m "Add BreakdownBars control (top apps)

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

## Task 8: `DashboardPage` (compose layout + empty state)

**Files:**
- Create: `src/VoiceToText/Dashboard/DashboardPage.cs`

- [ ] **Step 1: Create `DashboardPage.cs`**

Stage to `stage\DashboardPage.cs`, then `cp` to `src/VoiceToText/Dashboard/DashboardPage.cs`:

```csharp
using System.Drawing;
using VoiceToText.Dashboard.Controls;

namespace VoiceToText.Dashboard;

/// <summary>The Dashboard page: hero band, four stat tiles, the activity chart + top-apps
/// breakdown, and a records strip. Shows a centered empty state when there is no data.</summary>
internal sealed class DashboardPage : UserControl
{
    private readonly HeroPanel _hero = new();
    private readonly StatTile _tWords = new();
    private readonly StatTile _tDictations = new();
    private readonly StatTile _tAvg = new();
    private readonly StatTile _tWpm = new();
    private readonly BarChart _chart = new();
    private readonly BreakdownBars _apps = new();
    private readonly Label _records = new()
    {
        AutoSize = false,
        ForeColor = Theme.TextSecondary,
        Font = Theme.Caption,
        BackColor = Theme.WindowBg,
        TextAlign = ContentAlignment.MiddleLeft,
    };
    private readonly Label _empty = new()
    {
        Dock = DockStyle.Fill,
        TextAlign = ContentAlignment.MiddleCenter,
        ForeColor = Theme.TextSecondary,
        Font = Theme.Empty,
        BackColor = Theme.WindowBg,
        Visible = false,
        Text = "No dictations yet — press your hotkey and start talking.",
    };

    public DashboardPage()
    {
        BackColor = Theme.WindowBg;
        DoubleBuffered = true;
        Controls.AddRange(new Control[]
        {
            _hero, _tWords, _tDictations, _tAvg, _tWpm, _chart, _apps, _records, _empty,
        });
    }

    public void Bind(DashboardModel m)
    {
        _empty.Visible = !m.HasData;
        foreach (var c in new Control[] { _hero, _tWords, _tDictations, _tAvg, _tWpm, _chart, _apps, _records })
            c.Visible = m.HasData;
        if (!m.HasData) return;

        _hero.SetData(m.TimeSavedText, m.TimeSavedSubtext, m.Streak);
        _tWords.SetData(m.TotalWords.ToString("N0"), "Words dictated");
        _tDictations.SetData(m.TotalDictations.ToString("N0"), "Dictations");
        _tAvg.SetData(m.AvgWordsPerDictation.ToString("N0"), "Avg words/dictation");
        _tWpm.SetData(m.SpeakingWpm.ToString("N0"), "Speaking WPM");
        _chart.SetData(m.DailySeries, m.DailyMax);
        _apps.SetData(m.TopApps);

        var best = m.BestDictationText is null ? "" : $"Best dictation: {m.BestDictationText}";
        var busy = m.BusiestDayText is null ? "" : $"        Busiest day: {m.BusiestDayText}";
        _records.Text = best + busy;

        DoLayout();
    }

    protected override void OnSizeChanged(EventArgs e)
    {
        base.OnSizeChanged(e);
        DoLayout();
    }

    private void DoLayout()
    {
        const int pad = 20, gap = 10;
        int x = pad, w = Width - pad * 2;
        if (w <= 0 || Height <= 0) return;

        int y = pad;
        _hero.SetBounds(x, y, w, 96);
        y += 96 + 12;

        int tileW = (w - gap * 3) / 4;
        const int tileH = 64;
        _tWords.SetBounds(x, y, tileW, tileH);
        _tDictations.SetBounds(x + (tileW + gap), y, tileW, tileH);
        _tAvg.SetBounds(x + (tileW + gap) * 2, y, tileW, tileH);
        _tWpm.SetBounds(x + (tileW + gap) * 3, y, tileW, tileH);
        y += tileH + 12;

        const int recordsH = 22;
        int colsTop = y;
        int colsBottom = Height - pad - recordsH - 8;
        int colsH = Math.Max(80, colsBottom - colsTop);
        int chartW = (int)((w - gap) * 0.63);
        int appsW = w - gap - chartW;
        _chart.SetBounds(x, colsTop, chartW, colsH);
        _apps.SetBounds(x + chartW + gap, colsTop, appsW, colsH);
        _records.SetBounds(x, colsBottom + 8, w, recordsH);
    }
}
```

- [ ] **Step 2: Build**

```powershell
& "$env:USERPROFILE\.dotnet\dotnet.exe" build VoiceToText.slnx -c Debug
```
Expected: `Build succeeded`.

- [ ] **Step 3: Commit**

```bash
cd "D:/ClaudeCode/voice-to-text" && git add -A && git commit -m "Add DashboardPage (layout + empty state)

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

## Task 9: `SettingsPage` (migrate the Settings dialog)

**Files:**
- Create: `src/VoiceToText/Dashboard/SettingsPage.cs`

This mirrors `Settings/SettingsForm.cs` but as a `UserControl`, adds a **Save** button, a `SettingsSaved` event, `HotkeyCaptureStarted`/`HotkeyCaptureEnded` events (so the host can release the global hotkey only while capturing), a `TryCaptureHotkey` method (the host's `ProcessCmdKey` forwards to it), and a `ReloadFromSettings` method.

- [ ] **Step 1: Create `SettingsPage.cs`**

Stage to `stage\SettingsPage.cs`, then `cp` to `src/VoiceToText/Dashboard/SettingsPage.cs`:

```csharp
using System.Drawing;
using VoiceToText.App;
using VoiceToText.Audio;
using VoiceToText.Hotkeys;
using VoiceToText.Settings;

namespace VoiceToText.Dashboard;

/// <summary>
/// Settings as a page inside the dashboard window: microphone, global hotkey, auto-stop on
/// silence, the on-screen indicator, typing speed (WPM), and start-on-login. Save writes into
/// the shared <see cref="AppSettings"/> (and the Run key) and raises <see cref="SettingsSaved"/>.
/// </summary>
internal sealed class SettingsPage : UserControl
{
    private readonly AppSettings _settings;
    private readonly ComboBox _deviceCombo = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly TextBox _hotkeyBox = new() { ReadOnly = true, Cursor = Cursors.Hand, TextAlign = HorizontalAlignment.Center };
    private readonly Label _hintLabel = new() { AutoSize = true, ForeColor = Theme.TextSecondary, Location = new Point(20, 130), MaximumSize = new Size(440, 0) };
    private readonly CheckBox _autoStopCheck = new() { Text = "Auto-stop after a pause in speech", AutoSize = true, Location = new Point(20, 168), ForeColor = Theme.TextPrimary };
    private readonly NumericUpDown _silenceUpDown = new() { DecimalPlaces = 1, Minimum = 0.3M, Maximum = 10.0M, Increment = 0.1M };
    private readonly CheckBox _overlayCheck = new() { Text = "Show on-screen indicator while dictating", AutoSize = true, Location = new Point(20, 228), ForeColor = Theme.TextPrimary };
    private readonly NumericUpDown _wpmUpDown = new() { DecimalPlaces = 0, Minimum = 10, Maximum = 300, Increment = 5 };
    private readonly CheckBox _startupCheck = new() { Text = "Start automatically when I log in", AutoSize = true, Location = new Point(20, 296), ForeColor = Theme.TextPrimary };
    private readonly Label _savedLabel = new() { AutoSize = true, ForeColor = Theme.Accent, Visible = false, Text = "Settings saved ✓", Location = new Point(128, 336) };
    private HotkeyDefinition _hotkey;

    public event Action? SettingsSaved;
    public event Action? HotkeyCaptureStarted;
    public event Action? HotkeyCaptureEnded;

    public SettingsPage(AppSettings settings)
    {
        _settings = settings;
        _hotkey = settings.Hotkey;
        BackColor = Theme.WindowBg;
        ForeColor = Theme.TextPrimary;
        BuildUi();
        LoadDevices();
        LoadFromSettings();
    }

    /// <summary>Re-sync the controls if settings changed elsewhere (e.g. a rejected hotkey was reverted).</summary>
    public void ReloadFromSettings()
    {
        _hotkey = _settings.Hotkey;
        LoadDevices();
        LoadFromSettings();
        _savedLabel.Visible = false;
    }

    private void LoadFromSettings()
    {
        _hotkeyBox.Text = _hotkey.Describe();
        _startupCheck.Checked = AutoStart.IsEnabled();
        _autoStopCheck.Checked = _settings.AutoStopEnabled;
        _silenceUpDown.Value = (decimal)Math.Clamp(_settings.AutoStopSilenceSeconds, 0.3, 10.0);
        _silenceUpDown.Enabled = _autoStopCheck.Checked;
        _overlayCheck.Checked = _settings.ShowOverlay;
        _wpmUpDown.Value = (decimal)Math.Clamp(_settings.TypingSpeedWpm, 10, 300);
        UpdateHint();
    }

    private void BuildUi()
    {
        var deviceLabel = new Label { Text = "Microphone:", Location = new Point(20, 20), AutoSize = true, ForeColor = Theme.TextPrimary };
        _deviceCombo.SetBounds(20, 42, 440, 24);

        var hotkeyLabel = new Label { Text = "Dictation hotkey:", Location = new Point(20, 78), AutoSize = true, ForeColor = Theme.TextPrimary };
        _hotkeyBox.SetBounds(20, 100, 440, 26);
        _hotkeyBox.GotFocus += (_, _) => { _hotkeyBox.Text = "Press a key or combination…"; HotkeyCaptureStarted?.Invoke(); };
        _hotkeyBox.LostFocus += (_, _) => { _hotkeyBox.Text = _hotkey.Describe(); HotkeyCaptureEnded?.Invoke(); };

        _autoStopCheck.CheckedChanged += (_, _) => _silenceUpDown.Enabled = _autoStopCheck.Checked;
        var stopAfterLabel = new Label { Text = "Stop after", Location = new Point(40, 196), AutoSize = true, ForeColor = Theme.TextPrimary };
        _silenceUpDown.SetBounds(116, 194, 56, 24);
        var secondsLabel = new Label { Text = "seconds of silence", Location = new Point(178, 196), AutoSize = true, ForeColor = Theme.TextPrimary };

        var wpmLabel = new Label { Text = "Typing speed:", Location = new Point(20, 262), AutoSize = true, ForeColor = Theme.TextPrimary };
        _wpmUpDown.SetBounds(108, 260, 60, 24);
        var wpmSuffix = new Label { Text = "WPM  (used to estimate \"time saved\")", Location = new Point(176, 262), AutoSize = true, ForeColor = Theme.TextSecondary };

        var saveButton = new Button { Text = "Save", Location = new Point(20, 330), Size = new Size(96, 30), FlatStyle = FlatStyle.System };
        saveButton.Click += OnSave;

        Controls.AddRange(new Control[]
        {
            deviceLabel, _deviceCombo, hotkeyLabel, _hotkeyBox, _hintLabel,
            _autoStopCheck, stopAfterLabel, _silenceUpDown, secondsLabel,
            _overlayCheck, wpmLabel, _wpmUpDown, wpmSuffix,
            _startupCheck, saveButton, _savedLabel,
        });
    }

    private void LoadDevices()
    {
        _deviceCombo.Items.Clear();
        var devices = AudioDevices.GetInputDevices();
        foreach (var device in devices)
            _deviceCombo.Items.Add(device);

        var current = devices.FirstOrDefault(d => d.Id == _settings.InputDeviceId);
        if (current is not null) _deviceCombo.SelectedItem = current;
        else if (_deviceCombo.Items.Count > 0) _deviceCombo.SelectedIndex = 0;
    }

    /// <summary>
    /// Hotkey capture, called by the host form's <c>ProcessCmdKey</c> when this page is active.
    /// Returns true when the key was consumed as a hotkey; false to let the form handle it.
    /// </summary>
    public bool TryCaptureHotkey(ref Message msg, Keys keyData)
    {
        if (!_hotkeyBox.Focused) return false;

        var key = keyData & Keys.KeyCode;
        var hasModifier = (keyData & (Keys.Control | Keys.Alt | Keys.Shift)) != 0;

        // Reserve bare Esc/Tab/Enter so the window stays navigable.
        if (!hasModifier && key is Keys.Escape or Keys.Tab or Keys.Enter)
            return false;

        var definition = HotkeyDefinition.FromKeyEvent(keyData);
        if (definition is not null)
        {
            _hotkey = definition;
            _hotkeyBox.Text = definition.Describe();
            UpdateHint();
        }
        return true; // swallow (captured combo, or a lone modifier being held)
    }

    private void UpdateHint()
    {
        if (_hotkey.IsRiskyBareKey())
        {
            _hintLabel.ForeColor = Theme.Warning;
            _hintLabel.Text = "⚠ This is a normal typing key — it would be intercepted everywhere. Add Ctrl/Alt/Shift, or use a dedicated key (e.g. F13).";
        }
        else
        {
            _hintLabel.ForeColor = Theme.TextSecondary;
            _hintLabel.Text = "Click the box, then press a single key (e.g. an extra/macro key or F13) or a modifier combo.";
        }
    }

    private void OnSave(object? sender, EventArgs e)
    {
        _settings.InputDeviceId = (_deviceCombo.SelectedItem as AudioInputDevice)?.Id;
        _settings.Hotkey = _hotkey;
        _settings.AutoStopEnabled = _autoStopCheck.Checked;
        _settings.AutoStopSilenceSeconds = (double)_silenceUpDown.Value;
        _settings.ShowOverlay = _overlayCheck.Checked;
        _settings.TypingSpeedWpm = (double)_wpmUpDown.Value;
        AutoStart.Apply(_startupCheck.Checked);
        _savedLabel.Visible = true;
        SettingsSaved?.Invoke();
    }
}
```

- [ ] **Step 2: Build**

```powershell
& "$env:USERPROFILE\.dotnet\dotnet.exe" build VoiceToText.slnx -c Debug
```
Expected: `Build succeeded`. (`SettingsForm.cs` still exists and also compiles — it's removed in Task 11.)

- [ ] **Step 3: Commit**

```bash
cd "D:/ClaudeCode/voice-to-text" && git add -A && git commit -m "Add SettingsPage (Settings dialog migrated to a page)

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

## Task 10: `NavButton` + `DashboardForm` shell

**Files:**
- Create: `src/VoiceToText/Dashboard/NavButton.cs`
- Create: `src/VoiceToText/Dashboard/DashboardForm.cs`

- [ ] **Step 1: Create `NavButton.cs`**

Stage to `stage\NavButton.cs`, then `cp` to `src/VoiceToText/Dashboard/NavButton.cs`:

```csharp
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;

namespace VoiceToText.Dashboard;

/// <summary>A sidebar navigation item: a rounded pill that highlights when active or hovered.</summary>
internal sealed class NavButton : Control
{
    private bool _active;
    private bool _hover;

    public bool Active
    {
        get => _active;
        set { _active = value; Invalidate(); }
    }

    public NavButton(string text)
    {
        Text = text;
        Height = 40;
        Cursor = Cursors.Hand;
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint
               | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        BackColor = Theme.SidebarBg;
    }

    protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Theme.SidebarBg);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

        var pill = new Rectangle(10, 4, Width - 20, Height - 8);
        if (_active)
        {
            using (var b = new SolidBrush(Theme.NavActiveBg))
            using (var p = Theme.RoundedRect(pill, 7))
                g.FillPath(b, p);
            using var accent = new SolidBrush(Theme.Accent);
            g.FillRectangle(accent, pill.X, pill.Y + 6, 3, pill.Height - 12);
        }
        else if (_hover)
        {
            using var b = new SolidBrush(Theme.NavHoverBg);
            using var p = Theme.RoundedRect(pill, 7);
            g.FillPath(b, p);
        }

        var color = _active ? Theme.NavActiveText : Theme.TextSecondary;
        using var tb = new SolidBrush(color);
        using var sf = new StringFormat { LineAlignment = StringAlignment.Center };
        g.DrawString(Text, Theme.NavItem, tb, new RectangleF(pill.X + 14, pill.Y, pill.Width - 14, pill.Height), sf);
    }
}
```

- [ ] **Step 2: Create `DashboardForm.cs`**

Stage to `stage\DashboardForm.cs`, then `cp` to `src/VoiceToText/Dashboard/DashboardForm.cs`:

```csharp
using System.Drawing;
using VoiceToText.Settings;
using VoiceToText.Stats;

namespace VoiceToText.Dashboard;

internal enum DashboardPageKind { Dashboard, Settings }

/// <summary>
/// The app's main window: a sidebar (brand + nav + version) on the left and a content host on
/// the right that shows exactly one page. Refreshes the dashboard data on show and on activate.
/// </summary>
internal sealed class DashboardForm : Form
{
    private readonly AppSettings _settings;
    private readonly StatsService _stats;

    private readonly Panel _sidebar = new() { Dock = DockStyle.Left, Width = 172, BackColor = Theme.SidebarBg };
    private readonly Panel _content = new() { Dock = DockStyle.Fill, BackColor = Theme.WindowBg };
    private readonly NavButton _navDashboard = new("Dashboard") { Dock = DockStyle.Top };
    private readonly NavButton _navSettings = new("Settings") { Dock = DockStyle.Top };
    private readonly DashboardPage _dashboardPage = new() { Dock = DockStyle.Fill };
    private readonly SettingsPage _settingsPage;
    private DashboardPageKind _active = DashboardPageKind.Dashboard;

    public event Action? SettingsSaved;
    public event Action? HotkeyCaptureStarted;
    public event Action? HotkeyCaptureEnded;

    public DashboardForm(AppSettings settings, StatsService stats, string versionLabel)
    {
        _settings = settings;
        _stats = stats;
        _settingsPage = new SettingsPage(settings) { Dock = DockStyle.Fill, Visible = false };
        _settingsPage.SettingsSaved += () => SettingsSaved?.Invoke();
        _settingsPage.HotkeyCaptureStarted += () => HotkeyCaptureStarted?.Invoke();
        _settingsPage.HotkeyCaptureEnded += () => HotkeyCaptureEnded?.Invoke();
        BuildUi(versionLabel);
    }

    private void BuildUi(string versionLabel)
    {
        Text = "Voice to Text";
        try { Icon = System.Drawing.Icon.ExtractAssociatedIcon(Environment.ProcessPath!); }
        catch { /* no embedded icon under the debugger host */ }
        BackColor = Theme.WindowBg;
        ClientSize = new Size(920, 620);
        MinimumSize = new Size(900, 620);
        StartPosition = FormStartPosition.CenterScreen;
        DoubleBuffered = true;

        _content.Controls.Add(_settingsPage);
        _content.Controls.Add(_dashboardPage);

        var brand = new Label
        {
            Text = "  Voice to Text",
            Dock = DockStyle.Top,
            Height = 54,
            ForeColor = Theme.TextPrimary,
            Font = Theme.Brand,
            TextAlign = ContentAlignment.MiddleLeft,
            BackColor = Theme.SidebarBg,
        };
        var version = new Label
        {
            Text = "  " + versionLabel,
            Dock = DockStyle.Bottom,
            Height = 28,
            ForeColor = Theme.TextMuted,
            Font = Theme.Caption,
            TextAlign = ContentAlignment.MiddleLeft,
            BackColor = Theme.SidebarBg,
        };

        _navDashboard.Click += (_, _) => ShowPage(DashboardPageKind.Dashboard);
        _navSettings.Click += (_, _) => ShowPage(DashboardPageKind.Settings);

        // Dock.Top stacks in reverse add-order, so the last added sits highest.
        _sidebar.Controls.Add(_navSettings);
        _sidebar.Controls.Add(_navDashboard);
        _sidebar.Controls.Add(brand);
        _sidebar.Controls.Add(version);

        // Add the Fill host before the Left sidebar so the sidebar reserves its edge first.
        Controls.Add(_content);
        Controls.Add(_sidebar);

        SetActiveStyles();
    }

    public void ShowPage(DashboardPageKind page)
    {
        _active = page;
        _dashboardPage.Visible = page == DashboardPageKind.Dashboard;
        _settingsPage.Visible = page == DashboardPageKind.Settings;
        SetActiveStyles();
    }

    private void SetActiveStyles()
    {
        _navDashboard.Active = _active == DashboardPageKind.Dashboard;
        _navSettings.Active = _active == DashboardPageKind.Settings;
    }

    /// <summary>Rebuild the view-model from the current stats snapshot and bind the dashboard page.</summary>
    public void RefreshData()
    {
        var model = new DashboardModel(_stats.Data, DateOnly.FromDateTime(DateTime.Now), _settings.TypingSpeedWpm);
        _dashboardPage.Bind(model);
    }

    /// <summary>Re-sync the Settings page (e.g. after a rejected hotkey was reverted).</summary>
    public void ReloadSettings() => _settingsPage.ReloadFromSettings();

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        RefreshData();
    }

    protected override void OnActivated(EventArgs e)
    {
        base.OnActivated(e);
        RefreshData();
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (_active == DashboardPageKind.Settings && _settingsPage.TryCaptureHotkey(ref msg, keyData))
            return true;
        return base.ProcessCmdKey(ref msg, keyData);
    }
}
```

- [ ] **Step 3: Build**

```powershell
& "$env:USERPROFILE\.dotnet\dotnet.exe" build VoiceToText.slnx -c Debug
```
Expected: `Build succeeded`.

- [ ] **Step 4: Commit**

```bash
cd "D:/ClaudeCode/voice-to-text" && git add -A && git commit -m "Add NavButton + DashboardForm shell

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

## Task 11: Wire into the tray; remove the old Settings dialog + Stats popup

**Files:**
- Modify: `src/VoiceToText/App/TrayApplicationContext.cs`
- Delete: `src/VoiceToText/Settings/SettingsForm.cs`

- [ ] **Step 1: Add the dashboard field, the `using`, and the registered-hotkey tracker**

In `src/VoiceToText/App/TrayApplicationContext.cs`, add `using VoiceToText.Dashboard;` to the using block. Then add two fields next to the other private fields (e.g. just below `private ListeningOverlay? _overlay;`):

```csharp
    private DashboardForm? _dashboard;
    private HotkeyDefinition _registeredHotkey;
```

- [ ] **Step 2: Track the registered hotkey in `RegisterHotkey`**

Replace the `RegisterHotkey` method body so it records what is actually registered:

```csharp
    private void RegisterHotkey()
    {
        if (_hotkeys.Register(_settings.Hotkey))
        {
            _registeredHotkey = _settings.Hotkey;
        }
        else
        {
            _registeredHotkey = _settings.Hotkey; // nothing else is registered; keep intent for re-tries
            _trayIcon.ShowBalloonTip(
                6000,
                "Voice to Text",
                $"Hotkey {_settings.Hotkey.Describe()} is already in use. Open Settings to choose another.",
                ToolTipIcon.Warning);
        }
    }
```

- [ ] **Step 3: Repoint the tray double-click**

Change the double-click handler (currently `_trayIcon.DoubleClick += (_, _) => ShowSettings();`) to:

```csharp
        _trayIcon.DoubleClick += (_, _) => ShowDashboard(DashboardPageKind.Dashboard);
```

- [ ] **Step 4: Rebuild the menu**

Replace the `BuildMenu` method with:

```csharp
    private ContextMenuStrip BuildMenu()
    {
        var menu = new ContextMenuStrip();
        var open = new ToolStripMenuItem("Open Dashboard", null, (_, _) => ShowDashboard(DashboardPageKind.Dashboard))
        {
            Font = new Font(menu.Font, FontStyle.Bold),
        };
        menu.Items.Add(open);
        menu.Items.Add("Settings…", null, (_, _) => ShowDashboard(DashboardPageKind.Settings));
        menu.Items.Add("Check for updates…", null, (_, _) => _ = CheckForUpdatesAsync(userInitiated: true));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => ExitApp());
        return menu;
    }
```

- [ ] **Step 5: Replace `ShowSettings` + `ShowStats` with `ShowDashboard` + handlers**

Delete the entire `ShowSettings` method (lines ~258-296) and the entire `ShowStats` method (lines ~451-454), and add these methods in their place:

```csharp
    private void ShowDashboard(DashboardPageKind page)
    {
        if (_dashboard is null || _dashboard.IsDisposed)
        {
            _dashboard = new DashboardForm(_settings, _stats, VersionLabel);
            _dashboard.HotkeyCaptureStarted += OnHotkeyCaptureStarted;
            _dashboard.HotkeyCaptureEnded += OnHotkeyCaptureEnded;
            _dashboard.SettingsSaved += OnSettingsSaved;
            _dashboard.FormClosed += (_, _) => _dashboard = null;
        }

        _dashboard.ShowPage(page);
        if (!_dashboard.Visible) _dashboard.Show();
        if (_dashboard.WindowState == FormWindowState.Minimized) _dashboard.WindowState = FormWindowState.Normal;
        _dashboard.Activate();
        _dashboard.BringToFront();
    }

    // Release the global hotkey only while the user is capturing one in Settings, so the
    // captured keypress can't start dictation; restore the last good one when they leave the box.
    private void OnHotkeyCaptureStarted() => _hotkeys.Unregister();
    private void OnHotkeyCaptureEnded() => _hotkeys.Register(_registeredHotkey);

    // Re-apply settings after a Save on the Settings page (mirrors the old post-dialog logic).
    private void OnSettingsSaved()
    {
        _hotkeys.Unregister();
        if (_hotkeys.Register(_settings.Hotkey))
        {
            _registeredHotkey = _settings.Hotkey;
            _settings.Save();
            _trayIcon.Text = $"Voice to Text {VersionLabel} — ready ({_settings.Hotkey.Describe()})";
        }
        else
        {
            // OS rejected the new combo — keep the previous working one instead of none.
            var rejected = _settings.Hotkey;
            _settings.Hotkey = _registeredHotkey;
            _settings.Save();
            _hotkeys.Register(_registeredHotkey);
            _dashboard?.ReloadSettings();
            _trayIcon.ShowBalloonTip(
                6000,
                "Voice to Text",
                $"{rejected.Describe()} is reserved or already in use. Kept {_registeredHotkey.Describe()}.",
                ToolTipIcon.Warning);
        }

        ApplyOverlaySetting();
    }
```

- [ ] **Step 6: Dispose the dashboard with the context**

In `Dispose(bool disposing)`, add `_dashboard?.Dispose();` inside the `if (disposing)` block (e.g. just after `_overlay?.Dispose();`):

```csharp
            _overlay?.Dispose();
            _dashboard?.Dispose();
            _window.Dispose();
```

- [ ] **Step 7: Delete the obsolete `SettingsForm`**

```bash
cd "D:/ClaudeCode/voice-to-text" && git rm src/VoiceToText/Settings/SettingsForm.cs
```

- [ ] **Step 8: Build**

```powershell
& "$env:USERPROFILE\.dotnet\dotnet.exe" build VoiceToText.slnx -c Debug
```
Expected: `Build succeeded` with no references to `SettingsForm` remaining. If the build complains about an unused `using VoiceToText.Settings;`, leave it — `AppSettings` is in that namespace and is still used.

- [ ] **Step 9: Re-run `--dashtest` (regression — wiring didn't break the model)**

```powershell
$env:DOTNET_ROOT = "$env:USERPROFILE\.dotnet"
$exe = "src\VoiceToText\bin\Debug\net10.0-windows\VoiceToText.exe"
Start-Process $exe -ArgumentList "--dashtest" -WorkingDirectory $PWD -Wait
Get-Content dashtest-output.txt
```
Expected: `ALL DASH TESTS PASSED`.

- [ ] **Step 10: Commit**

```bash
cd "D:/ClaudeCode/voice-to-text" && git add -A && git commit -m "Wire dashboard into tray; remove Settings dialog + Stats popup

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

## Task 12: Manual verification (run the app)

**Files:** none (build + run + observe).

- [ ] **Step 1: Build and launch**

```powershell
& "$env:USERPROFILE\.dotnet\dotnet.exe" build VoiceToText.slnx -c Debug
$env:DOTNET_ROOT = "$env:USERPROFILE\.dotnet"
Start-Process "src\VoiceToText\bin\Debug\net10.0-windows\VoiceToText.exe"
```

- [ ] **Step 2: Walk the checklist** (the real `%APPDATA%\VoiceToText\stats.json` has data, so the populated path shows):

  - Tray menu shows **Open Dashboard** (bold), **Settings…**, **Check for updates…**, **Exit** — no "Stats…".
  - **Open Dashboard** opens the window on the Dashboard page; the hero shows a time-saved value, four tiles, the 30-day chart, the top-apps breakdown, and the records strip.
  - **Double-clicking** the tray icon also opens the Dashboard page; opening again does **not** create a second window (it re-focuses the one window).
  - Sidebar **Settings** switches to the Settings page; the active nav pill highlights; **Dashboard** switches back.
  - **Resize** the window larger/smaller (down to the minimum) — the chart and the two columns reflow without clipping or overlap.
  - On the **Settings** page: click the hotkey box (global hotkey releases), press your F13/extra key — it's captured; toggle auto-stop, overlay, change WPM; click **Save** → "Settings saved ✓" appears; confirm `%APPDATA%\VoiceToText\settings.json` reflects the changes and dictation still triggers on the hotkey.
  - With the window open in the background, do a dictation into another app, then click back to the window → the hero/tiles/chart update (refresh-on-activate).

- [ ] **Step 3: Record the result** — note any issues. If something needs fixing, fix it in the relevant task's file (stage→cp), rebuild, and re-verify before proceeding. No commit needed unless a fix was made (commit fixes with a clear message).

---

## Task 13: Ship v0.6.0 to the update feed

**Files:**
- Modify: `src/VoiceToText/VoiceToText.csproj` (`<Version>`)
- Write: `D:\ClaudeCode\VoiceToText-Releases\latest.json` + the setup exe (outside the repo)

- [ ] **Step 1: Bump the version to 0.6.0**

Edit `src/VoiceToText/VoiceToText.csproj` line 13 from `<Version>0.5.1</Version>` to `<Version>0.6.0</Version>`. Via Bash:

```bash
cd "D:/ClaudeCode/voice-to-text" && perl -0pi -e 's{<Version>0\.5\.1</Version>}{<Version>0.6.0</Version>}' src/VoiceToText/VoiceToText.csproj && grep -n "<Version>" src/VoiceToText/VoiceToText.csproj
```
Expected: prints `<Version>0.6.0</Version>`.

- [ ] **Step 2: Publish the self-contained exe**

```powershell
& "$env:USERPROFILE\.dotnet\dotnet.exe" --version > $null  # ensure SDK present
& "D:\ClaudeCode\voice-to-text\publish.ps1"
```
Expected: ends with `Done. Standalone app:` and `publish\VoiceToText.exe` exists. Confirm the published version:

```powershell
(Get-Item "D:\ClaudeCode\voice-to-text\publish\VoiceToText.exe").VersionInfo.ProductVersion
```
Expected: `0.6.0`.

- [ ] **Step 3: Build the installer**

```powershell
& "C:\Users\Luke\.claude\jobs\f39a9536\tmp\innosetup\tools\ISCC.exe" "D:\ClaudeCode\voice-to-text\installer\VoiceToText.iss"
```
Expected: `Successful compile` and `installer\Output\VoiceToText-Setup.exe` exists. (The `.iss` derives the version from the published exe, so it will read 0.6.0.)

- [ ] **Step 4: Copy to the feed, hash it, and write `latest.json`**

```powershell
$ver = "0.6.0"
$feed = "D:\ClaudeCode\VoiceToText-Releases"
$setup = Join-Path $feed "VoiceToText-Setup-$ver.exe"
Copy-Item "D:\ClaudeCode\voice-to-text\installer\Output\VoiceToText-Setup.exe" $setup -Force
$sha = (Get-FileHash $setup -Algorithm SHA256).Hash.ToLower()
$notes = "New Dashboard window: time saved, a 30-day activity chart, top apps, streak and records. Settings now live inside the window too."
$released = (Get-Date).ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ")
$json = @"
{
    "Version":  "$ver",
    "SetupFileName":  "VoiceToText-Setup-$ver.exe",
    "Sha256":  "$sha",
    "ReleaseNotes":  "$notes",
    "Mandatory":  false,
    "ReleasedUtc":  "$released"
}
"@
[IO.File]::WriteAllText((Join-Path $feed "latest.json"), $json)
Get-Content (Join-Path $feed "latest.json")
```
Expected: prints the JSON with `"Version": "0.6.0"`, a 64-hex lowercase `Sha256`, and the matching setup filename. Confirm the setup exists:

```powershell
Test-Path (Join-Path $feed "VoiceToText-Setup-0.6.0.exe")
```
Expected: `True`.

- [ ] **Step 5: Commit the version bump**

```bash
cd "D:/ClaudeCode/voice-to-text" && git add -A && git commit -m "v0.6.0: dashboard window (unified main UI)

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

- [ ] **Step 6: Sanity-check the feed is consumable**

```powershell
$env:DOTNET_ROOT = "$env:USERPROFILE\.dotnet"
$exe = "src\VoiceToText\bin\Debug\net10.0-windows\VoiceToText.exe"
Start-Process $exe -ArgumentList "--updatecheck","D:\ClaudeCode\VoiceToText-Releases" -WorkingDirectory $PWD -Wait
Get-Content updatecheck-output.txt
```
Expected: ends with `ALL UPDATE-CHECK TESTS PASSED` (this exercises the feed-folder logic against a temp feed; it confirms the updater path still works end-to-end).

The running v0.5.x instance will see v0.6.0 on its next check and offer the upgrade.

---

## Self-Review

**1. Spec coverage:**
- Unified window + sidebar (Dashboard/Settings) → Tasks 8, 9, 10, 11. ✓
- Native GDI+, no deps → Tasks 3-8 (all `System.Drawing`); csproj unchanged except version. ✓
- Pure `DashboardModel` + `--dashtest` → Task 2. ✓
- `StatsFormat` shared → Task 1. ✓
- `Theme` palette → Task 3. ✓
- Four controls (Hero/StatTile/BarChart/BreakdownBars) → Tasks 4-7. ✓
- Option-A layout + empty state → Task 8. ✓
- SettingsPage migration, hotkey capture via `ProcessCmdKey` forwarding, `SettingsSaved` → Tasks 9, 10, 11. ✓
- Tray: Open Dashboard + Settings, double-click, single instance, remove Stats popup, delete SettingsForm → Task 11. ✓
- Refresh on show/activate → Task 10 (`OnShown`/`OnActivated`). ✓
- Ship v0.6.0 to feed → Task 13. ✓

**2. Placeholder scan:** No TBD/TODO; every code step has full content; every command has expected output. ✓

**3. Type consistency:** `DashboardModel(StatsData, DateOnly, double)`, `DayBar(Date, Words)`, `AppBar(Name, Words, Fraction)`, `DashboardPageKind`, `SetData(...)` signatures, and the events `SettingsSaved`/`HotkeyCaptureStarted`/`HotkeyCaptureEnded` + methods `ShowPage`/`RefreshData`/`ReloadSettings`/`TryCaptureHotkey` are used identically across Tasks 2, 8, 9, 10, 11. ✓

**Note on native input theming:** the Settings page's `ComboBox` and `NumericUpDown` keep their default (light) field backgrounds on the dark page — readable and acceptable for v1. Fully dark-theming native inputs is intentionally out of scope (YAGNI).
