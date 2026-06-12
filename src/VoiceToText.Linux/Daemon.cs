using VoiceToText.App;
using VoiceToText.Diagnostics;
using VoiceToText.History;
using VoiceToText.Injection;
using VoiceToText.Linux.Platform;
using VoiceToText.Settings;
using VoiceToText.Stats;
using VoiceToText.Stt;

namespace VoiceToText.Linux;

/// <summary>Phase-2a injector: the transcript goes to stdout (and history/stats).
/// Clipboard + paste injection arrive with the GUI in phase 2b.</summary>
internal sealed class ConsoleInjector : ITextInjector
{
    public void Inject(string text) => Console.WriteLine($"TRANSCRIPT: {text}");
}

/// <summary>
/// The phase-2a daemon: engine + IPC, no GUI yet. Dictation is triggered by running
/// `voicetotext --toggle` (which any desktop's keyboard-shortcut settings can bind).
/// </summary>
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
            Console.Error.WriteLine("voicetotext is already running. Use --toggle to dictate.");
            return 2;
        }

        Log.Info($"voicetotext daemon started (model {settings.ModelType}).");
        Console.WriteLine("voicetotext daemon running. Trigger dictation with: voicetotext --toggle");
        _ = stt.LoadAsync(); // background model download/load + warm-up, like the Windows head

        using var quit = new ManualResetEventSlim(false);
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            quit.Set();
        };
        AppDomain.CurrentDomain.ProcessExit += (_, _) => quit.Set();
        quit.Wait();
        Log.Info("voicetotext daemon stopping.");
        return 0;
    }

    private static string Toggle(DictationController controller)
    {
        var wasIdle = controller.State == DictationState.Idle;
        _ = controller.ToggleAsync();
        return wasIdle ? "starting" : "stopping";
    }
}
