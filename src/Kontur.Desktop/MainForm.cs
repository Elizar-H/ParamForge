using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using Kontur.Core;

namespace Kontur.Desktop;

internal sealed class MainForm : Form
{
    private readonly Dictionary<string, TextBox> inputs = new(StringComparer.OrdinalIgnoreCase);
    private readonly ErrorProvider errors = new() { BlinkStyle = ErrorBlinkStyle.NeverBlink };
    private readonly ToolTip tips = new() { AutoPopDelay = 14000, InitialDelay = 400 };
    private readonly SketchCanvas sketch = new() { Dock = DockStyle.Fill };
    private readonly Button exportButton = Theme.Button("Сформировать документы", true);
    private readonly ComboBox controllerInput = new() { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
    private ResultInspectionForm? inspection;
    private readonly Label status = Theme.Label("", Theme.Small);
    private readonly System.Windows.Forms.Timer previewTimer = new() { Interval = 180 };
    private Parameters working = Engine.Defaults;
    private Project? project;
    private bool loading;
    private bool stale = true;
    private readonly List<Font> smokeFonts = [];
    internal Project? CurrentProject => project;
    internal bool IsStale => stale;
    internal int MainNumericInputCount => inputs.Count;
    internal string StatusText => status.Text;
    internal bool DrawingLabelsInside => sketch.LabelsInside;
    internal bool DrawingLabelsOverlap => sketch.LabelsOverlap;
    internal bool RearWallsClosed => sketch.RearWallsClosed;
    internal bool DrawingLabelsClearOfMaterial => sketch.LabelsClearOfMaterial;
    internal bool DrawingBoreDimensionsClearOfMaterial => sketch.BoreDimensionsClearOfMaterial;
    internal int DrawingDpi => sketch.DeviceDpi;
    internal int DrawingDimensionCount => sketch.DimensionCount;
    internal float DrawingScale => sketch.DrawingScale;
    internal float DrawingCornerRadiusPixels => sketch.CornerRadiusPixels;
    internal int DrawingCornerArcCount => sketch.CornerArcCount;
    internal bool DrawingLabelsClearOfRearEdges => sketch.LabelsClearOfRearEdges;
    internal string DimensionText(string key) => sketch.DimensionText(key);

    internal MainForm()
    {
        Text = "ParamForge " + Engine.Version;
        Name = "ParamForgeMainForm";
        Font = Theme.Body;
        ForeColor = Theme.Ink;
        BackColor = Theme.Surface;
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(1280, 850);
        MinimumSize = new Size(1100, 720);
        StartPosition = FormStartPosition.CenterScreen;
        errors.ContainerControl = this;

        var menu = new MenuStrip { BackColor = Color.White, Font = Theme.Body, Padding = new Padding(12, 3, 0, 3) };
        var file = new ToolStripMenuItem("Файл");
        file.DropDownItems.Add("Открыть параметры JSON…", null, (_, _) => OpenParameters());
        file.DropDownItems.Add("Сохранить параметры JSON…", null, (_, _) => SaveParameters());
        file.DropDownItems.Add(new ToolStripSeparator());
        file.DropDownItems.Add("Исходные параметры", null, (_, _) => { SetParameters(Engine.Defaults); GenerateNow(); });
        file.DropDownItems.Add(new ToolStripSeparator());
        file.DropDownItems.Add("Выход", null, (_, _) => Close());
        menu.Items.Add(file);
        var view = new ToolStripMenuItem("Вид");
        var axes = new ToolStripMenuItem("Ноль детали и оси") { Checked = true, CheckOnClick = true };
        var tolerances = new ToolStripMenuItem("Показывать допуски") { CheckOnClick = true };
        axes.CheckedChanged += (_, _) => { sketch.ShowAxes = axes.Checked; sketch.Invalidate(); };
        tolerances.CheckedChanged += (_, _) => { sketch.ShowTolerances = tolerances.Checked; sketch.Invalidate(); };
        view.DropDownItems.AddRange([axes, tolerances]); menu.Items.Add(view);
        MainMenuStrip = menu;

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1, Padding = new Padding(18), BackColor = Theme.Surface };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 360));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 16));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.Controls.Add(BuildInputs(), 0, 0);
        var drawingPanel = new Panel { Dock = DockStyle.Fill, BackColor = Color.White, Padding = new Padding(1), Margin = Padding.Empty };
        drawingPanel.Controls.Add(sketch);
        root.Controls.Add(drawingPanel, 2, 0);
        Controls.Add(root);
        Controls.Add(menu);
        previewTimer.Tick += (_, _) => { previewTimer.Stop(); UpdatePreview(); };
        sketch.DimensionClicked += EditDimension;
        SetParameters(Engine.Defaults);
        GenerateNow();
    }

    private Control BuildInputs()
    {
        var panel = new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty };
        var footer = new TableLayoutPanel { Dock = DockStyle.Bottom, Height = 184, RowCount = 4, ColumnCount = 1, Padding = new Padding(0, 8, 0, 0) };
        footer.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        footer.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        footer.RowStyles.Add(new RowStyle(SizeType.Absolute, 49));
        footer.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        exportButton.Dock = DockStyle.Fill;
        exportButton.FlatAppearance.BorderColor = Theme.Accent;
        exportButton.Click += (_, _) => ExportDialog();
        controllerInput.Items.AddRange(["AxiOMA Control", "SINUMERIK 840D sl"]);
        controllerInput.AccessibleName = "Профиль ЧПУ";
        controllerInput.Font = Theme.Body;
        controllerInput.SelectedIndexChanged += (_, _) =>
        {
            if (loading) return;
            working = working with { Controller = controllerInput.SelectedIndex == 1 ? CncProfiles.Sinumerik : CncProfiles.Axioma };
            stale = true; GenerateNow();
        };
        footer.Controls.Add(controllerInput, 0, 0);
        var inspect = Theme.Button("Траектория УП…"); inspect.Dock = DockStyle.Fill;
        inspect.Click += (_, _) => OpenInspection(); footer.Controls.Add(inspect, 0, 1);
        footer.Controls.Add(exportButton, 0, 2);
        status.ForeColor = Theme.Muted;
        status.Padding = new Padding(0, 5, 0, 0);
        status.TextAlign = ContentAlignment.TopLeft;
        footer.Controls.Add(status, 0, 3);
        panel.Controls.Add(footer);

        var scroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(0, 0, 0, 4) };
        var form = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 1, RowCount = 5, Padding = Padding.Empty, Margin = Padding.Empty };
        form.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        form.RowStyles.Add(new RowStyle(SizeType.Absolute, 31));
        form.RowStyles.Add(new RowStyle(SizeType.Absolute, 365));
        form.RowStyles.Add(new RowStyle(SizeType.Absolute, 43));
        form.RowStyles.Add(new RowStyle(SizeType.Absolute, 35));
        form.RowStyles.Add(new RowStyle(SizeType.Absolute, 8));
        form.Controls.Add(Theme.Label("Геометрия", Theme.Strong), 0, 0);
        form.Controls.Add(BuildDimensions(), 0, 1);
        var processing = Theme.Button("Параметры обработки…");
        processing.Dock = DockStyle.Fill;
        processing.FlatAppearance.BorderColor = Theme.Border;
        processing.Click += (_, _) => OpenProcessing();
        form.Controls.Add(processing, 0, 2);
        var targets = Theme.Button("Расчётные размеры"); targets.Dock = DockStyle.Fill;
        targets.Click += (_, _) => ShowTargets();
        form.Controls.Add(targets, 0, 3);
        scroll.Controls.Add(form);
        panel.Controls.Add(scroll);
        scroll.BringToFront();
        return panel;
    }

    // The panel is built from parameter descriptors; the current geometry remains a sleeve.
    private static readonly (string Symbol, string Name, string Key, string? Low, string? High)[] Dimensions =
    [
        ("L", "Длина", "length", "lengthLowerDeviation", "lengthUpperDeviation"),
        ("D", "Диаметр бурта", "flangeDiameter", "flangeLowerDeviation", "flangeUpperDeviation"),
        ("d", "Диаметр отверстия", "boreDiameter", "boreLowerDeviation", "boreUpperDeviation"),
        ("d2", "Диаметр расточки", "counterboreDiameter", "counterboreLowerDeviation", "counterboreUpperDeviation"),
        ("h", "Глубина расточки", "counterboreDepth", "counterboreDepthLowerDeviation", "counterboreDepthUpperDeviation"),
        ("Db", "Диаметр тела", "bodyDiameter", null, null),
        ("b", "Ширина бурта", "flangeWidth", null, null)
    ];
    private Control BuildDimensions()
    {
        var tabs = new TabControl { Dock = DockStyle.Fill, Font = Theme.Small, Margin = Padding.Empty };
        var geometry = new TabPage("Размеры") { BackColor = Color.White, Padding = new Padding(4) };
        var tolerances = new TabPage("Отклонения") { BackColor = Color.White, Padding = new Padding(4) };
        tabs.TabPages.AddRange([geometry, tolerances]);
        var table = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, RowCount = Dimensions.Length, Margin = Padding.Empty };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 29));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 74));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 34));
        for (var row = 0; row < Dimensions.Length; row++)
        {
            table.RowStyles.Add(new RowStyle(SizeType.Percent, 100F / Dimensions.Length));
            var d = Dimensions[row];
            table.Controls.Add(Theme.Label(d.Symbol, Theme.Small), 0, row);
            var name = Theme.Label(d.Name, Theme.Small); name.Name = "dimension_caption_" + d.Key;
            table.Controls.Add(name, 1, row);
            var value = Input(d.Key); value.TextAlign = HorizontalAlignment.Right;
            table.Controls.Add(value, 2, row);
            var edit = Theme.Button("±"); edit.Name = "dimension_edit_" + d.Key;
            edit.AccessibleName = "Изменить размер и допуск: " + d.Name;
            edit.Dock = DockStyle.Fill; edit.Padding = Padding.Empty; edit.Margin = new Padding(2, 5, 0, 5);
            tips.SetToolTip(edit, "Размер и допуск"); edit.Click += (_, _) => EditDimension(d.Key);
            table.Controls.Add(edit, 3, row);
        }
        geometry.Controls.Add(table);
        var deviations = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 6, Margin = Padding.Empty };
        deviations.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        deviations.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 80));
        deviations.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 80));
        deviations.RowStyles.Add(new RowStyle(SizeType.Absolute, 29));
        deviations.Controls.Add(Theme.Label("Нижнее", Theme.Small), 1, 0);
        deviations.Controls.Add(Theme.Label("Верхнее", Theme.Small), 2, 0);
        var rowIndex = 1;
        foreach (var d in Dimensions.Where(d => d.Low is not null))
        {
            deviations.RowStyles.Add(new RowStyle(SizeType.Percent, 20));
            deviations.Controls.Add(Theme.Label(d.Name, Theme.Small), 0, rowIndex);
            deviations.Controls.Add(Input(d.Low!), 1, rowIndex);
            deviations.Controls.Add(Input(d.High!), 2, rowIndex++);
        }
        tolerances.Controls.Add(deviations);
        return tabs;
    }

    private TextBox Input(string key)
    {
        var field = Engine.Fields.First(f => f.Key == key);
        var box = new TextBox { Name = "parameter_" + key, Font = Theme.Body, BorderStyle = BorderStyle.FixedSingle, Dock = DockStyle.Fill, Margin = new Padding(3, 9, 5, 7), AccessibleName = field.Label };
        inputs.Add(key, box);
        tips.SetToolTip(box, field.Note);
        errors.SetIconAlignment(box, ErrorIconAlignment.MiddleLeft);
        errors.SetIconPadding(box, 1);
        box.TextChanged += (_, _) =>
        {
            if (loading) return;
            var dimension = Tolerances.Dimensions.FirstOrDefault(d =>
                string.Equals(d.LowerProperty, key, StringComparison.OrdinalIgnoreCase) || string.Equals(d.UpperProperty, key, StringComparison.OrdinalIgnoreCase));
            if (dimension is not null) working = Tolerances.SetDesignation(working, dimension.Key, "");
            stale = true;
            errors.SetError(box, "");
            box.BackColor = Color.White;
            status.Text = "";
            previewTimer.Stop();
            previewTimer.Start();
        };
        return box;
    }

    private static PropertyInfo Property(string key) => typeof(Parameters).GetProperty(char.ToUpperInvariant(key[0]) + key[1..]) ?? throw new InvalidOperationException(key);
    internal static string F(double value) => value.ToString("0.####", CultureInfo.GetCultureInfo("ru-RU"));
    internal static bool TryNumber(string text, out double value) => double.TryParse(text.Trim().Replace(',', '.'), NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out value) && double.IsFinite(value);

    internal void SetParameters(Parameters value)
    {
        loading = true;
        previewTimer.Stop();
        try
        {
            working = value;
            controllerInput.SelectedIndex = value.Controller == CncProfiles.Sinumerik ? 1 : 0;
            errors.Clear();
            foreach (var input in inputs)
            {
                input.Value.Text = F((double)Property(input.Key).GetValue(value)!);
                input.Value.BackColor = Color.White;
            }
        }
        finally { loading = false; }
        stale = true;
        status.Text = "";
        UpdatePreview();
    }

    internal void SetInput(string key, string value) { inputs[key].Text = value; UpdatePreview(); }
    internal string GetInput(string key) => inputs[key].Text;

    private Parameters? ReadParameters(bool showErrors, bool validate = true)
    {
        var result = working with { };
        var invalid = new List<ValidationError>();
        foreach (var input in inputs)
            if (TryNumber(input.Value.Text, out var number)) Property(input.Key).SetValue(result, number);
            else invalid.Add(new(input.Key, "Введите число в выделенном поле."));
        if (invalid.Count == 0)
        {
            try { result = Tolerances.Resolve(result); }
            catch (ArgumentException ex) { invalid.Add(new("tolerances", ex.Message)); }
        }
        if (invalid.Count == 0 && validate) invalid.AddRange(Engine.Validate(result));
        if (invalid.Count == 0) return result;
        if (showErrors) ShowErrors(invalid);
        return null;
    }

    private void UpdatePreview()
    {
        var value = ReadParameters(false, false);
        if (value is not null && Engine.Validate(value).Any(e => inputs.ContainsKey(e.Field) && !IsSetupLengthError(e))) value = null;
        if (value is not null)
        {
            loading = true;
            try
            {
                foreach (var dimension in Tolerances.Dimensions.Where(d => Tolerances.Designation(value, d).Length != 0))
                    foreach (var property in new[] { dimension.LowerProperty, dimension.UpperProperty })
                    {
                        var key = char.ToLowerInvariant(property[0]) + property[1..];
                        inputs[key].Text = F(Tolerances.Number(value, property));
                    }
            }
            finally { loading = false; }
        }
        sketch.Parameters = value;
        sketch.Invalidate();
        if (inspection is { IsDisposed: false }) inspection.SetResult(value is null ? null : Engine.Generate(value).Project);
    }

    // The selected bar/setup can reject a length without making the nominal
    // part impossible to draw. Generation still validates every process rule.
    private static bool IsSetupLengthError(ValidationError error) => error.Field == "length" &&
        (error.Message.StartsWith("Длина обработки превышает", StringComparison.Ordinal) ||
         error.Message.StartsWith("Недостаточно длины для второго установа", StringComparison.Ordinal));

    internal bool GenerateNow()
    {
        previewTimer.Stop();
        var value = ReadParameters(true);
        if (value is null) return false;
        var result = Engine.Generate(value);
        if (!result.Ok || result.Project is null) { ShowErrors(result.Errors); return false; }
        working = result.Project.Params;
        project = result.Project;
        stale = false;
        sketch.Parameters = value;
        status.Text = "";
        errors.Clear();
        if (inspection is { IsDisposed: false }) inspection.SetResult(project);
        return true;
    }

    private void ShowErrors(IReadOnlyList<ValidationError> invalid)
    {
        stale = true;
        foreach (var error in invalid)
            if (inputs.TryGetValue(error.Field, out var box))
            {
                errors.SetError(box, error.Message);
                box.BackColor = Color.FromArgb(255, 239, 239);
            }
        if (invalid.Count > 0 && inputs.TryGetValue(invalid[0].Field, out var first)) { first.Focus(); first.SelectAll(); }
        status.ForeColor = Theme.Error;
        status.Text = ShortError(invalid);
        tips.SetToolTip(status, string.Join(Environment.NewLine, invalid.Select(e => e.Message)));
        UpdatePreview();
    }

    private string ShortError(IReadOnlyList<ValidationError> invalid)
    {
        if (ProfileError(invalid) is { } profileError) return profileError;
        if (invalid.Any(e => e.Message.Contains("3 мм", StringComparison.Ordinal))) return "Минимальная стенка — 3 мм.";
        if (invalid.Any(e => e.Message.Contains("трёх знаков", StringComparison.Ordinal))) return "Не более 3 знаков после запятой.";
        if (invalid.Any(e => e.Message.StartsWith("Верхнее отклонение", StringComparison.Ordinal))) return "Верхнее отклонение ниже нижнего.";
        foreach (var error in invalid)
            if (inputs.TryGetValue(error.Field, out var box))
            {
                if (!TryNumber(box.Text, out var number)) return "Введите число.";
                var field = Engine.Fields.FirstOrDefault(f => f.Key == error.Field);
                if (field is not null && (number < field.Min || number > field.Max))
                {
                    var name = field.Key switch
                    {
                        "length" => "Длина L", "flangeDiameter" => "D", "bodyDiameter" => "Db", "flangeWidth" => "b",
                        "boreDiameter" => "d", "counterboreDiameter" => "d2", "counterboreDepth" => "h", _ => "Отклонение"
                    };
                    return $"{name}: {F(field.Min)}…{F(field.Max)} мм.";
                }
                if (error.Message.Length <= 38) return error.Message;
            }
        return invalid.Any(e => inputs.ContainsKey(e.Field)) ? "Проверьте связь размеров." : "Проверьте параметры обработки.";
    }

    internal static string? ProfileError(IReadOnlyList<ValidationError> errors)
    {
        if (errors.Any(e => e.Field == "length" && e.Message.StartsWith("Недостаточно длины для второго установа", StringComparison.Ordinal)))
            return "Мало длины для второго установа.";
        if (errors.Any(e => e.Field == "stockBore" && e.Message.Contains("оправки", StringComparison.Ordinal)))
            return $"Для оправки нужно отверстие от {F(ToolingProfile.BoringMinimumDiameter)} мм.";
        if (errors.Any(e => e.Field == "clearanceX" && e.Message.Contains("оправки", StringComparison.Ordinal)))
            return $"Отвод оправки: диаметр от {F(ToolingProfile.BoringMinimumDiameter)} мм.";
        if (errors.Any(e => e.Field == "length" && e.Message.StartsWith("Длина обработки превышает", StringComparison.Ordinal)))
            return $"Вылет оправки: обработка до {F(ToolingProfile.MaximumBoringDepth)} мм.";
        if (errors.Any(e => e.Field == "gripLength" && e.Message.StartsWith("Длина зажима", StringComparison.Ordinal)))
            return $"Зажим должен быть не короче {F(ToolingProfile.ChuckMinimumGrip)} мм.";
        if (errors.Any(e => e.Field == "chuckClearance" && e.Message.Contains("диапазон", StringComparison.Ordinal)))
        {
            var field = Engine.Fields.Single(f => f.Key == "chuckClearance");
            return $"Зазор до кулачков: {F(field.Min)}…{F(field.Max)} мм.";
        }
        if (errors.Any(e => e.Field == "chuckClearance" && e.Message.StartsWith("Осевой зазор", StringComparison.Ordinal)))
            return $"Зазор до кулачков — от {F(ToolingProfile.ChuckMinimumClearance)} мм.";
        if (errors.Any(e => e.Message.Contains("галтел", StringComparison.OrdinalIgnoreCase)))
            return "Недостаточно места для сопряжения.";
        return null;
    }

    private void OpenProcessing()
    {
        var value = ReadParameters(true, false);
        if (value is null) return;
        using var dialog = new ProcessingDialog(value, inputs.Keys.ToHashSet());
        if (dialog.ShowDialog(this) == DialogResult.OK) SetParameters(dialog.Result!);
    }

    private void EditDimension(string key)
    {
        var baseline = ReadParameters(false, false);
        if (baseline is null) return;
        previewTimer.Stop();
        UpdatePreview();
        using var editor = new DimensionEditDialog(key, baseline, candidate => Engine.Generate(candidate));
        if (editor.ShowDialog(this) != DialogResult.OK || editor.Result is null) return;
        SetParameters(editor.Result.Params);
        project = editor.Result;
        if (inspection is { IsDisposed: false }) inspection.SetResult(project);
        stale = false;
        sketch.Parameters = project.Params;
        status.Text = "";
    }

    internal void OpenInspection()
    {
        if (!GenerateNow()) return;
        if (inspection is null || inspection.IsDisposed) inspection = new ResultInspectionForm();
        inspection.SetResult(project); inspection.Show(this); inspection.Activate();
    }
    internal void SetController(string controller)
    {
        controllerInput.SelectedIndex = controller == CncProfiles.Sinumerik ? 1 : 0;
    }
    internal void SetDrawingOptions(bool axes, bool tolerances)
    {
        sketch.ShowAxes = axes; sketch.ShowTolerances = tolerances; sketch.Invalidate();
    }
    internal SketchCanvas Sketch => sketch;
    internal ResultInspectionForm? Inspection => inspection;
    internal Parameters? ReadForSmoke() => ReadParameters(false);

    internal Control DrawingControl => sketch;
    internal RectangleF DimensionHitBounds(string key) => sketch.DimensionHitBounds(key);
    internal string? HitDimension(Point point) => sketch.HitDimension(point);

    private void ShowTargets()
    {
        if (!GenerateNow()) return;
        var c = project!.Calculations;
        MessageBox.Show(this,
            $"d = {F(c.TargetBoreDiameter)} мм\nd2 = {F(c.TargetCounterboreDiameter)} мм\nD = {F(c.TargetFlangeDiameter)} мм\nL = {F(c.TargetLength)} мм\nh = {F(c.TargetCounterboreDepth)} мм\n\nЦели выбраны в середине полей допуска.\nДостижение допуска подтверждается измерениями.",
            "Расчётные размеры", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private void OpenParameters()
    {
        using var dialog = new OpenFileDialog { Title = "Открыть параметры", Filter = "Параметры JSON (*.json)|*.json", CheckFileExists = true };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try { ImportFromPath(dialog.FileName); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException) { FileError(ex.Message); }
    }

    internal void ImportFromPath(string path)
    {
        var imported = ProjectFiles.ImportParameters(path);
        var result = Engine.Generate(imported);
        if (!result.Ok || result.Project is null) throw new InvalidDataException(string.Join(Environment.NewLine, result.Errors.Select(e => e.Message)));
        SetParameters(imported);
        project = result.Project;
        stale = false;
    }

    private void SaveParameters()
    {
        if (!GenerateNow()) return;
        using var dialog = new SaveFileDialog { Title = "Сохранить параметры", Filter = "Параметры JSON (*.json)|*.json", FileName = "Параметры втулки.json", DefaultExt = "json", AddExtension = true, OverwritePrompt = true };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try { SaveToPath(dialog.FileName); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { FileError(ex.Message); }
    }

    internal void SaveToPath(string path)
    {
        if (stale || project is null) throw new InvalidOperationException("Параметры требуют проверки.");
        ProjectFiles.SaveParameters(project, path);
    }

    private void ExportDialog()
    {
        if (!GenerateNow()) return;
        using var picker = new FolderBrowserDialog { Description = "Куда сохранить документы?", UseDescriptionForTitle = true, ShowNewFolderButton = true };
        if (picker.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            var files = ExportToNewSubfolder(picker.SelectedPath);
            status.ForeColor = Theme.Muted;
            status.Text = "Готово: 3 файла.";
            using var result = new ExportResultDialog(files);
            result.ShowDialog(this);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException) { FileError(ex.Message); }
    }

    internal IReadOnlyList<string> ExportToNewSubfolder(string parentFolder)
    {
        if (stale || project is null) throw new InvalidOperationException("Параметры изменены; сначала выполните проверку.");
        var folder = Path.Combine(parentFolder, "Документы втулки");
        for (var suffix = 2; Directory.Exists(folder) || File.Exists(folder); suffix++)
            folder = Path.Combine(parentFolder, "Документы втулки (" + suffix + ")");
        return ProjectFiles.Export(project, folder);
    }

    private void FileError(string message) => MessageBox.Show(this, message, "Не удалось сохранить или открыть файл", MessageBoxButtons.OK, MessageBoxIcon.Error);

    internal void CaptureForm(string path)
    {
        PerformLayout();
        Refresh();
        Application.DoEvents();
        using var bitmap = new Bitmap(Width, Height);
        DrawToBitmap(bitmap, new Rectangle(Point.Empty, Size));
        bitmap.Save(path, System.Drawing.Imaging.ImageFormat.Png);
    }

    internal void CaptureProcessingDialog(string path)
    {
        using var dialog = new ProcessingDialog(working, inputs.Keys.ToHashSet());
        CaptureOwnedDialog(dialog, path);
    }

    internal void CaptureExportDialog(IReadOnlyList<string> files, string path)
    {
        using var dialog = new ExportResultDialog(files);
        CaptureOwnedDialog(dialog, path);
    }

    private void CaptureOwnedDialog(Form dialog, string path)
    {
        dialog.Show(this);
        Application.DoEvents();
        using var bitmap = new Bitmap(dialog.Width, dialog.Height);
        dialog.DrawToBitmap(bitmap, new Rectangle(Point.Empty, dialog.Size));
        bitmap.Save(path, System.Drawing.Imaging.ImageFormat.Png);
        dialog.Close();
    }

    // Text/layout stress simulation only; this does not change Windows display settings.
    internal void SimulateDpiText(int targetDpi)
    {
        var factor = targetDpi / (float)DeviceDpi;
        var controls = new List<Control>();
        void Collect(Control parent) { controls.Add(parent); foreach (Control child in parent.Controls) Collect(child); }
        Collect(this);
        var originals = controls.Select(c => (Control: c, Font: c.Font)).ToList();
        var originalSize = ClientSize;
        Scale(new SizeF(factor, factor));
        ClientSize = new Size((int)Math.Round(originalSize.Width * factor), (int)Math.Round(originalSize.Height * factor));
        foreach (var item in originals)
        {
            var font = new Font(item.Font.FontFamily, item.Font.Size * factor, item.Font.Style);
            smokeFonts.Add(font);
            item.Control.Font = font;
        }
        sketch.SimulatedTextScale = factor;
        PerformLayout();
        Refresh();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) { errors.Dispose(); tips.Dispose(); previewTimer.Dispose(); foreach (var font in smokeFonts) font.Dispose(); }
        base.Dispose(disposing);
    }

    private sealed class ProcessingDialog : Form
    {
        private readonly Dictionary<string, TextBox> boxes = [];
        private readonly Parameters original;
        private readonly ErrorProvider errors = new() { BlinkStyle = ErrorBlinkStyle.NeverBlink };
        internal Parameters? Result { get; private set; }
        internal ProcessingDialog(Parameters parameters, HashSet<string> mainKeys)
        {
            original = parameters;
            Text = "Параметры обработки";
            Font = Theme.Body;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(650, 700);
            MinimumSize = new Size(610, 540);
            MaximizeBox = false;
            MinimizeBox = false;
            BackColor = Color.White;
            errors.ContainerControl = this;
            var footer = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 58, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(12, 5, 12, 5) };
            var cancel = Theme.Button("Отмена"); cancel.Width = 140; cancel.DialogResult = DialogResult.Cancel;
            var apply = Theme.Button("Применить", true); apply.Width = 140;
            apply.Click += (_, _) => Apply();
            footer.Controls.AddRange([cancel, apply]);
            Controls.Add(footer);
            CancelButton = cancel;
            AcceptButton = apply;
            var scroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(18) };
            var table = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2 };
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 72));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 28));
            void Add(string key, string label)
            {
                var row = table.RowCount++;
                var property = Property(key);
                var value = property.GetValue(original);
                var box = new TextBox { Text = value is double number ? F(number) : value?.ToString(), Font = Theme.Body, Dock = DockStyle.Fill, Margin = new Padding(4, 9, 8, 6) };
                boxes.Add(key, box);
                if (property.PropertyType == typeof(string))
                {
                    table.RowStyles.Add(new RowStyle(SizeType.Absolute, 78));
                    var textRow = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Margin = Padding.Empty };
                    textRow.RowStyles.Add(new RowStyle(SizeType.Absolute, 29));
                    textRow.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
                    textRow.Controls.Add(Theme.Label(label, Theme.Small), 0, 0);
                    box.Margin = new Padding(0, 2, 8, 9);
                    textRow.Controls.Add(box, 0, 1);
                    table.Controls.Add(textRow, 0, row);
                    table.SetColumnSpan(textRow, 2);
                }
                else
                {
                    table.RowStyles.Add(new RowStyle(SizeType.Absolute, 47));
                    table.Controls.Add(Theme.Label(label, Theme.Small), 0, row);
                    table.Controls.Add(box, 1, row);
                }
            }
            Add("partName", "Наименование детали");
            Add("material", "Материал");
            foreach (var field in Engine.Fields.Where(f => !mainKeys.Contains(f.Key))) Add(field.Key, field.Label + ", " + field.Unit);
            scroll.Controls.Add(table);
            Controls.Add(scroll);
            scroll.BringToFront();
        }
        private void Apply()
        {
            errors.Clear();
            var value = original with { };
            var invalid = new List<ValidationError>();
            foreach (var pair in boxes)
            {
                var property = Property(pair.Key);
                if (property.PropertyType == typeof(string)) property.SetValue(value, pair.Value.Text.Trim());
                else if (TryNumber(pair.Value.Text, out var number)) property.SetValue(value, number);
                else invalid.Add(new(pair.Key, "Введите число."));
            }
            if (invalid.Count == 0) invalid.AddRange(Engine.Validate(value));
            if (invalid.Count > 0)
            {
                foreach (var error in invalid) if (boxes.TryGetValue(error.Field, out var box)) errors.SetError(box, error.Message);
                MessageBox.Show(this, ProfileError(invalid) ?? invalid[0].Message, "Проверьте параметры", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            Result = value;
            DialogResult = DialogResult.OK;
        }
        protected override void Dispose(bool disposing) { if (disposing) errors.Dispose(); base.Dispose(disposing); }
    }

    private sealed class ExportResultDialog : Form
    {
        internal ExportResultDialog(IReadOnlyList<string> files)
        {
            Text = "Файлы готовы"; Font = Theme.Body; ClientSize = new Size(620, 340);
            MinimumSize = new Size(540, 340); StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = false; MinimizeBox = false; BackColor = Color.White;
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(18), RowCount = 4, ColumnCount = 1 };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 33));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 70));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
            layout.Controls.Add(Theme.Label("Готово: 3 файла", Theme.Strong), 0, 0);
            var folder = Path.GetDirectoryName(files[0])!;
            layout.Controls.Add(new TextBox { Text = folder, ReadOnly = true, Multiline = true, BorderStyle = BorderStyle.None, BackColor = Color.White, Font = Theme.Small, Dock = DockStyle.Fill, ScrollBars = ScrollBars.Vertical, TabStop = false }, 0, 1);
            var list = new ListBox { Dock = DockStyle.Fill, Font = Theme.Body, BorderStyle = BorderStyle.None, IntegralHeight = false };
            foreach (var path in files) list.Items.Add(Path.GetFileName(path));
            list.SelectedIndex = 0;
            layout.Controls.Add(list, 0, 2);
            var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
            var openFolder = Theme.Button("Открыть папку"); openFolder.Width = 175;
            openFolder.Click += (_, _) => Open(folder);
            var openFile = Theme.Button("Открыть файл"); openFile.Width = 165;
            openFile.Click += (_, _) => { if (list.SelectedIndex >= 0) Open(files[list.SelectedIndex]); };
            var close = Theme.Button("Закрыть"); close.Width = 120; close.DialogResult = DialogResult.OK;
            buttons.Controls.AddRange([openFolder, openFile, close]);
            layout.Controls.Add(buttons, 0, 3);
            Controls.Add(layout);
            AcceptButton = close; CancelButton = close;
            Shown += (_, _) => list.Focus();
        }
        private void Open(string path)
        {
            try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "Не удалось открыть", MessageBoxButtons.OK, MessageBoxIcon.Information); }
        }
    }
}
