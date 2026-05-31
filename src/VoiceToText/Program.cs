using VoiceToText.App;
using VoiceToText.Diagnostics;

namespace VoiceToText;

internal static class Program
{
    /// <summary>
    /// Entry point. Normally launches the tray application. If started with
    /// "--selftest &lt;wav&gt; [outFile]" it runs a one-shot transcription so the
    /// Whisper + GPU pipeline can be verified without a microphone.
    /// </summary>
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length > 0 && args[0].Equals("--selftest", StringComparison.OrdinalIgnoreCase))
        {
            var wav = args.Length > 1 ? args[1] : "jfk.wav";
            var outFile = args.Length > 2 ? args[2] : "selftest-output.txt";
            var model = args.Length > 3 ? args[3] : null;
            return SelfTest.Run(wav, outFile, model);
        }

        if (args.Length > 0 && args[0].Equals("--vadtest", StringComparison.OrdinalIgnoreCase))
        {
            var outFile = args.Length > 1 ? args[1] : "vadtest-output.txt";
            return SelfTest.RunVadTest(outFile);
        }

        ApplicationConfiguration.Initialize();
        using var context = new TrayApplicationContext();
        Application.Run(context);
        return 0;
    }
}
