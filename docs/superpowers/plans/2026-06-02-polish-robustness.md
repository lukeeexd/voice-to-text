# Polish & Robustness Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add logging, a crash handler, friendlier error surfacing, a dark title bar, an unsaved-changes affordance in Settings, and an About/diagnostics page — shipped as v0.7.0.

**Architecture:** A dependency-free `Log` (rolling file, metadata-only) underpins a global crash handler and visible error surfacing across the audio/STT paths. A `DwmSetWindowAttribute` call darkens the caption. Settings gains baseline-snapshot dirty tracking with a confirm-on-leave prompt centralized in `DashboardForm.ShowPage`. A pure `DiagnosticsInfo` feeds a new About sidebar page that confirms the Vulkan/GPU path and exposes log + diagnostics access.

**Tech Stack:** C#/.NET 10 WinForms, GDI+ custom controls, source-gen + classic P/Invoke, System.Text.Json. Per-user SDK at `C:\Users\Luke\.dotnet`.

---

## Build & test environment (every task uses these)

**Build** (always `--no-incremental` — incremental hides WFO1000 analyzer warnings):
```powershell
& "$env:USERPROFILE\.dotnet\dotnet.exe" build VoiceToText.slnx -c Debug --no-incremental
```
Success = `Build succeeded.` with `0 Warning(s)` / `0 Error(s)`.

**Run a headless self-test** (output written to a `*.txt` in the CWD):
```powershell
$env:DOTNET_ROOT = "$env:USERPROFILE\.dotnet"
& "src\VoiceToText\bin\Debug\net10.0-windows\VoiceToText.exe" --logtest
Get-Content .\logtest-output.txt
```

**Sandbox editing:** if a direct `Edit`/`Write` to a repo file is blocked, stage to `C:\Users\Luke\.claude\jobs\f39a9536\tmp\stage\<name>` then `cp` into place via Bash, or use `perl -0pi -e`. Commit with Bash `git` on `main`.

**WFO1000 caution:** the analyzer errors on a *new public property of a `Control` subclass*. New public members in this plan are **methods** (`HasUnsavedChanges()`, `Save()`, `Reload()`) and **events** — both exempt. If WFO1000 ever fires, add `[System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]`.

---

## File structure

**Create**
- `src/VoiceToText/Diagnostics/Log.cs` — `LogWriter` (rolling file writer) + static `Log` facade. Metadata only.
- `src/VoiceToText/Diagnostics/RuntimeProbe.cs` — reflection probe for the loaded Whisper runtime (Vulkan/CPU), shared with `SelfTest`.
- `src/VoiceToText/Diagnostics/GpuInfo.cs` — `EnumDisplayDevices` (classic P/Invoke) → primary GPU adapter name.
- `src/VoiceToText/Diagnostics/DiagnosticsInfo.cs` — pure diagnostics row model (testable) + a live `Current()` factory.
- `src/VoiceToText/Dashboard/DarkTitleBar.cs` — `DwmSetWindowAttribute` helper.
- `src/VoiceToText/Dashboard/AboutPage.cs` — the About page UserControl.

**Modify**
- `src/VoiceToText/Program.cs` — early `Log` init, `SetUnhandledExceptionMode`, route `--logtest`/`--abouttest`.
- `src/VoiceToText/App/TrayApplicationContext.cs` — crash handlers; no-mic + device-lost + model error surfacing; transcription-timing logging; About's check-for-updates bubble.
- `src/VoiceToText/Audio/WasapiAudioSource.cs` — surface device-lost error via a `RecordingFailed` event.
- `src/VoiceToText/Dashboard/SettingsPage.cs` — dirty tracking, indicator, Save-enable, `HasUnsavedChanges()`, `Save()`.
- `src/VoiceToText/Dashboard/DashboardForm.cs` — dark title bar; About page; unsaved-changes leave/close prompts.
- `src/VoiceToText/Diagnostics/SelfTest.cs` — `RunLogTest`, `RunAboutTest`, extend `RunDashWindow`, reuse `RuntimeProbe`.
- `src/VoiceToText/VoiceToText.csproj` — `<Version>0.7.0</Version>` (Task 9).

---

## Task 1: Logging (`Log` + `LogWriter`) + `--logtest`

**Files:** Create `src/VoiceToText/Diagnostics/Log.cs`; Modify `src/VoiceToText/Program.cs`, `src/VoiceToText/Diagnostics/SelfTest.cs`.

- [ ] **Step 1: Write the failing test (`RunLogTest`)**

In `src/VoiceToText/Diagnostics/SelfTest.cs`, add this method just before `RunTextRulesTest`:

```csharp
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
```

Add `using VoiceToText.Diagnostics;`? — `RunLogTest` lives in `SelfTest` which is already in `namespace VoiceToText.Diagnostics`, so `LogWriter`/`Log` resolve without a using.

In `src/VoiceToText/Program.cs`, add the route after the `--textrulestest` block:
```csharp
        if (args.Length > 0 && args[0].Equals("--logtest", StringComparison.OrdinalIgnoreCase))
            return SelfTest.RunLogTest("logtest-output.txt");
```

- [ ] **Step 2: Build to verify it fails**

Run the build. Expected: FAIL — `error CS0246: ... 'LogWriter' could not be found`.

- [ ] **Step 3: Create `Log.cs`**

Create `src/VoiceToText/Diagnostics/Log.cs`:

```csharp
namespace VoiceToText.Diagnostics;

/// <summary>
/// A tiny, dependency-free rolling log writer. One clear unit: owns a file path, appends
/// timestamped lines, and rolls the file to "<name>.1" once it passes a size cap. Thread-safe
/// and never throws — logging must never break dictation. Records metadata only (never transcripts).
/// </summary>
public sealed class LogWriter
{
    private readonly object _lock = new();
    private readonly string _filePath;
    private readonly long _maxBytes;

    public LogWriter(string filePath, long maxBytes = 512 * 1024)
    {
        _filePath = filePath;
        _maxBytes = maxBytes;
    }

    public void Write(string level, string message, Exception? ex)
    {
        try
        {
            lock (_lock)
            {
                var dir = Path.GetDirectoryName(_filePath);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

                if (File.Exists(_filePath) && new FileInfo(_filePath).Length > _maxBytes)
                {
                    var rolled = _filePath + ".1";
                    if (File.Exists(rolled)) File.Delete(rolled);
                    File.Move(_filePath, rolled);
                }

                var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {message}";
                if (ex is not null) line += Environment.NewLine + ex;
                File.AppendAllText(_filePath, line + Environment.NewLine);
            }
        }
        catch
        {
            // Logging is best-effort; never propagate.
        }
    }
}

/// <summary>Static facade over a default <see cref="LogWriter"/> at %APPDATA%\VoiceToText\logs.</summary>
public static class Log
{
    public static string LogFolder { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "VoiceToText", "logs");

    private static readonly LogWriter Writer = new(Path.Combine(LogFolder, "voicetotext.log"));

    public static void Info(string message) => Writer.Write("INFO", message, null);
    public static void Error(string message, Exception? ex = null) => Writer.Write("ERROR", message, ex);
}
```

- [ ] **Step 4: Build + run `--logtest`**

```powershell
& "$env:USERPROFILE\.dotnet\dotnet.exe" build VoiceToText.slnx -c Debug --no-incremental
$env:DOTNET_ROOT = "$env:USERPROFILE\.dotnet"
& "src\VoiceToText\bin\Debug\net10.0-windows\VoiceToText.exe" --logtest
Get-Content .\logtest-output.txt
```
Expected: `0 Warning(s)`, `0 Error(s)`, `ALL LOG TESTS PASSED`.

- [ ] **Step 5: Wire an early startup log line in `Program.cs`**

In `src/VoiceToText/Program.cs`, immediately before the single-instance `using var mutex = ...` line, add:
```csharp
        Diagnostics.Log.Info("Voice to Text starting.");
```
(Fully-qualified to avoid touching the using block; `Program` is in `namespace VoiceToText`, so `Diagnostics.Log` resolves.)

- [ ] **Step 6: Commit**

```bash
git add src/VoiceToText/Diagnostics/Log.cs src/VoiceToText/Program.cs src/VoiceToText/Diagnostics/SelfTest.cs
git commit -m "feat(diag): rolling Log + LogWriter (metadata-only) + --logtest"
```

---

## Task 2: Global crash handler

**Files:** Modify `src/VoiceToText/Program.cs`, `src/VoiceToText/App/TrayApplicationContext.cs`.

No unit test (process-level handlers); verified by build + manual. Logs feed Task 1's file.

- [ ] **Step 1: Set the unhandled-exception mode in `Program.cs`**

In `src/VoiceToText/Program.cs`, immediately before `ApplicationConfiguration.Initialize();`, add:
```csharp
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
```

- [ ] **Step 2: Subscribe + handle in `TrayApplicationContext`**

In `src/VoiceToText/App/TrayApplicationContext.cs`, add to the `using` block:
```csharp
using System.Threading;
using VoiceToText.Diagnostics;
```

In the constructor, immediately after `_settings = AppSettings.Load();`, add:
```csharp
        Application.ThreadException += OnUiThreadException;
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
```

Add these two handler methods (e.g. just after the constructor, before `CreateOverlay`):
```csharp
    private void OnUiThreadException(object? sender, ThreadExceptionEventArgs e)
    {
        Log.Error("Unhandled UI-thread exception", e.Exception);
        try { ShowError("Something went wrong. Details are in the log (About → Open log folder)."); }
        catch { /* never re-throw from the handler */ }
    }

    private void OnUnhandledException(object? sender, UnhandledExceptionEventArgs e)
        => Log.Error("Unhandled exception", e.ExceptionObject as Exception);
```

In `Dispose(bool disposing)`, inside the `if (disposing)` block (before `_window.Dispose();`), add:
```csharp
            Application.ThreadException -= OnUiThreadException;
            AppDomain.CurrentDomain.UnhandledException -= OnUnhandledException;
```

- [ ] **Step 3: Build**

Run the build. Expected: `0 Warning(s)`, `0 Error(s)`.

- [ ] **Step 4: Commit**

```bash
git add src/VoiceToText/Program.cs src/VoiceToText/App/TrayApplicationContext.cs
git commit -m "feat(robust): global crash handler logs + shows a friendly balloon"
```

---

## Task 3: Friendlier error surfacing

**Files:** Modify `src/VoiceToText/Audio/WasapiAudioSource.cs`, `src/VoiceToText/App/TrayApplicationContext.cs`.

- [ ] **Step 1: Surface a device-lost error from `WasapiAudioSource`**

In `src/VoiceToText/Audio/WasapiAudioSource.cs`, add an event next to the existing ones (after `public event Action<float>? LevelChanged;`):
```csharp
    /// <summary>Raised on the capture thread when recording stops because of a device error
    /// (e.g. the mic was unplugged). Not raised on a normal user/auto stop.</summary>
    public event Action<Exception>? RecordingFailed;
```

Replace the existing `OnRecordingStopped`:
```csharp
    private void OnRecordingStopped(object? sender, StoppedEventArgs e)
        => _stopped?.TrySetResult(true);
```
with:
```csharp
    private void OnRecordingStopped(object? sender, StoppedEventArgs e)
    {
        _stopped?.TrySetResult(true);
        if (e.Exception is not null)
            RecordingFailed?.Invoke(e.Exception);
    }
```

Also add the same event to the interface so the tray can subscribe. In `src/VoiceToText/Audio/IAudioSource.cs`, add (next to the other events):
```csharp
    event Action<Exception>? RecordingFailed;
```
(If `IAudioSource` does not already declare `SilenceDetected`/`LevelChanged` as events, place `RecordingFailed` alongside however those are declared; the implementer should match the existing interface style. If `RecordingFailed` cannot be added to the interface cleanly, instead cast in the tray: `((WasapiAudioSource)_audio).RecordingFailed += ...` — but prefer the interface event.)

- [ ] **Step 2: Build to confirm the interface/impl line up**

Run the build. Expected: `0 Warning(s)`, `0 Error(s)` (a mismatch here surfaces immediately).

- [ ] **Step 3: No-microphone guard + device-lost handling in the tray**

In `src/VoiceToText/App/TrayApplicationContext.cs`, in the constructor where audio events are wired (next to `_audio.SilenceDetected += OnSilenceDetected;`), add:
```csharp
        _audio.RecordingFailed += OnRecordingFailed;
```

Replace `StartRecording`:
```csharp
    private void StartRecording()
    {
        try
        {
            var autoStop = !_settings.HoldToTalk && _settings.AutoStopEnabled;
            _audio.Start(_settings.InputDeviceId, autoStop, _settings.AutoStopSilenceSeconds);
            SetState(AppState.Recording);
        }
        catch (Exception ex)
        {
            ShowError($"Could not start recording: {ex.Message}");
            SetState(AppState.Idle);
        }
    }
```
with:
```csharp
    private void StartRecording()
    {
        if (Audio.AudioDevices.GetInputDevices().Count == 0)
        {
            Log.Error("Start recording aborted: no input device present.");
            ShowError("No microphone found — connect one and try again.");
            return;
        }

        try
        {
            var autoStop = !_settings.HoldToTalk && _settings.AutoStopEnabled;
            _audio.Start(_settings.InputDeviceId, autoStop, _settings.AutoStopSilenceSeconds);
            SetState(AppState.Recording);
        }
        catch (Exception ex)
        {
            Log.Error("Could not start recording", ex);
            ShowError($"Could not start recording: {ex.Message}");
            SetState(AppState.Idle);
        }
    }
```

Add the device-lost handler (e.g. just after `OnSilenceDetected`):
```csharp
    // Fires on the capture thread when the mic drops out mid-recording; marshal to the UI
    // thread, tell the user, discard the partial capture, and return to Idle.
    private void OnRecordingFailed(Exception ex)
    {
        if (!_window.IsHandleCreated)
            return;
        _window.BeginInvoke(() =>
        {
            if (_state != AppState.Recording || _busy)
                return;
            _busy = true;
            Log.Error("Microphone lost during recording", ex);
            ShowError("Microphone disconnected — recording stopped.");
            _ = ResetAfterFailureAsync();
        });
    }

    private async Task ResetAfterFailureAsync()
    {
        try { await _audio.StopAndGetSamplesAsync().ConfigureAwait(false); }
        catch (Exception ex) { Log.Error("Cleanup after mic loss failed", ex); }
        _window.BeginInvoke(() =>
        {
            SetState(AppState.Idle);
            _busy = false;
        });
    }
```

- [ ] **Step 4: Log model load/download/transcription failures with clearer messages**

In `WarmUpAsync`, replace the body so it distinguishes a download failure and logs. The current method computes `ModelManager.IsModelPresent(_settings.ModelType)` for a balloon; reuse that. Replace:
```csharp
            await _stt.LoadAsync().ConfigureAwait(false);
            _window.BeginInvoke(() => _trayIcon.Text = $"Voice to Text {VersionLabel} — ready ({_settings.Hotkey.Describe()})");
        }
        catch (Exception ex)
        {
            _window.BeginInvoke(() => ShowError($"Speech model failed to load: {ex.Message}"));
        }
```
with:
```csharp
            var wasMissing = !ModelManager.IsModelPresent(_settings.ModelType);
            await _stt.LoadAsync().ConfigureAwait(false);
            Log.Info($"Speech model ready ({_settings.ModelType}) on runtime {RuntimeProbe.LoadedRuntime()}.");
            _window.BeginInvoke(() => _trayIcon.Text = $"Voice to Text {VersionLabel} — ready ({_settings.Hotkey.Describe()})");
        }
        catch (Exception ex)
        {
            Log.Error("Speech model load/warm-up failed", ex);
            var msg = !ModelManager.IsModelPresent(_settings.ModelType)
                ? "Couldn't download the speech model — check your internet connection."
                : "Speech model failed to load — see the log (About → Open log folder).";
            _window.BeginInvoke(() => ShowError(msg));
        }
```
(`RuntimeProbe` is created in Task 7; if implementing tasks strictly in order, this line references it before Task 7. To keep Task 3 self-contained and building, **temporarily** use `"(runtime probe pending)"` here and the implementer of Task 7 will swap it to `RuntimeProbe.LoadedRuntime()`. If Task 7 is already done, use `RuntimeProbe.LoadedRuntime()` directly.)

> **Note for the executor:** to avoid a forward reference, the simplest path is to write the success-log line in Task 3 as `Log.Info($"Speech model ready ({_settings.ModelType}).");` (no runtime), and add the runtime to it in Task 7 when `RuntimeProbe` exists. Use that simpler line now.

So actually write:
```csharp
            Log.Info($"Speech model ready ({_settings.ModelType}).");
```

In `StopAndTranscribeAsync`, add transcription timing + error logging. Replace:
```csharp
            var samples = await _audio.StopAndGetSamplesAsync().ConfigureAwait(false);
            var text = await _stt.TranscribeAsync(samples).ConfigureAwait(false);
            text = TextRules.Apply(text, _settings.Replacements, _settings.SpokenCommandsEnabled);
```
with:
```csharp
            var samples = await _audio.StopAndGetSamplesAsync().ConfigureAwait(false);
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var text = await _stt.TranscribeAsync(samples).ConfigureAwait(false);
            sw.Stop();
            text = TextRules.Apply(text, _settings.Replacements, _settings.SpokenCommandsEnabled);
            Log.Info($"Transcribed {StatsData.CountWords(text)} words in {sw.Elapsed.TotalSeconds:F2}s ({samples.Length / 16000.0:F1}s audio).");
```
And in the `catch (Exception ex)` of `StopAndTranscribeAsync`, add a log line as the first statement:
```csharp
            Log.Error("Transcription failed", ex);
```

- [ ] **Step 5: Build**

Run the build. Expected: `0 Warning(s)`, `0 Error(s)`.

- [ ] **Step 6: Commit**

```bash
git add src/VoiceToText/Audio/WasapiAudioSource.cs src/VoiceToText/Audio/IAudioSource.cs src/VoiceToText/App/TrayApplicationContext.cs
git commit -m "feat(robust): surface no-mic / mic-disconnect / model errors + log timings"
```

---

## Task 4: Dark title bar

**Files:** Create `src/VoiceToText/Dashboard/DarkTitleBar.cs`; Modify `src/VoiceToText/Dashboard/DashboardForm.cs`.

UI; verified by `--dashwindow` smoke (the call must not throw) + manual.

- [ ] **Step 1: Create `DarkTitleBar.cs`**

Create `src/VoiceToText/Dashboard/DarkTitleBar.cs`:
```csharp
using System.Runtime.InteropServices;

namespace VoiceToText.Dashboard;

/// <summary>Applies Windows' immersive dark-mode caption so the title bar matches the dark theme.</summary>
internal static partial class DarkTitleBar
{
    // 20 on Windows 10 20H1+ / Windows 11; 19 on early Windows 10 builds.
    private const int DwmwaUseImmersiveDarkMode = 20;
    private const int DwmwaUseImmersiveDarkModeOld = 19;

    [LibraryImport("dwmapi.dll")]
    private static partial int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    /// <summary>Best-effort: no-ops on older Windows or if dwmapi is unavailable.</summary>
    public static void Apply(IntPtr handle)
    {
        if (handle == IntPtr.Zero)
            return;
        try
        {
            int enabled = 1;
            if (DwmSetWindowAttribute(handle, DwmwaUseImmersiveDarkMode, ref enabled, sizeof(int)) != 0)
                DwmSetWindowAttribute(handle, DwmwaUseImmersiveDarkModeOld, ref enabled, sizeof(int));
        }
        catch
        {
            // Cosmetic — never block window creation.
        }
    }
}
```

- [ ] **Step 2: Apply it from `DashboardForm`**

In `src/VoiceToText/Dashboard/DashboardForm.cs`, add an `OnHandleCreated` override (e.g. just after the constructor or near `OnShown`):
```csharp
    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        DarkTitleBar.Apply(Handle);
    }
```

- [ ] **Step 3: Build + `--dashwindow` smoke**

```powershell
& "$env:USERPROFILE\.dotnet\dotnet.exe" build VoiceToText.slnx -c Debug --no-incremental
$env:DOTNET_ROOT = "$env:USERPROFILE\.dotnet"
& "src\VoiceToText\bin\Debug\net10.0-windows\VoiceToText.exe" --dashwindow
Get-Content .\dashwindow-output.txt
```
Expected: `0 Warning(s)`, `0 Error(s)`, `DASH WINDOW OK`.

- [ ] **Step 4: Commit**

```bash
git add src/VoiceToText/Dashboard/DarkTitleBar.cs src/VoiceToText/Dashboard/DashboardForm.cs
git commit -m "feat(polish): dark title bar via DwmSetWindowAttribute"
```

---

## Task 5: Unsaved-changes tracking in Settings

**Files:** Modify `src/VoiceToText/Dashboard/SettingsPage.cs`.

UI; verified by `--dashwindow` smoke + manual. Adds dirty tracking, an indicator, Save-enable, and public `HasUnsavedChanges()` / `Save()`.

- [ ] **Step 1: Add fields for the Save button, indicator, baseline, and loading guard**

In `src/VoiceToText/Dashboard/SettingsPage.cs`, the Save button is currently a local `var saveButton` in `BuildUi`. Promote it to a field and add the indicator + state. Add these fields next to `_savedLabel`:
```csharp
    private readonly Button _saveButton = new() { Text = "Save", Location = new Point(20, 584), Size = new Size(96, 30), FlatStyle = FlatStyle.Flat, BackColor = Theme.Accent, ForeColor = Color.White };
    private readonly Label _unsavedLabel = new() { AutoSize = true, ForeColor = Theme.Warning, Visible = false, Text = "● Unsaved changes", Location = new Point(230, 590) };
    private string _baseline = "";
    private bool _loading;
```
(The `_saveButton` Location 20,584 matches the current Save button position after the Task-6-era re-flow already in `SettingsPage` from v0.6.8.)

- [ ] **Step 2: Replace the local Save button in `BuildUi` and register the indicator**

In `BuildUi`, replace:
```csharp
        var saveButton = new Button { Text = "Save", Location = new Point(20, 552), Size = new Size(96, 30), FlatStyle = FlatStyle.Flat, BackColor = Theme.Accent, ForeColor = Color.White };
        saveButton.FlatAppearance.BorderSize = 0;
        saveButton.FlatAppearance.MouseOverBackColor = Theme.AccentLight;
        saveButton.Click += OnSave;
```
with:
```csharp
        _saveButton.FlatAppearance.BorderSize = 0;
        _saveButton.FlatAppearance.MouseOverBackColor = Theme.AccentLight;
        _saveButton.Click += OnSave;
```
(Note: the existing Save button line in your tree reads `Location = new Point(20, 584)` after v0.6.8's re-flow — match whatever the current literal is and remove it; the field initializer now carries the position.)

In the `Controls.AddRange(...)` call, replace `saveButton` with `_saveButton` and add `_unsavedLabel`:
```csharp
            _startupCheck, _saveButton, _savedLabel, _unsavedLabel,
```

- [ ] **Step 3: Add the snapshot + dirty logic**

Add these methods to `SettingsPage`:
```csharp
    // A stable string of every value OnSave persists; baseline vs. current => dirty.
    private string Snapshot() => string.Join("|",
        (_deviceCombo.SelectedItem as AudioInputDevice)?.Id ?? "",
        (_modelCombo.SelectedItem as ModelOption)?.Type.ToString() ?? "",
        _hotkey.Describe(),
        _activationCombo.SelectedIndex,
        _autoStopCheck.Checked,
        _silenceUpDown.Value,
        _overlayCheck.Checked,
        _historyCheck.Checked,
        _wpmUpDown.Value,
        _autoUpdateCheck.Checked,
        _updateFolderBox.Text.Trim(),
        _startupCheck.Checked);

    private void UpdateDirty()
    {
        if (_loading) return;
        bool dirty = Snapshot() != _baseline;
        _unsavedLabel.Visible = dirty;
        _saveButton.Enabled = dirty;
    }

    /// <summary>True when the controls differ from the last loaded/saved settings.</summary>
    public bool HasUnsavedChanges() => Snapshot() != _baseline;
```

- [ ] **Step 4: Capture the baseline in `LoadFromSettings`; wire change events**

Wrap `LoadFromSettings` body in the loading guard and capture the baseline at the end. Change the method to:
```csharp
    private void LoadFromSettings()
    {
        _loading = true;
        _hotkeyBox.Text = _hotkey.Describe();
        _startupCheck.Checked = AutoStart.IsEnabled();
        _activationCombo.SelectedIndex = _settings.HoldToTalk ? 1 : 0;
        _autoStopCheck.Checked = _settings.AutoStopEnabled;
        _silenceUpDown.Value = (decimal)Math.Clamp(_settings.AutoStopSilenceSeconds, 0.3, 10.0);
        _overlayCheck.Checked = _settings.ShowOverlay;
        _historyCheck.Checked = _settings.HistoryEnabled;
        _wpmUpDown.Value = (decimal)Math.Clamp(_settings.TypingSpeedWpm, 10, 300);
        _autoUpdateCheck.Checked = _settings.AutoUpdateEnabled;
        _updateFolderBox.Text = _settings.UpdateFeedFolder;
        UpdateAutoStopEnabled();
        UpdateHint();
        _loading = false;
        _baseline = Snapshot();
        UpdateDirty();
    }
```
(The `_historyCheck.Checked = _settings.HistoryEnabled;` line already exists from v0.6.8 — keep it; this shows the full method for clarity.)

In `BuildUi`, after the controls are constructed and added, register change handlers (place near the end of `BuildUi`, after `Controls.AddRange`):
```csharp
        _deviceCombo.SelectedIndexChanged += (_, _) => UpdateDirty();
        _modelCombo.SelectedIndexChanged += (_, _) => UpdateDirty();
        _activationCombo.SelectedIndexChanged += (_, _) => UpdateDirty();
        _autoStopCheck.CheckedChanged += (_, _) => UpdateDirty();
        _silenceUpDown.ValueChanged += (_, _) => UpdateDirty();
        _overlayCheck.CheckedChanged += (_, _) => UpdateDirty();
        _historyCheck.CheckedChanged += (_, _) => UpdateDirty();
        _wpmUpDown.ValueChanged += (_, _) => UpdateDirty();
        _autoUpdateCheck.CheckedChanged += (_, _) => UpdateDirty();
        _updateFolderBox.TextChanged += (_, _) => UpdateDirty();
        _startupCheck.CheckedChanged += (_, _) => UpdateDirty();
```
In `TryCaptureHotkey`, after `UpdateHint();` (inside the `if (definition is not null)` block), add:
```csharp
            UpdateDirty();
```

- [ ] **Step 5: Split `OnSave` into a public `Save()`**

Replace the existing `OnSave`:
```csharp
    private void OnSave(object? sender, EventArgs e)
    {
        _settings.InputDeviceId = (_deviceCombo.SelectedItem as AudioInputDevice)?.Id;
        if (_modelCombo.SelectedItem is ModelOption model)
            _settings.ModelType = model.Type;
        _settings.Hotkey = _hotkey;
        _settings.HoldToTalk = _activationCombo.SelectedIndex == 1;
        _settings.AutoStopEnabled = _autoStopCheck.Checked;
        _settings.AutoStopSilenceSeconds = (double)_silenceUpDown.Value;
        _settings.ShowOverlay = _overlayCheck.Checked;
        _settings.HistoryEnabled = _historyCheck.Checked;
        _settings.TypingSpeedWpm = (double)_wpmUpDown.Value;
        _settings.AutoUpdateEnabled = _autoUpdateCheck.Checked;
        _settings.UpdateFeedFolder = _updateFolderBox.Text.Trim();
        _settings.UpdateConsentAccepted = _autoUpdateCheck.Checked;
        AutoStart.Apply(_startupCheck.Checked);
        _savedLabel.Visible = true;
        SettingsSaved?.Invoke();
    }
```
with:
```csharp
    private void OnSave(object? sender, EventArgs e) => Save();

    /// <summary>Persist the current control values and clear the dirty state.</summary>
    public void Save()
    {
        _settings.InputDeviceId = (_deviceCombo.SelectedItem as AudioInputDevice)?.Id;
        if (_modelCombo.SelectedItem is ModelOption model)
            _settings.ModelType = model.Type;
        _settings.Hotkey = _hotkey;
        _settings.HoldToTalk = _activationCombo.SelectedIndex == 1;
        _settings.AutoStopEnabled = _autoStopCheck.Checked;
        _settings.AutoStopSilenceSeconds = (double)_silenceUpDown.Value;
        _settings.ShowOverlay = _overlayCheck.Checked;
        _settings.HistoryEnabled = _historyCheck.Checked;
        _settings.TypingSpeedWpm = (double)_wpmUpDown.Value;
        _settings.AutoUpdateEnabled = _autoUpdateCheck.Checked;
        _settings.UpdateFeedFolder = _updateFolderBox.Text.Trim();
        _settings.UpdateConsentAccepted = _autoUpdateCheck.Checked;
        AutoStart.Apply(_startupCheck.Checked);
        _savedLabel.Visible = true;
        _baseline = Snapshot();
        UpdateDirty();
        SettingsSaved?.Invoke();
    }
```
(The `_settings.HistoryEnabled = _historyCheck.Checked;` line already exists from v0.6.8 — keep it.)

`ReloadFromSettings` already calls `LoadFromSettings`, which now re-captures `_baseline` and clears dirty — no change needed there.

- [ ] **Step 6: Build + `--dashwindow` smoke**

```powershell
& "$env:USERPROFILE\.dotnet\dotnet.exe" build VoiceToText.slnx -c Debug --no-incremental
$env:DOTNET_ROOT = "$env:USERPROFILE\.dotnet"
& "src\VoiceToText\bin\Debug\net10.0-windows\VoiceToText.exe" --dashwindow
Get-Content .\dashwindow-output.txt
```
Expected: `0 Warning(s)`, `0 Error(s)`, `DASH WINDOW OK`.

- [ ] **Step 7: Commit**

```bash
git add src/VoiceToText/Dashboard/SettingsPage.cs
git commit -m "feat(settings): unsaved-changes indicator + Save-when-dirty + Save()/HasUnsavedChanges()"
```

---

## Task 6: Confirm-on-leave prompt in `DashboardForm`

**Files:** Modify `src/VoiceToText/Dashboard/DashboardForm.cs`.

- [ ] **Step 1: Prompt when navigating away from a dirty Settings page**

In `src/VoiceToText/Dashboard/DashboardForm.cs`, replace `ShowPage`:
```csharp
    public void ShowPage(DashboardPageKind page)
    {
        _active = page;
        _dashboardPage.Visible = page == DashboardPageKind.Dashboard;
        _settingsPage.Visible = page == DashboardPageKind.Settings;
        _textRulesPage.Visible = page == DashboardPageKind.TextRules;
        _historyPage.Visible = page == DashboardPageKind.History;
        SetActiveStyles();
    }
```
with:
```csharp
    public void ShowPage(DashboardPageKind page)
    {
        // Leaving a dirty Settings page? Offer to save first (covers nav clicks + programmatic navigation).
        if (_active == DashboardPageKind.Settings && page != DashboardPageKind.Settings
            && _settingsPage.HasUnsavedChanges() && !PromptSaveSettings())
            return; // user cancelled — stay on Settings

        _active = page;
        _dashboardPage.Visible = page == DashboardPageKind.Dashboard;
        _settingsPage.Visible = page == DashboardPageKind.Settings;
        _textRulesPage.Visible = page == DashboardPageKind.TextRules;
        _historyPage.Visible = page == DashboardPageKind.History;
        SetActiveStyles();
    }

    // Returns true to proceed (Save or Discard chosen), false to abort (Cancel).
    private bool PromptSaveSettings()
    {
        var choice = MessageBox.Show(this,
            "You have unsaved settings changes. Save them?",
            "Voice to Text",
            MessageBoxButtons.YesNoCancel, MessageBoxIcon.Warning);
        switch (choice)
        {
            case DialogResult.Yes: _settingsPage.Save(); return true;
            case DialogResult.No: _settingsPage.ReloadFromSettings(); return true;
            default: return false; // Cancel
        }
    }
```

- [ ] **Step 2: Prompt on user-initiated window close**

Add an `OnFormClosing` override (e.g. after `ShowPage`):
```csharp
    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        // Only intercept a user clicking the X — never block app shutdown / logoff.
        if (e.CloseReason == CloseReason.UserClosing && _settingsPage.HasUnsavedChanges() && !PromptSaveSettings())
        {
            e.Cancel = true;
            return;
        }
        base.OnFormClosing(e);
    }
```

- [ ] **Step 3: Build + `--dashwindow` smoke**

```powershell
& "$env:USERPROFILE\.dotnet\dotnet.exe" build VoiceToText.slnx -c Debug --no-incremental
$env:DOTNET_ROOT = "$env:USERPROFILE\.dotnet"
& "src\VoiceToText\bin\Debug\net10.0-windows\VoiceToText.exe" --dashwindow
Get-Content .\dashwindow-output.txt
```
Expected: `0 Warning(s)`, `0 Error(s)`, `DASH WINDOW OK`. (The smoke never has unsaved changes, so no prompt blocks it.)

- [ ] **Step 4: Commit**

```bash
git add src/VoiceToText/Dashboard/DashboardForm.cs
git commit -m "feat(settings): Save/Discard/Cancel prompt when leaving unsaved Settings"
```

---

## Task 7: Diagnostics model (`RuntimeProbe` + `GpuInfo` + `DiagnosticsInfo`) + `--abouttest`

**Files:** Create `src/VoiceToText/Diagnostics/RuntimeProbe.cs`, `GpuInfo.cs`, `DiagnosticsInfo.cs`; Modify `src/VoiceToText/Diagnostics/SelfTest.cs`, `src/VoiceToText/Program.cs`.

- [ ] **Step 1: Write the failing test (`RunAboutTest`)**

In `src/VoiceToText/Diagnostics/SelfTest.cs`, add just before `RunTextRulesTest`:
```csharp
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

        var text = gpu.ToClipboardText();
        Pass("clipboard text has key fields", text.Contains("0.7.0") && text.Contains("Vulkan") && text.Contains("7900 XT"));

        log.AppendLine(allPass ? "ALL ABOUT TESTS PASSED" : "SOME ABOUT TESTS FAILED");
        var result = log.ToString();
        File.WriteAllText(outputPath, result);
        Console.WriteLine(result);
        return allPass ? 0 : 1;
    }
```
Add `using System.Linq;` to `SelfTest.cs` if not already present (the file uses LINQ elsewhere; confirm/add).

In `src/VoiceToText/Program.cs`, add after the `--logtest` route:
```csharp
        if (args.Length > 0 && args[0].Equals("--abouttest", StringComparison.OrdinalIgnoreCase))
            return SelfTest.RunAboutTest("abouttest-output.txt");
```

- [ ] **Step 2: Build to verify it fails**

Run the build. Expected: FAIL — `error CS0246: ... 'DiagnosticsInfo' could not be found`.

- [ ] **Step 3: Create `RuntimeProbe.cs`**

Create `src/VoiceToText/Diagnostics/RuntimeProbe.cs`:
```csharp
namespace VoiceToText.Diagnostics;

/// <summary>
/// Reads Whisper.net's loaded native runtime (Vulkan vs Cpu) via reflection — the enum
/// member names are not part of our compile surface. Returns "Unknown" before a model
/// has loaded or if the internals move.
/// </summary>
public static class RuntimeProbe
{
    public static string LoadedRuntime()
    {
        try
        {
            var type = Type.GetType("Whisper.net.LibraryLoader.RuntimeOptions, Whisper.net");
            var value = type?.GetProperty("LoadedLibrary")?.GetValue(null);
            return value?.ToString() ?? "Unknown";
        }
        catch
        {
            return "Unknown";
        }
    }
}
```

In `src/VoiceToText/Diagnostics/SelfTest.cs`, replace the private `GetLoadedRuntime()` method body so it delegates (DRY):
```csharp
    private static string GetLoadedRuntime() => RuntimeProbe.LoadedRuntime();
```
(`SelfTest` is in the same `VoiceToText.Diagnostics` namespace, so no using is needed.)

- [ ] **Step 4: Create `GpuInfo.cs`**

Create `src/VoiceToText/Diagnostics/GpuInfo.cs`:
```csharp
using System.Runtime.InteropServices;

namespace VoiceToText.Diagnostics;

/// <summary>Best-effort primary GPU adapter name via EnumDisplayDevices. "Unknown" on failure.</summary>
public static class GpuInfo
{
    private const int DisplayDeviceAttachedToDesktop = 0x1;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DISPLAY_DEVICE
    {
        public int cb;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DeviceName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceString;
        public int StateFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceID;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceKey;
    }

    // Classic DllImport: source-gen LibraryImport does not marshal ByValTStr struct fields.
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumDisplayDevices(string? lpDevice, uint iDevNum, ref DISPLAY_DEVICE lpDisplayDevice, uint dwFlags);

    public static string PrimaryGpuName()
    {
        try
        {
            string? firstAdapter = null;
            for (uint i = 0; ; i++)
            {
                var dd = new DISPLAY_DEVICE { cb = Marshal.SizeOf<DISPLAY_DEVICE>() };
                if (!EnumDisplayDevices(null, i, ref dd, 0))
                    break;
                if (string.IsNullOrWhiteSpace(dd.DeviceString))
                    continue;
                firstAdapter ??= dd.DeviceString;
                if ((dd.StateFlags & DisplayDeviceAttachedToDesktop) != 0)
                    return dd.DeviceString; // the adapter actually driving the desktop
            }
            return firstAdapter ?? "Unknown";
        }
        catch
        {
            return "Unknown";
        }
    }
}
```

- [ ] **Step 5: Create `DiagnosticsInfo.cs`**

Create `src/VoiceToText/Diagnostics/DiagnosticsInfo.cs`:
```csharp
using System.Reflection;
using System.Runtime.InteropServices;
using VoiceToText.Settings;
using VoiceToText.Stt;

namespace VoiceToText.Diagnostics;

/// <summary>
/// Pure diagnostics view-model: turns injected facts into labelled rows + a clipboard string,
/// with an acceleration flag for green/amber rendering. No I/O — fully testable via --abouttest.
/// Use <see cref="Current"/> for the live values.
/// </summary>
public sealed class DiagnosticsInfo
{
    public bool IsGpuAccelerated { get; }
    public IReadOnlyList<(string Label, string Value)> Rows { get; }

    public DiagnosticsInfo(
        string version, string runtime, string gpu, string model,
        string modelPath, long modelSizeBytes, string os, string framework, string arch)
    {
        IsGpuAccelerated =
            !runtime.Equals("Cpu", StringComparison.OrdinalIgnoreCase) &&
            !runtime.Equals("Unknown", StringComparison.OrdinalIgnoreCase) &&
            runtime.Length > 0;

        var acceleration = IsGpuAccelerated
            ? $"{runtime} (GPU)"
            : runtime.Equals("Cpu", StringComparison.OrdinalIgnoreCase)
                ? "CPU (no GPU acceleration)"
                : runtime; // Unknown / Loading

        var fileValue = modelSizeBytes > 0 ? $"{modelPath} · {FormatSize(modelSizeBytes)}" : modelPath;

        Rows = new List<(string, string)>
        {
            ("Version", version),
            ("Acceleration", acceleration),
            ("GPU", gpu),
            ("Speech model", model),
            ("Model file", fileValue),
            ("System", $"{os} · {framework} · {arch}"),
        };
    }

    public string ToClipboardText()
        => "Voice to Text diagnostics" + Environment.NewLine +
           string.Join(Environment.NewLine, Rows.Select(r => $"{r.Label}: {r.Value}"));

    private static string FormatSize(long bytes)
    {
        double gb = bytes / 1024.0 / 1024.0 / 1024.0;
        if (gb >= 1.0) return $"{gb:0.0} GB";
        double mb = bytes / 1024.0 / 1024.0;
        return $"{mb:0} MB";
    }

    /// <summary>Gather the live values for the running app.</summary>
    public static DiagnosticsInfo Current(AppSettings settings)
    {
        var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "?";
        var runtime = RuntimeProbe.LoadedRuntime();
        var gpu = GpuInfo.PrimaryGpuName();
        var modelType = settings.ModelType;
        var model = ModelOption.All.FirstOrDefault(m => m.Type == modelType)?.ToString() ?? modelType.ToString();
        var modelPath = ModelManager.GetModelPath(modelType);
        long size = 0;
        try { if (File.Exists(modelPath)) size = new FileInfo(modelPath).Length; } catch { }
        return new DiagnosticsInfo(
            version, runtime, gpu, model, modelPath, size,
            RuntimeInformation.OSDescription,
            RuntimeInformation.FrameworkDescription,
            RuntimeInformation.OSArchitecture.ToString());
    }
}
```
> **Type note:** `ModelOption` (used by `SettingsPage`) is expected to render a friendly name via `ToString()` (e.g. "Large V3 Turbo"). If `ModelOption.ToString()` does not produce a friendly label, use its display property instead — match how `SettingsPage`'s model combo shows it. Verify against `src/VoiceToText/Dashboard/ModelOption.cs` (or wherever `ModelOption` lives) before implementing.

- [ ] **Step 6: Build + run `--abouttest`**

```powershell
& "$env:USERPROFILE\.dotnet\dotnet.exe" build VoiceToText.slnx -c Debug --no-incremental
$env:DOTNET_ROOT = "$env:USERPROFILE\.dotnet"
& "src\VoiceToText\bin\Debug\net10.0-windows\VoiceToText.exe" --abouttest
Get-Content .\abouttest-output.txt
```
Expected: `0 Warning(s)`, `0 Error(s)`, `ALL ABOUT TESTS PASSED`.

- [ ] **Step 7: (Optional cleanup) point Task 3's warm-up log at the real runtime**

If Task 3 left the success line as `Log.Info($"Speech model ready ({_settings.ModelType}).");`, optionally enrich it now that `RuntimeProbe` exists:
```csharp
            Log.Info($"Speech model ready ({_settings.ModelType}) on runtime {RuntimeProbe.LoadedRuntime()}.");
```
(Requires `using VoiceToText.Diagnostics;` in `TrayApplicationContext.cs`, added in Task 2.)

- [ ] **Step 8: Commit**

```bash
git add src/VoiceToText/Diagnostics/RuntimeProbe.cs src/VoiceToText/Diagnostics/GpuInfo.cs src/VoiceToText/Diagnostics/DiagnosticsInfo.cs src/VoiceToText/Diagnostics/SelfTest.cs src/VoiceToText/Program.cs src/VoiceToText/App/TrayApplicationContext.cs
git commit -m "feat(diag): RuntimeProbe + GpuInfo + pure DiagnosticsInfo + --abouttest"
```

---

## Task 8: About page + wire into the window

**Files:** Create `src/VoiceToText/Dashboard/AboutPage.cs`; Modify `src/VoiceToText/Dashboard/DashboardForm.cs`, `src/VoiceToText/App/TrayApplicationContext.cs`, `src/VoiceToText/Diagnostics/SelfTest.cs`.

- [ ] **Step 1: Create `AboutPage.cs`**

Create `src/VoiceToText/Dashboard/AboutPage.cs`:
```csharp
using System.Diagnostics;
using System.Drawing;
using VoiceToText.Diagnostics;
using VoiceToText.Settings;

namespace VoiceToText.Dashboard;

/// <summary>
/// The About / diagnostics page: a dark card of facts (version, Vulkan/CPU acceleration, GPU,
/// model + file, system) with actions — Check for updates, Open log folder, Copy diagnostics.
/// Reloads its facts each time it is shown.
/// </summary>
internal sealed class AboutPage : UserControl
{
    private static readonly Font HeadingFont = new("Segoe UI", 14f, FontStyle.Bold);

    private readonly AppSettings _settings;
    private readonly Label _title = new()
    {
        Text = "About", AutoSize = true, Location = new Point(20, 16),
        ForeColor = Theme.TextPrimary, Font = HeadingFont,
    };
    private readonly Label _subtitle = new()
    {
        AutoSize = true, Location = new Point(20, 48), ForeColor = Theme.TextSecondary,
        Font = Theme.Caption, Text = "Voice to Text — local, offline dictation.",
    };
    private readonly Panel _card = new() { BackColor = Theme.CardBg };
    private readonly Button _check = MakeButton("Check for updates", primary: true);
    private readonly Button _openLog = MakeButton("Open log folder", primary: false);
    private readonly Button _copy = MakeButton("Copy diagnostics", primary: false);
    private readonly Label _footer = new()
    {
        AutoSize = true, ForeColor = Theme.TextMuted, Font = Theme.Caption,
        Text = "🔒 Fully local — your audio and transcripts never leave this PC.",
    };

    /// <summary>Raised when the user clicks "Check for updates"; the host runs the update flow.</summary>
    public event Action? CheckForUpdatesRequested;

    public AboutPage(AppSettings settings)
    {
        _settings = settings;
        BackColor = Theme.WindowBg;
        DoubleBuffered = true;
        _check.Click += (_, _) => CheckForUpdatesRequested?.Invoke();
        _openLog.Click += (_, _) => OpenLogFolder();
        _copy.Click += (_, _) => CopyDiagnostics();
        Controls.AddRange(new Control[] { _title, _subtitle, _card, _check, _openLog, _copy, _footer });
    }

    private static Button MakeButton(string text, bool primary) => new()
    {
        Text = text,
        AutoSize = false,
        Size = new Size(132, 30),
        FlatStyle = FlatStyle.Flat,
        Font = Theme.Caption,
        BackColor = primary ? Theme.Accent : Theme.CardBg,
        ForeColor = primary ? Color.White : Theme.NavActiveText,
    };

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

    /// <summary>Rebuild the diagnostics card from the live values.</summary>
    public void Reload()
    {
        var info = DiagnosticsInfo.Current(_settings);

        _card.Controls.Clear();
        int y = 6;
        foreach (var (label, value) in info.Rows)
        {
            var k = new Label { Text = label, AutoSize = true, Location = new Point(14, y + 2), ForeColor = Theme.TextSecondary, Font = Theme.Caption };
            bool accelRow = label == "Acceleration";
            var v = new Label
            {
                Text = value, AutoSize = true, Location = new Point(150, y),
                ForeColor = accelRow ? (info.IsGpuAccelerated ? Color.FromArgb(0x7B, 0xD8, 0x8F) : Theme.Warning) : Theme.TextPrimary,
                Font = accelRow ? Theme.LabelBold : Theme.NavItem,
                MaximumSize = new Size(Math.Max(80, _card.Width - 164), 0),
            };
            _card.Controls.Add(k);
            _card.Controls.Add(v);
            y += Math.Max(24, v.PreferredHeight + 8);
        }
        _card.Height = y + 6;

        DoLayout();
    }

    private void DoLayout()
    {
        const int pad = 20;
        int w = Width - pad * 2;
        if (w <= 0 || Height <= 0) return;

        _card.SetBounds(pad, 78, w, _card.Height);
        int by = _card.Bottom + 16;
        _check.Location = new Point(pad, by);
        _openLog.Location = new Point(pad + 142, by);
        _copy.Location = new Point(pad + 284, by);
        _footer.Location = new Point(pad, by + 44);

        // Re-clamp value-label wrap width to the current card width.
        foreach (Control c in _card.Controls)
            if (c is Label lbl && lbl.Location.X == 150)
                lbl.MaximumSize = new Size(Math.Max(80, _card.Width - 164), 0);
    }

    private static void OpenLogFolder()
    {
        try
        {
            Directory.CreateDirectory(Log.LogFolder);
            Process.Start(new ProcessStartInfo { FileName = "explorer.exe", Arguments = $"\"{Log.LogFolder}\"", UseShellExecute = true });
        }
        catch (Exception ex) { Log.Error("Open log folder failed", ex); }
    }

    private void CopyDiagnostics()
    {
        try { Clipboard.SetText(DiagnosticsInfo.Current(_settings).ToClipboardText()); }
        catch (Exception ex) { Log.Error("Copy diagnostics failed", ex); }
    }
}
```
> **Theme fonts:** this uses `Theme.LabelBold` (acceleration row), `Theme.NavItem` (value rows), `Theme.Caption`, and the local `HeadingFont` — all present in `Theme.cs` (`HeroNumber`/`TileNumber`/`LabelBold`/`Caption`/`NavItem`/`Brand`/`Empty`).

- [ ] **Step 2: Wire the page into `DashboardForm`**

In `src/VoiceToText/Dashboard/DashboardForm.cs`:

Change the enum:
```csharp
internal enum DashboardPageKind { Dashboard, Settings, TextRules, History }
```
to:
```csharp
internal enum DashboardPageKind { Dashboard, Settings, TextRules, History, About }
```

Add the nav-button field after `_navHistory`:
```csharp
    private readonly NavButton _navAbout = new("About") { Dock = DockStyle.Top };
```
Add the page field after `_historyPage`:
```csharp
    private readonly AboutPage _aboutPage;
```
Add an event next to the existing `SettingsSaved`:
```csharp
    public event Action? CheckForUpdatesRequested;
```
In the constructor, after the `_historyPage = new HistoryPage(...)` line, add:
```csharp
        _aboutPage = new AboutPage(settings) { Dock = DockStyle.Fill, Visible = false };
        _aboutPage.CheckForUpdatesRequested += () => CheckForUpdatesRequested?.Invoke();
```

In `BuildUi`, change the content adds to include About first:
```csharp
        _content.Controls.Add(_aboutPage);
        _content.Controls.Add(_historyPage);
        _content.Controls.Add(_textRulesPage);
        _content.Controls.Add(_settingsPage);
        _content.Controls.Add(_dashboardPage);
```
Add the nav click handler (next to `_navHistory.Click`):
```csharp
        _navAbout.Click += (_, _) => ShowPage(DashboardPageKind.About);
```
Add `_navAbout` to the sidebar **first** (so it sits at the very bottom). Change the sidebar adds to:
```csharp
        _sidebar.Controls.Add(_navAbout);
        _sidebar.Controls.Add(_navHistory);
        _sidebar.Controls.Add(_navTextRules);
        _sidebar.Controls.Add(_navSettings);
        _sidebar.Controls.Add(_navDashboard);
        _sidebar.Controls.Add(brand);
        _sidebar.Controls.Add(version);
```
In `ShowPage`, after the `_historyPage.Visible = ...` line, add:
```csharp
        _aboutPage.Visible = page == DashboardPageKind.About;
```
In `SetActiveStyles`, after the `_navHistory.Active = ...` line, add:
```csharp
        _navAbout.Active = _active == DashboardPageKind.About;
```

- [ ] **Step 3: Bubble the update request from the tray**

In `src/VoiceToText/App/TrayApplicationContext.cs`, in `ShowDashboard`, where the dashboard's events are subscribed (next to `_dashboard.SettingsSaved += OnSettingsSaved;`), add:
```csharp
            _dashboard.CheckForUpdatesRequested += () => _ = CheckForUpdatesAsync(userInitiated: true);
```

- [ ] **Step 4: Exercise the About page in `RunDashWindow`**

In `src/VoiceToText/Diagnostics/SelfTest.cs`, in `RunDashWindow`, after the History block (the `form.ShowPage(DashboardPageKind.History); ... Application.DoEvents();` group), insert:
```csharp
            form.ShowPage(DashboardPageKind.About);
            Application.DoEvents();
            form.Refresh();           // synchronous WM_PAINT for the About page (card + actions)
            Application.DoEvents();
```

- [ ] **Step 5: Build + run `--dashwindow`, `--abouttest`, `--logtest`**

```powershell
& "$env:USERPROFILE\.dotnet\dotnet.exe" build VoiceToText.slnx -c Debug --no-incremental
$env:DOTNET_ROOT = "$env:USERPROFILE\.dotnet"
& "src\VoiceToText\bin\Debug\net10.0-windows\VoiceToText.exe" --dashwindow ; Get-Content .\dashwindow-output.txt
& "src\VoiceToText\bin\Debug\net10.0-windows\VoiceToText.exe" --abouttest  ; Get-Content .\abouttest-output.txt
& "src\VoiceToText\bin\Debug\net10.0-windows\VoiceToText.exe" --logtest    ; Get-Content .\logtest-output.txt
```
Expected: `0 Warning(s)`, `0 Error(s)`; `DASH WINDOW OK`; `ALL ABOUT TESTS PASSED`; `ALL LOG TESTS PASSED`.

- [ ] **Step 6: Commit**

```bash
git add src/VoiceToText/Dashboard/AboutPage.cs src/VoiceToText/Dashboard/DashboardForm.cs src/VoiceToText/App/TrayApplicationContext.cs src/VoiceToText/Diagnostics/SelfTest.cs
git commit -m "feat(diag): About page (diagnostics + log/update actions) wired into the window"
```

---

## Task 9: Ship v0.7.0 to the update feed — FOREGROUND ONLY

**Files:** Modify `src/VoiceToText/VoiceToText.csproj`; Modify (out-of-repo) `D:\ClaudeCode\VoiceToText-Releases\`.

> **Must run from the foreground session** (out-of-repo writes from an isolated subagent don't persist).

- [ ] **Step 1: Manual run-through**

Launch `src\VoiceToText\bin\Debug\net10.0-windows\VoiceToText.exe` and verify: dark title bar; Settings shows "● Unsaved changes" + Save enabled only when dirty + Save/Discard/Cancel on leaving dirty; About shows "Vulkan (GPU)" + the GPU name, and Check-for-updates / Open-log-folder / Copy-diagnostics work; unplug the mic mid-dictation → "Microphone disconnected" balloon + a log line in `%APPDATA%\VoiceToText\logs\voicetotext.log`.

- [ ] **Step 2: Bump the version**

In `src/VoiceToText/VoiceToText.csproj`, set:
```xml
<Version>0.7.0</Version>
```

- [ ] **Step 3: Publish, package, populate the feed**

```powershell
.\publish.ps1
& "C:\Users\Luke\.claude\jobs\f39a9536\tmp\innosetup\tools\ISCC.exe" installer\VoiceToText.iss
Copy-Item installer\Output\VoiceToText-Setup.exe "D:\ClaudeCode\VoiceToText-Releases\VoiceToText-Setup-0.7.0.exe" -Force
```
Then write `D:\ClaudeCode\VoiceToText-Releases\latest.json` (4-space indent, matching existing):
- `Version`: `0.7.0`
- `SetupFileName`: `VoiceToText-Setup-0.7.0.exe`
- `Sha256`: `(Get-FileHash "D:\ClaudeCode\VoiceToText-Releases\VoiceToText-Setup-0.7.0.exe" -Algorithm SHA256).Hash.ToLower()`
- `ReleaseNotes`: "Polish & robustness: dark title bar; unsaved-changes prompt in Settings; new About page (confirms GPU/Vulkan, open logs, copy diagnostics); clearer mic/model error messages; rolling log + crash handler."
- `Mandatory`: `false`
- `ReleasedUtc`: current UTC (`(Get-Date).ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ")`)

- [ ] **Step 4: Commit the version bump**

```bash
git add src/VoiceToText/VoiceToText.csproj
git commit -m "v0.7.0: polish & robustness (dark title bar, About page, logging, error surfacing, unsaved-changes)"
```

- [ ] **Step 5: Verify the feed SAFELY**

**Do NOT** pass the real Releases folder to `--updatecheck` (it writes test files then `Directory.Delete()`s the folder). Instead:
```powershell
$env:DOTNET_ROOT = "$env:USERPROFILE\.dotnet"
& "src\VoiceToText\bin\Debug\net10.0-windows\VoiceToText.exe" --updatecheck ; Get-Content .\updatecheck-output.txt   # temp-feed self-test
$feed = "D:\ClaudeCode\VoiceToText-Releases"
$m = Get-Content "$feed\latest.json" -Raw | ConvertFrom-Json
$onDisk = (Get-FileHash "$feed\$($m.SetupFileName)" -Algorithm SHA256).Hash.ToLower()
"version=$($m.Version) sha matches=$($onDisk -eq $m.Sha256) setup present=$(Test-Path "$feed\$($m.SetupFileName)")"
```
Expected: `ALL UPDATE-CHECK TESTS PASSED`; `version=0.7.0 sha matches=True setup present=True`; the feed's historical setups remain intact.

---

## Notes on testing strategy

- **Pure logic** (`LogWriter` rotation, `DiagnosticsInfo` rows/flag/size-format) → `--logtest`, `--abouttest`. Deterministic, no app state.
- **UI** (dark title bar, unsaved-changes indicator/prompt, About page) → `--dashwindow` smoke (construct + paint every page) + manual run-through.
- **Process-level** (crash handlers) and **hardware** (no-mic, mic-disconnect, offline download) → manual; covered defensively in code + logged.
- **Privacy:** the log and `ToClipboardText()` carry only metadata (versions, runtime, counts, timings, errors) — never transcribed text; `--abouttest` asserts the clipboard text's contents.
