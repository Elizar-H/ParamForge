using System.Reflection;
using Kontur.Core;

namespace Kontur.Desktop;

internal sealed class DimensionEditDialog : Form
{
    private readonly string key;
    private readonly Parameters original;
    private readonly Func<Parameters, GenerationResult> generate;
    private readonly DimensionTolerance? dimension;
    private readonly Label error = Theme.Label("", Theme.Small);
    private readonly Label summary = Theme.Label("", Theme.Small);
    private readonly ToolTip tips = new();
    private bool updating;
    internal readonly TextBox NominalInput = new();
    internal readonly TextBox? LowerInput;
    internal readonly TextBox? UpperInput;
    internal readonly ComboBox ModeInput = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    internal readonly ComboBox PositionInput = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    internal readonly ComboBox GradeInput = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    internal readonly Button ApplyButton = Theme.Button("ОК", true);
    internal readonly Button CancelEditorButton = Theme.Button("Отмена");
    internal Project? Result { get; private set; }
    internal string ErrorText => error.Text;

    internal DimensionEditDialog(string key, Parameters original, Func<Parameters, GenerationResult> generate)
    {
        this.key = key; this.original = original; this.generate = generate;
        dimension = Tolerances.Dimensions.SingleOrDefault(d => d.Key == key);
        Text = Engine.Fields.Single(f => f.Key == key).Label;
        Font = Theme.Body; BackColor = Color.White; StartPosition = FormStartPosition.CenterParent;
        AutoScaleMode = AutoScaleMode.Dpi;
        FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = MinimizeBox = false;
        ClientSize = new Size(500, dimension is null ? 200 : 440);
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(16), ColumnCount = 1, RowCount = 6 };
        foreach (var height in new[] { 66, dimension is null ? 0 : 70, dimension is null ? 0 : 70, dimension is null ? 0 : 80 })
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        layout.Controls.Add(Field("Номинал, мм", NominalInput, Value(Property(key))), 0, 0);
        if (dimension is not null)
        {
            ModeInput.Items.AddRange(["Вручную", "По стандарту"]);
            PositionInput.Items.AddRange(dimension.Positions.Cast<object>().ToArray());
            GradeInput.Items.AddRange(Enumerable.Range(6, 11).Select(i => (object)("IT" + i)).ToArray());
            var selectors = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3 };
            selectors.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 46));
            selectors.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 27)); selectors.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 27));
            selectors.Controls.Add(Field("Способ задания", ModeInput), 0, 0);
            selectors.Controls.Add(Field("Поле", PositionInput), 1, 0); selectors.Controls.Add(Field("Квалитет", GradeInput), 2, 0);
            layout.Controls.Add(selectors, 0, 1);
            LowerInput = new TextBox(); UpperInput = new TextBox();
            var deviations = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2 };
            deviations.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50)); deviations.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            deviations.Controls.Add(Field("Нижнее откл., мм", LowerInput, Value(Property(dimension.LowerProperty))), 0, 0);
            deviations.Controls.Add(Field("Верхнее откл., мм", UpperInput, Value(Property(dimension.UpperProperty))), 1, 0);
            layout.Controls.Add(deviations, 0, 2);
            summary.TextAlign = ContentAlignment.MiddleLeft; layout.Controls.Add(summary, 0, 3);
            var designation = Tolerances.Designation(original, dimension);
            ModeInput.SelectedIndex = designation.Length == 0 ? 0 : 1;
            PositionInput.SelectedItem = dimension.Positions.FirstOrDefault(position => designation.StartsWith(position, StringComparison.Ordinal)) ?? dimension.Positions[0];
            GradeInput.SelectedItem = "IT" + (designation.Length == 0 ? "7" : new string(designation.Where(char.IsDigit).ToArray()));
            ModeInput.SelectedIndexChanged += (_, _) => RefreshTolerance(); PositionInput.SelectedIndexChanged += (_, _) => RefreshTolerance();
            GradeInput.SelectedIndexChanged += (_, _) => RefreshTolerance();
            foreach (var box in new[] { LowerInput, UpperInput }) box.TextChanged += (_, _) =>
            {
                if (updating) return;
                ModeInput.SelectedIndex = 0; RefreshTolerance();
            };
        }
        error.ForeColor = Theme.Error; error.TextAlign = ContentAlignment.MiddleLeft; layout.Controls.Add(error, 0, 4);
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, WrapContents = false };
        CancelEditorButton.Width = ApplyButton.Width = 110; CancelEditorButton.Height = ApplyButton.Height = 36;
        CancelEditorButton.DialogResult = DialogResult.Cancel; ApplyButton.Click += (_, _) => Apply();
        buttons.Controls.AddRange([CancelEditorButton, ApplyButton]); layout.Controls.Add(buttons, 0, 5);
        Controls.Add(layout); AcceptButton = ApplyButton; CancelButton = CancelEditorButton;
        NominalInput.TextChanged += (_, _) => RefreshTolerance(); RefreshTolerance();
        Shown += (_, _) => { NominalInput.Focus(); NominalInput.SelectAll(); };
    }
    private static PropertyInfo Property(string key) => typeof(Parameters).GetProperty(char.ToUpperInvariant(key[0]) + key[1..])!;
    private string Value(PropertyInfo property) => MainForm.F((double)property.GetValue(original)!);
    private Control Field(string caption, Control control, string? text = null)
    {
        var row = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, Margin = new Padding(0, 0, 8, 0) };
        row.RowStyles.Add(new RowStyle(SizeType.Absolute, 27)); row.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        row.Controls.Add(Theme.Label(caption, Theme.Small), 0, 0); control.Name = caption;
        if (text is not null) control.Text = text;
        control.Font = Theme.Body; control.Dock = DockStyle.Top; control.Margin = Padding.Empty;
        control.TextChanged += (_, _) => { error.Text = ""; control.BackColor = Color.White; };
        row.Controls.Add(control, 0, 1); return row;
    }
    private string SelectedDesignation => ModeInput.SelectedIndex == 1
        ? (PositionInput.SelectedItem?.ToString() ?? "") + (GradeInput.SelectedItem?.ToString() ?? "IT7")[2..] : "";
    private void RefreshTolerance()
    {
        if (updating || dimension is null) return;
        updating = true;
        try
        {
            PositionInput.Enabled = GradeInput.Enabled = ModeInput.SelectedIndex == 1;
            if (!MainForm.TryNumber(NominalInput.Text, out var nominal)) { summary.Text = "Введите номинал."; return; }
            if (ModeInput.SelectedIndex == 1)
            {
                var (lower, upper) = Tolerances.Deviations(dimension, nominal, SelectedDesignation);
                LowerInput!.Text = MainForm.F(lower); UpperInput!.Text = MainForm.F(upper);
            }
            if (!MainForm.TryNumber(LowerInput!.Text, out var lo) || !MainForm.TryNumber(UpperInput!.Text, out var hi))
            { summary.Text = "Введите отклонения."; return; }
            var target = (double)Math.Round((decimal)nominal + ((decimal)lo + (decimal)hi) / 2m, 3, MidpointRounding.AwayFromZero);
            summary.Text = $"Границы: {MainForm.F(nominal + lo)} … {MainForm.F(nominal + hi)} мм\nЦель обработки: {MainForm.F(target)} мм";
            tips.SetToolTip(summary, "ГОСТ 25346/25347. Изменение числового отклонения включает ручной режим.");
        }
        catch (ArgumentException ex) { summary.Text = ex.Message; }
        catch (OverflowException) { summary.Text = "Размер вне числового диапазона."; }
        finally { updating = false; }
    }
    private void Apply()
    {
        var candidate = original with { };
        var fields = new List<(string Property, TextBox Input)> { (Property(key).Name, NominalInput) };
        if (dimension is not null) { fields.Add((dimension.LowerProperty, LowerInput!)); fields.Add((dimension.UpperProperty, UpperInput!)); }
        foreach (var field in fields)
        {
            if (!MainForm.TryNumber(field.Input.Text, out var value)) { field.Input.BackColor = Color.MistyRose; error.Text = "Введите число."; return; }
            Property(field.Property).SetValue(candidate, value);
        }
        if (dimension is not null)
        {
            try { candidate = Tolerances.SetDesignation(candidate, key, SelectedDesignation); }
            catch (ArgumentException ex) { error.Text = ex.Message; return; }
        }
        var generated = generate(candidate);
        if (!generated.Ok || generated.Project is null)
        {
            foreach (var field in fields)
                if (generated.Errors.Any(e => string.Equals(e.Field, field.Property, StringComparison.OrdinalIgnoreCase))) field.Input.BackColor = Color.MistyRose;
            error.Text = ShortError(generated.Errors, candidate);
            tips.SetToolTip(error, string.Join(Environment.NewLine, generated.Errors.Select(e => e.Message))); return;
        }
        Result = generated.Project; DialogResult = DialogResult.OK;
    }
    private static string ShortError(IReadOnlyList<ValidationError> errors, Parameters candidate)
    {
        if (MainForm.ProfileError(errors) is { } profileError) return profileError;
        if (errors.Any(e => e.Message.Contains("3 мм", StringComparison.Ordinal))) return "Минимальная стенка — 3 мм.";
        if (errors.Any(e => e.Message.Contains("трёх знаков", StringComparison.Ordinal))) return "Не более 3 знаков после запятой.";
        if (errors.Any(e => e.Message.Contains("четырёх знаков", StringComparison.Ordinal))) return "Не более 4 знаков после запятой.";
        if (errors.Any(e => e.Message.StartsWith("Верхнее отклонение", StringComparison.Ordinal))) return "Верхнее отклонение ниже нижнего.";
        foreach (var field in Engine.Fields)
        {
            if (!errors.Any(e => e.Field == field.Key)) continue;
            var value = (double)Property(field.Key).GetValue(candidate)!;
            if (value < field.Min || value > field.Max) return $"Допустимо: {MainForm.F(field.Min)}…{MainForm.F(field.Max)} {field.Unit}.";
        }
        return errors.FirstOrDefault()?.Message ?? "Проверьте размеры.";
    }
    protected override void Dispose(bool disposing) { if (disposing) tips.Dispose(); base.Dispose(disposing); }
}
