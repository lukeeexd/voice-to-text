# Dashboard Extras Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a 7/30/All range toggle to the dashboard activity chart and an opt-in, locally-stored recent-dictation history (new History page), shipped as v0.6.8.

**Architecture:** Two independent slices. (1) A pure `DashboardModel.Activity(range)` series builder feeds a segmented toggle on the existing `BarChart`. (2) A pure `HistoryStore` (capped, newest-first) + an I/O `HistoryService` (`history.json`) recorded from the tray only when `AppSettings.HistoryEnabled` is on, surfaced on a new dark `HistoryPage` with copy + clear-all. Pure logic is unit-tested headlessly; UI is exercised by the `--dashwindow` smoke.

**Tech Stack:** C#/.NET 10 WinForms, System.Text.Json, GDI+ custom controls. Per-user SDK at `C:\Users\Luke\.dotnet`.

---

## Build & test environment (every task uses these)

The repo uses a per-user .NET SDK. **Build** (always `--no-incremental` — incremental builds hide WFO1000 analyzer warnings):

```powershell
& "$env:USERPROFILE\.dotnet\dotnet.exe" build VoiceToText.slnx -c Debug --no-incremental
```
Expected on success: `Build succeeded.` with `0 Warning(s)` / `0 Error(s)`.

**Run a headless self-test** (set `DOTNET_ROOT` so the framework-dependent exe finds the per-user runtime; output is written to a `*.txt` in the current directory):

```powershell
$env:DOTNET_ROOT = "$env:USERPROFILE\.dotnet"
& "src\VoiceToText\bin\Debug\net10.0-windows\VoiceToText.exe" --dashtest
Get-Content .\dashtest-output.txt
```

**Editing under the background sandbox:** if a direct `Edit`/`Write` to a repo file is blocked, stage the file to `C:\Users\Luke\.claude\jobs\f39a9536\tmp\stage\<name>` then `cp` it into place via Bash, or use a `perl -0pi -e` surgical edit. Commit with Bash `git` on `main`.

**WFO1000 caution:** the analyzer errors on a *new public property of a `Control` subclass*. All new controls in this plan use private fields and events only — no new public Control properties — so none should fire. If one does, add `[System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]`.

---

## File structure

**Create**
- `src/VoiceToText/History/HistoryEntry.cs` — one serializable history row (Time, App, Text, Words).
- `src/VoiceToText/History/HistoryStore.cs` — pure capped newest-first list (Add/Clear). Unit-tested.
- `src/VoiceToText/History/HistoryService.cs` — load/save `history.json`, Record/Clear. Mirrors `StatsService`.
- `src/VoiceToText/Dashboard/HistoryPage.cs` — the History page UserControl.

**Modify**
- `src/VoiceToText/Dashboard/DashboardModel.cs` — `ChartRange` enum, `ActivitySeries` record, `Activity(range)`.
- `src/VoiceToText/Dashboard/Controls/BarChart.cs` — title parameter on `SetData`.
- `src/VoiceToText/Dashboard/DashboardPage.cs` — segmented range toggle wired to `Activity`.
- `src/VoiceToText/Dashboard/DashboardForm.cs` — `History` page kind + nav + ctor `HistoryService`.
- `src/VoiceToText/Dashboard/SettingsPage.cs` — `HistoryEnabled` checkbox + re-flow.
- `src/VoiceToText/Settings/AppSettings.cs` — `HistoryEnabled`.
- `src/VoiceToText/App/TrayApplicationContext.cs` — `HistoryService` field, record in `StopAndTranscribeAsync`, pass to `DashboardForm`.
- `src/VoiceToText/Diagnostics/SelfTest.cs` — `RunHistoryTest`, extend `RunDashTest` + `RunDashWindow`.
- `src/VoiceToText/Program.cs` — `--historytest` route.
- `src/VoiceToText/VoiceToText.csproj` — `<Version>0.6.8</Version>` (Task 9).

---

## Task 1: `ChartRange` + pure `Activity(range)` series builder

**Files:**
- Modify: `src/VoiceToText/Dashboard/DashboardModel.cs`
- Test: `src/VoiceToText/Diagnostics/SelfTest.cs` (`RunDashTest`)

- [ ] **Step 1: Add the failing test cases to `RunDashTest`**

In `src/VoiceToText/Diagnostics/SelfTest.cs`, in `RunDashTest`, immediately after the existing `// --- Daily series window + zero-fill ---` block (after the line `Pass("t-40 excluded => max 10", dm.DailyMax == 10, $"={dm.DailyMax}");`), insert:

```csharp
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
```

- [ ] **Step 2: Build to verify it fails**

Run: `& "$env:USERPROFILE\.dotnet\dotnet.exe" build VoiceToText.slnx -c Debug --no-incremental`
Expected: FAIL — `error CS1061: ... does not contain a definition for 'Activity'` (and `ChartRange` not found).

- [ ] **Step 3: Implement `ChartRange`, `ActivitySeries`, and `Activity` in `DashboardModel.cs`**

In `src/VoiceToText/Dashboard/DashboardModel.cs`, add these two type declarations just below the existing `AppBar` record (after line 9, before the `DashboardModel` class doc comment):

```csharp
/// <summary>Which window of daily activity the chart shows.</summary>
public enum ChartRange { Week, Month, All }

/// <summary>A zero-filled daily series (oldest→newest) plus its max bar height.</summary>
public readonly record struct ActivitySeries(IReadOnlyList<DayBar> Bars, long Max);
```

Add two private fields to the `DashboardModel` class, right after the opening brace `{` and before `public const int SeriesDays = 30;`:

```csharp
    private readonly StatsData _data;
    private readonly DateOnly _today;
```

In the constructor, add these two assignments as the very first statements (before `HasData = ...`):

```csharp
        _data = data;
        _today = today;
```

Replace the existing 30-day series block:

```csharp
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
```

with:

```csharp
        // Default chart window (30 days). The page re-queries other ranges via Activity().
        var month = Activity(ChartRange.Month);
        DailySeries = month.Bars;
        DailyMax = month.Max;
```

Add the `Activity` method and its helper just before the existing `private static string? FormatBusiestDay(StatsData data)` method:

```csharp
    /// <summary>
    /// Build the daily-activity series for a range: Week = last 7 days, Month = last 30,
    /// All = earliest recorded day → today (one bar per day). All falls back to the Month
    /// window when no activity is recorded, so the chart is never degenerate. Pure.
    /// </summary>
    public ActivitySeries Activity(ChartRange range)
    {
        int days = range switch
        {
            ChartRange.Week => 7,
            ChartRange.All => AllRangeDays(),
            _ => SeriesDays,
        };

        var bars = new List<DayBar>(days);
        long max = 0;
        for (var i = days - 1; i >= 0; i--)
        {
            var day = _today.AddDays(-i);
            long words = _data.WordsOn(day);
            if (words > max) max = words;
            bars.Add(new DayBar(day, words));
        }
        return new ActivitySeries(bars, Math.Max(1, max));
    }

    // Inclusive day count from the earliest recorded day to today; Month-window fallback when empty.
    private int AllRangeDays()
    {
        DateOnly? earliest = null;
        foreach (var key in _data.Days.Keys)
            if (DateOnly.TryParse(key, out var day) && (earliest is null || day < earliest))
                earliest = day;

        if (earliest is null || earliest > _today)
            return SeriesDays;

        return _today.DayNumber - earliest.Value.DayNumber + 1;
    }
```

- [ ] **Step 4: Build + run `--dashtest` to verify pass**

```powershell
& "$env:USERPROFILE\.dotnet\dotnet.exe" build VoiceToText.slnx -c Debug --no-incremental
$env:DOTNET_ROOT = "$env:USERPROFILE\.dotnet"
& "src\VoiceToText\bin\Debug\net10.0-windows\VoiceToText.exe" --dashtest
Get-Content .\dashtest-output.txt
```
Expected: `0 Warning(s)`, `0 Error(s)`, and `ALL DASH TESTS PASSED` (including the new Activity lines).

- [ ] **Step 5: Commit**

```bash
git add src/VoiceToText/Dashboard/DashboardModel.cs src/VoiceToText/Diagnostics/SelfTest.cs
git commit -m "feat(dashboard): pure Activity(range) series builder for chart 7/30/All"
```

---

## Task 2: Chart title parameter + segmented range toggle on the Dashboard

**Files:**
- Modify: `src/VoiceToText/Dashboard/Controls/BarChart.cs`
- Modify: `src/VoiceToText/Dashboard/DashboardPage.cs`

This is UI (Win32/GDI+), verified by the `--dashwindow` smoke, not a unit test.

- [ ] **Step 1: Add a title parameter to `BarChart.SetData`**

In `src/VoiceToText/Dashboard/Controls/BarChart.cs`, add a title field next to `_max`:

```csharp
    private long _max = 1;
    private string _title = "Activity";
```

Replace the `SetData` method:

```csharp
    public void SetData(IReadOnlyList<DayBar> series, long max)
    {
        _series = series;
        _max = Math.Max(1, max);
        Invalidate();
    }
```

with:

```csharp
    public void SetData(IReadOnlyList<DayBar> series, long max, string title)
    {
        _series = series;
        _max = Math.Max(1, max);
        _title = title;
        Invalidate();
    }
```

In `OnPaint`, replace the hard-coded title draw:

```csharp
        g.DrawString("Activity — last 30 days", Theme.Caption, titleBrush, 14, 12);
```

with:

```csharp
        g.DrawString(_title, Theme.Caption, titleBrush, 14, 12);
```

- [ ] **Step 2: Add the toggle fields + range state to `DashboardPage`**

In `src/VoiceToText/Dashboard/DashboardPage.cs`, add these fields just after `private readonly BreakdownBars _apps = new();`:

```csharp
    private readonly Button _r7 = MakeRangeButton("7");
    private readonly Button _r30 = MakeRangeButton("30");
    private readonly Button _rAll = MakeRangeButton("All");
    private ChartRange _range = ChartRange.Month;
    private DashboardModel? _model;
```

Add the factory method anywhere in the class (e.g. just before `Bind`):

```csharp
    private static Button MakeRangeButton(string text) => new()
    {
        Text = text,
        FlatStyle = FlatStyle.Flat,
        Font = Theme.Caption,
        ForeColor = Theme.TextSecondary,
        BackColor = Theme.CardBg,
        TabStop = false,
    };
```

- [ ] **Step 3: Wire the buttons in the constructor**

In the `DashboardPage()` constructor, replace the `Controls.AddRange(...)` call:

```csharp
        Controls.AddRange(new Control[]
        {
            _hero, _tWords, _tDictations, _tAvg, _tWpm, _tSpeaking, _chart, _apps, _records, _empty,
        });
```

with:

```csharp
        _r7.FlatAppearance.BorderColor = Theme.CardBorder;
        _r30.FlatAppearance.BorderColor = Theme.CardBorder;
        _rAll.FlatAppearance.BorderColor = Theme.CardBorder;
        _r7.Click += (_, _) => SetRange(ChartRange.Week);
        _r30.Click += (_, _) => SetRange(ChartRange.Month);
        _rAll.Click += (_, _) => SetRange(ChartRange.All);

        Controls.AddRange(new Control[]
        {
            _hero, _tWords, _tDictations, _tAvg, _tWpm, _tSpeaking, _chart, _apps, _records, _empty,
            _r7, _r30, _rAll,
        });
```

- [ ] **Step 4: Bind via the range, and add the range helpers**

In `Bind`, add the toggle buttons to the visibility loop — replace:

```csharp
        foreach (var c in new Control[] { _hero, _tWords, _tDictations, _tAvg, _tWpm, _tSpeaking, _chart, _apps, _records })
            c.Visible = m.HasData;
```

with:

```csharp
        foreach (var c in new Control[] { _hero, _tWords, _tDictations, _tAvg, _tWpm, _tSpeaking, _chart, _apps, _records, _r7, _r30, _rAll })
            c.Visible = m.HasData;
```

In `Bind`, replace the chart bind line:

```csharp
        _chart.SetData(m.DailySeries, m.DailyMax);
```

with:

```csharp
        _model = m;
        ApplyRange();
```

Add these methods just after `Bind` (before `OnSizeChanged`):

```csharp
    private void SetRange(ChartRange range)
    {
        _range = range;
        ApplyRange();
    }

    private void ApplyRange()
    {
        StyleRangeButtons();
        if (_model is null) return;
        var series = _model.Activity(_range);
        _chart.SetData(series.Bars, series.Max, ChartTitle(_range));
    }

    private static string ChartTitle(ChartRange range) => range switch
    {
        ChartRange.Week => "Activity — last 7 days",
        ChartRange.All => "Activity — all time",
        _ => "Activity — last 30 days",
    };

    private void StyleRangeButtons()
    {
        void Style(Button b, ChartRange r)
        {
            bool on = r == _range;
            b.BackColor = on ? Theme.NavActiveBg : Theme.CardBg;
            b.ForeColor = on ? Theme.NavActiveText : Theme.TextSecondary;
            b.FlatAppearance.BorderColor = on ? Theme.Accent : Theme.CardBorder;
        }
        Style(_r7, ChartRange.Week);
        Style(_r30, ChartRange.Month);
        Style(_rAll, ChartRange.All);
    }
```

- [ ] **Step 5: Position the toggle at the chart card's top-right in `DoLayout`**

In `DoLayout`, after the line `_chart.SetBounds(x, colsTop, chartW, colsH);`, insert:

```csharp
        // Range toggle in the chart card's top-right corner.
        const int segGap = 4, segH = 22, numW = 32, allW = 40;
        int segY = colsTop + 9;
        int segRight = x + chartW - 12;
        _rAll.SetBounds(segRight - allW, segY, allW, segH);
        _r30.SetBounds(_rAll.Left - segGap - numW, segY, numW, segH);
        _r7.SetBounds(_r30.Left - segGap - numW, segY, numW, segH);
        _r7.BringToFront();
        _r30.BringToFront();
        _rAll.BringToFront();
```

- [ ] **Step 6: Build + `--dashwindow` smoke**

```powershell
& "$env:USERPROFILE\.dotnet\dotnet.exe" build VoiceToText.slnx -c Debug --no-incremental
$env:DOTNET_ROOT = "$env:USERPROFILE\.dotnet"
& "src\VoiceToText\bin\Debug\net10.0-windows\VoiceToText.exe" --dashwindow
Get-Content .\dashwindow-output.txt
```
Expected: `0 Warning(s)`, `0 Error(s)`, and `DASH WINDOW OK`.

- [ ] **Step 7: Commit**

```bash
git add src/VoiceToText/Dashboard/Controls/BarChart.cs src/VoiceToText/Dashboard/DashboardPage.cs
git commit -m "feat(dashboard): 7/30/All segmented range toggle on the activity chart"
```

---

## Task 3: `HistoryEntry` + pure `HistoryStore` + `--historytest`

**Files:**
- Create: `src/VoiceToText/History/HistoryEntry.cs`
- Create: `src/VoiceToText/History/HistoryStore.cs`
- Modify: `src/VoiceToText/Diagnostics/SelfTest.cs` (new `RunHistoryTest`)
- Modify: `src/VoiceToText/Program.cs` (`--historytest` route)

- [ ] **Step 1: Write the failing test (`RunHistoryTest`)**

In `src/VoiceToText/Diagnostics/SelfTest.cs`, add `using VoiceToText.History;` to the `using` block at the top (after `using VoiceToText.Dashboard;`). Then add this method just before `RunTextRulesTest`:

```csharp
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

        log.AppendLine(allPass ? "ALL HISTORY TESTS PASSED" : "SOME HISTORY TESTS FAILED");
        var result = log.ToString();
        File.WriteAllText(outputPath, result);
        Console.WriteLine(result);
        return allPass ? 0 : 1;
    }
```

Add the route in `src/VoiceToText/Program.cs`, after the `--dashtest` block (after line 38):

```csharp
        if (args.Length > 0 && args[0].Equals("--historytest", StringComparison.OrdinalIgnoreCase))
            return SelfTest.RunHistoryTest("historytest-output.txt");
```

Also update the XML doc list on `Main` to include `"--historytest"` (cosmetic; add it to the comma list alongside `"--dashtest"`).

- [ ] **Step 2: Build to verify it fails**

Run: `& "$env:USERPROFILE\.dotnet\dotnet.exe" build VoiceToText.slnx -c Debug --no-incremental`
Expected: FAIL — `error CS0246: The type or namespace name 'HistoryStore' could not be found` (and `HistoryEntry`).

- [ ] **Step 3: Create `HistoryEntry`**

Create `src/VoiceToText/History/HistoryEntry.cs`:

```csharp
namespace VoiceToText.History;

/// <summary>One recorded dictation kept in the opt-in history (serializable).</summary>
public sealed class HistoryEntry
{
    public DateTime Time { get; set; }
    public string App { get; set; } = "";
    public string Text { get; set; } = "";
    public int Words { get; set; }
}
```

- [ ] **Step 4: Create `HistoryStore`**

Create `src/VoiceToText/History/HistoryStore.cs`:

```csharp
namespace VoiceToText.History;

/// <summary>
/// Pure, serializable history model: a capped, newest-first list of dictations. All mutation
/// goes through <see cref="Add"/>/<see cref="Clear"/>; no I/O, fully unit-testable via --historytest.
/// </summary>
public sealed class HistoryStore
{
    public const int MaxEntries = 50;

    /// <summary>Most-recent first.</summary>
    public List<HistoryEntry> Entries { get; set; } = new();

    /// <summary>Prepend an entry and trim to the newest <see cref="MaxEntries"/>.</summary>
    public void Add(HistoryEntry entry)
    {
        Entries.Insert(0, entry);
        if (Entries.Count > MaxEntries)
            Entries.RemoveRange(MaxEntries, Entries.Count - MaxEntries);
    }

    public void Clear() => Entries.Clear();
}
```

- [ ] **Step 5: Build + run `--historytest` to verify pass**

```powershell
& "$env:USERPROFILE\.dotnet\dotnet.exe" build VoiceToText.slnx -c Debug --no-incremental
$env:DOTNET_ROOT = "$env:USERPROFILE\.dotnet"
& "src\VoiceToText\bin\Debug\net10.0-windows\VoiceToText.exe" --historytest
Get-Content .\historytest-output.txt
```
Expected: `0 Warning(s)`, `0 Error(s)`, `ALL HISTORY TESTS PASSED`.

- [ ] **Step 6: Commit**

```bash
git add src/VoiceToText/History/HistoryEntry.cs src/VoiceToText/History/HistoryStore.cs src/VoiceToText/Diagnostics/SelfTest.cs src/VoiceToText/Program.cs
git commit -m "feat(history): pure HistoryStore (capped, newest-first) + --historytest"
```

---

## Task 4: `HistoryService` (I/O) + `AppSettings.HistoryEnabled`

**Files:**
- Create: `src/VoiceToText/History/HistoryService.cs`
- Modify: `src/VoiceToText/Settings/AppSettings.cs`

- [ ] **Step 1: Add the `HistoryEnabled` setting**

In `src/VoiceToText/Settings/AppSettings.cs`, add this property immediately after the `HoldToTalk` property (after its closing line ~58):

```csharp
    /// <summary>Keep a local, opt-in log of recent dictations (history.json). Off by default.</summary>
    public bool HistoryEnabled { get; set; } = false;
```

- [ ] **Step 2: Create `HistoryService`**

Create `src/VoiceToText/History/HistoryService.cs` (mirrors `StatsService`):

```csharp
using System.Text.Json;

namespace VoiceToText.History;

/// <summary>
/// Loads/saves the opt-in dictation history (%APPDATA%\VoiceToText\history.json) and records
/// entries. Wraps the pure <see cref="HistoryStore"/>; Record/Clear run on the UI thread.
/// History is non-critical — failures never throw into the dictation path.
/// </summary>
public sealed class HistoryService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private static string HistoryPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "VoiceToText", "history.json");

    public HistoryStore Data { get; private set; } = new();

    /// <summary>Newest-first entries for display.</summary>
    public IReadOnlyList<HistoryEntry> Entries => Data.Entries;

    public HistoryService() => Load();

    private void Load()
    {
        try
        {
            var path = HistoryPath;
            if (File.Exists(path))
            {
                var loaded = JsonSerializer.Deserialize<HistoryStore>(File.ReadAllText(path), JsonOptions);
                if (loaded is not null) Data = loaded;
            }
        }
        catch
        {
            Data = new HistoryStore(); // corrupt/unreadable — start fresh
        }
    }

    private void Save()
    {
        try
        {
            var path = HistoryPath;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(Data, JsonOptions));
        }
        catch
        {
            // non-critical
        }
    }

    /// <summary>Record one dictation and persist. Called only when history is enabled.</summary>
    public void Record(string text, int words, string? app)
    {
        Data.Add(new HistoryEntry
        {
            Time = DateTime.Now,
            App = string.IsNullOrWhiteSpace(app) ? "Unknown" : app!,
            Text = text,
            Words = words,
        });
        Save();
    }

    /// <summary>Erase all stored history.</summary>
    public void Clear()
    {
        Data.Clear();
        Save();
    }
}
```

- [ ] **Step 3: Build to verify it compiles cleanly**

Run: `& "$env:USERPROFILE\.dotnet\dotnet.exe" build VoiceToText.slnx -c Debug --no-incremental`
Expected: `0 Warning(s)`, `0 Error(s)`.

- [ ] **Step 4: Commit**

```bash
git add src/VoiceToText/History/HistoryService.cs src/VoiceToText/Settings/AppSettings.cs
git commit -m "feat(history): HistoryService (history.json) + AppSettings.HistoryEnabled"
```

---

## Task 5: Record history from the tray (opt-in)

**Files:**
- Modify: `src/VoiceToText/App/TrayApplicationContext.cs`

- [ ] **Step 1: Add the `using` and the service field**

In `src/VoiceToText/App/TrayApplicationContext.cs`, add to the `using` block:

```csharp
using VoiceToText.History;
```

Add the field immediately after `private readonly StatsService _stats = new();` (line ~32):

```csharp
    private readonly HistoryService _history = new();
```

- [ ] **Step 2: Record in `StopAndTranscribeAsync`**

In `StopAndTranscribeAsync`, replace the `BeginInvoke` body:

```csharp
                _window.BeginInvoke(() =>
                {
                    var app = NativeForeground.GetForegroundProcessName();
                    _injector.Inject(text);
                    _stats.Record(words, seconds, app);
                });
```

with:

```csharp
                _window.BeginInvoke(() =>
                {
                    var app = NativeForeground.GetForegroundProcessName();
                    _injector.Inject(text);
                    _stats.Record(words, seconds, app);
                    if (_settings.HistoryEnabled)
                        _history.Record(text, words, app);
                });
```

- [ ] **Step 3: Build to verify it compiles cleanly**

Run: `& "$env:USERPROFILE\.dotnet\dotnet.exe" build VoiceToText.slnx -c Debug --no-incremental`
Expected: `0 Warning(s)`, `0 Error(s)`.

- [ ] **Step 4: Commit**

```bash
git add src/VoiceToText/App/TrayApplicationContext.cs
git commit -m "feat(history): record each dictation to history when enabled"
```

---

## Task 6: `HistoryEnabled` checkbox in Settings

**Files:**
- Modify: `src/VoiceToText/Dashboard/SettingsPage.cs`

Adds one checkbox below "Show on-screen indicator…" and shifts every control below it down by 32px.

- [ ] **Step 1: Add the checkbox field and shift the field-initializer Locations**

In `src/VoiceToText/Dashboard/SettingsPage.cs`, add the new field immediately after the `_overlayCheck` field declaration (line ~26):

```csharp
    private readonly CheckBox _historyCheck = new() { Text = "Save recent dictation history (kept only on this PC)", AutoSize = true, Location = new Point(20, 376), ForeColor = Theme.TextPrimary };
```

In the field initializers, change these three `Location` Y-values (+32 each):
- `_autoUpdateCheck`: `Location = new Point(20, 412)` → `Location = new Point(20, 444)`
- `_startupCheck`: `Location = new Point(20, 512)` → `Location = new Point(20, 544)`
- `_savedLabel`: `Location = new Point(126, 558)` → `Location = new Point(126, 590)`

- [ ] **Step 2: Shift the `BuildUi` coordinates (wpm block and below, +32)**

In `BuildUi`, apply these exact replacements:

```
wpmLabel:        Location = new Point(20, 376)   → new Point(20, 408)
_wpmUpDown:      _wpmUpDown.SetBounds(108, 374, 60, 24)   → _wpmUpDown.SetBounds(108, 406, 60, 24)
wpmSuffix:       Location = new Point(176, 376)  → new Point(176, 408)
updateFolderLabel: Location = new Point(20, 446) → new Point(20, 478)
_updateFolderBox: _updateFolderBox.SetBounds(110, 444, 280, 24) → _updateFolderBox.SetBounds(110, 476, 280, 24)
browseButton:    Location = new Point(396, 443)  → new Point(396, 475)
updateNote:      Location = new Point(20, 474)   → new Point(20, 506)
saveButton:      Location = new Point(20, 552)   → new Point(20, 584)
```

(`_autoStopCheck` at 282, the stop-after row at ~306–308, and `_overlayCheck` at 342 are unchanged; the new `_historyCheck` sits at 376, and everything from wpm down moves +32.)

- [ ] **Step 3: Register the checkbox in `Controls.AddRange`**

In `BuildUi`, in the `Controls.AddRange(...)` call, add `_historyCheck` to the list — change the line:

```csharp
            _overlayCheck, wpmLabel, _wpmUpDown, wpmSuffix,
```

to:

```csharp
            _overlayCheck, _historyCheck, wpmLabel, _wpmUpDown, wpmSuffix,
```

- [ ] **Step 4: Load and save the value**

In `LoadFromSettings`, add after `_overlayCheck.Checked = _settings.ShowOverlay;`:

```csharp
        _historyCheck.Checked = _settings.HistoryEnabled;
```

In `OnSave`, add after `_settings.ShowOverlay = _overlayCheck.Checked;`:

```csharp
        _settings.HistoryEnabled = _historyCheck.Checked;
```

- [ ] **Step 5: Build + `--dashwindow` smoke (Settings page constructs/paints)**

```powershell
& "$env:USERPROFILE\.dotnet\dotnet.exe" build VoiceToText.slnx -c Debug --no-incremental
$env:DOTNET_ROOT = "$env:USERPROFILE\.dotnet"
& "src\VoiceToText\bin\Debug\net10.0-windows\VoiceToText.exe" --dashwindow
Get-Content .\dashwindow-output.txt
```
Expected: `0 Warning(s)`, `0 Error(s)`, `DASH WINDOW OK`.

- [ ] **Step 6: Commit**

```bash
git add src/VoiceToText/Dashboard/SettingsPage.cs
git commit -m "feat(settings): opt-in 'Save recent dictation history' checkbox"
```

---

## Task 7: `HistoryPage` UserControl

**Files:**
- Create: `src/VoiceToText/Dashboard/HistoryPage.cs`

Not yet wired into the window (Task 8). Builds standalone.

- [ ] **Step 1: Create `HistoryPage.cs`**

Create `src/VoiceToText/Dashboard/HistoryPage.cs`:

```csharp
using System.Drawing;
using VoiceToText.History;
using VoiceToText.Settings;

namespace VoiceToText.Dashboard;

/// <summary>
/// The History page: a scrollable, newest-first list of recent dictations (opt-in). Each row
/// shows time · app · word-count and the text, with a Copy action; Clear all wipes the log.
/// Shows an off/empty message when history is disabled or empty. Reloads when shown.
/// </summary>
internal sealed class HistoryPage : UserControl
{
    private static readonly Font HeadingFont = new("Segoe UI", 14f, FontStyle.Bold);

    private readonly HistoryService _history;
    private readonly AppSettings _settings;

    private readonly Label _title = new()
    {
        Text = "History",
        AutoSize = true,
        Location = new Point(20, 16),
        ForeColor = Theme.TextPrimary,
        Font = HeadingFont,
    };
    private readonly Label _subtitle = new()
    {
        AutoSize = true,
        Location = new Point(20, 48),
        ForeColor = Theme.TextSecondary,
        Font = Theme.Caption,
        Text = "Your last 50 dictations, kept only on this PC.",
    };
    private readonly Button _clear = new()
    {
        Text = "Clear all",
        FlatStyle = FlatStyle.Flat,
        Size = new Size(80, 26),
        BackColor = Theme.CardBg,
        ForeColor = Theme.TextSecondary,
        Font = Theme.Caption,
        TabStop = false,
    };
    private readonly FlowLayoutPanel _list = new()
    {
        FlowDirection = FlowDirection.TopDown,
        WrapContents = false,
        AutoScroll = true,
        BackColor = Theme.WindowBg,
    };
    private readonly Label _empty = new()
    {
        AutoSize = false,
        TextAlign = ContentAlignment.MiddleCenter,
        ForeColor = Theme.TextSecondary,
        Font = Theme.Empty,
        BackColor = Theme.WindowBg,
        Visible = false,
    };

    public HistoryPage(HistoryService history, AppSettings settings)
    {
        _history = history;
        _settings = settings;
        BackColor = Theme.WindowBg;
        DoubleBuffered = true;
        _clear.FlatAppearance.BorderColor = Theme.CardBorder;
        _clear.Click += OnClear;
        Controls.AddRange(new Control[] { _title, _subtitle, _clear, _list, _empty });
    }

    protected override void OnVisibleChanged(EventArgs e)
    {
        base.OnVisibleChanged(e);
        if (Visible) Reload();
    }

    protected override void OnSizeChanged(EventArgs e)
    {
        base.OnSizeChanged(e);
        DoLayout();
    }

    /// <summary>Rebuild the list from the current settings + stored entries.</summary>
    public void Reload()
    {
        DoLayout();

        _list.SuspendLayout();
        _list.Controls.Clear();
        var entries = _settings.HistoryEnabled ? _history.Entries : Array.Empty<HistoryEntry>();
        foreach (var entry in entries)
            _list.Controls.Add(BuildRow(entry));
        _list.ResumeLayout();

        bool any = _list.Controls.Count > 0;
        _list.Visible = any;
        _empty.Visible = !any;
        _empty.Text = _settings.HistoryEnabled
            ? "No dictations recorded yet."
            : "History is off — enable it in Settings to keep your recent dictations.";
        _clear.Enabled = any;
    }

    private void OnClear(object? sender, EventArgs e)
    {
        if (_history.Entries.Count == 0) return;
        if (MessageBox.Show(this, "Erase all recorded dictation history?", "Voice to Text",
                MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
            return;
        _history.Clear();
        Reload();
    }

    private int RowWidth() => Math.Max(40, _list.ClientSize.Width - 6);

    private void DoLayout()
    {
        const int pad = 20;
        int w = Width - pad * 2;
        if (w <= 0 || Height <= 0) return;

        _clear.Location = new Point(Width - pad - _clear.Width, 16);
        int top = 78;
        _list.SetBounds(pad, top, w, Height - top - pad);
        _empty.SetBounds(pad, top, w, Height - top - pad);

        foreach (Control c in _list.Controls)
            if (c is Panel p) LayoutRow(p);
    }

    private Control BuildRow(HistoryEntry entry)
    {
        var card = new Panel
        {
            BackColor = Theme.CardBg,
            Margin = new Padding(0, 0, 0, 8),
            Width = RowWidth(),
        };

        var meta = new Label
        {
            AutoSize = true,
            Location = new Point(12, 8),
            ForeColor = Theme.TextSecondary,
            Font = Theme.Caption,
            Text = $"{FormatTime(entry.Time)}   ·   {entry.App}   ·   {entry.Words} words",
        };

        var copy = new LinkLabel
        {
            AutoSize = true,
            Text = "Copy",
            Font = Theme.Caption,
            LinkColor = Theme.Accent,
            ActiveLinkColor = Theme.AccentLight,
        };
        copy.LinkClicked += (_, _) => CopyText(entry.Text);

        var body = new Label
        {
            AutoSize = true,
            Location = new Point(12, 28),
            ForeColor = Theme.TextPrimary,
            Text = string.IsNullOrEmpty(entry.Text) ? "(empty)" : entry.Text,
        };

        card.Controls.Add(meta);
        card.Controls.Add(copy);
        card.Controls.Add(body);
        card.Tag = body;
        LayoutRow(card);
        return card;
    }

    // Width-track + height-to-wrapped-text for one row; place Copy at the row's top-right.
    private void LayoutRow(Panel card)
    {
        card.Width = RowWidth();
        var body = (Label)card.Tag!;
        body.MaximumSize = new Size(Math.Max(40, card.Width - 24), 0);
        card.Height = body.Top + body.PreferredHeight + 12;
        foreach (Control c in card.Controls)
            if (c is LinkLabel link)
                link.Location = new Point(card.Width - link.PreferredWidth - 14, 8);
    }

    private static string FormatTime(DateTime t)
    {
        var today = DateTime.Today;
        if (t.Date == today) return $"Today {t:HH:mm}";
        if (t.Date == today.AddDays(-1)) return $"Yesterday {t:HH:mm}";
        return t.ToString("MMM d, HH:mm");
    }

    private static void CopyText(string text)
    {
        try { if (!string.IsNullOrEmpty(text)) Clipboard.SetText(text); }
        catch { /* clipboard contention — best effort */ }
    }
}
```

- [ ] **Step 2: Build to verify it compiles cleanly**

Run: `& "$env:USERPROFILE\.dotnet\dotnet.exe" build VoiceToText.slnx -c Debug --no-incremental`
Expected: `0 Warning(s)`, `0 Error(s)`. (If WFO1000 fires on any member, it means a public property slipped in — all members here are private/static, so it should not.)

- [ ] **Step 3: Commit**

```bash
git add src/VoiceToText/Dashboard/HistoryPage.cs
git commit -m "feat(history): HistoryPage list view (copy + clear-all + off/empty state)"
```

---

## Task 8: Wire `HistoryPage` into the window + extend `--dashwindow`

**Files:**
- Modify: `src/VoiceToText/Dashboard/DashboardForm.cs`
- Modify: `src/VoiceToText/App/TrayApplicationContext.cs` (ctor call)
- Modify: `src/VoiceToText/Diagnostics/SelfTest.cs` (`RunDashWindow`)

- [ ] **Step 1: Add the page kind, nav button, and page to `DashboardForm`**

In `src/VoiceToText/Dashboard/DashboardForm.cs`:

Change the enum:

```csharp
internal enum DashboardPageKind { Dashboard, Settings, TextRules }
```

to:

```csharp
internal enum DashboardPageKind { Dashboard, Settings, TextRules, History }
```

Add the nav button field after `_navTextRules`:

```csharp
    private readonly NavButton _navHistory = new("History") { Dock = DockStyle.Top };
```

Add the page field after `_textRulesPage`:

```csharp
    private readonly HistoryPage _historyPage;
```

Change the constructor signature:

```csharp
    public DashboardForm(AppSettings settings, StatsService stats, string versionLabel)
```

to:

```csharp
    public DashboardForm(AppSettings settings, StatsService stats, HistoryService history, string versionLabel)
```

and add the page construction inside the constructor, just after the `_textRulesPage = new TextRulesPage(...)` line:

```csharp
        _historyPage = new HistoryPage(history, settings) { Dock = DockStyle.Fill, Visible = false };
```

Add the `using` at the top of the file:

```csharp
using VoiceToText.History;
```

- [ ] **Step 2: Register the page + nav in `BuildUi`**

In `BuildUi`, add the history page to the content host — change:

```csharp
        _content.Controls.Add(_textRulesPage);
        _content.Controls.Add(_settingsPage);
        _content.Controls.Add(_dashboardPage);
```

to:

```csharp
        _content.Controls.Add(_historyPage);
        _content.Controls.Add(_textRulesPage);
        _content.Controls.Add(_settingsPage);
        _content.Controls.Add(_dashboardPage);
```

Add the nav click handler next to the others:

```csharp
        _navHistory.Click += (_, _) => ShowPage(DashboardPageKind.History);
```

Add the nav button to the sidebar **first** (Dock.Top stacks in reverse add-order, so the first added sits lowest → History at the bottom). Change:

```csharp
        _sidebar.Controls.Add(_navTextRules);
        _sidebar.Controls.Add(_navSettings);
        _sidebar.Controls.Add(_navDashboard);
        _sidebar.Controls.Add(brand);
        _sidebar.Controls.Add(version);
```

to:

```csharp
        _sidebar.Controls.Add(_navHistory);
        _sidebar.Controls.Add(_navTextRules);
        _sidebar.Controls.Add(_navSettings);
        _sidebar.Controls.Add(_navDashboard);
        _sidebar.Controls.Add(brand);
        _sidebar.Controls.Add(version);
```

- [ ] **Step 3: Handle the page in `ShowPage` and `SetActiveStyles`**

In `ShowPage`, add after `_textRulesPage.Visible = page == DashboardPageKind.TextRules;`:

```csharp
        _historyPage.Visible = page == DashboardPageKind.History;
```

In `SetActiveStyles`, add after `_navTextRules.Active = _active == DashboardPageKind.TextRules;`:

```csharp
        _navHistory.Active = _active == DashboardPageKind.History;
```

(The History page reloads itself via `OnVisibleChanged` when it becomes visible — no extra call needed.)

- [ ] **Step 4: Update the `DashboardForm` constructor calls**

In `src/VoiceToText/App/TrayApplicationContext.cs`, in `ShowDashboard`, change:

```csharp
            _dashboard = new DashboardForm(_settings, _stats, VersionLabel);
```

to:

```csharp
            _dashboard = new DashboardForm(_settings, _stats, _history, VersionLabel);
```

In `src/VoiceToText/Diagnostics/SelfTest.cs`, in `RunDashWindow`, change:

```csharp
            var settings = AppSettings.Load();
            var stats = new StatsService();
            using var form = new DashboardForm(settings, stats, "v-smoketest");
```

to:

```csharp
            var settings = AppSettings.Load();
            var stats = new StatsService();
            var history = new HistoryService();
            using var form = new DashboardForm(settings, stats, history, "v-smoketest");
```

- [ ] **Step 5: Exercise the History page in `RunDashWindow`**

In `RunDashWindow`, after the Text rules block:

```csharp
            form.ShowPage(DashboardPageKind.TextRules);
            Application.DoEvents();
            form.Refresh();           // synchronous WM_PAINT for the Text rules page (grid + preview)
            Application.DoEvents();
```

insert:

```csharp
            form.ShowPage(DashboardPageKind.History);
            Application.DoEvents();
            form.Refresh();           // synchronous WM_PAINT for the History page (list + empty state)
            Application.DoEvents();
```

- [ ] **Step 6: Build + run `--dashwindow`, `--dashtest`, `--historytest`**

```powershell
& "$env:USERPROFILE\.dotnet\dotnet.exe" build VoiceToText.slnx -c Debug --no-incremental
$env:DOTNET_ROOT = "$env:USERPROFILE\.dotnet"
& "src\VoiceToText\bin\Debug\net10.0-windows\VoiceToText.exe" --dashwindow ; Get-Content .\dashwindow-output.txt
& "src\VoiceToText\bin\Debug\net10.0-windows\VoiceToText.exe" --dashtest    ; Get-Content .\dashtest-output.txt
& "src\VoiceToText\bin\Debug\net10.0-windows\VoiceToText.exe" --historytest ; Get-Content .\historytest-output.txt
```
Expected: `0 Warning(s)`, `0 Error(s)`; `DASH WINDOW OK`; `ALL DASH TESTS PASSED`; `ALL HISTORY TESTS PASSED`.

- [ ] **Step 7: Commit**

```bash
git add src/VoiceToText/Dashboard/DashboardForm.cs src/VoiceToText/App/TrayApplicationContext.cs src/VoiceToText/Diagnostics/SelfTest.cs
git commit -m "feat(history): add History sidebar page to the dashboard window"
```

---

## Task 9: Ship v0.6.8 to the update feed — FOREGROUND ONLY

**Files:**
- Modify: `src/VoiceToText/VoiceToText.csproj`
- Modify (out-of-repo): `D:\ClaudeCode\VoiceToText-Releases\` (setup exe + `latest.json`)

> **Must run from the foreground session.** Out-of-repo writes from an isolated subagent do not persist, so the feed must be populated in the foreground. This packages an installer and runs it from a trusted folder.

- [ ] **Step 1: Manual run-through before shipping**

Launch the built app (`src\VoiceToText\bin\Debug\net10.0-windows\VoiceToText.exe`) and verify:
- Dashboard chart toggle: `7` / `30` / `All` change the bars, the active segment highlights, and the title reads "last 7 days" / "last 30 days" / "all time".
- Settings → check **Save recent dictation history**, Save. Dictate a few times. Open **History**: entries appear newest-first with time · app · word-count + text; **Copy** copies the text; **Clear all** prompts then empties.
- Uncheck history in Settings, Save → no new entries are recorded; the History page shows the "off" message once cleared (existing entries persist until Clear all).

- [ ] **Step 2: Bump the version**

In `src/VoiceToText/VoiceToText.csproj`, set:

```xml
<Version>0.6.8</Version>
```

- [ ] **Step 3: Publish, package, and populate the feed**

Run the standing ship procedure from the repo root (PowerShell, foreground):

```powershell
.\publish.ps1
& "C:\Users\Luke\.claude\jobs\f39a9536\tmp\innosetup\tools\ISCC.exe" installer\VoiceToText.iss
Copy-Item installer\Output\VoiceToText-Setup.exe "D:\ClaudeCode\VoiceToText-Releases\VoiceToText-Setup-0.6.8.exe" -Force
```

Then write `D:\ClaudeCode\VoiceToText-Releases\latest.json` with:
- `Version`: `0.6.8`
- `SetupFileName`: `VoiceToText-Setup-0.6.8.exe`
- `Sha256`: lowercase hex of the copied setup exe — `(Get-FileHash "D:\ClaudeCode\VoiceToText-Releases\VoiceToText-Setup-0.6.8.exe" -Algorithm SHA256).Hash.ToLower()`
- `ReleaseNotes`: "Activity chart 7/30/All range toggle; opt-in recent-dictation history (new History page) with copy + clear-all."
- `Mandatory`: `false`
- `ReleasedUtc`: current UTC timestamp

- [ ] **Step 4: Commit the version bump**

```bash
git add src/VoiceToText/VoiceToText.csproj
git commit -m "v0.6.8: dashboard chart range toggle + opt-in dictation history"
```

- [ ] **Step 5: Verify the feed**

```powershell
& "src\VoiceToText\bin\Debug\net10.0-windows\VoiceToText.exe" --updatecheck "D:\ClaudeCode\VoiceToText-Releases"
Get-Content .\updatecheck-output.txt
```
Expected: the simulated-feed section passes and the manifest for 0.6.8 validates (SHA matches). Confirm the running app's tray "Check for updates" offers 0.6.8.

---

## Notes on testing strategy

- **Pure logic** (`Activity(range)`, `HistoryStore`) is covered by `--dashtest` and `--historytest` — deterministic, no UI/mic/disk.
- **UI** (chart toggle, Settings checkbox re-flow, History page) is covered by the `--dashwindow` smoke (construct + show every page + force WM_PAINT) plus the Task 9 manual run-through. The History smoke exercises the off/empty path (default settings have history disabled); rows are verified manually.
- **No new `--historytest`-style test for `HistoryService`/tray recording**: those are thin I/O + UI-thread glue mirroring `StatsService`, with the cap/order logic already proven in `HistoryStore`.
