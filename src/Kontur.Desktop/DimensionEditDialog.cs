using System.Reflection;
using Kontur.Core;

namespace Kontur.Desktop;

internal sealed class DimensionEditDialog : Form
{
    private readonly string key;
    private readonly string? lowerKey;
    private readonly string? upperKey;
    private readonly Parameters original;
    private readonly Func<Parameters, GenerationResult> generate;
    private readonly Label error = Theme.Label("", Theme.Small);
    private readonly ToolTip tips = new();
    internal readonly TextBox NominalInput = new();
    internal readonly TextBox? LowerInput;
    internal readonly TextBox? UpperInput;
    internal readonly Button ApplyButton = Theme.Button("ОК", true);
    internal readonly Button CancelEditorButton = Theme.Button("Отмена");
    internal Project? Result { get; private set; }
    internal string ErrorText => error.Text;

    internal DimensionEditDialog(string key, Parameters original, Func<Parameters, GenerationResult> generate)
    {
        this.key = key;
        this.original = original;
        this.generate = generate;
        (Text, lowerKey, upperKey) = key switch
        {
            "length" => ("Длина L", "lengthLowerDeviation", "lengthUpperDeviation"),
            "flangeDiameter" => ("Диаметр буртика D", "flangeLowerDeviation", "flangeUpperDeviation"),
            "boreDiameter" => ("Диаметр отверстия d", "boreLowerDeviation", "boreUpperDeviation"),
            "counterboreDiameter" => ("Диаметр расточки d2", "counterboreLowerDeviation", "counterboreUpperDeviation"),
            "counterboreDepth" => ("Глубина расточки h", "counterboreDepthLowerDeviation", "counterboreDepthUpperDeviation"),
            "bodyDiameter" => ("Диаметр тела Db", (string?)null, null),
            "flangeWidth" => ("Ширина буртика b", (string?)null, null),
            _ => throw new ArgumentOutOfRangeException(nameof(key))
        };
        Font = Theme.Body;
        BackColor = Color.White;
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        ClientSize = new Size(430, lowerKey is null ? 190 : 260);
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(16), ColumnCount = 1, RowCount = 4 };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 69));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, lowerKey is null ? 0 : 80));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        layout.Controls.Add(Field("Номинал, мм", NominalInput, Value(key)), 0, 0);
        if (lowerKey is not null && upperKey is not null)
        {
            LowerInput = new TextBox();
            UpperInput = new TextBox();
            var deviations = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty };
            deviations.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            deviations.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            deviations.Controls.Add(Field("Нижнее откл., мм", LowerInput, Value(lowerKey)), 0, 0);
            deviations.Controls.Add(Field("Верхнее откл., мм", UpperInput, Value(upperKey)), 1, 0);
            layout.Controls.Add(deviations, 0, 1);
        }
        error.ForeColor = Theme.Error;
        error.TextAlign = ContentAlignment.MiddleLeft;
        layout.Controls.Add(error, 0, 2);
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, WrapContents = false, Margin = Padding.Empty };
        CancelEditorButton.Width = ApplyButton.Width = 110;
        CancelEditorButton.Height = ApplyButton.Height = 36;
        CancelEditorButton.DialogResult = DialogResult.Cancel;
        ApplyButton.Click += (_, _) => Apply();
        buttons.Controls.AddRange([CancelEditorButton, ApplyButton]);
        layout.Controls.Add(buttons, 0, 3);
        Controls.Add(layout);
        AcceptButton = ApplyButton;
        CancelButton = CancelEditorButton;
        Shown += (_, _) => { NominalInput.Focus(); NominalInput.SelectAll(); };
    }

    private Control Field(string caption, TextBox box, string text)
    {
        var row = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1, Margin = new Padding(0, 0, 10, 0) };
        row.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        row.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        row.Controls.Add(Theme.Label(caption, Theme.Small), 0, 0);
        box.Name = caption;
        box.Text = text;
        box.Font = Theme.Body;
        box.Dock = DockStyle.Top;
        box.Margin = Padding.Empty;
        box.TextChanged += (_, _) => { error.Text = ""; box.BackColor = Color.White; };
        row.Controls.Add(box, 0, 1);
        return row;
    }

    private static PropertyInfo Property(string key) => typeof(Parameters).GetProperty(char.ToUpperInvariant(key[0]) + key[1..])!;
    private string Value(string key) => MainForm.F((double)Property(key).GetValue(original)!);

    private void Apply()
    {
        var candidate = original with { };
        var fields = new List<(string Key, TextBox Input)> { (key, NominalInput) };
        if (lowerKey is not null && LowerInput is not null) fields.Add((lowerKey, LowerInput));
        if (upperKey is not null && UpperInput is not null) fields.Add((upperKey, UpperInput));
        foreach (var field in fields)
        {
            if (!MainForm.TryNumber(field.Input.Text, out var value))
            {
                field.Input.BackColor = Color.FromArgb(255, 239, 239);
                error.Text = "Введите число.";
                return;
            }
            Property(field.Key).SetValue(candidate, value);
        }
        var generated = generate(candidate);
        if (!generated.Ok || generated.Project is null)
        {
            foreach (var field in fields)
                if (generated.Errors.Any(e => e.Field == field.Key)) field.Input.BackColor = Color.FromArgb(255, 239, 239);
            error.Text = ShortError(generated.Errors, candidate);
            tips.SetToolTip(error, string.Join(Environment.NewLine, generated.Errors.Select(e => e.Message)));
            return;
        }
        Result = generated.Project;
        DialogResult = DialogResult.OK;
    }

    private string ShortError(IReadOnlyList<ValidationError> errors, Parameters candidate)
    {
        if (errors.Any(e => e.Message.Contains("3 мм", StringComparison.Ordinal))) return "Минимальная стенка — 3 мм.";
        if (errors.Any(e => e.Message.Contains("трёх знаков", StringComparison.Ordinal))) return "Не более 3 знаков после запятой.";
        if (errors.Any(e => e.Message.StartsWith("Верхнее отклонение", StringComparison.Ordinal))) return "Верхнее отклонение ниже нижнего.";
        foreach (var field in Engine.Fields)
        {
            if (!errors.Any(e => e.Field == field.Key)) continue;
            var value = (double)Property(field.Key).GetValue(candidate)!;
            if (value < field.Min || value > field.Max) return $"Допустимо: {MainForm.F(field.Min)}…{MainForm.F(field.Max)} {field.Unit}.";
        }
        var first = errors.FirstOrDefault()?.Message;
        return first is { Length: <= 43 } ? first : "Проверьте связь размеров.";
    }

    protected override void Dispose(bool disposing) { if (disposing) tips.Dispose(); base.Dispose(disposing); }
}
