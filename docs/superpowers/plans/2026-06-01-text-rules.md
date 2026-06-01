# Text Rules Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a local post-transcription "text rules" layer (custom replacements + spoken formatting commands) with a new dashboard editor page.

**Architecture:** A pure `TextRules.Apply` transform runs on Whisper's output before paste; rules persist in `settings.json`; a `TextRulesPage` (3rd sidebar page) edits them with a live preview.

**Tech Stack:** C# / .NET 10 (`net10.0-windows`), WinForms (`DataGridView`), `System.Text.RegularExpressions`.

---

## Conventions (read first)

**Background-isolation guard: the `Write`/`Edit` tools cannot modify repo files.** Create/replace a repo file by `Write`-ing to `C:\Users\Luke\.claude\jobs\f39a9536\tmp\stage\<filename>` then Bash `cp "C:/Users/Luke/.claude/jobs/f39a9536/tmp/stage/<filename>" "<repo path>"`. For surgical edits use Bash `perl -0pi -e '...'`. Commits via Bash `git` on `main`.

**Build (PowerShell tool) — always clean (`--no-incremental`) so warnings surface:**
```powershell
& "$env:USERPROFILE/.dotnet/dotnet.exe" build VoiceToText.slnx -c Debug --no-incremental
```
Expected: `0 Warning(s)`, `0 Error(s)`. (.NET 10 WFO1000 errors on a *new public property* on a `Control` subclass — none added here. `TextRulesPage` exposes only private fields + methods.)

**Run a self-test** (WinExe writes to a file), from repo root `D:\ClaudeCode\voice-to-text`:
```powershell
$env:DOTNET_ROOT = "$env:USERPROFILE/.dotnet"
$exe = "src/VoiceToText/bin/Debug/net10.0-windows/VoiceToText.exe"
Start-Process $exe -ArgumentList "--textrulestest" -WorkingDirectory $PWD -Wait ; Get-Content textrulestest-output.txt
Start-Process $exe -ArgumentList "--dashwindow"     -WorkingDirectory $PWD -Wait ; Get-Content dashwindow-output.txt
```

---

## File Structure

**Create**
- `src/VoiceToText/TextProcessing/ReplacementRule.cs` — serializable find/replace rule.
- `src/VoiceToText/TextProcessing/TextRules.cs` — pure `Apply` engine.
- `src/VoiceToText/Dashboard/TextRulesPage.cs` — the editor page.

**Modify**
- `src/VoiceToText/Diagnostics/SelfTest.cs` — `RunTextRulesTest`; extend `RunDashWindow`.
- `src/VoiceToText/Program.cs` — route `--textrulestest`.
- `src/VoiceToText/Settings/AppSettings.cs` — `Replacements` + `SpokenCommandsEnabled`.
- `src/VoiceToText/App/TrayApplicationContext.cs` — apply `TextRules` in `StopAndTranscribeAsync`.
- `src/VoiceToText/Dashboard/DashboardForm.cs` — `TextRules` page kind + sidebar nav + host (full rewrite).
- `src/VoiceToText/VoiceToText.csproj` — `<Version>0.6.5</Version>` (ship task).

---

## Task 1: Pure engine (`TextRules` + `ReplacementRule`) + `--textrulestest` (TDD)

**Files:**
- Create: `src/VoiceToText/TextProcessing/ReplacementRule.cs`, `src/VoiceToText/TextProcessing/TextRules.cs`
- Modify: `src/VoiceToText/Diagnostics/SelfTest.cs`, `src/VoiceToText/Program.cs`

- [ ] **Step 1: Write the failing test + route the arg**

Add `using VoiceToText.TextProcessing;` to the using block of `src/VoiceToText/Diagnostics/SelfTest.cs`, then add this method inside the `SelfTest` class (e.g. after `RunDashTest`):

```csharp
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

        // Spoken commands
        Pass("new line", TextRules.Apply("a new line b", none, true) == "a\nb", Vis(TextRules.Apply("a new line b", none, true)));
        Pass("new paragraph", TextRules.Apply("a new paragraph b", none, true) == "a\n\nb", Vis(TextRules.Apply("a new paragraph b", none, true)));
        Pass("case + punctuation tolerant", TextRules.Apply("a. New line. b", none, true) == "a.\nb", Vis(TextRules.Apply("a. New line. b", none, true)));
        Pass("one-word newline", TextRules.Apply("a newline b", none, true) == "a\nb");
        Pass("commands off => literal", TextRules.Apply("a new line b", none, false) == "a new line b");
        Pass("commands run before replacements", TextRules.Apply("a new line b", R(("line", "LINE")), true) == "a\nb", Vis(TextRules.Apply("a new line b", R(("line", "LINE")), true)));

        // Edges
        Pass("empty unchanged", TextRules.Apply("", none, true) == "");
        Pass("trims output", TextRules.Apply("  hello world  ", none, false) == "hello world");

        log.AppendLine(allPass ? "ALL TEXTRULES TESTS PASSED" : "SOME TEXTRULES TESTS FAILED");
        var result = log.ToString();
        File.WriteAllText(outputPath, result);
        Console.WriteLine(result);
        return allPass ? 0 : 1;
    }
```

Then in `src/VoiceToText/Program.cs`, add this route after the existing `--dashtest` route and add `--textrulestest` to the XML doc comment list:

```csharp
        if (args.Length > 0 && args[0].Equals("--textrulestest", StringComparison.OrdinalIgnoreCase))
            return SelfTest.RunTextRulesTest("textrulestest-output.txt");
```

- [ ] **Step 2: Build to verify it fails**

```powershell
& "$env:USERPROFILE/.dotnet/dotnet.exe" build VoiceToText.slnx -c Debug --no-incremental
```
Expected: FAILS — `CS0246`/`CS0103` for `ReplacementRule` / `TextRules` (and the `VoiceToText.TextProcessing` namespace). Confirms the test references the not-yet-created engine.

- [ ] **Step 3: Create `ReplacementRule.cs`**

Stage → `cp` to `src/VoiceToText/TextProcessing/ReplacementRule.cs`:

```csharp
namespace VoiceToText.TextProcessing;

/// <summary>One find→replace rule, applied to transcribed text before pasting.
/// Mutable for JSON (de)serialization and the editor grid.</summary>
public sealed class ReplacementRule
{
    public string Find { get; set; } = "";
    public string Replace { get; set; } = "";
}
```

- [ ] **Step 4: Create `TextRules.cs`**

Stage → `cp` to `src/VoiceToText/TextProcessing/TextRules.cs`:

```csharp
using System.Text.RegularExpressions;

namespace VoiceToText.TextProcessing;

/// <summary>
/// Pure post-transcription transform: spoken formatting commands first, then custom
/// replacements (case-insensitive, whole-word, verbatim), then trim. No I/O, no UI.
/// </summary>
public static class TextRules
{
    // Surrounding absorption uses [ \t]* (NOT \s*) so a command never eats an adjacent
    // line break produced by another command.
    private static readonly Regex ParagraphCmd =
        new(@"[ \t]*\bnew\s+paragraph\b[.,!?;:]*[ \t]*", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex LineCmd =
        new(@"[ \t]*\b(?:new\s+line|newline)\b[.,!?;:]*[ \t]*", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static string Apply(string text, IReadOnlyList<ReplacementRule>? rules, bool spokenCommands)
    {
        if (string.IsNullOrEmpty(text)) return text;

        var s = text;

        if (spokenCommands)
        {
            s = ParagraphCmd.Replace(s, "\n\n"); // paragraph before line
            s = LineCmd.Replace(s, "\n");
        }

        if (rules is not null)
        {
            foreach (var rule in rules)
            {
                if (rule is null || string.IsNullOrWhiteSpace(rule.Find)) continue;
                // Whole-word via lookarounds (works even when Find starts/ends with a symbol).
                var pattern = @"(?<!\w)" + Regex.Escape(rule.Find) + @"(?!\w)";
                var replacement = rule.Replace ?? "";
                // MatchEvaluator => replacement is literal (no $-group substitution).
                s = Regex.Replace(s, pattern, _ => replacement, RegexOptions.IgnoreCase);
            }
        }

        return s.Trim();
    }
}
```

- [ ] **Step 5: Build (passes) and run the test**

```powershell
& "$env:USERPROFILE/.dotnet/dotnet.exe" build VoiceToText.slnx -c Debug --no-incremental
$env:DOTNET_ROOT = "$env:USERPROFILE/.dotnet"
Start-Process "src/VoiceToText/bin/Debug/net10.0-windows/VoiceToText.exe" -ArgumentList "--textrulestest" -WorkingDirectory $PWD -Wait
Get-Content textrulestest-output.txt
```
Expected: `Build succeeded` (0/0); every line `[PASS]`; final `ALL TEXTRULES TESTS PASSED`. If a line FAILS, fix `TextRules` (not the test).

- [ ] **Step 6: Commit**

```bash
cd "D:/ClaudeCode/voice-to-text" && git add -A && git commit -m "Add pure TextRules engine + --textrulestest" -m "Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

## Task 2: Persist rules in `AppSettings`

**Files:**
- Modify: `src/VoiceToText/Settings/AppSettings.cs`

- [ ] **Step 1: Add the using**

```bash
cd "D:/ClaudeCode/voice-to-text" && perl -0pi -e 's/using VoiceToText\.Hotkeys;\n/using VoiceToText.Hotkeys;\nusing VoiceToText.TextProcessing;\n/' src/VoiceToText/Settings/AppSettings.cs && grep -n "using VoiceToText.TextProcessing;" src/VoiceToText/Settings/AppSettings.cs
```
Expected: prints the new using line.

- [ ] **Step 2: Add the two fields after `ModelType`**

```bash
cd "D:/ClaudeCode/voice-to-text" && perl -0pi -e 's{    public GgmlType ModelType \{ get; set; \} = GgmlType\.LargeV3Turbo;\n}{    public GgmlType ModelType { get; set; } = GgmlType.LargeV3Turbo;\n\n    /// <summary>Custom find\xe2\x86\x92replace rules applied to transcribed text before pasting.</summary>\n    public List<ReplacementRule> Replacements { get; set; } = new();\n\n    /// <summary>Turn spoken "new line"/"new paragraph" into line breaks.</summary>\n    public bool SpokenCommandsEnabled { get; set; } = true;\n}' src/VoiceToText/Settings/AppSettings.cs && grep -n "Replacements\|SpokenCommandsEnabled" src/VoiceToText/Settings/AppSettings.cs
```
Expected: prints the two new property lines. (`List<>` resolves via the SDK implicit `System.Collections.Generic` using.)

- [ ] **Step 3: Build**

```powershell
& "$env:USERPROFILE/.dotnet/dotnet.exe" build VoiceToText.slnx -c Debug --no-incremental
```
Expected: `Build succeeded`, 0/0.

- [ ] **Step 4: Commit**

```bash
cd "D:/ClaudeCode/voice-to-text" && git add -A && git commit -m "AppSettings: persist text replacements + spoken-commands toggle" -m "Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

## Task 3: Apply rules in the dictation path

**Files:**
- Modify: `src/VoiceToText/App/TrayApplicationContext.cs`

- [ ] **Step 1: Add the using**

```bash
cd "D:/ClaudeCode/voice-to-text" && perl -0pi -e 's/using VoiceToText\.Stt;\n/using VoiceToText.Stt;\nusing VoiceToText.TextProcessing;\n/' src/VoiceToText/App/TrayApplicationContext.cs && grep -n "using VoiceToText.TextProcessing;" src/VoiceToText/App/TrayApplicationContext.cs
```
Expected: prints the new using line. (If `using VoiceToText.Stt;` isn't matched, the using block ordering differs — open the file and add `using VoiceToText.TextProcessing;` alongside the other `using VoiceToText.*;` lines.)

- [ ] **Step 2: Apply rules right after transcription**

The current code in `StopAndTranscribeAsync` is:
```csharp
            var samples = await _audio.StopAndGetSamplesAsync().ConfigureAwait(false);
            var text = await _stt.TranscribeAsync(samples).ConfigureAwait(false);

            if (!string.IsNullOrWhiteSpace(text))
```
Insert the rules call between the transcription and the `if`:
```bash
cd "D:/ClaudeCode/voice-to-text" && perl -0pi -e 's{            var text = await _stt\.TranscribeAsync\(samples\)\.ConfigureAwait\(false\);\n\n            if \(!string\.IsNullOrWhiteSpace\(text\)\)}{            var text = await _stt.TranscribeAsync(samples).ConfigureAwait(false);\n            text = TextRules.Apply(text, _settings.Replacements, _settings.SpokenCommandsEnabled);\n\n            if (!string.IsNullOrWhiteSpace(text))}' src/VoiceToText/App/TrayApplicationContext.cs && grep -n "TextRules.Apply" src/VoiceToText/App/TrayApplicationContext.cs
```
Expected: prints the `text = TextRules.Apply(...)` line. (Now the word-count, inject, and stats all use the rule-processed text.)

- [ ] **Step 3: Build + regression test**

```powershell
& "$env:USERPROFILE/.dotnet/dotnet.exe" build VoiceToText.slnx -c Debug --no-incremental
$env:DOTNET_ROOT = "$env:USERPROFILE/.dotnet"
Start-Process "src/VoiceToText/bin/Debug/net10.0-windows/VoiceToText.exe" -ArgumentList "--textrulestest" -WorkingDirectory $PWD -Wait
Get-Content textrulestest-output.txt -Tail 1
```
Expected: `Build succeeded` 0/0; `ALL TEXTRULES TESTS PASSED`.

- [ ] **Step 4: Commit**

```bash
cd "D:/ClaudeCode/voice-to-text" && git add -A && git commit -m "Apply text rules to transcription before paste" -m "Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

## Task 4: `TextRulesPage` editor

**Files:**
- Create: `src/VoiceToText/Dashboard/TextRulesPage.cs`

- [ ] **Step 1: Create the page**

Stage → `cp` to `src/VoiceToText/Dashboard/TextRulesPage.cs`, exact content:

```csharp
using System.Drawing;
using VoiceToText.Settings;
using VoiceToText.TextProcessing;

namespace VoiceToText.Dashboard;

/// <summary>
/// Editor for the text rules applied to every transcription before pasting: a spoken-commands
/// toggle, a grid of find→replace rules, and a live "Try it" preview. Save persists to AppSettings.
/// </summary>
internal sealed class TextRulesPage : UserControl
{
    private readonly AppSettings _settings;
    private readonly CheckBox _commandsCheck = new() { Text = "Turn spoken commands into formatting", AutoSize = true, ForeColor = Theme.TextPrimary };
    private readonly Label _commandsHint = new() { AutoSize = true, ForeColor = Theme.TextSecondary, MaximumSize = new Size(620, 0), Text = "Say \"new line\" or \"new paragraph\" while dictating and they become line breaks instead of literal words." };
    private readonly Label _replacementsLabel = new() { AutoSize = true, ForeColor = Theme.Accent, Text = "REPLACEMENTS", Font = Theme.Caption };
    private readonly DataGridView _grid = new();
    private readonly Label _tryLabel = new() { AutoSize = true, ForeColor = Theme.Accent, Text = "TRY IT", Font = Theme.Caption };
    private readonly TextBox _previewInput = new() { BackColor = Theme.CardBg, ForeColor = Theme.TextPrimary, BorderStyle = BorderStyle.FixedSingle, Text = "i pushed to github new line all tests pass" };
    private readonly TextBox _previewOutput = new() { BackColor = Theme.CardBg, ForeColor = Color.FromArgb(0x9B, 0xE6, 0xA8), BorderStyle = BorderStyle.FixedSingle, ReadOnly = true, Multiline = true };
    private readonly Button _saveButton = new() { Text = "Save", Size = new Size(96, 30), FlatStyle = FlatStyle.Flat, BackColor = Theme.Accent, ForeColor = Color.White };
    private readonly Label _savedLabel = new() { AutoSize = true, ForeColor = Theme.Accent, Visible = false, Text = "Saved ✓" };

    public TextRulesPage(AppSettings settings)
    {
        _settings = settings;
        BackColor = Theme.WindowBg;
        ForeColor = Theme.TextPrimary;
        BuildUi();
        LoadFromSettings();
    }

    private void BuildUi()
    {
        ConfigureGrid();

        _commandsCheck.CheckedChanged += (_, _) => UpdatePreview();
        _grid.CellEndEdit += (_, _) => UpdatePreview();
        _grid.RowsRemoved += (_, _) => UpdatePreview();
        _previewInput.TextChanged += (_, _) => UpdatePreview();

        _saveButton.FlatAppearance.BorderSize = 0;
        _saveButton.FlatAppearance.MouseOverBackColor = Theme.AccentLight;
        _saveButton.Click += OnSave;

        Controls.AddRange(new Control[]
        {
            _commandsCheck, _commandsHint, _replacementsLabel, _grid,
            _tryLabel, _previewInput, _previewOutput, _saveButton, _savedLabel,
        });
    }

    private void ConfigureGrid()
    {
        _grid.AllowUserToAddRows = true;
        _grid.AllowUserToDeleteRows = true;
        _grid.AllowUserToResizeRows = false;
        _grid.RowHeadersWidth = 28;
        _grid.BackgroundColor = Theme.WindowBg;
        _grid.GridColor = Theme.CardBorder;
        _grid.BorderStyle = BorderStyle.None;
        _grid.EnableHeadersVisualStyles = false;
        _grid.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.Single;
        _grid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
        _grid.SelectionMode = DataGridViewSelectionMode.CellSelect;
        _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        _grid.Font = new Font("Segoe UI", 9.5f);

        _grid.ColumnHeadersDefaultCellStyle.BackColor = Theme.CardBg;
        _grid.ColumnHeadersDefaultCellStyle.ForeColor = Theme.TextSecondary;
        _grid.ColumnHeadersDefaultCellStyle.SelectionBackColor = Theme.CardBg;
        _grid.DefaultCellStyle.BackColor = Theme.CardBg;
        _grid.DefaultCellStyle.ForeColor = Theme.TextPrimary;
        _grid.DefaultCellStyle.SelectionBackColor = Theme.NavActiveBg;
        _grid.DefaultCellStyle.SelectionForeColor = Theme.TextPrimary;
        _grid.RowHeadersDefaultCellStyle.BackColor = Theme.CardBg;
        _grid.RowHeadersDefaultCellStyle.SelectionBackColor = Theme.NavActiveBg;

        var find = new DataGridViewTextBoxColumn { HeaderText = "Find (heard)", FillWeight = 50, SortMode = DataGridViewColumnSortMode.NotSortable };
        var replace = new DataGridViewTextBoxColumn { HeaderText = "Replace with", FillWeight = 50, SortMode = DataGridViewColumnSortMode.NotSortable };
        _grid.Columns.AddRange(find, replace);
    }

    protected override void OnVisibleChanged(EventArgs e)
    {
        base.OnVisibleChanged(e);
        if (Visible) _savedLabel.Visible = false;
    }

    private void LoadFromSettings()
    {
        _commandsCheck.Checked = _settings.SpokenCommandsEnabled;
        _grid.Rows.Clear();
        foreach (var rule in _settings.Replacements)
            _grid.Rows.Add(rule.Find, rule.Replace);
        UpdatePreview();
    }

    private List<ReplacementRule> GatherRules()
    {
        var rules = new List<ReplacementRule>();
        foreach (DataGridViewRow row in _grid.Rows)
        {
            if (row.IsNewRow) continue;
            var find = row.Cells[0].Value?.ToString() ?? "";
            var replace = row.Cells[1].Value?.ToString() ?? "";
            if (string.IsNullOrWhiteSpace(find)) continue;
            rules.Add(new ReplacementRule { Find = find, Replace = replace });
        }
        return rules;
    }

    private void UpdatePreview()
        => _previewOutput.Text = TextRules.Apply(_previewInput.Text, GatherRules(), _commandsCheck.Checked);

    private void OnSave(object? sender, EventArgs e)
    {
        _settings.Replacements = GatherRules();
        _settings.SpokenCommandsEnabled = _commandsCheck.Checked;
        _settings.Save();
        _savedLabel.Visible = true;
    }

    protected override void OnSizeChanged(EventArgs e)
    {
        base.OnSizeChanged(e);
        DoLayout();
    }

    private void DoLayout()
    {
        const int pad = 20;
        int w = Width - pad * 2;
        if (w <= 0 || Height <= 0) return;

        int y = pad;
        _commandsCheck.Location = new Point(pad, y); y += 26;
        _commandsHint.Location = new Point(pad, y); y += 34;
        _replacementsLabel.Location = new Point(pad, y); y += 22;

        // Bottom block (try-it + save) is fixed height; the grid fills the space between.
        const int tryLabelH = 22, inputH = 24, outputH = 56, saveH = 30, gap = 8;
        int saveY = Height - pad - saveH;
        int outputY = saveY - 14 - outputH;
        int inputY = outputY - gap - inputH;
        int tryY = inputY - tryLabelH;

        int gridTop = y;
        int gridH = Math.Max(120, tryY - gridTop - 12);
        _grid.SetBounds(pad, gridTop, w, gridH);

        _tryLabel.Location = new Point(pad, tryY);
        _previewInput.SetBounds(pad, inputY, w, inputH);
        _previewOutput.SetBounds(pad, outputY, w, outputH);
        _saveButton.Location = new Point(pad, saveY);
        _savedLabel.Location = new Point(pad + 106, saveY + 6);
    }
}
```

- [ ] **Step 2: Build** (it isn't shown anywhere yet, but must compile)

```powershell
& "$env:USERPROFILE/.dotnet/dotnet.exe" build VoiceToText.slnx -c Debug --no-incremental
```
Expected: `Build succeeded`, 0/0.

- [ ] **Step 3: Commit**

```bash
cd "D:/ClaudeCode/voice-to-text" && git add -A && git commit -m "Add TextRulesPage editor (grid + live preview)" -m "Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

## Task 5: Wire the page into the dashboard + extend the smoke test

**Files:**
- Modify (full replace): `src/VoiceToText/Dashboard/DashboardForm.cs`
- Modify: `src/VoiceToText/Diagnostics/SelfTest.cs` (`RunDashWindow`)

- [ ] **Step 1: Replace `DashboardForm.cs`**

Stage → `cp` to `src/VoiceToText/Dashboard/DashboardForm.cs`, exact content (adds the `TextRules` page kind, a "Text rules" nav button placed below Settings, the page in the content host, and the toggles in `ShowPage`/`SetActiveStyles`):

```csharp
using System.Drawing;
using VoiceToText.Settings;
using VoiceToText.Stats;

namespace VoiceToText.Dashboard;

internal enum DashboardPageKind { Dashboard, Settings, TextRules }

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
    private readonly NavButton _navTextRules = new("Text rules") { Dock = DockStyle.Top };
    private readonly DashboardPage _dashboardPage = new() { Dock = DockStyle.Fill };
    private readonly SettingsPage _settingsPage;
    private readonly TextRulesPage _textRulesPage;
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
        _textRulesPage = new TextRulesPage(settings) { Dock = DockStyle.Fill, Visible = false };
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

        _content.Controls.Add(_textRulesPage);
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
        _navTextRules.Click += (_, _) => ShowPage(DashboardPageKind.TextRules);

        // Dock.Top stacks in reverse add-order, so the last added sits highest.
        // Added first => lowest, giving visual order: brand, Dashboard, Settings, Text rules.
        _sidebar.Controls.Add(_navTextRules);
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
        _textRulesPage.Visible = page == DashboardPageKind.TextRules;
        SetActiveStyles();
    }

    private void SetActiveStyles()
    {
        _navDashboard.Active = _active == DashboardPageKind.Dashboard;
        _navSettings.Active = _active == DashboardPageKind.Settings;
        _navTextRules.Active = _active == DashboardPageKind.TextRules;
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

- [ ] **Step 2: Extend `RunDashWindow` to paint the Text rules page**

In `src/VoiceToText/Diagnostics/SelfTest.cs`, the `RunDashWindow` method shows the Settings then Dashboard pages, then calls `form.Close();`. Insert a Text-rules paint pass before `form.Close();`:
```bash
cd "D:/ClaudeCode/voice-to-text" && perl -0pi -e 's{            form\.ShowPage\(DashboardPageKind\.Dashboard\);\n            Application\.DoEvents\(\);\n            form\.Refresh\(\);           // synchronous WM_PAINT for the Dashboard page \(hero/tiles/chart/apps\)\n            Application\.DoEvents\(\);\n}{            form.ShowPage(DashboardPageKind.Dashboard);\n            Application.DoEvents();\n            form.Refresh();           // synchronous WM_PAINT for the Dashboard page (hero/tiles/chart/apps)\n            Application.DoEvents();\n\n            form.ShowPage(DashboardPageKind.TextRules);\n            Application.DoEvents();\n            form.Refresh();           // synchronous WM_PAINT for the Text rules page (grid + preview)\n            Application.DoEvents();\n}' src/VoiceToText/Diagnostics/SelfTest.cs && grep -n "Text rules page" src/VoiceToText/Diagnostics/SelfTest.cs
```
Expected: prints the new comment line.

- [ ] **Step 3: Clean build**

```powershell
& "$env:USERPROFILE/.dotnet/dotnet.exe" build VoiceToText.slnx -c Debug --no-incremental
```
Expected: `Build succeeded`, 0/0.

- [ ] **Step 4: Smoke + engine regression**

```powershell
$env:DOTNET_ROOT = "$env:USERPROFILE/.dotnet"
$exe = "src/VoiceToText/bin/Debug/net10.0-windows/VoiceToText.exe"
Start-Process $exe -ArgumentList "--dashwindow"     -WorkingDirectory $PWD -Wait ; Get-Content dashwindow-output.txt
Start-Process $exe -ArgumentList "--textrulestest"  -WorkingDirectory $PWD -Wait ; Get-Content textrulestest-output.txt -Tail 1
```
Expected: `DASH WINDOW OK ...` (now also painting the Text rules page) and `ALL TEXTRULES TESTS PASSED`. If `--dashwindow` prints `ERROR: ...`, that's a real construction/paint bug in `TextRulesPage` (e.g. the DataGridView) — fix before committing.

- [ ] **Step 5: Commit**

```bash
cd "D:/ClaudeCode/voice-to-text" && git add -A && git commit -m "Add Text rules sidebar page; paint it in --dashwindow smoke" -m "Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

## Task 6: Manual verification

**Files:** none.

- [ ] **Step 1: Build and launch**

```powershell
& "$env:USERPROFILE/.dotnet/dotnet.exe" build VoiceToText.slnx -c Debug --no-incremental
$env:DOTNET_ROOT = "$env:USERPROFILE/.dotnet"
Start-Process "src/VoiceToText/bin/Debug/net10.0-windows/VoiceToText.exe"
```

- [ ] **Step 2: Checklist**
  - Tray → Open Dashboard → the sidebar shows **Dashboard / Settings / Text rules**; clicking Text rules opens the dark page (toggle + grid + Try-it + Save). No white/un-themed controls except the grid's editable cells (acceptable).
  - Add a rule `github` → `GitHub`; in **Try it**, type `i use github new line done` → output shows `i use GitHub` then a line break then `done`, updating live as you type and as you edit the grid / toggle the checkbox.
  - **Save** → "Saved ✓"; confirm `%APPDATA%\VoiceToText\settings.json` now has `Replacements` + `SpokenCommandsEnabled`.
  - Dictate "testing github new line done" into any app → the paste reads `testing GitHub`⏎`done`. Toggle spoken commands off, Save, dictate again → "new line" stays literal.
  - Resize the window — grid stretches, Try-it/Save stay anchored at the bottom.

- [ ] **Step 3: Record the result.** Fix any issue in the relevant file (stage→cp / perl), clean-build, re-verify, commit.

---

## Task 7: Ship v0.6.5

**Files:**
- Modify: `src/VoiceToText/VoiceToText.csproj`
- Write (outside repo): the feed.

**Feed population MUST run in the foreground session** (isolated subagent out-of-repo writes don't persist).

- [ ] **Step 1: Bump version**

```bash
cd "D:/ClaudeCode/voice-to-text" && perl -0pi -e 's{<Version>0\.6\.4</Version>}{<Version>0.6.5</Version>}' src/VoiceToText/VoiceToText.csproj && grep -n "<Version>" src/VoiceToText/VoiceToText.csproj
```
Expected: `<Version>0.6.5</Version>`.

- [ ] **Step 2: Publish + installer**

```powershell
& "D:/ClaudeCode/voice-to-text/publish.ps1" | Select-Object -Last 1
(Get-Item "D:/ClaudeCode/voice-to-text/publish/VoiceToText.exe").VersionInfo.ProductVersion   # expect 0.6.5
& "C:/Users/Luke/.claude/jobs/f39a9536/tmp/innosetup/tools/ISCC.exe" "D:/ClaudeCode/voice-to-text/installer/VoiceToText.iss" | Select-Object -Last 2
Test-Path "D:/ClaudeCode/voice-to-text/installer/Output/VoiceToText-Setup.exe"   # expect True
```

- [ ] **Step 3: Copy to feed + write latest.json (Bash, foreground)**

```bash
cd "D:/ClaudeCode/voice-to-text"
FEED="D:/ClaudeCode/VoiceToText-Releases"
cp "installer/Output/VoiceToText-Setup.exe" "$FEED/VoiceToText-Setup-0.6.5.exe"
SHA=$(sha256sum "$FEED/VoiceToText-Setup-0.6.5.exe" | cut -d' ' -f1)
TS=$(date -u +"%Y-%m-%dT%H:%M:%SZ")
cat > "$FEED/latest.json" <<EOF
{
    "Version": "0.6.5",
    "SetupFileName": "VoiceToText-Setup-0.6.5.exe",
    "Sha256": "$SHA",
    "ReleaseNotes": "New Text rules page: custom word replacements + spoken 'new line'/'new paragraph' commands, applied to every dictation.",
    "Mandatory": false,
    "ReleasedUtc": "$TS"
}
EOF
cat "$FEED/latest.json"; echo "verify:"; sha256sum "$FEED/VoiceToText-Setup-0.6.5.exe" | cut -d' ' -f1
```
Expected: `latest.json` shows `"Version": "0.6.5"` and the printed SHA matches its `Sha256`.

- [ ] **Step 4: Commit**

```bash
cd "D:/ClaudeCode/voice-to-text" && git add -A && git commit -m "v0.6.5: text rules (replacements + spoken commands)" -m "Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

## Self-Review

**1. Spec coverage:**
- Pure `TextRules.Apply` (commands-first, `[ \t]*`-bounded command regexes, lookaround whole-word replacements, verbatim via MatchEvaluator, trim) → Task 1. ✓
- `ReplacementRule` type → Task 1. ✓
- `--textrulestest` + Program route → Task 1. ✓
- Storage in settings.json → Task 2. ✓
- Integration in `StopAndTranscribeAsync` before check/count/inject/stats → Task 3. ✓
- `TextRulesPage` (toggle, dark DataGridView, live preview, Save→`_settings.Save()`, saved-label cleared OnVisibleChanged, load on construct) → Task 4. ✓
- Sidebar 3rd page + `DashboardPageKind.TextRules` → Task 5. ✓
- `RunDashWindow` paints the new page → Task 5. ✓
- Ship v0.6.5 from foreground → Task 7. ✓

**2. Placeholder scan:** No TBD/TODO; every step has full code/commands + expected output.

**3. Type consistency:** `TextRules.Apply(string, IReadOnlyList<ReplacementRule>?, bool)`, `ReplacementRule { Find, Replace }`, `DashboardPageKind.TextRules`, `TextRulesPage(AppSettings)`, `_settings.Replacements`/`SpokenCommandsEnabled`, `_navTextRules`/`_textRulesPage` are used identically across Tasks 1–5 and match the current code read from the repo (DashboardForm, AppSettings, RunDashWindow, StopAndTranscribeAsync, Program). The page declares no new public Control property (no WFO1000).

**Note on the spec's `\s*` → plan's `[ \t]*`:** the command regexes use `[ \t]*` for surrounding-whitespace absorption (not `\s*`) so a command can't swallow a line break created by the other command. This realises the spec's intent ("no double spaces/stray punctuation") more safely; the `--textrulestest` cases pin the behaviour.
