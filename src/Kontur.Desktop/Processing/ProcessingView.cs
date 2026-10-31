using System.ComponentModel;
using System.Reflection;
using Kontur.Core;
namespace Kontur.Desktop;

internal sealed partial class ProcessingView : UserControl
{
    private Parameters original;
    private readonly Dictionary<string, TextBox> boxes = [];
    private readonly Dictionary<string, Label> targets = [];
    private readonly HashSet<string> manualDeviations = [];
    private readonly ErrorProvider errors = new() { BlinkStyle = ErrorBlinkStyle.NeverBlink };
    private readonly ToolTip tips = new();
    private readonly Label error = Theme.Label("", Theme.Detail);
    private readonly Label stockMinimum = Theme.Label("", Theme.Detail);
    private readonly ComboBox stockMode = new() { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Top };
    private bool updating;
    private bool applied;
    private string manualDiameter = "", manualBore = "";
    internal readonly SettingsSections Sections = new();
    internal readonly Button ChooseStock = Theme.Button("Рассчитать по детали");
    internal readonly Button ApplyButton = Theme.Button("Применить", true);
    internal readonly Button CancelEditorButton = Theme.Button("Отмена");
    private readonly HashSet<string> editedProperties = [];
    internal IReadOnlySet<string> EditedProperties => editedProperties;
    internal bool IsDirty => editedProperties.Count != 0;
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal Func<Parameters, GenerationResult>? GenerateCandidate
    {
        get; set;
    }
    internal event Action<Project>? Accepted;
    internal event Action? Cancelled;
    internal void Close() => Cancelled?.Invoke();
    private readonly Label boreLimits = Theme.Label("", Theme.Detail);
    internal string BoreLimitsText => boreLimits.Text;
    internal readonly LinkLabel EditRetract = new() { Text = "Изменить отвод инструмента", AutoSize = true, LinkColor = Theme.Ink, ActiveLinkColor = Theme.Ink, VisitedLinkColor = Theme.Ink, LinkBehavior = LinkBehavior.HoverUnderline, Font = Theme.Detail };
    internal Parameters? Result
    {
        get; private set;
    }
    internal string ErrorText => error.Text;
    internal TextBox Field(string key) => boxes[key];
    internal ComboBox StockMode => stockMode;
    internal void FocusField(string key)
    {
        if (!boxes.TryGetValue(key, out var box))
        {
            return;
        }
        Sections.ShowField(box);
        if (box.ReadOnly && key is "stockBore" or "stockDiameter")
        {
            stockMode.Focus();
        }
        else
        {
            box.Focus();
            box.SelectAll();
        }
    }
    internal ProcessingView(Parameters parameters, bool targets = false)
    {
        original = StockSelection.Resolve(Tolerances.Resolve(parameters));
        Text = "Заготовка и обработка";
        Font = Theme.Detail;
        BackColor = Color.White;
        AutoScaleMode = AutoScaleMode.Dpi;
        errors.ContainerControl = this;
        var footer = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 58, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(12, 5, 12, 5) };
        var cancel = CancelEditorButton;
        cancel.Width = 140;
        cancel.Click += (_, _) => Cancelled?.Invoke();
        var apply = ApplyButton;
        apply.Width = 140;
        apply.Click += (_, _) => Apply();
        footer.Controls.AddRange([cancel, apply]);
        error.Dock = DockStyle.Bottom;
        error.Height = 42;
        error.Padding = new Padding(18, 0, 18, 0);
        error.ForeColor = Theme.Error;
        error.Visible = false;
        error.TextChanged += (_, _) => error.Visible = error.Text.Length != 0;
        Controls.Add(Sections);
        Controls.Add(error);
        Controls.Add(footer);
        Sections.BringToFront();
        tips.SetToolTip(Sections.Navigation, "← / → — раздел; ↓ — к полям. Enter — следующая строка; Ctrl+Enter — применить.");
        foreach (var (caption, group) in new[] { ("Заготовка", "Заготовка"), ("Закрепление", "Зажим"), ("Режимы", "Режимы"), ("Подходы", "Подходы") })
        {
            var table = Page(caption);
            if (group == "Заготовка")
            {
                stockMode.Items.AddRange(["Рассчитывать по детали", "Задать размеры"]);
                stockMode.SelectedIndex = original.AutoStock ? 0 : 1;
                var row = table.RowCount++;
                table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                table.Controls.Add(Theme.Label("Выбор заготовки"), 0, row);
                stockMode.Margin = new Padding(5, 8, 0, 8);
                table.Controls.Add(stockMode, 1, row);
            }
            foreach (var field in Engine.Fields.Where(f => Group(f) == group))
            {
                var label = field.Key switch
                {
                    "stockBore" => "Начальное отверстие",
                    "stockDiameter" => "Наружный диаметр",
                    "trimAllowance" => "Запас на задний торец",
                    "frontFacingAllowance" => "Запас на передний торец",
                    "gripLength" => "Длина в кулачках",
                    _ => field.Label
                };
                AddField(table, field.Key, label + ", " + field.Unit, field.Note);
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
                row = table.RowCount++;
                table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                boreLimits.AutoSize = true;
                boreLimits.Margin = new Padding(0, 16, 0, 6);
                table.Controls.Add(boreLimits, 0, row);
                table.SetColumnSpan(boreLimits, 2);
                row = table.RowCount++;
                table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                EditRetract.Margin = new Padding(0, 0, 0, 12);
                EditRetract.LinkClicked += (_, _) => FocusField("clearanceX");
                table.Controls.Add(EditRetract, 0, row);
                table.SetColumnSpan(EditRetract, 2);
                tips.SetToolTip(
                    stockMode,
                    "Расчётная: наружный запас 4 мм, отверстие на 2 мм меньше d, округление до целого мм; учитываются припуски и выбранная оправка. Это расчёт, не наличие сортамента."
                );
            }
        }
        AddTargets();
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
            if (updating)
            {
                return;
            }
            editedProperties.Add("autoStock");
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
                editedProperties.Add("stockDiameter");
                editedProperties.Add("stockBore");
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
            var originalValue = Property(key).GetValue(original);
            var unchanged = originalValue is double number ? NumericText.TryParse(box.Text, out var parsed) && parsed == number : Equals(originalValue, box.Text.Trim());
            if (unchanged)
            {
                editedProperties.Remove(key);
            }
            else
            {
                editedProperties.Add(key);
            }
            if (d is not null)
            {
                manualDeviations.Add(d.Key);
                editedProperties.Add(Key(d.DesignationProperty));
            }

            error.Text = "";
            RefreshValues();
        };
        }

        RefreshValues();
        AttachKeyboardNavigation();
        if (targets)
        {
            Sections.SelectedIndex = 4;
        }
    }
    private static string Key(string property) => char.ToLowerInvariant(property[0]) + property[1..];
    private static string Group(FieldDefinition field) => field.Key is "gripLength" or "chuckClearance" ? "Зажим" : field.Group;
    private static PropertyInfo Property(string key) => typeof(Parameters).GetProperty(char.ToUpperInvariant(key[0]) + key[1..])!;
    private TableLayoutPanel Page(string caption)
    {
        var description = caption switch
        {
            "Заготовка" => "Полая заготовка перед началом обработки.",
            "Закрепление" => "Зажим для операции 010. Переустановка выполняется позже, в операции 020.",
            "Режимы" => "Глубина проходов, подачи и ограничение оборотов.",
            "Подходы" => "Свободное место для подвода и отвода инструмента.",
            "Деталь" => "Наименование и материал для документов.",
            _ => "Реквизиты маршрутной и операционной карт."
        };
        var page = Sections.AddPage(caption == "Подходы" ? "Движения инструмента" : caption, description);
        var table = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2, RowCount = 0, Margin = Padding.Empty };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 60));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40));
        page.Controls.Add(table);
        table.BringToFront();
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
            ChooseStock.Visible = !auto;
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
            var (minimum, maximum) = StockSelection.BoreLimits(p);
            boreLimits.ForeColor = minimum > maximum || p.StockBore < minimum || p.StockBore > maximum ? Theme.Error : Theme.Muted;
            boreLimits.Text = minimum > maximum
                ? $"Оправке с отводом нужно от Ø{NumericText.Format(minimum)} мм.\nДля растачивания допустимо до Ø{NumericText.Format(maximum)} мм.\nУменьшите отвод или выберите другую оправку."
                : $"Допустимое начальное отверстие: Ø{NumericText.Format(minimum)}…Ø{NumericText.Format(maximum)} мм.\nМеньше — не пройдёт оправка с отводом.\nБольше — не хватит запаса для растачивания.";
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
            foreach (var key in editedProperties.ToArray())
            {
                if (boxes.TryGetValue(key, out var box) && Property(key).PropertyType == typeof(double) && !NumericText.TryParse(box.Text, out _))
                {
                    continue;
                }
                if (Equals(Property(key).GetValue(p), Property(key).GetValue(original)))
                {
                    editedProperties.Remove(key);
                }
            }
        }
        catch (Exception ex) when (ex is ArgumentException or OverflowException) { stockMinimum.Text = "Проверьте размеры и отклонения."; }
        finally
        {
            updating = false;
            UpdateApplyState();
        }
    }
    private void UpdateApplyState()
    {
        ApplyButton.Enabled = IsDirty;
        ApplyButton.Text = !IsDirty && applied ? "Применено" : "Применить";
        ApplyButton.BackColor = IsDirty ? Theme.Accent : Theme.Surface;
        ApplyButton.ForeColor = IsDirty ? Color.White : Theme.Muted;
        ApplyButton.FlatAppearance.BorderColor = IsDirty ? Theme.Accent : Theme.Border;
        ApplyButton.Cursor = IsDirty ? Cursors.Hand : Cursors.Default;
    }
    private void AddTargets()
    {
        var page = Sections.AddPage("Размеры и допуски", "Для обработки используется середина допуска. Номинал и отклонения можно изменить.");
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
        page.Controls.Add(table);
        table.BringToFront();
    }
    private void Apply()
    {
        if (!IsDirty)
        {
            return;
        }
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
        Project? generatedProject = null;
        if (invalid.Count == 0)
        {
            var generated = GenerateCandidate?.Invoke(value) ?? Engine.Generate(value);
            invalid.AddRange(generated.Errors);
            generatedProject = generated.Project;
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

            FocusField(invalid[0].Field);
            return;
        }
        if (generatedProject is null)
        {
            return;
        }
        Result = generatedProject.Params;
        applied = true;
        editedProperties.Clear();
        manualDeviations.Clear();
        Rebase(Result);
        Accepted?.Invoke(generatedProject);
    }

    internal void Rebase(Parameters latest)
    {
        updating = true;
        try
        {
            original = latest;
            foreach (var (key, box) in boxes)
            {
                if (editedProperties.Contains(key))
                {
                    continue;
                }
                var value = Property(key).GetValue(latest);
                box.Text = value is double number ? NumericText.Format(number) : value?.ToString();
            }
            if (!editedProperties.Contains("autoStock"))
            {
                stockMode.SelectedIndex = latest.AutoStock ? 0 : 1;
            }
            if (stockMode.SelectedIndex == 1)
            {
                manualDiameter = boxes["stockDiameter"].Text;
                manualBore = boxes["stockBore"].Text;
            }
        }
        finally { updating = false; }
        RefreshValues();
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == Keys.Escape)
        {
            Cancelled?.Invoke();
            return true;
        }
        if (keyData == (Keys.Control | Keys.Enter))
        {
            Apply();
            return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
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
