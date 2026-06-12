using Avalonia;
using Avalonia.Controls;
using VoiceToText.Diagnostics;
using VoiceToText.Linux.Platform;
using VoiceToText.Linux.Ui;

namespace VoiceToText.Linux;

/// <summary>
/// The dictation daemon: engine + IPC + tray-resident Avalonia UI. Dictation is
/// triggered by the global hotkey (X11 sessions) or by `voicetotext --toggle`
/// (any desktop's keyboard-shortcut settings can bind it).
/// </summary>
internal static class Daemon
{
    public static int Run(string[] args)
    {
        using var services = new AppServices();
        using var ipc = new IpcServer(services.HandleCommand);
        if (!ipc.Start())
        {
            Console.Error.WriteLine("voicetotext is already running. Use --toggle to dictate or --settings to configure.");
            return 2;
        }

        Log.Info($"voicetotext daemon started (model {services.Settings.ModelType}, hotkey tier {services.HotkeyTier}).");
        Console.WriteLine("voicetotext daemon running. Trigger dictation with the hotkey or: voicetotext --toggle");
        services.Controller.Transcribed += t => Console.WriteLine($"TRANSCRIPT: {t}");
        services.WarmUp();

        VttApp.Services = services;
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args, ShutdownMode.OnExplicitShutdown);

        Log.Info("voicetotext daemon stopping.");
        return 0;
    }

    private static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<VttApp>().UsePlatformDetect().LogToTrace();
}
