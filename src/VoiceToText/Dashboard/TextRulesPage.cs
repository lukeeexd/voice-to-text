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
    private readonly CheckBox _commandsCheck = new() { Text = "Turn spoken commands into formatting", AutoSize = true, ForeColor = Theme.TextPrimary };
    private readonly Label _commandsHint = new() { AutoSize = true, ForeColor = Theme.TextSecondary, MaximumSize = new Size(620, 0), Text = "Say \"new line\" or \"new paragraph\" while dictating and they become line breaks instead of literal words." };
    private readonly Label _replacementsLabel = new() { AutoSize = true, ForeColor = Theme.Accent, Text = "REPLACEMENTS", Font = Theme.Caption };
    private readonly DataGridView _grid = new();
    private readonly Label _tryLabel = new() { AutoSize = true, ForeColor = Theme.Accent, Text = "TRY IT", Font = Theme.Caption };
    private readonly TextBox _previewInput = new() { BackColor = Theme.CardBg, ForeColor = Theme.TextPrimary, BorderStyle = BorderStyle.FixedSingle, Text = "i pushed to github new line all tests pass" };
    private readonly TextBox _previewOutput = new() { BackColor = Theme.CardBg, ForeColor = Color.FromArgb(0x9B, 0xE6, 0xA8), BorderStyle = BorderStyle.FixedSingle, ReadOnly = true, Multiline = true };
    private readonly DarkButton _saveButton = new() { Variant = DarkButtonVariant.Primary, Text = "Save", Size = new Size(96, 30) };
    private readonly Label _savedLabel = new() { AutoSize = true, ForeColor = Theme.Accent, Visible = false, Text = "Saved ✓" };

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

        _commandsCheck.CheckedChanged += (_, _) => UpdatePreview();
        _grid.CellEndEdit += (_, _) => UpdatePreview();
        _grid.RowsRemoved += (_, _) => UpdatePreview();
        _previewInput.TextChanged += (_, _) => UpdatePreview();

        _saveButton.Click += OnSave;

        Controls.AddRange(new Control[]
        {
            _commandsCheck, _commandsHint, _replacementsLabel, _grid,
            _tryLabel, _previewInput, _previewOutput, _saveButton, _savedLabel,
        });
    }

    private void ConfigureGrid()
    {
        _grid.AllowUserToAddRows = true;
        _grid.AllowUserToDeleteRows = true;
        _grid.AllowUserToResizeRows = false;
        _grid.RowHeadersWidth = 28;
        _grid.BackgroundColor = Theme.WindowBg;
        _grid.GridColor = Theme.CardBorder;
        _grid.BorderStyle = BorderStyle.None;
        _grid.EnableHeadersVisualStyles = false;
        _grid.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.Single;
        _grid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
        _grid.SelectionMode = DataGridViewSelectionMode.CellSelect;
        _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        _grid.Font = new Font("Segoe UI", 9.5f);

        _grid.ColumnHeadersDefaultCellStyle.BackColor = Theme.CardBg;
        _grid.ColumnHeadersDefaultCellStyle.ForeColor = Theme.TextSecondary;
        _grid.ColumnHeadersDefaultCellStyle.SelectionBackColor = Theme.CardBg;
        _grid.DefaultCellStyle.BackColor = Theme.CardBg;
        _grid.DefaultCellStyle.ForeColor = Theme.TextPrimary;
        _grid.DefaultCellStyle.SelectionBackColor = Theme.NavActiveBg;
        _grid.DefaultCellStyle.SelectionForeColor = Theme.TextPrimary;
        _grid.RowHeadersDefaultCellStyle.BackColor = Theme.CardBg;
        _grid.RowHeadersDefaultCellStyle.SelectionBackColor = Theme.NavActiveBg;

        var find = new DataGridViewTextBoxColumn { HeaderText = "Find (heard)", FillWeight = 50, SortMode = DataGridViewColumnSortMode.NotSortable };
        var replace = new DataGridViewTextBoxColumn { HeaderText = "Replace with", FillWeight = 50, SortMode = DataGridViewColumnSortMode.NotSortable };
        _grid.Columns.AddRange(find, replace);
    }

    protected override void OnVisibleChanged(EventArgs e)
    {
        base.OnVisibleChanged(e);
        if (Visible) _savedLabel.Visible = false;
    }

    private void LoadFromSettings()
    {
        _commandsCheck.Checked = _settings.SpokenCommandsEnabled;
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
        => _previewOutput.Text = TextRules.Apply(_previewInput.Text, GatherRules(), _commandsCheck.Checked);

    private void OnSave(object? sender, EventArgs e)
    {
        _settings.Replacements = GatherRules();
        _settings.SpokenCommandsEnabled = _commandsCheck.Checked;
        _settings.Save();
        _savedLabel.Visible = true;
    }

    protected override void OnSizeChanged(EventArgs e)
    {
        base.OnSizeChanged(e);
        DoLayout();
    }

    private void DoLayout()
    {
        const int pad = 20;
        int w = Width - pad * 2;
        if (w <= 0 || Height <= 0) return;

        int y = pad;
        _commandsCheck.Location = new Point(pad, y); y += 26;
        _commandsHint.Location = new Point(pad, y); y += 34;
        _replacementsLabel.Location = new Point(pad, y); y += 22;

        // Bottom block (try-it + save) is fixed height; the grid fills the space between.
        const int tryLabelH = 22, inputH = 24, outputH = 56, saveH = 30, gap = 8;
        int saveY = Height - pad - saveH;
        int outputY = saveY - 14 - outputH;
        int inputY = outputY - gap - inputH;
        int tryY = inputY - tryLabelH;

        int gridTop = y;
        int gridH = Math.Max(120, tryY - gridTop - 12);
        _grid.SetBounds(pad, gridTop, w, gridH);

        _tryLabel.Location = new Point(pad, tryY);
        _previewInput.SetBounds(pad, inputY, w, inputH);
        _previewOutput.SetBounds(pad, outputY, w, outputH);
        _saveButton.Location = new Point(pad, saveY);
        _savedLabel.Location = new Point(pad + 106, saveY + 6);
    }
}
