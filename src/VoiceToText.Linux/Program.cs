using VoiceToText.Diagnostics;
using VoiceToText.Hotkeys;
using VoiceToText.Linux.Platform;

namespace VoiceToText.Linux;

internal static class Program
{
    /// <summary>
    /// Entry point. Self-test flags mirror the Windows head's portable battery and
    /// run headless. `--toggle`/`--status` forward to a running daemon over the
    /// single-instance unix socket; with no args the dictation daemon starts.
    /// </summary>
    private static int Main(string[] args)
    {
        HotkeyDefinition.KeyNameResolver = LinuxKeyNames.Resolve;

        if (args.Length > 0)
        {
            switch (args[0].ToLowerInvariant())
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
                case "--historytest": return RunFragment(Out(args, "historytest"), "HISTORY", CoreSelfTest.HistoryStoreChecks);
                case "--textrulestest": return RunFragment(Out(args, "textrulestest"), "TEXTRULES", CoreSelfTest.TextRulesChecks);
                case "--audiotest": return PulseSelfTest.Run(Out(args, "audiotest"));
                case "--uitest": return Ui.UiSelfTest.Run(Out(args, "uitest"));
                case "--toggle": return IpcClient.Send("toggle");
                case "--status": return IpcClient.Send("status");
                case "--settings": return IpcClient.Send("settings");
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
