# Linux Port — Phase 2a: Headless Linux Foundation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A `VoiceToText.Linux` executable whose engine (record → VAD → Whisper → text rules → history/stats) runs and self-tests green on real Linux in GitHub Actions CI, controllable via a `--toggle` unix-socket IPC — no GUI yet (tray/hotkeys/paste are phase 2b).

**Architecture:** A portable `DictationController` state machine joins Core (used by the Linux head only; Windows keeps its proven path untouched). The Linux head is a console-style daemon: PulseAudio capture/cue playback via a 5-function `libpulse-simple` P/Invoke, single-instance + IPC over a unix socket, CLI dispatch of the Core self-test battery. CI on ubuntu-22.04/24.04 runs the battery from the publish output, with a real PulseAudio null-source so the P/Invoke layer is empirically validated, plus a CPU `--selftest` with the Tiny model.

**Tech Stack:** .NET 10 (`net10.0`), libpulse-simple ABI (P/Invoke), System.Net.Sockets UnixDomainSocket, GitHub Actions.

**Spec:** `docs/superpowers/specs/2026-06-12-linux-port-design.md` (phase 2, first half).

---

## Conventions

- Build/battery commands as in the phase 1 plan (Windows side). The Linux project compiles on Windows (`dotnet build` of net10.0 is OS-agnostic); only RUNNING it needs Linux (CI).
- All new Linux-head code: namespaces `VoiceToText.Linux.*`. Core additions keep `VoiceToText.*`.
- No version bump in this phase (the Windows app does not change → no release).

### Task 1: Core — `ICuePlayer` + `DictationController`

**Files:**
- Create: `src/VoiceToText.Core/Audio/ICuePlayer.cs`
- Create: `src/VoiceToText.Core/App/DictationController.cs`

- [ ] **Step 1.1:** `src/VoiceToText.Core/Audio/ICuePlayer.cs`:

```csharp
namespace VoiceToText.Audio;

/// <summary>Plays the start/stop dictation cues. Implementations must never throw
/// into the dictation path and must honor a 0..1 volume.</summary>
public interface ICuePlayer
{
    float Volume { get; set; }
    void PlayStart();
    void PlayStop();
}
```

- [ ] **Step 1.2:** `src/VoiceToText.Core/App/DictationController.cs` — the portable engine loop, mirroring the Windows flow (`TrayApplicationContext.StopAndTranscribeAsync`, lines 294–322) minus UI concerns:

```csharp
using VoiceToText.Audio;
using VoiceToText.Diagnostics;
using VoiceToText.History;
using VoiceToText.Injection;
using VoiceToText.Settings;
using VoiceToText.Stats;
using VoiceToText.Stt;
using VoiceToText.TextProcessing;

namespace VoiceToText.App;

public enum DictationState { Idle, Recording, Transcribing }

/// <summary>
/// Portable dictation orchestrator: toggle → record (auto-stop on silence) →
/// transcribe → text rules → inject → stats/history. Single-threaded by contract:
/// call <see cref="ToggleAsync"/> from one logical context (the IPC/hotkey pump).
/// Errors never escape — they surface via <see cref="StatusChanged"/> and the log.
/// </summary>
public sealed class DictationController(
    IAudioSource audio,
    ISttEngine stt,
    ITextInjector injector,
    ICuePlayer? cues,
    AppSettings settings,
    StatsService stats,
    HistoryService history)
{
    private int _busy; // 0 = idle/recording, 1 = transcribing (toggle ignored)

    public DictationState State { get; private set; } = DictationState.Idle;

    /// <summary>UI hook: state changes + user-facing status text ("Transcribing…", errors).</summary>
    public event Action<DictationState, string>? StatusChanged;

    /// <summary>Raised with the final injected text (after rules). The Linux head logs it.</summary>
    public event Action<string>? Transcribed;

    /// <summary>Name recorded in per-app stats. Phase 2b can supply a real value; defaults to "Unknown".</summary>
    public string AppNameProvider { get; set; } = "Unknown";

    public async Task ToggleAsync()
    {
        if (Interlocked.CompareExchange(ref _busy, 0, 0) == 1)
            return; // mid-transcription: ignore, exactly like the Windows head

        if (State == DictationState.Idle)
            await StartAsync().ConfigureAwait(false);
        else
            await StopAndTranscribeAsync().ConfigureAwait(false);
    }

    private Task StartAsync()
    {
        try
        {
            audio.SilenceDetected += OnSilence;
            audio.Start(settings.InputDeviceId, settings.AutoStopEnabled, settings.AutoStopSilenceSeconds);
            State = DictationState.Recording;
            if (settings.SoundCuesEnabled && cues is not null)
            {
                cues.Volume = (float)settings.SoundCuesVolume;
                cues.PlayStart();
            }
            StatusChanged?.Invoke(State, "Recording");
        }
        catch (Exception ex)
        {
            audio.SilenceDetected -= OnSilence;
            State = DictationState.Idle;
            Log.Error("Could not start recording", ex);
            StatusChanged?.Invoke(State, $"Could not start recording: {ex.Message}");
        }
        return Task.CompletedTask;
    }

    private void OnSilence() => _ = StopAndTranscribeAsync();

    private async Task StopAndTranscribeAsync()
    {
        if (Interlocked.Exchange(ref _busy, 1) == 1)
            return;
        try
        {
            audio.SilenceDetected -= OnSilence;
            State = DictationState.Transcribing;
            if (settings.SoundCuesEnabled && cues is not null)
            {
                cues.Volume = (float)settings.SoundCuesVolume;
                cues.PlayStop();
            }
            StatusChanged?.Invoke(State, "Transcribing");

            var samples = await audio.StopAndGetSamplesAsync().ConfigureAwait(false);
            var seconds = samples.Length / 16_000.0;

            var sw = System.Diagnostics.Stopwatch.StartNew();
            var text = await stt.TranscribeAsync(samples).ConfigureAwait(false);
            var transcribeSeconds = sw.Elapsed.TotalSeconds;

            text = TextRules.Apply(text, settings.Replacements, settings.SpokenCommandsEnabled);

            if (!string.IsNullOrWhiteSpace(text))
            {
                injector.Inject(text);
                var words = StatsData.CountWords(text);
                var app = AppNameProvider;
                stats.Record(words, seconds, app);
                if (settings.HistoryEnabled)
                    history.Record(text, words, app, transcribeSeconds, settings.ModelType.ToString());
                Transcribed?.Invoke(text);
            }

            State = DictationState.Idle;
            StatusChanged?.Invoke(State, "Done");
        }
        catch (Exception ex)
        {
            State = DictationState.Idle;
            Log.Error("Dictation failed", ex);
            StatusChanged?.Invoke(State, $"Dictation failed: {ex.Message}");
        }
        finally
        {
            Interlocked.Exchange(ref _busy, 0);
        }
    }
}
```

- [ ] **Step 1.3:** CHECK the real signatures before building: `StatsService.Record(...)` and `HistoryService.Record(...)` — open both files and match the controller's calls to the actual parameter lists (the Windows call sites are `_stats.Record(words, seconds, app)` and `_history.Record(text, words, app, transcribeSeconds, model)`; adjust the controller if the service signatures differ, e.g. a `DateOnly` parameter).
- [ ] **Step 1.4:** Build (`--no-incremental`). Expected: success, 0 warnings.
- [ ] **Step 1.5:** Commit: `git add -A && git commit -m "feat(core): portable DictationController + ICuePlayer (Linux head orchestration)"`

### Task 2: Core — controller self-test with fakes

**Files:**
- Modify: `src/VoiceToText.Core/Diagnostics/CoreSelfTest.cs` (add `RunControllerTest`)
- Modify: `src/VoiceToText/Diagnostics/SelfTest.cs` (delegation)
- Modify: `src/VoiceToText/Program.cs` (new flag)

- [ ] **Step 2.1:** Add to `CoreSelfTest`:

```csharp
    /// <summary>End-to-end engine flow with fakes: toggle → samples → STT → rules → inject → stats/history.</summary>
    public static int RunControllerTest(string outputPath)
    {
        var log = new StringBuilder();
        var allPass = true;
        void Pass(string name, bool ok, string detail = "")
        {
            allPass &= ok;
            log.AppendLine($"[{(ok ? "PASS" : "FAIL")}] {name}{(detail.Length > 0 ? ": " + detail : "")}");
        }

        try
        {
            var audio = new FakeAudioSource(new float[16_000]); // 1 s of silence-shaped samples
            var stt = new FakeStt("hello new line world");
            var injector = new FakeInjector();
            var settings = new AppSettings
            {
                SoundCuesEnabled = false, HistoryEnabled = true,
                AutoStopEnabled = false, SpokenCommandsEnabled = true,
            };
            var stats = new StatsService();
            var statsBefore = stats.Data.TotalDictations;
            var history = new HistoryService();
            history.Data.Entries.Clear(); // in-memory only; Save is never called

            var c = new App.DictationController(audio, stt, injector, null, settings, stats, history);
            var states = new List<DictationState>();
            c.StatusChanged += (s, _) => states.Add(s);

            c.ToggleAsync().GetAwaiter().GetResult();           // start
            Pass("recording after first toggle", c.State == App.DictationState.Recording, $"={c.State}");
            c.ToggleAsync().GetAwaiter().GetResult();           // stop + transcribe
            Pass("idle after second toggle", c.State == App.DictationState.Idle, $"={c.State}");
            Pass("audio was started and stopped", audio.Started && audio.Stopped);
            Pass("text rules applied (new line)", injector.LastText == "hello\nworld", injector.LastText ?? "null");
            Pass("stats recorded one dictation", stats.Data.TotalDictations == statsBefore + 1);
            Pass("history recorded", history.Data.Entries.Count == 1 && history.Data.Entries[0].Text == "hello\nworld");
            Pass("state sequence", states.Count >= 3
                && states[0] == App.DictationState.Recording
                && states[^1] == App.DictationState.Idle, string.Join(",", states));

            // Empty transcript: nothing injected/recorded.
            var audio2 = new FakeAudioSource(new float[16_000]);
            var c2 = new App.DictationController(audio2, new FakeStt("   "), injector, null, settings, stats, history);
            injector.LastText = null;
            c2.ToggleAsync().GetAwaiter().GetResult();
            c2.ToggleAsync().GetAwaiter().GetResult();
            Pass("blank transcript not injected", injector.LastText is null);
            Pass("blank transcript not counted", stats.Data.TotalDictations == statsBefore + 1);
        }
        catch (Exception ex)
        {
            Pass("controller flow ran without throwing", false, $"{ex.GetType().Name}: {ex.Message}");
        }

        log.AppendLine(allPass ? "ALL CONTROLLER TESTS PASSED" : "SOME CONTROLLER TESTS FAILED");
        var result = log.ToString();
        File.WriteAllText(outputPath, result);
        Console.WriteLine(result);
        return allPass ? 0 : 1;
    }

    private sealed class FakeAudioSource(float[] samples) : Audio.IAudioSource
    {
        public bool Started; public bool Stopped;
        public bool IsRecording { get; private set; }
        public event Action? SilenceDetected { add { } remove { } }
        public event Action<float>? LevelChanged { add { } remove { } }
        public event Action<Exception>? RecordingFailed { add { } remove { } }
        public void Start(string? deviceId, bool autoStop, double autoStopSilenceSeconds)
        { Started = true; IsRecording = true; }
        public Task<float[]> StopAndGetSamplesAsync()
        { Stopped = true; IsRecording = false; return Task.FromResult(samples); }
    }

    private sealed class FakeStt(string result) : Stt.ISttEngine
    {
        public Task LoadAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task<string> TranscribeAsync(float[] s, CancellationToken ct = default) => Task.FromResult(result);
        public void Dispose() { }
    }

    private sealed class FakeInjector : Injection.ITextInjector
    {
        public string? LastText;
        public void Inject(string text) => LastText = text;
    }
```

  NOTE: if `IAudioSource`'s events are plain `event Action?` fields, drop the explicit add/remove bodies and declare them as the interface requires. Check `StatsService.Record`/`HistoryService.Record` parameter order against the real signatures (as in Task 1) and adapt the assertions.

- [ ] **Step 2.2:** Head delegation in `SelfTest.cs`: `public static int RunControllerTest(string outputPath) => CoreSelfTest.RunControllerTest(outputPath);` and in `Program.cs` after the `--logtest` dispatch:

```csharp
        if (args.Length > 0 && args[0].Equals("--controllertest", StringComparison.OrdinalIgnoreCase))
            return SelfTest.RunControllerTest(args.Length > 1 ? args[1] : "controllertest-output.txt");
```

- [ ] **Step 2.3:** Build; run `--controllertest` on Windows. Expected: exit 0, ALL CONTROLLER TESTS PASSED. Beware: `StatsService`/`HistoryService` constructors LOAD the user's real files — the test must not call `Save()`/`Record` persistence side effects? `stats.Record(...)` SAVES. If so, point the test's services at temp state: check whether `StatsService.Record` persists; if it does, set the test to tolerate it (it writes the user's real stats!) — in that case add internal-only constructors `StatsService(bool load)` is NOT allowed (no behavior change); instead redirect `AppPaths` is static… SOLUTION: run the controller test with `HOME`/`APPDATA` redirected — NOT possible in-process. PRAGMATIC: keep `HistoryEnabled = true` but accept one extra stats entry on dev machines is unacceptable. → Give `StatsService` and `HistoryService` a public constructor overload `(string path)` used by tests and the existing parameterless one delegating to the default path. This is additive (no behavior change). Implement that in this step, pointing the test at `%TEMP%` files, and assert against those.
- [ ] **Step 2.4:** Run the full Windows battery (now 11 flags). Expected: all 0.
- [ ] **Step 2.5:** Commit: `git add -A && git commit -m "feat(core): controller self-test with fakes (--controllertest)"`

### Task 3: Linux project skeleton + CLI dispatch

**Files:**
- Create: `src/VoiceToText.Linux/VoiceToText.Linux.csproj`
- Create: `src/VoiceToText.Linux/Program.cs`
- Modify: `VoiceToText.slnx`

- [ ] **Step 3.1:** `src/VoiceToText.Linux/VoiceToText.Linux.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <RootNamespace>VoiceToText.Linux</RootNamespace>
    <AssemblyName>voicetotext</AssemblyName>
    <InvariantGlobalization>false</InvariantGlobalization>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="..\VoiceToText.Core\VoiceToText.Core.csproj" />
  </ItemGroup>

</Project>
```

- [ ] **Step 3.2:** Add `<Project Path="src/VoiceToText.Linux/VoiceToText.Linux.csproj" />` to the slnx `/src/` folder.
- [ ] **Step 3.3:** `src/VoiceToText.Linux/Program.cs` — self-test dispatch (mirrors the Windows head's portable flags) + daemon mode + IPC client forwarding:

```csharp
using VoiceToText.Diagnostics;
using VoiceToText.Hotkeys;
using VoiceToText.Linux.Platform;

namespace VoiceToText.Linux;

internal static class Program
{
    private static int Main(string[] args)
    {
        HotkeyDefinition.KeyNameResolver = LinuxKeyNames.Resolve;

        if (args.Length > 0)
        {
            var flag = args[0].ToLowerInvariant();
            switch (flag)
            {
                case "--selftest":
                    return CoreSelfTest.Run(
                        args.Length > 1 ? args[1] : "jfk.wav",
                        args.Length > 2 ? args[2] : "selftest-output.txt",
                        args.Length > 3 ? args[3] : null);
                case "--vadtest": return CoreSelfTest.RunVadTest(Out(args, "vadtest"));
                case "--statstest": return CoreSelfTest.RunStatsTest(Out(args, "statstest"));
                case "--logtest": return CoreSelfTest.RunLogTest(Out(args, "logtest"));
                case "--controllertest": return CoreSelfTest.RunControllerTest(Out(args, "controllertest"));
                case "--updatecheck": return CoreSelfTest.RunUpdateCheck("updatecheck-output.txt", args.Length > 1 ? args[1] : null);
                case "--historytest": return RunFragment("historytest-output.txt", "HISTORY", CoreSelfTest.HistoryStoreChecks);
                case "--textrulestest": return RunFragment("textrulestest-output.txt", "TEXTRULES", CoreSelfTest.TextRulesChecks);
                case "--audiotest": return PulseSelfTest.Run(Out(args, "audiotest"));
                case "--toggle": return IpcClient.Send("toggle");
                case "--status": return IpcClient.Send("status");
                case "--version":
                    Console.WriteLine(typeof(Program).Assembly.GetName().Version?.ToString(3));
                    return 0;
            }
        }

        return Daemon.Run(args);
    }

    private static string Out(string[] args, string name)
        => args.Length > 1 ? args[1] : $"{name}-output.txt";

    private static int RunFragment(string outputPath, string label, Func<System.Text.StringBuilder, bool> checks)
    {
        var log = new System.Text.StringBuilder();
        var allPass = checks(log);
        log.AppendLine(allPass ? $"ALL {label} TESTS PASSED" : $"SOME {label} TESTS FAILED");
        File.WriteAllText(outputPath, log.ToString());
        Console.WriteLine(log.ToString());
        return allPass ? 0 : 1;
    }
}
```

  (`LinuxKeyNames`, `PulseSelfTest`, `IpcClient`, `Daemon` arrive in Tasks 4–6; to keep this task compiling, create them as minimal stubs IN THIS TASK with their final signatures: `LinuxKeyNames.Resolve(uint) => $"Key 0x{vk:X2}"`, others `=> 1` with a "not implemented" message. Each later task replaces its stub.)

- [ ] **Step 3.4:** Build the SOLUTION (`--no-incremental`). Expected: 3 projects build, 0 warnings. Run the Windows battery once (Core untouched, but cheap insurance).
- [ ] **Step 3.5:** Commit: `git add -A && git commit -m "feat(linux): VoiceToText.Linux head skeleton with portable self-test dispatch"`

### Task 4: PulseAudio P/Invoke — capture + cues

**Files:**
- Create: `src/VoiceToText.Linux/Platform/PulseNative.cs`
- Create: `src/VoiceToText.Linux/Platform/PulseAudioSource.cs`
- Create: `src/VoiceToText.Linux/Platform/PulseCuePlayer.cs`
- Create: `src/VoiceToText.Linux/Platform/PulseSelfTest.cs` (replaces stub)

- [ ] **Step 4.1:** `PulseNative.cs` — the complete binding (ABI stable since PulseAudio 0.9):

```csharp
using System.Runtime.InteropServices;

namespace VoiceToText.Linux.Platform;

/// <summary>
/// Minimal libpulse-simple binding: blocking record/playback streams. The pulse
/// daemon (PulseAudio or PipeWire's pipewire-pulse) does all resampling/mixing.
/// </summary>
internal static partial class PulseNative
{
    private const string LibSimple = "libpulse-simple.so.0";
    private const string LibPulse = "libpulse.so.0";

    public const int PA_STREAM_PLAYBACK = 1;
    public const int PA_STREAM_RECORD = 2;
    public const int PA_SAMPLE_S16LE = 3;
    public const int PA_SAMPLE_FLOAT32LE = 5;

    [StructLayout(LayoutKind.Sequential)]
    public struct pa_sample_spec
    {
        public int format;     // pa_sample_format_t
        public uint rate;
        public byte channels;  // struct is 4-byte aligned => marshaled size 12, matching C
    }

    [LibraryImport(LibSimple, StringMarshalling = StringMarshalling.Utf8)]
    public static partial IntPtr pa_simple_new(
        string? server, string name, int dir, string? dev, string streamName,
        in pa_sample_spec ss, IntPtr channelMap, IntPtr bufferAttr, out int error);

    [LibraryImport(LibSimple)]
    public static partial int pa_simple_read(IntPtr s, IntPtr data, nuint bytes, out int error);

    [LibraryImport(LibSimple)]
    public static partial int pa_simple_write(IntPtr s, IntPtr data, nuint bytes, out int error);

    [LibraryImport(LibSimple)]
    public static partial int pa_simple_drain(IntPtr s, out int error);

    [LibraryImport(LibSimple)]
    public static partial void pa_simple_free(IntPtr s);

    [LibraryImport(LibPulse)]
    private static partial IntPtr pa_strerror(int error);

    public static string ErrorText(int error)
        => Marshal.PtrToStringUTF8(pa_strerror(error)) ?? $"pulse error {error}";
}
```

- [ ] **Step 4.2:** `PulseAudioSource.cs` — `IAudioSource` over a blocking read thread. Requests 16 kHz mono float32 directly (no resampler needed). Mirrors `WasapiAudioSource` semantics: 5-minute cap, RMS → `LevelChanged`, `SilenceDetector` → `SilenceDetected` once, `RecordingFailed` on read errors:

```csharp
using System.Runtime.InteropServices;
using VoiceToText.Audio;

namespace VoiceToText.Linux.Platform;

public sealed class PulseAudioSource : IAudioSource
{
    private const int SampleRate = 16_000;
    private const int ChunkSamples = 1_600; // 100 ms reads
    private static readonly int MaxSamples = SampleRate * 60 * 5; // 5-minute cap

    private readonly object _lock = new();
    private List<float>? _samples;
    private Thread? _thread;
    private volatile bool _stopRequested;
    private TaskCompletionSource<bool>? _stopped;
    private SilenceDetector? _silenceDetector;
    private bool _silenceSignaled;

    public bool IsRecording { get; private set; }

    public event Action? SilenceDetected;
    public event Action<float>? LevelChanged;
    public event Action<Exception>? RecordingFailed;

    public void Start(string? deviceId, bool autoStop, double autoStopSilenceSeconds)
    {
        if (IsRecording)
            throw new InvalidOperationException("Already recording.");

        var spec = new PulseNative.pa_sample_spec
        { format = PulseNative.PA_SAMPLE_FLOAT32LE, rate = SampleRate, channels = 1 };
        var stream = PulseNative.pa_simple_new(
            null, "VoiceToText", PulseNative.PA_STREAM_RECORD, deviceId, "dictation",
            in spec, IntPtr.Zero, IntPtr.Zero, out var err);
        if (stream == IntPtr.Zero)
            throw new InvalidOperationException($"PulseAudio: {PulseNative.ErrorText(err)}");

        _samples = new List<float>(SampleRate * 30);
        _silenceDetector = autoStop ? new SilenceDetector(autoStopSilenceSeconds) : null;
        _silenceSignaled = false;
        _stopRequested = false;
        _stopped = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        IsRecording = true;

        _thread = new Thread(() => ReadLoop(stream)) { IsBackground = true, Name = "pulse-capture" };
        _thread.Start();
    }

    private void ReadLoop(IntPtr stream)
    {
        var buf = new float[ChunkSamples];
        var handle = GCHandle.Alloc(buf, GCHandleType.Pinned);
        try
        {
            while (!_stopRequested)
            {
                if (PulseNative.pa_simple_read(stream, handle.AddrOfPinnedObject(),
                        (nuint)(ChunkSamples * sizeof(float)), out var err) < 0)
                {
                    if (!_stopRequested)
                        RecordingFailed?.Invoke(new IOException($"PulseAudio read: {PulseNative.ErrorText(err)}"));
                    break;
                }

                lock (_lock)
                {
                    if (_samples is not null && _samples.Count < MaxSamples)
                        _samples.AddRange(buf);
                }

                double sum = 0;
                for (var i = 0; i < buf.Length; i++) sum += buf[i] * buf[i];
                var rms = Math.Sqrt(sum / buf.Length);
                LevelChanged?.Invoke((float)rms);

                var detector = _silenceDetector;
                if (detector is not null && !_silenceSignaled
                    && detector.Process(rms, (double)ChunkSamples / SampleRate))
                {
                    _silenceSignaled = true;
                    SilenceDetected?.Invoke();
                }
            }
        }
        finally
        {
            handle.Free();
            PulseNative.pa_simple_free(stream);
            _stopped?.TrySetResult(true);
        }
    }

    public async Task<float[]> StopAndGetSamplesAsync()
    {
        if (!IsRecording)
            return [];
        _stopRequested = true;
        await (_stopped?.Task ?? Task.CompletedTask).ConfigureAwait(false);

        float[] result;
        lock (_lock)
        {
            result = _samples?.ToArray() ?? [];
            _samples = null;
        }
        _silenceDetector = null;
        _thread = null;
        IsRecording = false;
        return result;
    }
}
```

- [ ] **Step 4.3:** `PulseCuePlayer.cs` — `ICuePlayer` rendering via Core's `CueSynth`, software gain, fire-and-forget playback thread (must never throw into dictation):

```csharp
using System.Runtime.InteropServices;
using VoiceToText.Audio;
using VoiceToText.Diagnostics;

namespace VoiceToText.Linux.Platform;

public sealed class PulseCuePlayer : ICuePlayer
{
    private readonly byte[] _startPcm = CueSynth.RenderCue(CueSynth.StartFreqs);
    private readonly byte[] _stopPcm = CueSynth.RenderCue(CueSynth.StopFreqs);
    private float _volume = 1f;

    public float Volume
    {
        get => _volume;
        set => _volume = Math.Clamp(value, 0f, 1f);
    }

    public void PlayStart() => Play(_startPcm);
    public void PlayStop() => Play(_stopPcm);

    private void Play(byte[] pcm)
    {
        var vol = _volume;
        new Thread(() =>
        {
            try
            {
                var scaled = vol >= 0.999f ? pcm : Scale(pcm, vol);
                var spec = new PulseNative.pa_sample_spec
                { format = PulseNative.PA_SAMPLE_S16LE, rate = CueSynth.SampleRate, channels = 1 };
                var s = PulseNative.pa_simple_new(
                    null, "VoiceToText", PulseNative.PA_STREAM_PLAYBACK, null, "cue",
                    in spec, IntPtr.Zero, IntPtr.Zero, out var err);
                if (s == IntPtr.Zero) { Log.Error($"Cue playback: {PulseNative.ErrorText(err)}"); return; }
                try
                {
                    var h = GCHandle.Alloc(scaled, GCHandleType.Pinned);
                    try { PulseNative.pa_simple_write(s, h.AddrOfPinnedObject(), (nuint)scaled.Length, out _); }
                    finally { h.Free(); }
                    PulseNative.pa_simple_drain(s, out _);
                }
                finally { PulseNative.pa_simple_free(s); }
            }
            catch (Exception ex)
            {
                Log.Error("Sound cue playback failed", ex); // missing/locked device must never break dictation
            }
        }) { IsBackground = true, Name = "pulse-cue" }.Start();
    }

    private static byte[] Scale(byte[] pcm, float vol)
    {
        var scaled = new byte[pcm.Length];
        for (var i = 0; i < pcm.Length; i += 2)
        {
            var s = (short)(pcm[i] | (pcm[i + 1] << 8));
            s = (short)(s * vol);
            scaled[i] = (byte)(s & 0xFF);
            scaled[i + 1] = (byte)((s >> 8) & 0xFF);
        }
        return scaled;
    }
}
```

- [ ] **Step 4.4:** `PulseSelfTest.cs` (replaces the stub) — `--audiotest`: connect a record stream to the default source, read 0.5 s, report. Exits 0 with `SKIPPED` when no pulse daemon exists (so dev boxes without audio still pass), but the CI workflow asserts the output contains `PASS` (Task 7 starts a real daemon there — this is the empirical P/Invoke validation):

```csharp
using System.Text;
using VoiceToText.Audio;

namespace VoiceToText.Linux.Platform;

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
            // 0.5 s at 16 kHz ≈ 8000 samples; accept generous jitter from buffering.
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
```

- [ ] **Step 4.5:** Build solution. Expected: 0 warnings. (Behavior is CI-verified in Task 7.)
- [ ] **Step 4.6:** Commit: `git add -A && git commit -m "feat(linux): PulseAudio capture + cue playback via libpulse-simple"`

### Task 5: Single instance + IPC over a unix socket

**Files:**
- Create: `src/VoiceToText.Linux/Platform/IpcServer.cs`
- Create: `src/VoiceToText.Linux/Platform/IpcClient.cs` (replaces stub)

- [ ] **Step 5.1:** `IpcServer.cs`:

```csharp
using System.Net.Sockets;
using System.Text;
using VoiceToText.Diagnostics;

namespace VoiceToText.Linux.Platform;

/// <summary>
/// Single-instance guard + command channel: a unix socket in $XDG_RUNTIME_DIR.
/// If binding fails because a live instance owns the socket, Start returns false.
/// A stale socket file (after a crash) is detected by a failed connect and removed.
/// </summary>
public sealed class IpcServer : IDisposable
{
    public static string SocketPath
    {
        get
        {
            var dir = Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR");
            if (string.IsNullOrEmpty(dir)) dir = Path.GetTempPath();
            return Path.Combine(dir, "voicetotext.sock");
        }
    }

    private Socket? _listener;
    private readonly Func<string, string> _handle;

    public IpcServer(Func<string, string> handleCommand) => _handle = handleCommand;

    public bool Start()
    {
        if (File.Exists(SocketPath))
        {
            // Live instance or stale file? A connect attempt tells us.
            try
            {
                using var probe = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
                probe.Connect(new UnixDomainSocketEndPoint(SocketPath));
                return false; // someone is listening — we are the second instance
            }
            catch (SocketException)
            {
                try { File.Delete(SocketPath); } catch { /* race: another starter won */ }
            }
        }

        _listener = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        _listener.Bind(new UnixDomainSocketEndPoint(SocketPath));
        _listener.Listen(4);
        _ = AcceptLoopAsync(_listener);
        return true;
    }

    private async Task AcceptLoopAsync(Socket listener)
    {
        while (true)
        {
            Socket client;
            try { client = await listener.AcceptAsync().ConfigureAwait(false); }
            catch (ObjectDisposedException) { return; }
            _ = Task.Run(async () =>
            {
                try
                {
                    using var c = client;
                    var buf = new byte[256];
                    var n = await c.ReceiveAsync(buf).ConfigureAwait(false);
                    var cmd = Encoding.UTF8.GetString(buf, 0, n).Trim();
                    var reply = _handle(cmd);
                    await c.SendAsync(Encoding.UTF8.GetBytes(reply)).ConfigureAwait(false);
                }
                catch (Exception ex) { Log.Error("IPC request failed", ex); }
            });
        }
    }

    public void Dispose()
    {
        _listener?.Dispose();
        try { File.Delete(SocketPath); } catch { /* best effort */ }
    }
}
```

- [ ] **Step 5.2:** `IpcClient.cs` (replaces stub):

```csharp
using System.Net.Sockets;
using System.Text;

namespace VoiceToText.Linux.Platform;

internal static class IpcClient
{
    /// <summary>Send a command to the running instance. Exit 0 on a reply, 1 if none is running.</summary>
    public static int Send(string command)
    {
        try
        {
            using var s = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            s.Connect(new UnixDomainSocketEndPoint(IpcServer.SocketPath));
            s.Send(Encoding.UTF8.GetBytes(command));
            var buf = new byte[1024];
            var n = s.Receive(buf);
            Console.WriteLine(Encoding.UTF8.GetString(buf, 0, n));
            return 0;
        }
        catch (SocketException)
        {
            Console.Error.WriteLine("voicetotext is not running.");
            return 1;
        }
    }
}
```

- [ ] **Step 5.3:** Build; commit: `git add -A && git commit -m "feat(linux): unix-socket single instance + IPC (toggle/status)"`

### Task 6: Daemon wiring + Linux key names

**Files:**
- Create: `src/VoiceToText.Linux/Daemon.cs` (replaces stub)
- Create: `src/VoiceToText.Linux/Platform/LinuxKeyNames.cs` (replaces stub)

- [ ] **Step 6.1:** `LinuxKeyNames.cs` — name the stored Win32 VK codes portably (settings keep Win32 VK semantics across OSes; full keysym mapping arrives with the X11 hotkey in phase 2b):

```csharp
namespace VoiceToText.Linux.Platform;

internal static class LinuxKeyNames
{
    public static string Resolve(uint vk) => vk switch
    {
        >= 0x41 and <= 0x5A => ((char)vk).ToString(),          // A–Z
        >= 0x30 and <= 0x39 => ((char)vk).ToString(),          // 0–9
        >= 0x70 and <= 0x87 => $"F{vk - 0x6F}",                // F1–F24
        0x20 => "Space", 0x0D => "Enter", 0x09 => "Tab", 0x1B => "Esc",
        0x08 => "Backspace", 0x2E => "Delete", 0x24 => "Home", 0x23 => "End",
        _ => $"Key 0x{vk:X2}",
    };
}
```

- [ ] **Step 6.2:** `Daemon.cs` — single instance, engine assembly, IPC command handling; `--toggle` IS the dictation trigger in this phase:

```csharp
using VoiceToText.App;
using VoiceToText.Diagnostics;
using VoiceToText.History;
using VoiceToText.Injection;
using VoiceToText.Linux.Platform;
using VoiceToText.Settings;
using VoiceToText.Stats;
using VoiceToText.Stt;

namespace VoiceToText.Linux;

/// <summary>Phase-2a daemon: engine + IPC, no GUI. The transcript goes to the log,
/// stdout, and history; clipboard/paste injection arrives in phase 2b.</summary>
internal sealed class ConsoleInjector : ITextInjector
{
    public void Inject(string text) => Console.WriteLine($"TRANSCRIPT: {text}");
}

internal static class Daemon
{
    public static int Run(string[] args)
    {
        var settings = AppSettings.Load();
        var stats = new StatsService();
        var history = new HistoryService();
        var stt = new WhisperSttEngine(settings.ModelType, settings.Language);
        var controller = new DictationController(
            new PulseAudioSource(), stt, new ConsoleInjector(), new PulseCuePlayer(),
            settings, stats, history);
        controller.StatusChanged += (state, msg) => Console.WriteLine($"[{state}] {msg}");

        using var ipc = new IpcServer(cmd => cmd switch
        {
            "toggle" => Toggle(controller),
            "status" => controller.State.ToString(),
            "ping" => "pong",
            _ => $"unknown command: {cmd}",
        });
        if (!ipc.Start())
        {
            Console.Error.WriteLine("voicetotext is already running. Use --toggle.");
            return 2;
        }

        Log.Info($"voicetotext daemon started (model {settings.ModelType}).");
        Console.WriteLine("voicetotext daemon running. Trigger dictation with: voicetotext --toggle");
        _ = stt.LoadAsync(); // background model load/warm-up, mirrors the Windows head

        using var quit = new ManualResetEventSlim(false);
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; quit.Set(); };
        AppDomain.CurrentDomain.ProcessExit += (_, _) => quit.Set();
        quit.Wait();
        Log.Info("voicetotext daemon stopping.");
        return 0;
    }

    private static string Toggle(DictationController controller)
    {
        _ = controller.ToggleAsync();
        return controller.State == DictationState.Idle ? "starting" : "stopping";
    }
}
```

- [ ] **Step 6.3:** Build solution; run Windows battery (still green). Commit: `git add -A && git commit -m "feat(linux): daemon wiring — IPC-triggered dictation, no GUI yet"`

### Task 7: GitHub Actions CI on real Linux

**Files:**
- Create: `.github/workflows/linux.yml`

- [ ] **Step 7.1:** The workflow. Key points: ubuntu matrix; PulseAudio daemon with a null sink (its monitor becomes the default *source*) so `--audiotest` must genuinely PASS (not skip); Tiny-model `--selftest` with the model cached; jfk.wav fetched from whisper.cpp; transcript asserted.

```yaml
name: linux

on:
  push:
    branches: [main]
  pull_request:

jobs:
  battery:
    strategy:
      matrix:
        os: [ubuntu-22.04, ubuntu-24.04]
    runs-on: ${{ matrix.os }}
    steps:
      - uses: actions/checkout@v4

      - uses: actions/setup-dotnet@v4
        with:
          dotnet-version: 10.0.x

      - name: Build (Release)
        run: dotnet build src/VoiceToText.Linux/VoiceToText.Linux.csproj -c Release --no-incremental

      - name: Publish linux-x64
        run: dotnet publish src/VoiceToText.Linux/VoiceToText.Linux.csproj -c Release -r linux-x64 --self-contained -o publish-linux

      - name: Start PulseAudio with a virtual source
        run: |
          sudo apt-get update && sudo apt-get install -y pulseaudio pulseaudio-utils
          pulseaudio --start --exit-idle-time=-1
          pactl load-module module-null-sink sink_name=vmic
          pactl set-default-source vmic.monitor
          pactl info | grep -E "Server Name|Default Source"

      - name: Core battery
        working-directory: publish-linux
        run: |
          set -e
          for f in --vadtest --statstest --historytest --textrulestest --logtest --controllertest --updatecheck; do
            ./voicetotext $f
          done

      - name: Audio P/Invoke roundtrip (must PASS, not skip)
        working-directory: publish-linux
        run: |
          ./voicetotext --audiotest
          grep -q "PASS" audiotest-output.txt

      - name: Cache Whisper tiny model
        uses: actions/cache@v4
        with:
          path: ~/.local/share/VoiceToText/models
          key: whisper-tiny-v1

      - name: Full STT selftest (CPU, tiny model)
        working-directory: publish-linux
        run: |
          curl -sL -o jfk.wav https://github.com/ggml-org/whisper.cpp/raw/master/samples/jfk.wav
          ./voicetotext --selftest jfk.wav selftest-output.txt Tiny
          cat selftest-output.txt
          grep -qi "ask not what your country" selftest-output.txt

      - name: Upload outputs on failure
        if: failure()
        uses: actions/upload-artifact@v4
        with:
          name: selftest-outputs-${{ matrix.os }}
          path: publish-linux/*-output.txt
```

  NOTE for the executor: `--updatecheck` runs with NO argument (it self-feeds in /tmp). The Whisper.net CPU runtime ships in `publish-linux/runtimes/linux-x64`; if `--selftest` fails to load the native lib, inspect with `ldd publish-linux/runtimes/linux-x64/*.so` in a debug step. The `10.0.x` dotnet-version may need `include-prerelease: true` if .NET 10 is still RC on the runner image — check the action's output and adjust.

- [ ] **Step 7.2:** Commit and push: `git add -A && git commit -m "ci(linux): battery + pulse roundtrip + CPU STT on ubuntu matrix" && git push`
- [ ] **Step 7.3:** Watch the run: `gh run watch --exit-status` (or `gh run list --workflow=linux.yml`). Iterate on failures — each fix is its own commit (`fix(ci): ...` / `fix(linux): ...`). The phase gate: BOTH matrix legs fully green, with `--audiotest` PASS (not SKIPPED) proving the pulse P/Invoke on real Linux and the `--selftest` transcript containing the JFK phrase proving the whole Whisper pipeline.

### Task 8: Windows regression sweep

- [ ] **Step 8.1:** Clean `--no-incremental` solution build → 0 warnings; full Windows battery (11 flags incl. `--controllertest`) → all exit 0.
- [ ] **Step 8.2:** `publish.ps1` → exe version unchanged (0.8.14), runtimes win-x64 + vulkan only. (No release: the Windows app did not change.)

---

## Self-review checklist

- Spec coverage (phase-2a slice): controller ✔, pulse audio ✔ (default source only, per spec), cues ✔, single-instance socket = the IPC trigger tier ✔, portable battery on Linux ✔, CI with empirical audio test + CPU STT e2e ✔. Hotkeys/injection/tray/UI → phase 2b by design.
- Placeholders: none — stubs in Task 3 are explicit, signature-final, and each is replaced by a named later task.
- Type consistency: `DictationController(audio, stt, injector, cues, settings, stats, history)` matches Task 6's construction; `ICuePlayer.Volume` float vs settings double cast at call site; `IpcServer.SocketPath` shared by client; `CoreSelfTest.HistoryStoreChecks`/`TextRulesChecks` signatures match Program.cs usage.
- Known verify-at-execution points (flagged in steps): `StatsService.Record`/`HistoryService.Record` exact signatures (Task 1.3/2.1), `IAudioSource` event declaration style (Task 2.1), test-path constructor overloads for the services (Task 2.3), dotnet 10 availability on runners (Task 7.1).
