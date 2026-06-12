using Avalonia;
using Avalonia.Headless;

namespace VoiceToText.Linux.Ui;

internal sealed class HeadlessAppBuilder
{
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<VttApp>().UseHeadless(new AvaloniaHeadlessPlatformOptions());
}

/// <summary>
/// `--uitest`: the Linux equivalent of the Windows `--dashwindow` smoke — construct,
/// show and close the settings and first-run windows on the headless Avalonia
/// platform. Catches constructor/layout crashes in CI without a display server.
/// </summary>
internal static class UiSelfTest
{
    public static int Run(string outputPath)
    {
        try
        {
            using var session = HeadlessUnitTestSession.StartNew(typeof(HeadlessAppBuilder));
            session.Dispatch(() =>
            {
                var services = new AppServices();
                var dashboard = new Dashboard.DashboardWindow(services);
                dashboard.Show();
                foreach (var page in new[]
                {
                    Dashboard.LinuxPageKind.Dashboard, Dashboard.LinuxPageKind.Settings,
                    Dashboard.LinuxPageKind.TextRules, Dashboard.LinuxPageKind.History,
                    Dashboard.LinuxPageKind.About,
                })
                    dashboard.ShowPage(page);
                dashboard.Close();
                var firstRun = new FirstRunWindow(services);
                firstRun.Show();
                firstRun.Close();
            }, CancellationToken.None).GetAwaiter().GetResult();

            File.WriteAllText(outputPath, "LINUX UI OK (dashboard with 5 pages + first-run constructed, shown, closed)");
            Console.WriteLine("LINUX UI OK");
            return 0;
        }
        catch (Exception ex)
        {
            File.WriteAllText(outputPath, "ERROR: " + ex);
            Console.WriteLine("ERROR: " + ex);
            return 1;
        }
    }
}
