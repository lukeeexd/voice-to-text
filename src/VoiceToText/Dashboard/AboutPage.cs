using System.Diagnostics;
using System.Drawing;
using VoiceToText.Dashboard.Controls;
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
    private readonly AppSettings _settings;
    private readonly Label _title = new()
    {
        Text = "About", AutoSize = true, Location = new Point(20, 16),
        ForeColor = Theme.TextPrimary, Font = Theme.Heading,
    };
    private readonly Label _subtitle = new()
    {
        AutoSize = true, Location = new Point(20, 48), ForeColor = Theme.TextSecondary,
        Font = Theme.Caption, Text = "Voice to Text — local, offline dictation.",
    };
    private readonly Panel _card = new() { BackColor = Theme.CardBg };
    private readonly DarkButton _check = MakeButton("Check for updates", primary: true);
    private readonly DarkButton _openLog = MakeButton("Open log folder", primary: false);
    private readonly DarkButton _copy = MakeButton("Copy diagnostics", primary: false);
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

    private static DarkButton MakeButton(string text, bool primary) => new()
    {
        Variant = primary ? DarkButtonVariant.Primary : DarkButtonVariant.Secondary,
        Text = text,
        AutoSize = false,
        Size = new Size(132, 30),
        Font = Theme.Caption,
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

        // Dispose the previous rows before discarding them — Controls.Clear() detaches but does not
        // dispose, which would leak label HWNDs/fonts over repeated reloads (matches HistoryPage).
        var discarded = new Control[_card.Controls.Count];
        _card.Controls.CopyTo(discarded, 0);
        _card.Controls.Clear();
        foreach (var c in discarded) c.Dispose();

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
