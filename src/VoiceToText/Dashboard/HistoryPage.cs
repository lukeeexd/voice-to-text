using System.Drawing;
using VoiceToText.Dashboard.Controls;
using VoiceToText.Diagnostics;
using VoiceToText.History;
using VoiceToText.Settings;
using VoiceToText.Stt;

namespace VoiceToText.Dashboard;

/// <summary>
/// The History page: a scrollable, newest-first list of recent dictations (opt-in). Each row
/// shows time · app · word-count and the text, with a Copy action; Clear all wipes the log.
/// Shows an off/empty message when history is disabled or empty. Reloads when shown.
/// </summary>
internal sealed class HistoryPage : UserControl
{
    private readonly HistoryService _history;
    private readonly AppSettings _settings;

    private readonly Label _title = new()
    {
        Text = "History",
        AutoSize = true,
        Location = new Point(20, 16),
        ForeColor = Theme.TextPrimary,
        Font = Theme.Heading,
    };
    private readonly Label _subtitle = new()
    {
        AutoSize = true,
        Location = new Point(20, 48),
        ForeColor = Theme.TextSecondary,
        Font = Theme.Caption,
        Text = "Your last 50 dictations, kept only on this PC.",
    };
    private readonly DarkButton _clear = new()
    {
        Variant = DarkButtonVariant.Secondary,
        Text = "Clear all",
        Size = new Size(80, 30),
        TabStop = false,
    };
    private readonly FlowLayoutPanel _list = new()
    {
        FlowDirection = FlowDirection.TopDown,
        WrapContents = false,
        AutoScroll = true,
        BackColor = Theme.WindowBg,
    };
    private readonly Label _empty = new()
    {
        AutoSize = false,
        TextAlign = ContentAlignment.MiddleCenter,
        ForeColor = Theme.TextSecondary,
        Font = Theme.Empty,
        BackColor = Theme.WindowBg,
        Visible = false,
    };

    public HistoryPage(HistoryService history, AppSettings settings)
    {
        _history = history;
        _settings = settings;
        BackColor = Theme.WindowBg;
        DoubleBuffered = true;
        _clear.Click += OnClear;
        Controls.AddRange(new Control[] { _title, _subtitle, _clear, _list, _empty });
    }

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

    /// <summary>Rebuild the list from the current settings + stored entries.</summary>
    public void Reload()
    {
        DoLayout();

        _list.SuspendLayout();
        // Dispose the previous rows (and their child controls) before discarding them —
        // Controls.Clear() detaches but does not dispose, which would leak HWNDs over reloads.
        var discarded = new Control[_list.Controls.Count];
        _list.Controls.CopyTo(discarded, 0);
        _list.Controls.Clear();
        foreach (var c in discarded) c.Dispose();

        var entries = _settings.HistoryEnabled ? _history.Entries : Array.Empty<HistoryEntry>();
        foreach (var entry in entries)
            _list.Controls.Add(BuildRow(entry));
        _list.ResumeLayout();

        // A vertical scrollbar may have appeared during layout, shrinking the client width;
        // re-fit each row to the final width so no horizontal scrollbar shows.
        foreach (Control c in _list.Controls)
            if (c is Panel p) LayoutRow(p);

        bool any = _list.Controls.Count > 0;
        _list.Visible = any;
        _empty.Visible = !any;
        _empty.Text = _settings.HistoryEnabled
            ? "No dictations recorded yet."
            : "History is off — enable it in Settings to keep your recent dictations.";
        _clear.Enabled = any;
    }

    private void OnClear(object? sender, EventArgs e)
    {
        if (_history.Entries.Count == 0) return;
        const string msg = "Erase all recorded dictation history?";
        const string title = "Voice to Text";
        // Route through the form's modal guard so a tray re-activation can't hide this dialog (freeze);
        // if somehow unparented, fall back to an OWNERLESS box (which also can't be covered).
        var choice = FindForm() is DashboardForm form
            ? form.ShowOwnedDialog(() => MessageBox.Show(this, msg, title, MessageBoxButtons.YesNo, MessageBoxIcon.Warning))
            : MessageBox.Show(msg, title, MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
        if (choice != DialogResult.Yes) return;
        _history.Clear();
        Reload();
    }

    private int RowWidth() => Math.Max(40, _list.ClientSize.Width - 6);

    private void DoLayout()
    {
        const int pad = 20;
        int w = Width - pad * 2;
        if (w <= 0 || Height <= 0) return;

        _clear.Location = new Point(Width - pad - _clear.Width, 16);
        int top = 78;
        _list.SetBounds(pad, top, w, Height - top - pad);
        _empty.SetBounds(pad, top, w, Height - top - pad);

        foreach (Control c in _list.Controls)
            if (c is Panel p) LayoutRow(p);
    }

    private Control BuildRow(HistoryEntry entry)
    {
        var card = new Panel
        {
            BackColor = Theme.CardBg,
            Margin = new Padding(0, 0, 0, 8),
            Width = RowWidth(),
        };

        var meta = new Label
        {
            AutoSize = false,
            AutoEllipsis = true,
            Location = new Point(12, 8),
            ForeColor = Theme.TextSecondary,
            Font = Theme.Caption,
            Text = $"{FormatTime(entry.Time)}   ·   {entry.App}   ·   {entry.Words} words"
                 + (entry.Model is { Length: > 0 } m ? $"   ·   {ModelOption.ShortLabel(m)}" : "")
                 + (entry.TranscribeSeconds is double s ? $"   ·   {s:0.0}s" : ""),
        };

        var copy = new LinkLabel
        {
            AutoSize = true,
            Text = "Copy",
            Font = Theme.Caption,
            LinkColor = Theme.Accent,
            ActiveLinkColor = Theme.AccentLight,
        };
        copy.LinkClicked += (_, _) => CopyText(copy, entry.Text);

        var body = new Label
        {
            AutoSize = true,
            Location = new Point(12, 28),
            ForeColor = Theme.TextPrimary,
            Text = string.IsNullOrEmpty(entry.Text) ? "(empty)" : entry.Text,
        };

        card.Controls.Add(meta);
        card.Controls.Add(copy);
        card.Controls.Add(body);
        card.Tag = (body, meta);
        LayoutRow(card);
        return card;
    }

    // Width-track + height-to-wrapped-text for one row; place Copy at the row's top-right,
    // then fit the meta line to the space left of Copy so a long line ellipsizes instead
    // of ever running under the Copy link.
    private void LayoutRow(Panel card)
    {
        card.Width = RowWidth();
        var (body, meta) = ((Label Body, Label Meta))card.Tag!;
        body.MaximumSize = new Size(Math.Max(40, card.Width - 24), 0);
        card.Height = body.Top + body.PreferredHeight + 12;

        int copyLeft = card.Width;
        foreach (Control c in card.Controls)
            if (c is LinkLabel link)
            {
                link.Location = new Point(card.Width - link.PreferredWidth - 14, 8);
                copyLeft = link.Left;
            }

        meta.SetBounds(meta.Left, 8, Math.Max(40, copyLeft - meta.Left - 8), meta.PreferredHeight);
    }

    private static string FormatTime(DateTime t)
    {
        var today = DateTime.Today;
        if (t.Date == today) return $"Today {t:HH:mm}";
        if (t.Date == today.AddDays(-1)) return $"Yesterday {t:HH:mm}";
        return t.ToString("MMM d, HH:mm");
    }

    // Copy a row's text with visible feedback. Clipboard.SetText already retries internally; on
    // failure (the OS or our own paste-injector holding the clipboard) we log it and flip the link
    // to "Copy failed" instead of silently doing nothing. The link resets to "Copy" on the next
    // Reload (revisiting the tab). No timer => no disposed-control hazard.
    private static void CopyText(LinkLabel link, string text)
    {
        if (string.IsNullOrEmpty(text)) return;
        try
        {
            Clipboard.SetText(text);
            SetCopyState(link, "Copied ✓", Theme.Accent);
        }
        catch (Exception ex)
        {
            Log.Error("History: copy to clipboard failed", ex);
            SetCopyState(link, "Copy failed", Theme.Warning);
        }
    }

    private static void SetCopyState(LinkLabel link, string text, Color color)
    {
        link.Text = text;
        link.LinkColor = color;
        if (link.Parent is { } card)
        {
            link.Left = card.Width - link.PreferredWidth - 14; // keep right-aligned
            if (card.Tag is ValueTuple<Label, Label> tag) // re-fit the meta so the wider link can't overlap it
                tag.Item2.Width = Math.Max(40, link.Left - tag.Item2.Left - 8);
        }
    }
}
