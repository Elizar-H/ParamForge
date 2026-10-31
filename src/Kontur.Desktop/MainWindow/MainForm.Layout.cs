using Kontur.Core;

namespace Kontur.Desktop;

internal sealed partial class MainForm
{
    private Control BuildInputs()
    {
        var panel = new Panel
        {
            Dock = DockStyle.Fill,
            Margin = Padding.Empty
        };
        var footer = new TableLayoutPanel
        {
            Dock = DockStyle.Bottom,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            RowCount = 4,
            ColumnCount = 1,
            Margin = Padding.Empty,
            Padding = new Padding(0, 8, 0, 0)
        };
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (var i = 0; i < 4; i++)
        {
            footer.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        }

        exportButton.Dock = DockStyle.Top;
        exportButton.AutoSize = true;
        exportButton.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        exportButton.MinimumSize = new Size(0, 38);
        exportButton.FlatAppearance.BorderColor = Theme.Accent;
        exportButton.Click += (_, _) => ExportDialog();
        controllerInput.Items.AddRange(["AxiOMA Control", "SINUMERIK 840D sl"]);
        controllerInput.AccessibleName = "Профиль ЧПУ";
        controllerInput.Font = Theme.Detail;
        controllerInput.Dock = DockStyle.Top;
        controllerInput.Margin = new Padding(4, 4, 4, 4);
        controllerInput.SelectedIndexChanged += (_, _) =>
        {
            if (loading)
            {
                return;
            }

            working = working with
            {
                Controller = controllerInput.SelectedIndex == 1 ? CncProfiles.Sinumerik : CncProfiles.Axioma
            };
            stale = true;
            GenerateNow();
        };
        footer.Controls.Add(controllerInput, 0, 0);
        var inspect = Theme.Button("Траектория УП");
        inspect.Name = "InspectionAction";
        inspect.Dock = DockStyle.Top;
        inspect.AutoSize = true;
        inspect.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        inspect.MinimumSize = new Size(0, 34);
        inspect.Click += (_, _) => OpenInspection();
        footer.Controls.Add(inspect, 0, 1);
        footer.Controls.Add(exportButton, 0, 2);
        status.ForeColor = Theme.Muted;
        status.Height = TextRenderer.MeasureText("0", status.Font).Height * 3 + 8;
        status.Padding = new Padding(0, 5, 0, 0);
        status.TextAlign = ContentAlignment.TopLeft;
        footer.Controls.Add(status, 0, 3);
        panel.Controls.Add(footer);
        var scroll = new Panel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            Padding = new Padding(0, 0, 0, 4)
        };
        var form = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 1,
            RowCount = 4,
            Padding = Padding.Empty,
            Margin = Padding.Empty
        };
        geometrySections = form;
        form.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        form.RowStyles.Add(new RowStyle(SizeType.Absolute, 31));
        form.RowStyles.Add(new RowStyle(SizeType.Absolute, 334));
        form.RowStyles.Add(new RowStyle(SizeType.Absolute, 43));
        form.RowStyles.Add(new RowStyle(SizeType.Absolute, 8));
        form.Controls.Add(Theme.Label("Геометрия", Theme.Strong), 0, 0);
        form.Controls.Add(BuildDimensions(), 0, 1);
        var processing = Theme.Button("Заготовка и обработка…");
        processing.Dock = DockStyle.Fill;
        processing.FlatAppearance.BorderColor = Theme.Border;
        processing.Click += (_, _) => OpenProcessing();
        form.Controls.Add(processing, 0, 2);
        scroll.Controls.Add(form);
        panel.Controls.Add(scroll);
        scroll.BringToFront();
        return panel;
    }

    // The panel is built from parameter descriptors; the current geometry remains a sleeve.
    private static readonly (string Symbol, string Name, string Key, string? Low, string? High)[] Dimensions = [("L", "Длина", "length", "lengthLowerDeviation", "lengthUpperDeviation"), ("D", "Диаметр бурта", "flangeDiameter", "flangeLowerDeviation", "flangeUpperDeviation"), ("d", "Диаметр отверстия", "boreDiameter", "boreLowerDeviation", "boreUpperDeviation"), ("d2", "Диаметр расточки", "counterboreDiameter", "counterboreLowerDeviation", "counterboreUpperDeviation"), ("h", "Глубина расточки", "counterboreDepth", "counterboreDepthLowerDeviation", "counterboreDepthUpperDeviation"), ("Db", "Диаметр тела", "bodyDiameter", "bodyLowerDeviation", "bodyUpperDeviation"), ("b", "Ширина бурта", "flangeWidth", "flangeWidthLowerDeviation", "flangeWidthUpperDeviation")];
    private Control BuildDimensions()
    {
        var table = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 6,
            RowCount = Dimensions.Length + 1,
            Margin = Padding.Empty,
            BackColor = Theme.Surface
        };
        dimensionTable = table;
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 36));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 74));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 80));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 0));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 34));
        table.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));
        foreach (var (column, caption) in new[]
        {
            (2, "Номинал"),
            (3, "Отклонения")
        }

        )
        {
            var label = Theme.Label(caption, Theme.Detail);
            label.ForeColor = Theme.Muted;
            label.TextAlign = column == 2 ? ContentAlignment.MiddleRight : ContentAlignment.MiddleLeft;
            table.Controls.Add(label, column, 0);
        }

        for (var row = 0; row < Dimensions.Length; row++)
        {
            table.RowStyles.Add(new RowStyle(SizeType.Percent, 100F / Dimensions.Length));
            var d = Dimensions[row];
            var index = row + 1;
            table.Controls.Add(Theme.Label(d.Symbol, Theme.Detail), 0, index);
            var name = Theme.Label(d.Name, Theme.Detail);
            name.Name = "dimension_caption_" + d.Key;
            name.TextAlign = ContentAlignment.MiddleRight;
            name.Padding = new Padding(0, 0, 8, 0);
            name.FontChanged += (_, _) => FitGeometryMetrics();
            table.Controls.Add(name, 1, index);
            var value = Input(d.Key);
            value.TextAlign = HorizontalAlignment.Right;
            table.Controls.Add(new NumericFieldHost(value), 2, index);
            var deviation = new DeviationReadout
            {
                Name = "dimension_edit_" + d.Key,
                BackColor = Theme.Surface
            };
            deviation.FontChanged += (_, _) => FitGeometryMetrics();
            deviation.Click += (_, _) => EditDimension(d.Key);
            deviationReadouts.Add(d.Key, deviation);
            table.Controls.Add(deviation, 3, index);
            if (d.Low is not null)
            {
                var low = Input(d.Low);
                var high = Input(d.High!);
                low.TextAlign = high.TextAlign = HorizontalAlignment.Right;
                var backing = new Panel
                {
                    Visible = false
                };
                backing.Controls.Add(low);
                backing.Controls.Add(high);
                deviation.Controls.Add(backing);
            }
        }

        NominalNavigation.Attach(Dimensions.Select(d => inputs[d.Key]).ToArray(), () =>
        {
            UpdatePreview();
            if (ReadParameters(false) is not null)
            {
                GenerateNow();
            }
        });
        table.Layout += (_, _) => FitGeometryMetrics();
        ApplyToleranceView();
        FitGeometryMetrics();
        return table;
    }
    private void FitGeometryMetrics()
    {
        if (fittingGeometry || dimensionTable is null || geometrySections is null || deviationReadouts.Count != Dimensions.Length)
        {
            return;
        }

        fittingGeometry = true;
        try
        {
            var names = dimensionTable.Controls.OfType<Label>().Where(c => c.Name.StartsWith("dimension_caption_", StringComparison.Ordinal)).ToArray();
            var nameWidth = names.Max(c => TextRenderer.MeasureText(c.Text, c.Font).Width + c.Padding.Horizontal) + 4;
            var numericWidth = Math.Max(
                TextRenderer.MeasureText("Номинал", names[0].Font).Width,
                TextRenderer.MeasureText("000,000", inputs["length"].Font).Width
            ) + 12;
            var toleranceWidth = Math.Max(
                TextRenderer.MeasureText("Отклонения", names[0].Font).Width,
                TextRenderer.MeasureText("-0,0000", deviationReadouts.Values.First().Font).Width
            ) + 8;
            var symbolWidth = TextRenderer.MeasureText("Db", names[0].Font).Width + 6;
            foreach (var (column, width) in new[]
            {
                (0, symbolWidth),
                (1, nameWidth),
                (2, numericWidth),
                (3, toleranceWidth),
                (4, 0),
                (5, 0)
            }

            )
            {
                dimensionTable.ColumnStyles[column].SizeType = SizeType.Absolute;
                dimensionTable.ColumnStyles[column].Width = width;
            }

            geometryColumn.Width = nameWidth + numericWidth + toleranceWidth + symbolWidth + SystemInformation.VerticalScrollBarWidth;
            var rowHeight = Math.Max(TextRenderer.MeasureText("0", inputs["length"].Font).Height, TextRenderer.MeasureText("0", names[0].Font).Height) + 10;
            var totalHeight = dimensionTable.RowStyles[0].Height;
            for (var row = 1; row < dimensionTable.RowStyles.Count; row++)
            {
                var readout = deviationReadouts[Dimensions[row - 1].Key];
                var height = Math.Max(rowHeight, readout.NumericLineCount * TextRenderer.MeasureText("0", readout.Font).Height + 4);
                dimensionTable.RowStyles[row].SizeType = SizeType.Absolute;
                dimensionTable.RowStyles[row].Height = height;
                totalHeight += height;
            }

            geometrySections.RowStyles[1].Height = totalHeight;
        }
        finally
        {
            fittingGeometry = false;
        }
    }
    private void InitializeLayout()
    {
        var menu = new MenuStrip
        {
            BackColor = Color.White,
            Font = Theme.Body,
            Padding = new Padding(12, 3, 0, 3)
        };
        menu.Renderer = new ApplicationMenuRenderer();

        var file = new ToolStripMenuItem("Файл");

        file.DropDownItems.Add("Открыть параметры JSON…", null, (_, _) => OpenParameters());

        file.DropDownItems.Add("Сохранить параметры JSON…", null, (_, _) => SaveParameters());

        file.DropDownItems.Add(new ToolStripSeparator());

        file.DropDownItems.Add("Исходные параметры", null, (_, _) =>
        {
            SetParameters(Engine.Defaults with
            {
                AutoStock = true
            });
            GenerateNow();
        });

        file.DropDownItems.Add(new ToolStripSeparator());

        file.DropDownItems.Add("Выход", null, (_, _) => Close());

        menu.Items.Add(file);

        var view = new ToolStripMenuItem("Вид");

        var axes = new ToolStripMenuItem("Ноль детали и оси")
        {
            Checked = true,
            CheckOnClick = true
        };

        axes.CheckedChanged += (_, _) =>
        {
            sketch.ShowAxes = axes.Checked;
            sketch.Invalidate();
        };

        toleranceView.CheckedChanged += (_, _) =>
        {
            ApplyToleranceView();
            sketch.ShowTolerances = toleranceView.Checked;
            sketch.Invalidate();
        };

        view.DropDownItems.AddRange([axes, toleranceView]);
        ApplicationMenuRenderer.Configure(view);
        ApplicationMenuRenderer.Configure(file);

        menu.Items.Add(view);

        MainMenuStrip = menu;

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 1,
            Padding = new Padding(18),
            BackColor = Theme.Surface
        };

        root.ColumnStyles.Add(geometryColumn);

        root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 16));

        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        geometryInputs = BuildInputs();

        geometryHost.Controls.Add(geometryInputs);

        root.Controls.Add(geometryHost, 0, 0);

        var drawingPanel = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.White,
            Padding = new Padding(1),
            Margin = Padding.Empty
        };

        drawingPanel.Controls.Add(sketch);

        root.Controls.Add(drawingPanel, 2, 0);

        geometryPage.Controls.Add(root);
        workspace.TabPages.Add(geometryPage);
        workspace.SelectedIndexChanged += (_, _) =>
        {
            CloseDimensionEditor();
            if (workspace.SelectedTab == processingPage && processing is not null && ReadParameters(false, false) is { } latest)
            {
                processing.Rebase(latest);
            }
        };
        Controls.Add(workspace);

        Controls.Add(menu);

        Controls.Add(dimensionFlyout);

    }
}
