using Kontur.Core;

namespace Kontur.Desktop;

// A fixed set of choices, not an editable spreadsheet. Each grade stays on one row.
internal sealed class TolerancePicker : UserControl
{
    private readonly DimensionTolerance dimension;
    private readonly Dictionary<string, Button> choices = [];
    private readonly Dictionary<int, Label> values = [];
    private readonly ToolTip tips = new();
    private readonly TableLayoutPanel table;
    private readonly List<Label> headings = [];
    internal event Action<string>? Chosen;
    internal string? SelectedDesignation
    {
        get; private set;
    }
    internal int ChoiceCount => choices.Count;
    internal Button ChoiceButton(string designation) => choices[designation];

    internal TolerancePicker(DimensionTolerance dimension)
    {
        this.dimension = dimension;
        Dock = DockStyle.Top;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Margin = Padding.Empty;
        BackColor = Color.White;
        AccessibleName = "Выбор поля допуска и квалитета";
        var symmetric = dimension.Positions.Length == 1;
        table = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = symmetric ? 2 : 4, RowCount = 12, Margin = Padding.Empty };
        if (symmetric)
        {
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 42));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 58));
        }
        else
        {
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 48));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 88));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        }
        table.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
        var captions = symmetric ? new[] { "Квалитет", "Отклонения, мм" } : new[] { "IT", "Допуск, мм", dimension.Positions[0], dimension.Positions[1] };
        for (var col = 0; col < captions.Length; col++)
        {
            var label = Theme.Label(captions[col], Theme.Detail);
            label.ForeColor = Theme.Muted;
            label.TextAlign = ContentAlignment.MiddleCenter;
            table.Controls.Add(label, col, 0);
            headings.Add(label);
        }
        for (var grade = 6; grade <= 16; grade++)
        {
            var row = grade - 5;
            table.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
            var value = Theme.Label("", Theme.Detail);
            value.TextAlign = ContentAlignment.MiddleCenter;
            values.Add(grade, value);
            table.Controls.Add(value, 1, row);
            if (!symmetric)
            {
                var label = Theme.Label(grade.ToString(), Theme.Detail);
                label.TextAlign = ContentAlignment.MiddleCenter;
                table.Controls.Add(label, 0, row);
            }
            foreach (var position in dimension.Positions)
            {
                var designation = position + grade;
                var button = Theme.Button(designation);
                button.Font = Theme.Numeric;
                button.Dock = DockStyle.Fill;
                button.Padding = Padding.Empty;
                button.Margin = new Padding(3, 2, 3, 2);
                button.Tag = designation;
                button.AccessibleName = "Допуск " + designation;
                button.Click += (_, _) => SelectDesignation(designation);
                button.PreviewKeyDown += (_, e) => { if (e.KeyCode is Keys.Left or Keys.Right or Keys.Up or Keys.Down or Keys.Home or Keys.End) { e.IsInputKey = true; } };
                button.KeyDown += (_, e) => Navigate(designation, e);
                choices.Add(designation, button);
                table.Controls.Add(button, symmetric ? 0 : 2 + Array.IndexOf(dimension.Positions, position), row);
            }
        }
        Controls.Add(table);
        UpdateSelection();
        foreach (var control in headings.Cast<Control>().Concat(choices.Values))
        {
            control.FontChanged += (_, _) => { PerformLayout(); Parent?.PerformLayout(); };
        }
    }

    private int HeaderHeight => headings.Count == 0 ? 28 : Measure(headings[0]).Height + 8;
    private int ChoiceHeight => choices.Count == 0 ? 36 : Measure(choices.Values.First()).Height + choices.Values.First().Margin.Vertical + 6;
    private static Size Measure(Control control) => TextRenderer.MeasureText(control.Text, control.Font, Size.Empty, TextFormatFlags.SingleLine);
    public override Size GetPreferredSize(Size proposedSize) => new(proposedSize.Width > 0 ? proposedSize.Width : Width, HeaderHeight + 11 * ChoiceHeight);
    protected override void OnLayout(LayoutEventArgs e)
    {
        if (table is not null && headings.Count > 0 && table.RowStyles.Count == 12)
        {
            table.RowStyles[0].Height = HeaderHeight;
            var rowHeight = ChoiceHeight;
            for (var row = 1; row < 12; row++)
            {
                table.RowStyles[row].Height = rowHeight;
            }

            if (dimension.Positions.Length > 1)
            {
                table.ColumnStyles[1].Width = Measure(headings[1]).Width + 12;
            }
        }
        base.OnLayout(e);
    }

    internal void SetNominal(double nominal)
    {
        foreach (var (grade, label) in values)
        {
            try
            {
                var width = Tolerances.Width(nominal, grade);
                label.Text = dimension.Positions.Length == 1 ? "±" + NumericText.Format(width / 2) : NumericText.Format(width);
                foreach (var position in dimension.Positions)
                {
                    var designation = position + grade;
                    var (lower, upper) = Tolerances.Deviations(dimension, nominal, designation);
                    tips.SetToolTip(choices[designation], $"{designation}: {Tolerances.Signed(lower)} / {Tolerances.Signed(upper)} мм");
                    choices[designation].Enabled = true;
                }
            }
            catch (ArgumentException)
            {
                label.Text = "—";
                foreach (var position in dimension.Positions)
                {
                    choices[position + grade].Enabled = false;
                }
            }
        }
    }

    internal void SelectDesignation(string designation)
    {
        MarkChoice(designation);
        Chosen?.Invoke(designation);
    }
    internal void MarkChoice(string? designation)
    {
        if (designation is not null && !choices.ContainsKey(designation))
        {
            throw new ArgumentException("Неизвестный допуск.", nameof(designation));
        }

        SelectedDesignation = designation;
        UpdateSelection();
    }
    internal void ClearChoice()
    {
        SelectedDesignation = null;
        UpdateSelection();
    }
    private void UpdateSelection()
    {
        foreach (var (designation, button) in choices)
        {
            var selected = designation == SelectedDesignation;
            button.BackColor = selected ? Theme.Accent : Color.White;
            button.ForeColor = selected ? Color.White : Theme.Ink;
            button.FlatAppearance.BorderColor = selected ? Theme.Accent : Theme.Border;
            button.AccessibleDescription = selected ? "Выбрано" : "";
            button.TabStop = designation == (SelectedDesignation ?? dimension.Positions[0] + "6");
        }
    }
    private void Navigate(string designation, KeyEventArgs e)
    {
        var position = dimension.Positions.Single(p => designation.StartsWith(p, StringComparison.Ordinal));
        var grade = int.Parse(designation[position.Length..]);
        var column = Array.IndexOf(dimension.Positions, position);
        switch (e.KeyCode)
        {
            case Keys.Up:
                grade = Math.Max(6, grade - 1);
                break;
            case Keys.Down:
                grade = Math.Min(16, grade + 1);
                break;
            case Keys.Left:
                column = Math.Max(0, column - 1);
                break;
            case Keys.Right:
                column = Math.Min(dimension.Positions.Length - 1, column + 1);
                break;
            case Keys.Home:
                grade = 6;
                break;
            case Keys.End:
                grade = 16;
                break;
            default:
                return;
        }
        var next = dimension.Positions[column] + grade;
        SelectDesignation(next);
        choices[next].Focus();
        e.Handled = true;
        e.SuppressKeyPress = true;
    }
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            tips.Dispose();
        }

        base.Dispose(disposing);
    }
}
