using System.Drawing;
using VoiceToText.History;
using VoiceToText.Settings;

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
    private readonly Button _clear = new()
    {
        Text = "Clear all",
        FlatStyle = FlatStyle.Flat,
        Size = new Size(80, 26),
        BackColor = Theme.CardBg,
        ForeColor = Theme.TextSecondary,
        Font = Theme.Caption,
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
        _clear.FlatAppearance.BorderColor = Theme.CardBorder;
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
        if (MessageBox.Show(this, "Erase all recorded dictation history?", "Voice to Text",
                MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
            return;
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
            AutoSize = true,
            Location = new Point(12, 8),
            ForeColor = Theme.TextSecondary,
            Font = Theme.Caption,
            Text = $"{FormatTime(entry.Time)}   ·   {entry.App}   ·   {entry.Words} words",
        };

        var copy = new LinkLabel
        {
            AutoSize = true,
            Text = "Copy",
            Font = Theme.Caption,
            LinkColor = Theme.Accent,
            ActiveLinkColor = Theme.AccentLight,
        };
        copy.LinkClicked += (_, _) => CopyText(entry.Text);

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
        card.Tag = body;
        LayoutRow(card);
        return card;
    }

    // Width-track + height-to-wrapped-text for one row; place Copy at the row's top-right.
    private void LayoutRow(Panel card)
    {
        card.Width = RowWidth();
        var body = (Label)card.Tag!;
        body.MaximumSize = new Size(Math.Max(40, card.Width - 24), 0);
        card.Height = body.Top + body.PreferredHeight + 12;
        foreach (Control c in card.Controls)
            if (c is LinkLabel link)
                link.Location = new Point(card.Width - link.PreferredWidth - 14, 8);
    }

    private static string FormatTime(DateTime t)
    {
        var today = DateTime.Today;
        if (t.Date == today) return $"Today {t:HH:mm}";
        if (t.Date == today.AddDays(-1)) return $"Yesterday {t:HH:mm}";
        return t.ToString("MMM d, HH:mm");
    }

    private static void CopyText(string text)
    {
        try { if (!string.IsNullOrEmpty(text)) Clipboard.SetText(text); }
        catch { /* clipboard contention — best effort */ }
    }
}
