using VoiceToText.App;
using VoiceToText.Diagnostics;

namespace VoiceToText;

internal static class Program
{
    /// <summary>
    /// Entry point. Diagnostics ("--selftest", "--vadtest", "--updatecheck", "--widgettest",
    /// "--statstest", "--dashtest", "--dashwindow", "--textrulestest", "--historytest", "--logtest",
    /// "--abouttest") run headless and exit. Otherwise launches the tray app as
    /// a single instance.
    /// "--postupdate &lt;ver&gt;" is passed by the update relauncher so the app can confirm the upgrade.
    /// </summary>
    [STAThread]
    private static int Main(string[] args)
    {
        // Wire HotkeyDefinition.Describe() to WinForms key names before anything
        // (self-tests included) renders a hotkey label.
        Hotkeys.WinHotkeys.RegisterKeyNames();

        if (args.Length > 0 && args[0].Equals("--selftest", StringComparison.OrdinalIgnoreCase))
        {
            var wav = args.Length > 1 ? args[1] : "jfk.wav";
            var outFile = args.Length > 2 ? args[2] : "selftest-output.txt";
            var model = args.Length > 3 ? args[3] : null;
            return SelfTest.Run(wav, outFile, model);
        }

        if (args.Length > 0 && args[0].Equals("--vadtest", StringComparison.OrdinalIgnoreCase))
            return SelfTest.RunVadTest(args.Length > 1 ? args[1] : "vadtest-output.txt");

        if (args.Length > 0 && args[0].Equals("--updatecheck", StringComparison.OrdinalIgnoreCase))
            return SelfTest.RunUpdateCheck("updatecheck-output.txt", args.Length > 1 ? args[1] : null);

        if (args.Length > 0 && args[0].Equals("--widgettest", StringComparison.OrdinalIgnoreCase))
            return SelfTest.RunWidgetTest("widgettest-output.txt");

        if (args.Length > 0 && args[0].Equals("--statstest", StringComparison.OrdinalIgnoreCase))
            return SelfTest.RunStatsTest("statstest-output.txt");

        if (args.Length > 0 && args[0].Equals("--dashtest", StringComparison.OrdinalIgnoreCase))
            return SelfTest.RunDashTest("dashtest-output.txt");

        if (args.Length > 0 && args[0].Equals("--historytest", StringComparison.OrdinalIgnoreCase))
            return SelfTest.RunHistoryTest("historytest-output.txt");

        if (args.Length > 0 && args[0].Equals("--dashwindow", StringComparison.OrdinalIgnoreCase))
            return SelfTest.RunDashWindow("dashwindow-output.txt");

        if (args.Length > 0 && args[0].Equals("--textrulestest", StringComparison.OrdinalIgnoreCase))
            return SelfTest.RunTextRulesTest("textrulestest-output.txt");

        if (args.Length > 0 && args[0].Equals("--logtest", StringComparison.OrdinalIgnoreCase))
            return SelfTest.RunLogTest("logtest-output.txt");

        if (args.Length > 0 && args[0].Equals("--controllertest", StringComparison.OrdinalIgnoreCase))
            return SelfTest.RunControllerTest(args.Length > 1 ? args[1] : "controllertest-output.txt");

        if (args.Length > 0 && args[0].Equals("--abouttest", StringComparison.OrdinalIgnoreCase))
            return SelfTest.RunAboutTest("abouttest-output.txt");

        Diagnostics.Log.Info($"Voice to Text v{System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString(3)} starting.");

        // Single-instance guard. The name matches the installer's AppMutex so Inno's
        // Restart Manager reliably closes this instance during an update, and so the
        // post-update relaunch / start-on-login can't spawn a second tray icon.
        using var mutex = new Mutex(initiallyOwned: true, "VoiceToText_SingleInstance_Mutex", out var isNewInstance);
        if (!isNewInstance)
            return 0;

        string? postUpdateTarget = null;
        if (args.Length > 0 && args[0].Equals("--postupdate", StringComparison.OrdinalIgnoreCase))
            postUpdateTarget = args.Length > 1 ? args[1] : "";

        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        ApplicationConfiguration.Initialize();
        using var context = new TrayApplicationContext(postUpdateTarget);
        Application.Run(context);
        return 0;
    }
}
