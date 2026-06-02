using System.ComponentModel;
using System.Drawing;
using System.Globalization;

namespace VoiceToText.Dashboard.Controls;

/// <summary>
/// A dark rounded numeric field with a ▲▼ stepper — a drop-in for the NumericUpDown surface
/// SettingsPage uses (Value / Minimum / Maximum / Increment / DecimalPlaces / ValueChanged).
/// </summary>
internal sealed class DarkNumericUpDown : Control
{
    private const int Radius = 6;
    private const int StepperW = 22;

    private readonly TextBox _text;
    private decimal _value;
    private decimal _min, _max = 100m, _increment = 1m;
    private int _decimals;

    public DarkNumericUpDown()
    {
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint
               | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        BackColor = Theme.CardBg;
        Size = new Size(96, 30);

        _text = new TextBox
        {
            BorderStyle = BorderStyle.None,
            BackColor = Theme.InputBg,
            ForeColor = Theme.TextPrimary,
        };
        _text.Leave += (_, _) => CommitText();
        _text.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter) { CommitText(); e.SuppressKeyPress = true; } };
        Controls.Add(_text);
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public decimal Value { get => _value; set => SetValue(value, raise: true); }
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public decimal Minimum { get => _min; set { _min = value; SetValue(_value, raise: false); } }
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public decimal Maximum { get => _max; set { _max = value; SetValue(_value, raise: false); } }
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public decimal Increment { get => _increment; set => _increment = value; }
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int DecimalPlaces { get => _decimals; set { _decimals = value; UpdateText(); } }

    public event EventHandler? ValueChanged;

    private void SetValue(decimal v, bool raise)
    {
        if (_max < _min) _max = _min;
        v = Math.Clamp(v, _min, _max);
        bool changed = v != _value;
        _value = v;
        UpdateText();
        if (changed && raise) ValueChanged?.Invoke(this, EventArgs.Empty);
    }

    private void UpdateText()
    {
        var s = _value.ToString("F" + _decimals, CultureInfo.CurrentCulture);
        if (_text.Text != s) _text.Text = s;
    }

    private void CommitText()
    {
        if (decimal.TryParse(_text.Text, NumberStyles.Any, CultureInfo.CurrentCulture, out var v))
            SetValue(v, raise: true);
        else
            UpdateText();
    }

    protected override void OnLayout(LayoutEventArgs e)
    {
        base.OnLayout(e);
        int ih = _text.PreferredHeight;
        _text.SetBounds(10, Math.Max(1, (Height - ih) / 2), Math.Max(10, Width - StepperW - 14), ih);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (Enabled && e.X >= Width - StepperW)
            SetValue(_value + (e.Y < Height / 2 ? _increment : -_increment), raise: true);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        Theme.PaintField(g, ClientRectangle, BackColor, Radius);

        int sx = Width - StepperW;
        using (var pen = new Pen(Theme.InputBorder))
            g.DrawLine(pen, sx, 4, sx, Height - 5);
        using var arrow = new SolidBrush(Enabled ? Theme.TextSecondary : Theme.TextMuted);
        using var fmt = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        g.DrawString("▲", Theme.Caption, arrow, new Rectangle(sx, 1, StepperW, Height / 2 - 1), fmt);
        g.DrawString("▼", Theme.Caption, arrow, new Rectangle(sx, Height / 2, StepperW, Height / 2 - 1), fmt);
    }
}
