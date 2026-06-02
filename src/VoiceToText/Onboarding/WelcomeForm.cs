using System.Drawing;
using VoiceToText.Dashboard;
using VoiceToText.Settings;
using VoiceToText.Stt;

namespace VoiceToText.Onboarding;

/// <summary>
/// First-run welcome dialog: orients the user (what the app is, how to dictate, privacy),
/// shows the one-time speech-model download status, and offers a jump to Settings. UI only —
/// the tray decides when to show it and persists the "seen" flag. Shown once.
/// </summary>
internal sealed class WelcomeForm : Form
{
    /// <summary>Raised when the user clicks "Open Settings"; the host opens the dashboard Settings page.</summary>
    public event Action? OpenSettingsRequested;

    public WelcomeForm(AppSettings settings)
    {
        Text = "Welcome to Voice to Text";
        BackColor = Theme.WindowBg;
        ForeColor = Theme.TextPrimary;
        ClientSize = new Size(480, 360);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        ShowInTaskbar = true;
        try { Icon = Icon.ExtractAssociatedIcon(Environment.ProcessPath!); }
        catch { /* no embedded icon under the debugger host */ }

        var heading = new Label
        {
            Text = "Welcome to Voice to Text",
            Font = Theme.Heading,
            ForeColor = Theme.TextPrimary,
            AutoSize = true,
            Location = new Point(24, 22),
        };

        var hotkey = settings.Hotkey.Describe();
        var body = new Label
        {
            AutoSize = false,
            Location = new Point(24, 64),
            Size = new Size(432, 170),
            ForeColor = Theme.TextSecondary,
            Font = Theme.NavItem,
            Text =
                "Local, offline dictation — your voice becomes text in any app. Nothing ever leaves your PC.\r\n\r\n" +
                $"To dictate: press your hotkey ({hotkey}), speak, then stop — the text is typed into whatever app you're focused on.\r\n\r\n" +
                "Pick your microphone or change the hotkey any time in Settings.",
        };

        bool modelReady = false;
        try { modelReady = ModelManager.IsModelPresent(settings.ModelType); }
        catch { /* best-effort status only */ }
        var status = new Label
        {
            AutoSize = false,
            Location = new Point(24, 244),
            Size = new Size(432, 44),
            Font = Theme.Caption,
            ForeColor = modelReady ? Color.FromArgb(0x7B, 0xD8, 0x8F) : Theme.Warning,
            Text = modelReady
                ? "Speech model ready ✓"
                : "Downloading the speech model now (one-time, ~1.5 GB). Dictation works as soon as it's ready.",
        };

        var openSettings = new Button
        {
            Text = "Open Settings",
            FlatStyle = FlatStyle.Flat,
            Size = new Size(120, 32),
            Location = new Point(220, 312),
            BackColor = Theme.CardBg,
            ForeColor = Theme.NavActiveText,
        };
        openSettings.FlatAppearance.BorderColor = Theme.CardBorder;
        openSettings.Click += (_, _) => { OpenSettingsRequested?.Invoke(); Close(); };

        var gotIt = new Button
        {
            Text = "Got it",
            FlatStyle = FlatStyle.Flat,
            Size = new Size(96, 32),
            Location = new Point(360, 312),
            BackColor = Theme.Accent,
            ForeColor = Color.White,
        };
        gotIt.FlatAppearance.BorderSize = 0;
        gotIt.FlatAppearance.MouseOverBackColor = Theme.AccentLight;
        gotIt.Click += (_, _) => Close();
        AcceptButton = gotIt;

        Controls.AddRange(new Control[] { heading, body, status, openSettings, gotIt });
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        DarkTitleBar.Apply(Handle);
    }
}
