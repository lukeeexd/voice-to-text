using System.Drawing;
using VoiceToText.Dashboard.Controls;
using VoiceToText.Settings;
using VoiceToText.TextProcessing;

namespace VoiceToText.Dashboard;

/// <summary>
/// Editor for the text rules applied to every transcription before pasting: a spoken-commands
/// toggle, a grid of find→replace rules, and a live "Try it" preview. Save persists to AppSettings.
/// </summary>
internal sealed class TextRulesPage : UserControl
{
    private readonly AppSettings _settings;
    private const int CardWidth = 700;

    private readonly ToggleSwitch _commandsToggle = new();
    private readonly DataGridView _grid = new();
    private readonly TextBox _previewInput = new() { BorderStyle = BorderStyle.None, Text = "i pushed to github new line all tests pass" };
    private readonly TextBox _previewOutput = new() { BorderStyle = BorderStyle.None, ReadOnly = true, Multiline = true };
    private readonly DarkButton _saveButton = new() { Variant = DarkButtonVariant.Primary, Text = "Save", Size = new Size(96, 30) };
    private readonly Label _savedLabel = new() { AutoSize = true, ForeColor = Theme.Accent, Visible = false, Text = "Saved ✓" };
    private bool _layingOutCards;

    public TextRulesPage(AppSettings settings)
    {
        _settings = settings;
        BackColor = Theme.WindowBg;
        ForeColor = Theme.TextPrimary;
        BuildUi();
        LoadFromSettings();
    }

    private void BuildUi()
    {
        ConfigureGrid();

        _commandsToggle.CheckedChanged += (_, _) => UpdatePreview();
        _grid.CellEndEdit += (_, _) => UpdatePreview();
        _grid.RowsRemoved += (_, _) => UpdatePreview();
        _previewInput.TextChanged += (_, _) => UpdatePreview();

        // --- Card 1: spoken commands ---
        var commands = new SectionCard("Spoken commands") { Width = CardWidth, Margin = new Padding(0, 0, 0, 14) };
        var commandsHint = new Label { ForeColor = Theme.TextSecondary, Font = Theme.Caption, Text = "Say \"new line\" or \"new paragraph\" while dictating and they become line breaks instead of literal words." };
        commands.AddRow("Turn spoken commands into formatting", _commandsToggle, commandsHint);

        // --- Card 2: replacements (grid as a full-width content row) ---
        var replacements = new SectionCard("Replacements") { Width = CardWidth, Margin = new Padding(0, 0, 0, 14) };
        var gridPanel = new Panel { BackColor = Theme.CardBg, Height = 250, Padding = new Padding(4, 2, 4, 8) };
        _grid.Dock = DockStyle.Fill;
        gridPanel.Controls.Add(_grid);
        replacements.Content.Controls.Add(gridPanel);

        // --- Card 3: try it (input + green output, both in dark fields, full-width rows) ---
        var tryIt = new SectionCard("Try it") { Width = CardWidth, Margin = new Padding(0, 0, 0, 14) };

        var inputField = new DarkField(_previewInput, 200) { Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right };
        var inputRow = new Panel { BackColor = Theme.CardBg, Height = 38, Margin = Padding.Empty };
        inputField.SetBounds(4, 4, inputRow.Width - 8, 30);
        inputRow.Controls.Add(inputField);
        tryIt.Content.Controls.Add(inputRow);

        var outputField = new DarkField(_previewOutput, 200, 64) { Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right };
        // DarkField's ctor forces inner.ForeColor = TextPrimary; restore the green preview text.
        _previewOutput.ForeColor = Color.FromArgb(0x9B, 0xE6, 0xA8);
        var outputRow = new Panel { BackColor = Theme.CardBg, Height = 72, Margin = Padding.Empty };
        outputField.SetBounds(4, 4, outputRow.Width - 8, 64);
        outputRow.Controls.Add(outputField);
        tryIt.Content.Controls.Add(outputRow);

        // --- Scroll area (cards) + pinned Save bar (mirrors SettingsPage) ---
        var cards = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            BackColor = Theme.WindowBg,
            Margin = Padding.Empty,
            Location = new Point(0, 0),
        };
        cards.Controls.AddRange(new Control[] { commands, replacements, tryIt });

        var scroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = Theme.WindowBg };
        scroll.Controls.Add(cards);

        // Cards grow with the window up to a readable max and stay centered (both axes) when they
        // fit; when taller than the viewport they pin near the top and scroll. Re-runs on ClientSize
        // changes (incl. the scrollbar appearing).
        void LayoutCards()
        {
            if (_layingOutCards) return;
            _layingOutCards = true;
            try
            {
                int clientW = scroll.ClientSize.Width;
                int clientH = scroll.ClientSize.Height;
                if (clientW <= 1) return;
                int cardW = Math.Min(980, Math.Max(320, clientW - 48));
                foreach (Control c in cards.Controls) c.Width = cardW;
                cards.Left = Math.Max(16, (clientW - cardW) / 2);
                cards.Top = cards.Height + 36 <= clientH ? (clientH - cards.Height) / 2 : 18;
            }
            finally { _layingOutCards = false; }
        }
        scroll.ClientSizeChanged += (_, _) => LayoutCards();
        LayoutCards();

        _saveButton.Click += OnSave;
        _saveButton.Location = new Point(24, 12);
        _savedLabel.Location = new Point(130, 18);
        var saveBar = new Panel { Dock = DockStyle.Bottom, Height = 54, BackColor = Theme.WindowBg };
        saveBar.Controls.AddRange(new Control[] { _saveButton, _savedLabel });

        // Fill added before Bottom so the bar reserves its edge first (matches DashboardForm).
        Controls.Add(scroll);
        Controls.Add(saveBar);
    }

    private void ConfigureGrid()
    {
        _grid.AllowUserToAddRows = true;
        _grid.AllowUserToDeleteRows = true;
        _grid.AllowUserToResizeRows = false;
        _grid.RowHeadersVisible = false;
        _grid.BackgroundColor = Theme.CardBg;
        _grid.GridColor = Theme.CardBorder;
        _grid.BorderStyle = BorderStyle.None;
        _grid.EnableHeadersVisualStyles = false;
        _grid.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None;
        _grid.ColumnHeadersHeight = 30;
        _grid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
        _grid.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
        _grid.SelectionMode = DataGridViewSelectionMode.CellSelect;
        _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        _grid.RowTemplate.Height = 30;
        _grid.Font = new Font("Segoe UI", 9.5f);

        _grid.ColumnHeadersDefaultCellStyle.BackColor = Theme.CardBg;
        _grid.ColumnHeadersDefaultCellStyle.ForeColor = Theme.TextSecondary;
        _grid.ColumnHeadersDefaultCellStyle.SelectionBackColor = Theme.CardBg;
        _grid.ColumnHeadersDefaultCellStyle.Font = Theme.Caption;
        _grid.ColumnHeadersDefaultCellStyle.Padding = new Padding(6, 0, 0, 0);
        _grid.DefaultCellStyle.BackColor = Theme.CardBg;
        _grid.DefaultCellStyle.ForeColor = Theme.TextPrimary;
        _grid.DefaultCellStyle.SelectionBackColor = Theme.NavActiveBg;
        _grid.DefaultCellStyle.SelectionForeColor = Theme.NavActiveText;
        _grid.DefaultCellStyle.Padding = new Padding(6, 0, 0, 0);

        var find = new DataGridViewTextBoxColumn { HeaderText = "Find (heard)", FillWeight = 50, SortMode = DataGridViewColumnSortMode.NotSortable };
        var replace = new DataGridViewTextBoxColumn { HeaderText = "Replace with", FillWeight = 50, SortMode = DataGridViewColumnSortMode.NotSortable };
        var delete = new DataGridViewButtonColumn
        {
            Text = "✕",
            UseColumnTextForButtonValue = true,
            FlatStyle = FlatStyle.Flat,
            Width = 36,
            AutoSizeMode = DataGridViewAutoSizeColumnMode.None,
            SortMode = DataGridViewColumnSortMode.NotSortable,
        };
        delete.DefaultCellStyle.ForeColor = Theme.TextMuted;
        delete.DefaultCellStyle.BackColor = Theme.CardBg;
        delete.DefaultCellStyle.SelectionBackColor = Theme.CardBg;
        delete.DefaultCellStyle.SelectionForeColor = Theme.TextMuted;
        _grid.Columns.AddRange(find, replace, delete);

        // The dark in-place editor: keep the cell editor on the dark palette (it flashes white otherwise).
        _grid.EditingControlShowing += (_, e) =>
        {
            if (e.Control is TextBox editor)
            {
                editor.BackColor = Theme.InputBg;
                editor.ForeColor = Theme.TextPrimary;
                editor.BorderStyle = BorderStyle.None;
            }
        };

        // ✕ button cell deletes its row (guard the header row and the empty add row).
        _grid.CellClick += (_, e) =>
        {
            if (e.RowIndex < 0 || e.ColumnIndex != delete.Index) return;
            var row = _grid.Rows[e.RowIndex];
            if (row.IsNewRow) return;
            _grid.Rows.RemoveAt(e.RowIndex);
        };
    }

    protected override void OnVisibleChanged(EventArgs e)
    {
        base.OnVisibleChanged(e);
        if (Visible) _savedLabel.Visible = false;
    }

    private void LoadFromSettings()
    {
        _commandsToggle.Checked = _settings.SpokenCommandsEnabled;
        _grid.Rows.Clear();
        foreach (var rule in _settings.Replacements)
            _grid.Rows.Add(rule.Find, rule.Replace);
        UpdatePreview();
    }

    private List<ReplacementRule> GatherRules()
    {
        var rules = new List<ReplacementRule>();
        foreach (DataGridViewRow row in _grid.Rows)
        {
            if (row.IsNewRow) continue;
            var find = row.Cells[0].Value?.ToString() ?? "";
            var replace = row.Cells[1].Value?.ToString() ?? "";
            if (string.IsNullOrWhiteSpace(find)) continue;
            rules.Add(new ReplacementRule { Find = find, Replace = replace });
        }
        return rules;
    }

    private void UpdatePreview()
        // WinForms multiline boxes need CRLF to render a line break, so normalize the engine's "\n".
        => _previewOutput.Text = TextRules.Apply(_previewInput.Text, GatherRules(), _commandsToggle.Checked)
            .Replace("\r\n", "\n").Replace("\n", Environment.NewLine);

    private void OnSave(object? sender, EventArgs e)
    {
        _settings.Replacements = GatherRules();
        _settings.SpokenCommandsEnabled = _commandsToggle.Checked;
        _settings.Save();
        _savedLabel.Visible = true;
    }
}
