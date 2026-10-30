using System.Reflection;
using Kontur.Core;
namespace Kontur.Desktop;

internal sealed class ProcessingDialog : Form
{
    private readonly Parameters original;
    private readonly Dictionary<string, TextBox> boxes = [];
    private readonly Dictionary<string, Label> targets = [];
    private readonly HashSet<string> manualDeviations = [];
    private readonly ErrorProvider errors = new() { BlinkStyle = ErrorBlinkStyle.NeverBlink };
    private readonly ToolTip tips = new();
    private readonly Label error = Theme.Label("", Theme.Detail);
    private readonly Label stockMinimum = Theme.Label("", Theme.Detail);
    private readonly ComboBox stockMode = new() { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Top };
    private bool updating;
    private string manualDiameter = "", manualBore = "";
    internal readonly TabControl Tabs = new() { Dock = DockStyle.Fill, Name = "ProcessingTabs", Multiline = true };
    internal readonly Button ChooseStock = Theme.Button("Подобрать размеры");
    internal Parameters? Result
    {
        get; private set;
    }
    internal string ErrorText => error.Text;
    internal TextBox Field(string key) => boxes[key];
    internal ComboBox StockMode => stockMode;
    internal ProcessingDialog(Parameters parameters, HashSet<string> mainKeys, bool targets = false)
    {
        original = StockSelection.Resolve(Tolerances.Resolve(parameters));
        Text = "Заготовка и обработка";
        Font = Theme.Detail;
        BackColor = Color.White;
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(880, 650);
        MinimumSize = new Size(800, 570);
        StartPosition = FormStartPosition.CenterParent;
        MaximizeBox = MinimizeBox = false;
        errors.ContainerControl = this;
        var footer = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 58, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(12, 5, 12, 5) };
        var cancel = Theme.Button("Отмена");
        cancel.Width = 140;
        cancel.DialogResult = DialogResult.Cancel;
        var apply = Theme.Button("Применить", true);
        apply.Width = 140;
        apply.Click += (_, _) => Apply();
        footer.Controls.AddRange([cancel, apply]);
        AcceptButton = apply;
        CancelButton = cancel;
        error.Dock = DockStyle.Bottom;
        error.Height = 42;
        error.Padding = new Padding(18, 0, 18, 0);
        error.ForeColor = Theme.Error;
        Controls.Add(Tabs);
        Controls.Add(error);
        Controls.Add(footer);
        Tabs.BringToFront();
        foreach (var (caption, group) in new[] { ("Заготовка", "Заготовка"), ("Закрепление", "Зажим"), ("Режимы", "Режимы"), ("Подходы", "Подходы") })
        {
            var table = Page(caption);
            if (group == "Заготовка")
            {
                stockMode.Items.AddRange(["Расчётная", "Задана вручную"]);
                stockMode.SelectedIndex = original.AutoStock ? 0 : 1;
                var row = table.RowCount++;
                table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                table.Controls.Add(Theme.Label("Выбор заготовки"), 0, row);
                stockMode.Margin = new Padding(5, 8, 0, 8);
                table.Controls.Add(stockMode, 1, row);
            }
            foreach (var field in Engine.Fields.Where(f => Group(f) == group))
            {
                AddField(table, field.Key, field.Label + ", " + field.Unit, field.Note);
            }

            if (group == "Заготовка")
            {
                var row = table.RowCount++;
                table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                stockMinimum.ForeColor = Theme.Muted;
                stockMinimum.AutoSize = true;
                stockMinimum.Margin = new Padding(0, 8, 8, 8);
                table.Controls.Add(stockMinimum, 0, row);
                ChooseStock.Dock = DockStyle.Top;
                ChooseStock.AutoSize = true;
                ChooseStock.Margin = new Padding(5, 4, 0, 6);
                ChooseStock.Click += (_, _) => { stockMode.SelectedIndex = 0; RefreshValues(); };
                table.Controls.Add(ChooseStock, 1, row);
                tips.SetToolTip(
                    stockMode,
                    "Расчётная: наружный запас 4 мм, отверстие на 2 мм меньше d, округление до целого мм; учитываются припуски и выбранная оправка. Это расчёт, не наличие сортамента."
                );
            }
        }
        AddTargets();
        NominalNavigation.Attach(Tolerances.EditableDimensions.Select(d => boxes[Key(d.NominalProperty)]).ToArray());
        var metadata = Page("Деталь");
        AddField(metadata, "partName", "Наименование детали", "");
        AddField(metadata, "material", "Материал", "");
        var cards = Page("Карты");
        foreach (var field in CardFields.Fields)
        {
            AddField(cards, field.Key, field.Label, "Необязательное поле документа");
        }

        manualDiameter = boxes["stockDiameter"].Text;
        manualBore = boxes["stockBore"].Text;
        stockMode.SelectedIndexChanged += (_, _) =>
        {
            if (stockMode.SelectedIndex == 0)
            {
                manualDiameter = boxes["stockDiameter"].Text;
                manualBore = boxes["stockBore"].Text;
            }
            else
            {
                updating = true;
                boxes["stockDiameter"].Text = manualDiameter;
                boxes["stockBore"].Text = manualBore;
                updating = false;
            }
            RefreshValues();
        };
        foreach (var (key, box) in boxes)
        {
            box.TextChanged += (_, _) =>
        {
            if (updating)
            {
                return;
            }

            var d = Tolerances.EditableDimensions.SingleOrDefault(d => Key(d.LowerProperty) == key || Key(d.UpperProperty) == key);
            if (d is not null)
            {
                manualDeviations.Add(d.Key);
            }

            error.Text = "";
            RefreshValues();
        };
        }

        RefreshValues();
        if (targets)
        {
            Tabs.SelectedIndex = 4;
        }
    }
    private static string Key(string property) => char.ToLowerInvariant(property[0]) + property[1..];
    private static string Group(FieldDefinition field) => field.Key is "gripLength" or "chuckClearance" ? "Зажим" : field.Group;
    private static PropertyInfo Property(string key) => typeof(Parameters).GetProperty(char.ToUpperInvariant(key[0]) + key[1..])!;
    private TableLayoutPanel Page(string caption)
    {
        var page = new TabPage(caption) { BackColor = Color.White, Padding = new Padding(16) };
        var scroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
        var table = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2, RowCount = 0, Margin = Padding.Empty };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 60));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40));
        scroll.Controls.Add(table);
        page.Controls.Add(scroll);
        Tabs.TabPages.Add(page);
        return table;
    }
    private TextBox Box(string key)
    {
        var value = Property(key).GetValue(original);
        var box = new SelectAllTextBox
        {
            Name = "processing_" + key,
            Text = value is double number ? NumericText.Format(number) : value?.ToString(),
            Font = Theme.Numeric,
            Dock = DockStyle.Top,
            Margin = new Padding(5, 7, 0, 7),
            TextAlign = value is string ? HorizontalAlignment.Left : HorizontalAlignment.Right
        };
        boxes.Add(key, box);
        return box;
    }
    private void AddField(TableLayoutPanel table, string key, string label, string note)
    {
        var row = table.RowCount++;
        table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var caption = Theme.Label(label, Theme.Detail);
        caption.AutoSize = true;
        table.Controls.Add(caption, 0, row);
        var box = Box(key);
        tips.SetToolTip(box, note);
        table.Controls.Add(box, 1, row);
    }
    private Parameters CurrentValues()
    {
        var value = original with
        {
            AutoStock = stockMode.SelectedIndex == 0
        };
        foreach (var (key, box) in boxes)
        {
            var property = Property(key);
            if (property.PropertyType == typeof(string))
            {
                property.SetValue(value, box.Text.Trim());
            }
            else if (NumericText.TryParse(box.Text, out var number))
            {
                property.SetValue(value, number);
            }
        }
        foreach (var d in Tolerances.EditableDimensions.Where(d => manualDeviations.Contains(d.Key)))
        {
            Property(d.DesignationProperty).SetValue(value, "");
        }

        return StockSelection.Resolve(Tolerances.Resolve(value));
    }
    private void RefreshValues()
    {
        if (updating)
        {
            return;
        }

        updating = true;
        try
        {
            var p = CurrentValues();
            var auto = p.AutoStock;
            foreach (var key in new[] { "stockDiameter", "stockBore" })
            {
                boxes[key].ReadOnly = auto;
                boxes[key].BackColor = auto ? Theme.Surface : Color.White;
                if (auto)
                {
                    boxes[key].Text = NumericText.Format((double)Property(key).GetValue(p)!);
                }
            }
            stockMinimum.Text = auto ? "Размеры следуют за деталью" : "Сохраняются при изменении детали";
            foreach (var d in Tolerances.EditableDimensions)
            {
                var nominal = Tolerances.Number(p, d.NominalProperty);
                var lo = Tolerances.Number(p, d.LowerProperty);
                var hi = Tolerances.Number(p, d.UpperProperty);
                if (!manualDeviations.Contains(d.Key) && Tolerances.Designation(p, d).Length != 0)
                {
                    boxes[Key(d.LowerProperty)].Text = NumericText.Format(lo);
                    boxes[Key(d.UpperProperty)].Text = NumericText.Format(hi);
                }
                targets[d.Key].Text = NumericText.Format(Engine.Target(nominal, lo, hi));
            }
        }
        catch (Exception ex) when (ex is ArgumentException or OverflowException) { stockMinimum.Text = "Проверьте размеры и отклонения."; }
        finally { updating = false; }
    }
    private void AddTargets()
    {
        var page = new TabPage("Размеры для УП") { BackColor = Color.White, Padding = new Padding(16) };
        var explanation = Theme.Label("Номинал и отклонения можно менять. Для УП используется середина допуска.", Theme.Detail);
        explanation.Dock = DockStyle.Top;
        explanation.Height = 44;
        var table = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 5, RowCount = 8 };
        foreach (var width in new[] { 28F, 18F, 18F, 18F, 18F })
        {
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, width));
        }

        table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var titles = new[] { "Размер", "Номинал", "Нижнее", "Верхнее", "Для УП" };
        for (var col = 0; col < 5; col++)
        {
            var label = Theme.Label(titles[col], Theme.Detail);
            label.AutoSize = true;
            table.Controls.Add(label, col, 0);
        }
        for (var row = 0; row < Tolerances.EditableDimensions.Count; row++)
        {
            var d = Tolerances.EditableDimensions[row];
            table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            var name = d.Key switch
            {
                "length" => "Длина L",
                "flangeDiameter" => "Бурт D",
                "boreDiameter" => "Отверстие d",
                "counterboreDiameter" => "Расточка d2",
                "counterboreDepth" => "Глубина h",
                "bodyDiameter" => "Тело Db",
                _ => "Ширина b"
            };
            table.Controls.Add(Theme.Label(name, Theme.Detail), 0, row + 1);
            table.Controls.Add(Box(Key(d.NominalProperty)), 1, row + 1);
            table.Controls.Add(Box(Key(d.LowerProperty)), 2, row + 1);
            table.Controls.Add(Box(Key(d.UpperProperty)), 3, row + 1);
            var target = Theme.Label("", Theme.Strong);
            target.TextAlign = ContentAlignment.MiddleRight;
            targets.Add(d.Key, target);
            table.Controls.Add(target, 4, row + 1);
        }
        var body = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
        body.Controls.Add(table);
        body.Controls.Add(explanation);
        table.BringToFront();
        page.Controls.Add(body);
        Tabs.TabPages.Add(page);
    }
    private void Apply()
    {
        errors.Clear();
        var invalid = new List<ValidationError>();
        foreach (var (key, box) in boxes)
        {
            if (Property(key).PropertyType != typeof(string) && !NumericText.TryParse(box.Text, out _))
            {
                invalid.Add(new(key, "Введите число."));
            }
        }

        Parameters value;
        try
        {
            value = CurrentValues();
        }
        catch (Exception ex) when (ex is ArgumentException or OverflowException) { error.Text = ex.Message; return; }
        if (invalid.Count == 0)
        {
            invalid.AddRange(Engine.Validate(value));
        }

        if (invalid.Count != 0)
        {
            error.Text = ParameterMessages.ProfileError(invalid) ?? invalid[0].Message;
            foreach (var item in invalid)
            {
                if (boxes.TryGetValue(item.Field, out var input))
                {
                    errors.SetError(input, item.Message);
                }
            }

            if (boxes.TryGetValue(invalid[0].Field, out var first))
            {
                for (Control? parent = first.Parent; parent is not null; parent = parent.Parent)
                {
                    if (parent is TabPage page)
                    {
                        Tabs.SelectedTab = page;
                    }
                }

                first.Focus();
                first.SelectAll();
            }
            return;
        }
        Result = value;
        DialogResult = DialogResult.OK;
    }
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            errors.Dispose();
            tips.Dispose();
        }
        base.Dispose(disposing);
    }
}
