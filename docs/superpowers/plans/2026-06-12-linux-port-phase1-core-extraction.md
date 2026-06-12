# Linux Port — Phase 1: Core Extraction Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Extract the portable engine into a new `VoiceToText.Core` library (net10.0, no WinForms/NAudio/Win32) so a Linux head can share it, while keeping the Windows app byte-for-byte identical in behavior.

**Architecture:** Mechanical file moves (namespaces unchanged, so call sites don't change) plus four small seams: a central `AppPaths` class, a pluggable key-name resolver in `HotkeyDefinition`, a `CueSynth` split out of `SoundCues`, and a Windows-only relauncher shim split out of `UpdateService`. The self-test battery is the regression harness; it runs after every task.

**Tech Stack:** C#/.NET 10, Whisper.net 1.9.0 (packages move to Core), NAudio stays in the Windows head.

**Spec:** `docs/superpowers/specs/2026-06-12-linux-port-design.md` (this plan covers Phase 1 only).

---

## Conventions used by every task

- **Build:** from repo root. If `dotnet` isn't on PATH:
  `$env:DOTNET_ROOT="$env:USERPROFILE\.dotnet"; & "$env:USERPROFILE\.dotnet\dotnet.exe" build --no-incremental`
  Expected: `Build succeeded` with **0 warnings** (incremental builds hide WFO1000 — always `--no-incremental`).
- **Battery** (run from a scratch CWD, e.g. `%TEMP%\vtt-tests`, so output txt files don't litter the repo; exe path below is Debug):
  ```powershell
  $exe = "D:\ClaudeCode\voice-to-text\src\VoiceToText\bin\Debug\net10.0-windows\VoiceToText.exe"
  foreach ($f in '--vadtest','--statstest','--dashtest','--historytest','--textrulestest','--logtest','--abouttest','--widgettest','--updatecheck') {
    & $exe $f; if ($LASTEXITCODE -ne 0) { Write-Error "$f FAILED" }
  }
  & $exe --dashwindow; if ($LASTEXITCODE -ne 0) { Write-Error "--dashwindow FAILED" }
  ```
  Expected: every flag exits 0. **NEVER pass an argument to `--updatecheck`.**
- **Namespaces never change.** Files move between projects but keep `namespace VoiceToText.*`, so no `using` churn in the head.

---

### Task 0: Record the green baseline

**Files:** none modified.

- [ ] **Step 0.1:** `git status` → expect clean tree on `main`.
- [ ] **Step 0.2:** Build (see Conventions). Expected: success, 0 warnings.
- [ ] **Step 0.3:** Run the battery. Expected: all 10 flags exit 0. Copy the ten `*-output.txt` files to `%TEMP%\vtt-baseline\` for later diffing.

### Task 1: Create the empty Core project and wire it up

**Files:**
- Create: `src/VoiceToText.Core/VoiceToText.Core.csproj`
- Modify: `VoiceToText.slnx`
- Modify: `src/VoiceToText/VoiceToText.csproj`

- [ ] **Step 1.1:** Create `src/VoiceToText.Core/VoiceToText.Core.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <RootNamespace>VoiceToText</RootNamespace>
    <AssemblyName>VoiceToText.Core</AssemblyName>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Whisper.net" Version="1.9.0" />
    <PackageReference Include="Whisper.net.Runtime" Version="1.9.0" />
    <PackageReference Include="Whisper.net.Runtime.Vulkan" Version="1.9.0" />
  </ItemGroup>

</Project>
```

- [ ] **Step 1.2:** In `VoiceToText.slnx` add the project (inside the existing `/src/` folder element):

```xml
<Solution>
  <Folder Name="/src/">
    <Project Path="src/VoiceToText/VoiceToText.csproj" />
    <Project Path="src/VoiceToText.Core/VoiceToText.Core.csproj" />
  </Folder>
</Solution>
```

- [ ] **Step 1.3:** In `src/VoiceToText/VoiceToText.csproj`: delete the three `Whisper.net*` PackageReference lines (they now flow transitively from Core) and add, in the same `<ItemGroup>`:

```xml
    <ProjectReference Include="..\VoiceToText.Core\VoiceToText.Core.csproj" />
```

Keep `NAudio`. Keep the `StripForeignNativeRuntimes` target untouched (it operates on the publish dir; transitive runtimes land there the same way).

- [ ] **Step 1.4:** Build. Expected: success, 0 warnings (Core compiles empty; head unchanged semantically).
- [ ] **Step 1.5:** Commit:

```bash
git add -A && git commit -m "refactor(core): add empty VoiceToText.Core project, move Whisper.net packages there"
```

### Task 2: Move the clean portable files

**Files (git mv, content unchanged, namespaces unchanged):**
- `src/VoiceToText/Stt/ISttEngine.cs` → `src/VoiceToText.Core/Stt/ISttEngine.cs`
- `src/VoiceToText/Stt/WhisperSttEngine.cs` → `src/VoiceToText.Core/Stt/WhisperSttEngine.cs`
- `src/VoiceToText/Stt/ModelManager.cs` → `src/VoiceToText.Core/Stt/ModelManager.cs`
- `src/VoiceToText/Stt/ModelOption.cs` → `src/VoiceToText.Core/Stt/ModelOption.cs`
- `src/VoiceToText/Audio/IAudioSource.cs` → `src/VoiceToText.Core/Audio/IAudioSource.cs`
- `src/VoiceToText/Audio/SilenceDetector.cs` → `src/VoiceToText.Core/Audio/SilenceDetector.cs`
- `src/VoiceToText/Audio/AudioInputDevice.cs` → `src/VoiceToText.Core/Audio/AudioInputDevice.cs`
- `src/VoiceToText/TextProcessing/TextRules.cs` → `src/VoiceToText.Core/TextProcessing/TextRules.cs`
- `src/VoiceToText/TextProcessing/ReplacementRule.cs` → `src/VoiceToText.Core/TextProcessing/ReplacementRule.cs`
- `src/VoiceToText/History/HistoryStore.cs` → `src/VoiceToText.Core/History/HistoryStore.cs`
- `src/VoiceToText/History/HistoryEntry.cs` → `src/VoiceToText.Core/History/HistoryEntry.cs`
- `src/VoiceToText/History/HistoryService.cs` → `src/VoiceToText.Core/History/HistoryService.cs`
- `src/VoiceToText/Stats/StatsData.cs` → `src/VoiceToText.Core/Stats/StatsData.cs`
- `src/VoiceToText/Stats/StatsFormat.cs` → `src/VoiceToText.Core/Stats/StatsFormat.cs`
- `src/VoiceToText/Stats/StatsService.cs` → `src/VoiceToText.Core/Stats/StatsService.cs`
- `src/VoiceToText/Settings/AppSettings.cs` → `src/VoiceToText.Core/Settings/AppSettings.cs`
- `src/VoiceToText/Update/UpdateManifest.cs` → `src/VoiceToText.Core/Update/UpdateManifest.cs`
- `src/VoiceToText/Update/UpdateChecker.cs` → `src/VoiceToText.Core/Update/UpdateChecker.cs`
- `src/VoiceToText/Diagnostics/Log.cs` → `src/VoiceToText.Core/Diagnostics/Log.cs`
- `src/VoiceToText/Diagnostics/RuntimeProbe.cs` → `src/VoiceToText.Core/Diagnostics/RuntimeProbe.cs`
- `src/VoiceToText/Injection/ITextInjector.cs` → `src/VoiceToText.Core/Injection/ITextInjector.cs`

(Not moved yet: `HotkeyDefinition.cs`, `SoundCues.cs`, `UpdateService.cs`, `SelfTest.cs` — they need seams, Tasks 4–7. NOT moving at all: `WasapiAudioSource.cs`, `AudioDevices.cs`, `NativeForeground.cs`, `GpuInfo.cs`, `DiagnosticsInfo.cs`, everything UI/App/Overlay/Hotkeys-manager/Injection-impl.)

- [ ] **Step 2.1:** `git mv` each file above (create the Core subfolders as needed). PowerShell loop or individual `git mv src/VoiceToText/Stt/ISttEngine.cs src/VoiceToText.Core/Stt/ISttEngine.cs` etc.
- [ ] **Step 2.2:** Build. **Expected: FAILURE** — `HotkeyDefinition` (still in head) is referenced by `AppSettings` (now in Core). That's the next step's cue; do not panic-fix anything else.
- [ ] **Step 2.3:** `git mv src/VoiceToText/Hotkeys/HotkeyDefinition.cs src/VoiceToText.Core/Hotkeys/HotkeyDefinition.cs` — move it now WITH its WinForms problem; Task 4 fixes the seam. To make it compile in Core temporarily is NOT possible (it uses `Keys`), so do Task 4's edits as part of this task before building again (Tasks 2+4 land as one commit; see Task 4 steps).

> Execute Task 4's steps now, then return to Step 2.4.

- [ ] **Step 2.4:** Build. Expected: success, 0 warnings.
- [ ] **Step 2.5:** Run the battery. Expected: all flags exit 0.
- [ ] **Step 2.6:** Commit:

```bash
git add -A && git commit -m "refactor(core): move portable engine files to VoiceToText.Core (namespaces unchanged)"
```

### Task 3: Central `AppPaths` (replaces 5 scattered %APPDATA% computations)

**Files:**
- Create: `src/VoiceToText.Core/AppPaths.cs`
- Modify: `src/VoiceToText.Core/Settings/AppSettings.cs:92-94`
- Modify: `src/VoiceToText.Core/History/HistoryService.cs:14-16`
- Modify: `src/VoiceToText.Core/Stats/StatsService.cs:14-16`
- Modify: `src/VoiceToText.Core/Stt/ModelManager.cs:11-21`
- Modify: `src/VoiceToText.Core/Diagnostics/Log.cs:51-53`

- [ ] **Step 3.1:** Create `src/VoiceToText.Core/AppPaths.cs`:

```csharp
namespace VoiceToText;

/// <summary>
/// The app's storage roots, in one place. On Windows both are %APPDATA%\VoiceToText
/// (exactly the historical layout). On Linux they split per the XDG convention:
/// config (settings.json) under ~/.config, data (models/history/stats/logs) under
/// ~/.local/share — .NET maps the two SpecialFolders to those XDG dirs natively.
/// </summary>
public static class AppPaths
{
    public static string ConfigDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "VoiceToText");

    public static string DataDir => OperatingSystem.IsWindows()
        ? ConfigDir
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VoiceToText");
}
```

- [ ] **Step 3.2:** `AppSettings.cs` — replace the `SettingsPath` property body:

```csharp
    private static string SettingsPath => Path.Combine(AppPaths.ConfigDir, "settings.json");
```

- [ ] **Step 3.3:** `HistoryService.cs` — replace the `HistoryPath` property body:

```csharp
    private static string HistoryPath => Path.Combine(AppPaths.DataDir, "history.json");
```

- [ ] **Step 3.4:** `StatsService.cs` — replace the `StatsPath` property body:

```csharp
    private static string StatsPath => Path.Combine(AppPaths.DataDir, "stats.json");
```

- [ ] **Step 3.5:** `ModelManager.cs` — replace the `ModelDirectory` getter's `dir` computation:

```csharp
    public static string ModelDirectory
    {
        get
        {
            var dir = Path.Combine(AppPaths.DataDir, "models");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }
```

- [ ] **Step 3.6:** `Log.cs` — replace the `LogFolder` initializer:

```csharp
    public static string LogFolder { get; } = Path.Combine(AppPaths.DataDir, "logs");
```

- [ ] **Step 3.7:** Verify no stragglers: `grep -rn "SpecialFolder.ApplicationData" src/` → expected: only `AppPaths.cs`. (`DiagnosticsInfo.cs` line ~79 is display-only path collapsing — if it matches, leave it; it stays in the head.)
- [ ] **Step 3.8:** Build; run battery. Expected: success, all flags 0. On Windows every path is identical to before.
- [ ] **Step 3.9:** Commit: `git add -A && git commit -m "refactor(core): central AppPaths for config/data dirs (XDG-ready, Windows layout unchanged)"`

### Task 4: HotkeyDefinition seam (executed inside Task 2)

**Files:**
- Modify: `src/VoiceToText.Core/Hotkeys/HotkeyDefinition.cs`
- Create: `src/VoiceToText/Hotkeys/WinHotkeys.cs`
- Modify: `src/VoiceToText/Dashboard/SettingsPage.cs:328`
- Modify: `src/VoiceToText/Onboarding/OnboardingWizard.cs:189`
- Modify: `src/VoiceToText/Program.cs` (one line at top of `Main`)

- [ ] **Step 4.1:** In the moved `HotkeyDefinition.cs`: DELETE `FromKeyEvent` (lines 36-54) and `IsRiskyBareKey` (lines 56-70), and replace `KeyName` with a pluggable resolver:

```csharp
    /// <summary>
    /// Platform hook for naming a virtual-key code (e.g. WinForms Keys on Windows).
    /// Set once at startup by each head; the fallback is a raw hex name.
    /// </summary>
    public static Func<uint, string>? KeyNameResolver { get; set; }

    private static string KeyName(uint vk) => vk switch
    {
        VkSpace => "Space",
        _ => KeyNameResolver?.Invoke(vk) ?? $"Key 0x{vk:X2}",
    };
```

The record declaration, modifier constants, `VkSpace`, `Default`, and `Describe()` stay exactly as they are.

- [ ] **Step 4.2:** Create `src/VoiceToText/Hotkeys/WinHotkeys.cs` (the deleted members, verbatim logic, as statics/extension):

```csharp
namespace VoiceToText.Hotkeys;

/// <summary>WinForms-specific halves of HotkeyDefinition (key naming, capture, risk check).</summary>
internal static class WinHotkeys
{
    /// <summary>Wire HotkeyDefinition.Describe() to WinForms key names. Call once at startup.</summary>
    public static void RegisterKeyNames()
        => HotkeyDefinition.KeyNameResolver = vk => ((Keys)vk).ToString();

    /// <summary>
    /// Build a definition from a WinForms key event (used by the settings capture box).
    /// Returns null while only a modifier key is held on its own. A single key with
    /// no modifier is allowed — useful for dedicated/extra keyboard buttons (F13–F24,
    /// media keys, etc.).
    /// </summary>
    public static HotkeyDefinition? FromKeyEvent(Keys keyData)
    {
        var key = keyData & Keys.KeyCode;
        if (key is Keys.None or Keys.ControlKey or Keys.ShiftKey or Keys.Menu or Keys.LWin or Keys.RWin)
            return null;

        uint mods = 0;
        if ((keyData & Keys.Control) != 0) mods |= HotkeyDefinition.ModControl;
        if ((keyData & Keys.Alt) != 0) mods |= HotkeyDefinition.ModAlt;
        if ((keyData & Keys.Shift) != 0) mods |= HotkeyDefinition.ModShift;

        return new HotkeyDefinition(mods, (uint)key);
    }

    /// <summary>
    /// True if this is a bare (no-modifier) "normal typing" key — binding it globally
    /// would swallow that key everywhere, so the UI should warn before accepting it.
    /// </summary>
    public static bool IsRiskyBareKey(this HotkeyDefinition def)
    {
        if (def.Modifiers != 0)
            return false;
        var key = (Keys)def.VirtualKey;
        return key is (>= Keys.A and <= Keys.Z)
            or (>= Keys.D0 and <= Keys.D9)
            or (>= Keys.NumPad0 and <= Keys.NumPad9)
            or Keys.Space or Keys.Back or Keys.Oemcomma or Keys.OemPeriod
            or Keys.OemQuestion or Keys.OemSemicolon or Keys.Oem1 or Keys.Oemplus or Keys.OemMinus;
    }
}
```

Note `IsRiskyBareKey` is an **extension method**, so the existing call sites `_hotkey.IsRiskyBareKey()` (SettingsPage.cs:341, OnboardingWizard.cs:167) compile unchanged.

- [ ] **Step 4.3:** Update the two `FromKeyEvent` call sites: in `SettingsPage.cs:328` and `OnboardingWizard.cs:189` change `HotkeyDefinition.FromKeyEvent(keyData)` → `WinHotkeys.FromKeyEvent(keyData)`.
- [ ] **Step 4.4:** In `Program.cs`, first statement inside `Main` (BEFORE the self-test dispatch, so `--dashwindow`'s SettingsPage shows correct key names):

```csharp
        Hotkeys.WinHotkeys.RegisterKeyNames();
```

- [ ] **Step 4.5:** Behavior check (after Task 2 completes the build): `Describe()` output is identical to before for every key — Space special case kept, all others via `((Keys)vk).ToString()`.

### Task 5: SoundCues seam — pure synth to Core

**Files:**
- Create: `src/VoiceToText.Core/Audio/CueSynth.cs`
- Modify: `src/VoiceToText/Audio/SoundCues.cs`
- Modify: `src/VoiceToText/Diagnostics/SelfTest.cs:143-144`

- [ ] **Step 5.1:** Create `src/VoiceToText.Core/Audio/CueSynth.cs` — the constants and `RenderCue` lifted verbatim from `SoundCues`, plus the canonical frequency pairs:

```csharp
namespace VoiceToText.Audio;

/// <summary>
/// Pure PCM synthesis for the start/stop dictation cues (44.1 kHz mono 16-bit).
/// Deterministic and device-free — playback lives in each platform head.
/// </summary>
public static class CueSynth
{
    public const int SampleRate = 44_100;
    private const double Amplitude = 0.22;
    private const double NoteSeconds = 0.060; // ~60 ms per note
    private const double FadeSeconds = 0.006; // ~6 ms linear fade in/out per note to avoid clicks

    public static readonly double[] StartFreqs = { 660.0, 880.0 }; // rising
    public static readonly double[] StopFreqs = { 880.0, 660.0 };  // falling mirror

    /// <summary>
    /// Render a sequence of notes into a 44.1 kHz mono 16-bit PCM buffer. Pure and deterministic
    /// (no audio devices) — this is the unit-tested part. Each frequency is one note of
    /// <see cref="NoteSeconds"/> with a short linear fade in/out to avoid clicks.
    /// </summary>
    public static byte[] RenderCue(double[] freqs)
    {
        int samplesPerNote = (int)(SampleRate * NoteSeconds);
        int fadeSamples = Math.Max(1, (int)(SampleRate * FadeSeconds));
        int total = samplesPerNote * freqs.Length;
        var pcm = new byte[total * 2]; // 16-bit => 2 bytes/sample

        int pos = 0;
        foreach (var freq in freqs)
        {
            for (var i = 0; i < samplesPerNote; i++)
            {
                double t = (double)i / SampleRate;
                double sample = Math.Sin(2.0 * Math.PI * freq * t) * Amplitude;

                // Linear fade in at the note start and out at the note end.
                double env = 1.0;
                if (i < fadeSamples) env = (double)i / fadeSamples;
                else if (i >= samplesPerNote - fadeSamples) env = (double)(samplesPerNote - i) / fadeSamples;
                sample *= env;

                short s = (short)(sample * short.MaxValue);
                pcm[pos++] = (byte)(s & 0xFF);
                pcm[pos++] = (byte)((s >> 8) & 0xFF);
            }
        }

        return pcm;
    }
}
```

- [ ] **Step 5.2:** In `src/VoiceToText/Audio/SoundCues.cs`: delete the four constants (`SampleRate`, `Amplitude`, `NoteSeconds`, `FadeSeconds`) and the whole `RenderCue` method (lines 20-23, 59-92); change the `Format` field and constructor to:

```csharp
    private static readonly WaveFormat Format = new(CueSynth.SampleRate, 16, 1); // 44.1 kHz mono 16-bit PCM

    public SoundCues()
    {
        _startPcm = CueSynth.RenderCue(CueSynth.StartFreqs);
        _stopPcm = CueSynth.RenderCue(CueSynth.StopFreqs);
    }
```

Everything else in `SoundCues` (Volume, Play, Dispose, NAudio plumbing) is untouched.

- [ ] **Step 5.3:** In `SelfTest.cs` lines 143-144 replace with:

```csharp
        var startCue = CueSynth.RenderCue(CueSynth.StartFreqs);
        var stopCue = CueSynth.RenderCue(CueSynth.StopFreqs);
```

- [ ] **Step 5.4:** Build; run `--widgettest`. Expected: exit 0, output identical to baseline (same PCM bytes — the code is verbatim).
- [ ] **Step 5.5:** Commit: `git add -A && git commit -m "refactor(core): split pure cue synthesis (CueSynth) from NAudio playback"`

### Task 6: UpdateService to Core, relauncher shim stays Windows

**Files:**
- Move: `src/VoiceToText/Update/UpdateService.cs` → `src/VoiceToText.Core/Update/UpdateService.cs`
- Create: `src/VoiceToText/Update/WindowsRelauncher.cs`
- Modify: `src/VoiceToText/App/TrayApplicationContext.cs:591`

- [ ] **Step 6.1:** `git mv src/VoiceToText/Update/UpdateService.cs src/VoiceToText.Core/Update/UpdateService.cs`, then DELETE the `WriteRelauncherShim` method (lines 157-192) from the moved file. Everything else (CheckAsync, StageInstallerAsync, ComputeSha256Async, CleanStaging, StagingDir, UpdateLogPath, IsHttpFeed) stays verbatim.
- [ ] **Step 6.2:** Create `src/VoiceToText/Update/WindowsRelauncher.cs` containing the deleted method as a static (script content verbatim from the original — note `"""` raw string):

```csharp
namespace VoiceToText.Update;

/// <summary>
/// Writes the self-deleting batch shim that outlives the app during an update: it waits for
/// our PID to exit (so all file/native-DLL locks release), runs the installer silently, then
/// relaunches the app (passing the target version so it can confirm success), and finally
/// deletes the staged setup and itself. Windows-only by nature (cmd + Inno Setup).
/// </summary>
internal static class WindowsRelauncher
{
    public static string WriteShim()
    {
        Directory.CreateDirectory(UpdateService.StagingDir);
        var shimPath = Path.Combine(UpdateService.StagingDir, "relaunch.cmd");
        const string script = """
            @echo off
            setlocal
            set "APPPID=%~1"
            set "SETUP=%~2"
            set "APPEXE=%~3"
            set "LOGFILE=%~4"
            set "TARGETVER=%~5"
            set /a tries=0
            :waitloop
            tasklist /FI "PID eq %APPPID%" 2>nul | find /I "VoiceToText.exe" >nul
            if errorlevel 1 goto runsetup
            set /a tries+=1
            if %tries% geq 30 goto runsetup
            timeout /t 1 /nobreak >nul
            goto waitloop
            :runsetup
            "%SETUP%" /VERYSILENT /SUPPRESSMSGBOXES /CLOSEAPPLICATIONS /NORESTART /LOG="%LOGFILE%"
            start "" "%APPEXE%" --postupdate "%TARGETVER%"
            del /q "%SETUP%" >nul 2>&1
            endlocal
            (goto) 2>nul & del "%~f0"
            """;
        File.WriteAllText(shimPath, script);
        return shimPath;
    }
}
```

- [ ] **Step 6.3:** In `TrayApplicationContext.cs:591` change `shim = _updates.WriteRelauncherShim();` → `shim = WindowsRelauncher.WriteShim();` (the file already has `using VoiceToText.Update;` for UpdateService — verify, add if missing).
- [ ] **Step 6.4:** Build; run `--updatecheck` (NO argument). Expected: exit 0, output matches baseline.
- [ ] **Step 6.5:** Commit: `git add -A && git commit -m "refactor(core): move UpdateService to Core; Windows relauncher shim stays in the head"`

### Task 7: SelfTest split — portable tests to Core

**Files:**
- Create: `src/VoiceToText.Core/Diagnostics/CoreSelfTest.cs`
- Modify: `src/VoiceToText/Diagnostics/SelfTest.cs`

**Disposition per flag:** `--selftest`, `--vadtest`, `--statstest`, `--logtest`, `--updatecheck` move wholesale to Core (they touch only Core types). `--historytest` and `--textrulestest` split: pure checks → Core "checks" methods writing into a shared StringBuilder; UI checks stay in the head and append to the same log (output format unchanged — pure checks already run first today). `--widgettest`, `--dashtest`, `--dashwindow`, `--abouttest` stay in the head untouched.

- [ ] **Step 7.1:** Create `src/VoiceToText.Core/Diagnostics/CoreSelfTest.cs`, public static class, namespace `VoiceToText.Diagnostics`. Move these members from `SelfTest.cs` verbatim (including their `using` needs: `System.Diagnostics`, `System.Text`, `System.Text.Json`, `VoiceToText.Audio`, `VoiceToText.History`, `VoiceToText.Settings`, `VoiceToText.Stats`, `VoiceToText.Stt`, `VoiceToText.TextProcessing`, `VoiceToText.Update`, `Whisper.net`, `Whisper.net.Ggml`):
  - `Run` (rename target class only; body verbatim)
  - `RunAsync` + `GetLoadedRuntime`
  - `RunVadTest`
  - `RunStatsTest`
  - `RunLogTest`
  - `RunUpdateCheck`
  Then ADD the two fragment methods, extracted from today's `RunHistoryTest` (its pure section, lines 340-367) and `RunTextRulesTest` (its pure section, lines 514-540):

```csharp
    /// <summary>Pure history-store checks (prepend/cap/clear/round-trip). Appends to the caller's log; returns all-pass.</summary>
    public static bool HistoryStoreChecks(StringBuilder log)
    {
        var allPass = true;
        void Pass(string name, bool ok, string detail = "")
        {
            allPass &= ok;
            log.AppendLine($"[{(ok ? "PASS" : "FAIL")}] {name}{(detail.Length > 0 ? ": " + detail : "")}");
        }
        // ... the existing pure checks from SelfTest.RunHistoryTest lines 340-367, verbatim:
        // "newest first", "capped to 50", "cap keeps newest", "clear empties",
        // "transcribe seconds round-trip", "model round-trip",
        // "legacy entry has null seconds", "legacy entry has null model"
        return allPass;
    }

    /// <summary>Pure text-rules checks (replacements, spoken commands, edges). Appends to the caller's log; returns all-pass.</summary>
    public static bool TextRulesChecks(StringBuilder log)
    {
        var allPass = true;
        void Pass(string name, bool ok, string detail = "")
        {
            allPass &= ok;
            log.AppendLine($"[{(ok ? "PASS" : "FAIL")}] {name}{(detail.Length > 0 ? ": " + detail : "")}");
        }
        // ... the existing pure checks from SelfTest.RunTextRulesTest lines 514-540, verbatim,
        // including the local helpers R(), none, Vis().
        return allPass;
    }
```

  (The `// ...` lines above mean: paste the existing check code from SelfTest.cs exactly as it is today — it is being MOVED, not rewritten. The executor copies lines 340-367 / 514-540 from the current file.)

- [ ] **Step 7.2:** Slim the head's `SelfTest.cs`:
  - Delete the moved methods (`Run`, `RunAsync`, `GetLoadedRuntime`, `RunVadTest`, `RunStatsTest`, `RunLogTest`, `RunUpdateCheck`) and replace with delegations so `Program.cs` keeps compiling unchanged:

```csharp
    public static int Run(string wavPath, string outputPath, string? modelName = null)
        => CoreSelfTest.Run(wavPath, outputPath, modelName);
    public static int RunVadTest(string outputPath) => CoreSelfTest.RunVadTest(outputPath);
    public static int RunStatsTest(string outputPath) => CoreSelfTest.RunStatsTest(outputPath);
    public static int RunLogTest(string outputPath) => CoreSelfTest.RunLogTest(outputPath);
    public static int RunUpdateCheck(string outputPath, string? feedFolder)
        => CoreSelfTest.RunUpdateCheck(outputPath, feedFolder);
```

  - In `RunHistoryTest`: replace the pure-check section (lines 340-367) with `var allPass = CoreSelfTest.HistoryStoreChecks(log);` and adjust the local `Pass` helper to fold into `allPass` as today (the UI section after it is unchanged; final `allPass` combines both).
  - In `RunTextRulesTest`: same pattern with `CoreSelfTest.TextRulesChecks(log)`.
  - `RunWidgetTest`, `RunDashTest`, `RunDashWindow`, `RunAboutTest` untouched.

- [ ] **Step 7.3:** Build. Expected: success, 0 warnings.
- [ ] **Step 7.4:** Run the FULL battery. Expected: all flags 0. Diff `historytest-output.txt` and `textrulestest-output.txt` against `%TEMP%\vtt-baseline\` — check names and order must be identical.
- [ ] **Step 7.5:** Commit: `git add -A && git commit -m "refactor(core): portable self-tests move to CoreSelfTest; heads keep UI tests"`

### Task 8: Final verification sweep

- [ ] **Step 8.1:** Clean rebuild: delete `src/*/bin`, `src/*/obj`; build `--no-incremental`. Expected: 0 warnings (WFO1000 class would surface here).
- [ ] **Step 8.2:** Full battery from scratch CWD; diff ALL ten outputs against `%TEMP%\vtt-baseline\` (allow timing-value differences in `--logtest`/`--updatecheck` file sizes; check names/PASS lines must match).
- [ ] **Step 8.3:** Sanity-grep the head for dead references: `grep -rn "Whisper.net" src/VoiceToText/*.csproj` → none; `grep -rn "RenderCue" src/VoiceToText/Audio/SoundCues.cs` → only `CueSynth.RenderCue` calls.
- [ ] **Step 8.4:** Verify `publish.ps1` still works: run it; confirm `publish\VoiceToText.exe` exists and `runtimes\` contains win-x64 (+ vulkan/win-x64) only.
- [ ] **Step 8.5:** Launch the real app briefly (`dotnet run --project src/VoiceToText`): tray icon appears, dashboard opens, settings page shows the hotkey text correctly (KeyNameResolver proof), then exit.

### Task 9: Version bump + Windows release (proves zero regression in production)

- [ ] **Step 9.1:** Bump `<Version>` in `src/VoiceToText/VoiceToText.csproj` to `0.8.14`.
- [ ] **Step 9.2:** Commit: `git add -A && git commit -m "v0.8.14: shared-core refactor (engine extracted to VoiceToText.Core, no user-facing change)"`
- [ ] **Step 9.3:** Invoke the `/release` skill (ships installer to GitHub Releases + local dev feed, verifies manifest + SHA). All its verify steps must pass.

---

## Self-review checklist (run after writing, before executing)

- Spec coverage: Phase 1 of the spec = Core project, interface-bearing files moved, AppPaths, Windows release. ✔ (Platform interfaces `IAudioCapture`/`ICuePlayer`/`IHotkeyService` etc. are Phase 2 — they get DEFINED when the Linux head needs them; existing `IAudioSource`/`ITextInjector` seams move now.)
- No placeholders: the two `// ...` markers in Task 7 are move instructions referencing exact line ranges of existing code, not TBDs.
- Type consistency: `CueSynth.SampleRate` (public const) used by head's `Format`; `WinHotkeys.IsRiskyBareKey` extension keeps `_hotkey.IsRiskyBareKey()` call sites; `CoreSelfTest` naming avoids the head's `SelfTest` class collision.
