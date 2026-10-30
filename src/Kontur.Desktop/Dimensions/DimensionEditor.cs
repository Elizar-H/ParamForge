using System.Reflection;
using Kontur.Core;

namespace Kontur.Desktop;

internal sealed class DimensionEditor : UserControl
{
    private readonly string key;
    private readonly Parameters original;
    private readonly Func<Parameters, GenerationResult> generate;
    private readonly DimensionTolerance? dimension;
    private readonly Label error = Theme.Label("", Theme.Detail);
    private readonly Label summary = Theme.Label("", Theme.Detail);
    private readonly ToolTip tips = new();
    private readonly Label toleranceName = Theme.Label("", Theme.Detail);
    private readonly TolerancePicker? picker;
    private string selectedDesignation = "";
    private bool updating;
    internal readonly TextBox NominalInput = new SelectAllTextBox();
    internal readonly TextBox? LowerInput;
    internal readonly TextBox? UpperInput;
    internal readonly Button ApplyButton = Theme.Button("Применить", true);
    internal readonly Button CancelEditorButton = Theme.Button("Отмена");
    internal Project? Result
    {
        get; private set;
    }
    internal string ErrorText => error.Text;
    internal TolerancePicker? Picker => picker;
    internal bool UsesStandard => selectedDesignation.Length != 0;
    internal string DimensionKey => key;
    internal event Action? Accepted;
    internal event Action? Cancelled;
    internal void SelectStandard(string designation)
    {
        if (designation != "m")
        {
            picker!.SelectDesignation(designation);
            return;
        }
        selectedDesignation = "m";
        picker!.ClearChoice();
        RefreshTolerance(true);
    }

    internal DimensionEditor(string key, Parameters original, Func<Parameters, GenerationResult> generate)
    {
        this.key = key;
        this.original = original;
        this.generate = generate;
        dimension = Tolerances.EditableDimensions.SingleOrDefault(d => d.Key == key);
        Text = Engine.Fields.Single(f => f.Key == key).Label;
        Name = "DimensionEditor";
        AccessibleName = "Размер и допуск: " + Text;
        Font = Theme.Numeric;
        BackColor = Color.White;
        Dock = DockStyle.Fill;
        Margin = Padding.Empty;
        ApplyButton.Font = CancelEditorButton.Font = Theme.Numeric;
        AutoScaleMode = AutoScaleMode.Dpi;
        Padding = new Padding(12, 0, 12, 8);
        var heading = new TableLayoutPanel { Dock = DockStyle.Top, Height = 42, ColumnCount = 2, Margin = Padding.Empty };
        heading.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        heading.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 32));
        heading.Controls.Add(Theme.Label(Text, Theme.Strong), 0, 0);
        var close = Theme.Button("×");
        close.AccessibleName = "Закрыть редактор размера";
        close.Dock = DockStyle.Fill;
        close.Padding = Padding.Empty;
        close.Margin = new Padding(0, 5, 0, 5);
        close.FlatAppearance.BorderSize = 0;
        close.Click += (_, _) => Cancelled?.Invoke();
        heading.Controls.Add(close, 1, 0);
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5, Padding = Padding.Empty, Margin = Padding.Empty };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, dimension is null ? 0 : 64));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 56));
        var numbers = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty };
        numbers.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, dimension is null ? 100 : 40));
        numbers.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, dimension is null ? 0 : 60));
        numbers.Controls.Add(Field("Номинал", NominalInput, Value(Property(key))), 0, 0);
        layout.Controls.Add(numbers, 0, 0);
        toleranceName.ForeColor = Theme.Muted;
        layout.Controls.Add(toleranceName, 0, 1);
        if (key is "bodyDiameter" or "flangeWidth")
        {
            var general = Theme.Button("Общий m");
            general.Name = "GeneralTolerance";
            general.Dock = DockStyle.Right;
            general.Width = 110;
            general.Margin = Padding.Empty;
            general.FlatAppearance.BorderSize = 0;
            general.Click += (_, _) => SelectStandard("m");
            toleranceName.Controls.Add(general);
        }
        if (dimension is not null)
        {
            picker = new TolerancePicker(dimension);
            var choices = new Panel { Dock = DockStyle.Fill, AutoScroll = true, Margin = Padding.Empty };
            choices.Controls.Add(picker);
            layout.Controls.Add(choices, 0, 2);
            LowerInput = new SelectAllTextBox();
            UpperInput = new SelectAllTextBox();
            var deviations = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2, RowCount = 3, Margin = new Padding(6, 0, 0, 6) };
            deviations.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            deviations.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            for (var row = 0; row < 3; row++)
            {
                deviations.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            }

            var caption = Theme.Label("Отклонения", Theme.Detail);
            caption.AutoSize = true;
            caption.Margin = new Padding(0, 3, 0, 3);
            deviations.Controls.Add(caption, 0, 0);
            deviations.SetColumnSpan(caption, 2);
            foreach (var (label, input, property, row) in new[] { ("Верх.", UpperInput, dimension.UpperProperty, 1), ("Ниж.", LowerInput, dimension.LowerProperty, 2) })
            {
                var name = Theme.Label(label, Theme.Detail);
                name.AutoSize = true;
                name.Margin = new Padding(0, 4, 6, 0);
                input.Name = property;
                input.AccessibleName = label == "Верх." ? "Верхнее отклонение, мм" : "Нижнее отклонение, мм";
                input.Text = Value(Property(property));
                input.Font = Theme.Numeric;
                input.Dock = DockStyle.Top;
                input.TextAlign = HorizontalAlignment.Right;
                input.Margin = new Padding(0, 0, 0, 3);
                deviations.Controls.Add(name, 0, row);
                deviations.Controls.Add(input, 1, row);
            }
            numbers.Controls.Add(deviations, 1, 0);
            summary.TextAlign = ContentAlignment.MiddleLeft;
            layout.Controls.Add(summary, 0, 3);
            var designation = Tolerances.Designation(original, dimension);
            selectedDesignation = designation;
            picker.SetNominal((double)Property(key).GetValue(original)!);
            if (UsesStandard && selectedDesignation != "m")
            {
                picker.MarkChoice(selectedDesignation);
            }

            picker.Chosen += value => { selectedDesignation = value; RefreshTolerance(true); };
            foreach (var box in new[] { LowerInput, UpperInput })
            {
                box.TextChanged += (_, _) =>
            {
                if (updating)
                {
                    return;
                }

                error.Text = "";
                box.BackColor = Color.White;
                selectedDesignation = MatchingDesignation();
                picker.MarkChoice(UsesStandard ? selectedDesignation : null);
                RefreshTolerance();
            };
            }
        }
        error.AutoSize = true;
        error.MaximumSize = new Size(340, 0);
        error.ForeColor = Theme.Error;
        error.TextAlign = ContentAlignment.MiddleLeft;
        layout.Controls.Add(error, 0, 4);
        var buttons = new TableLayoutPanel { Dock = DockStyle.Bottom, Height = 52, ColumnCount = 2, Padding = new Padding(0, 8, 0, 0) };
        buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        CancelEditorButton.Dock = ApplyButton.Dock = DockStyle.Fill;
        CancelEditorButton.Margin = new Padding(4);
        ApplyButton.Margin = new Padding(4);
        CancelEditorButton.Click += (_, _) => Cancelled?.Invoke();
        ApplyButton.Click += (_, _) => Apply();
        buttons.Controls.Add(CancelEditorButton, 0, 0);
        buttons.Controls.Add(ApplyButton, 1, 0);
        Controls.Add(layout);
        Controls.Add(buttons);
        Controls.Add(heading);
        layout.BringToFront();
        NominalInput.TextChanged += (_, _) => RefreshTolerance(true);
        RefreshTolerance();
    }
    private static PropertyInfo Property(string key) => typeof(Parameters).GetProperty(char.ToUpperInvariant(key[0]) + key[1..])!;
    private string Value(PropertyInfo property) => NumericText.Format((double)property.GetValue(original)!);
    private Control Field(string caption, Control control, string? text = null)
    {
        var row = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 1, RowCount = 2, Margin = new Padding(0, 0, 8, 6) };
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        row.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        row.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var label = Theme.Label(caption, Theme.Detail);
        label.AutoSize = true;
        label.Margin = new Padding(0, 3, 0, 3);
        row.Controls.Add(label, 0, 0);
        control.Name = caption;
        if (text is not null)
        {
            control.Text = text;
        }

        control.Font = Theme.Numeric;
        control.Dock = DockStyle.Top;
        control.Margin = Padding.Empty;
        control.TextChanged += (_, _) => { error.Text = ""; control.BackColor = Color.White; };
        row.Controls.Add(control, 0, 1);
        return row;
    }
    private string SelectedDesignation => selectedDesignation;
    private string MatchingDesignation()
    {
        if (dimension is null || !NumericText.TryParse(NominalInput.Text, out var nominal) || !NumericText.TryParse(LowerInput!.Text, out var lo) || !NumericText.TryParse(UpperInput!.Text, out var hi))
        {
            return "";
        }

        try
        {
            foreach (var position in dimension.Positions)
            {
                for (var grade = 6; grade <= 16; grade++)
                {
                    var designation = position + grade;
                    var pair = Tolerances.Deviations(dimension, nominal, designation);
                    if (Math.Abs(pair.Lower - lo) < .00000001 && Math.Abs(pair.Upper - hi) < .00000001)
                    {
                        return designation;
                    }
                }
            }
        }
        catch (ArgumentException) { }
        return "";
    }
    private void RefreshTolerance(bool applyStandard = false)
    {
        if (updating || dimension is null)
        {
            return;
        }

        updating = true;
        try
        {
            if (!NumericText.TryParse(NominalInput.Text, out var nominal))
            {
                picker!.SetNominal(double.NaN);
                summary.Text = "Введите номинал.";
                return;
            }
            picker!.SetNominal(nominal);
            toleranceName.Text = selectedDesignation == "m" ? "Общий допуск m" : UsesStandard ? selectedDesignation : "Произвольный допуск";
            if (applyStandard && UsesStandard)
            {
                var (lower, upper) = Tolerances.Deviations(dimension, nominal, selectedDesignation);
                LowerInput!.Text = NumericText.Format(lower);
                UpperInput!.Text = NumericText.Format(upper);
            }
            if (!NumericText.TryParse(LowerInput!.Text, out var lo) || !NumericText.TryParse(UpperInput!.Text, out var hi))
            {
                summary.Text = "Введите отклонения.";
                return;
            }
            var target = (double)Math.Round((decimal)nominal + ((decimal)lo + (decimal)hi) / 2m, 3, MidpointRounding.AwayFromZero);
            summary.Text = $"Границы: {NumericText.Format(nominal + lo)} … {NumericText.Format(nominal + hi)} мм\nЦель обработки: {NumericText.Format(target)} мм";
        }
        catch (ArgumentException ex) { summary.Text = ex.Message; }
        catch (OverflowException) { summary.Text = "Размер вне числового диапазона."; }
        finally { updating = false; }
    }
    private void Apply()
    {
        var candidate = original with
        {
        };
        var fields = new List<(string Property, TextBox Input)> { (Property(key).Name, NominalInput) };
        if (dimension is not null)
        {
            fields.Add((dimension.LowerProperty, LowerInput!));
            fields.Add((dimension.UpperProperty, UpperInput!));
        }
        foreach (var field in fields)
        {
            if (!NumericText.TryParse(field.Input.Text, out var value))
            {
                field.Input.BackColor = Color.MistyRose;
                error.Text = "Введите число.";
                return;
            }
            Property(field.Property).SetValue(candidate, value);
        }
        if (dimension is not null)
        {
            try
            {
                candidate = Tolerances.SetDesignation(candidate, key, SelectedDesignation);
            }
            catch (ArgumentException ex) { error.Text = ex.Message; return; }
        }
        var generated = generate(candidate);
        if (!generated.Ok || generated.Project is null)
        {
            foreach (var field in fields)
            {
                if (generated.Errors.Any(e => string.Equals(e.Field, field.Property, StringComparison.OrdinalIgnoreCase)))
                {
                    field.Input.BackColor = Color.MistyRose;
                }
            }

            error.Text = ShortError(generated.Errors, candidate);
            tips.SetToolTip(error, string.Join(Environment.NewLine, generated.Errors.Select(e => e.Message)));
            return;
        }
        Result = generated.Project;
        Accepted?.Invoke();
    }
    internal void FocusNominal()
    {
        NominalInput.Focus();
        NominalInput.SelectAll();
    }
    protected override bool ProcessDialogKey(Keys keyData)
    {
        if (keyData == Keys.Escape)
        {
            Cancelled?.Invoke();
            return true;
        }
        if (keyData == Keys.Enter)
        {
            var focused = FocusedButton(this);
            if (focused is not null)
            {
                focused.PerformClick();
            }
            else
            {
                Apply();
            }

            return true;
        }
        return base.ProcessDialogKey(keyData);
    }
    private static Button? FocusedButton(Control parent)
    {
        foreach (Control child in parent.Controls)
        {
            if (child is Button { Focused: true } button)
            {
                return button;
            }

            if (FocusedButton(child) is { } nested)
            {
                return nested;
            }
        }
        return null;
    }
    private static string ShortError(IReadOnlyList<ValidationError> errors, Parameters candidate)
    {
        if (ParameterMessages.ProfileError(errors) is { } profileError)
        {
            return profileError;
        }

        if (errors.Any(e => e.Message.Contains("3 мм", StringComparison.Ordinal)))
        {
            return "Минимальная стенка — 3 мм.";
        }

        if (errors.Any(e => e.Message.Contains("трёх знаков", StringComparison.Ordinal)))
        {
            return "Не более 3 знаков после запятой.";
        }

        if (errors.Any(e => e.Message.Contains("четырёх знаков", StringComparison.Ordinal)))
        {
            return "Не более 4 знаков после запятой.";
        }

        if (errors.Any(e => e.Message.StartsWith("Верхнее отклонение", StringComparison.Ordinal)))
        {
            return "Верхнее отклонение ниже нижнего.";
        }

        foreach (var field in Engine.Fields)
        {
            if (!errors.Any(e => e.Field == field.Key))
            {
                continue;
            }

            var value = (double)Property(field.Key).GetValue(candidate)!;
            if (value < field.Min || value > field.Max)
            {
                return $"Допустимо: {NumericText.Format(field.Min)}…{NumericText.Format(field.Max)} {field.Unit}.";
            }
        }
        return errors.FirstOrDefault()?.Message ?? "Проверьте размеры.";
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
