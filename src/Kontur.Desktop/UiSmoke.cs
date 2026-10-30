using System.Text.Json;
using System.Reflection;
using System.Runtime.InteropServices;
using Kontur.Core;

namespace Kontur.Desktop;

internal static partial class UiSmoke
{
    internal static void Run(string folder)
    {
        Directory.CreateDirectory(folder);
        var checks = new List<object>();
        var failures = new List<string>();
        void Check(string name, bool passed, object? evidence = null)
        {
            checks.Add(new { name, passed, evidence });
            if (!passed) failures.Add(name);
        }
        try
        {
            using var form = new MainForm();
            form.Show();
            Application.DoEvents();
            Check("native_form", form.Visible && form.IsHandleCreated, new { type = form.GetType().BaseType?.FullName, actualDeviceDpi = form.DeviceDpi });
            Check("seventeen_main_numeric_inputs", form.MainNumericInputCount == 17, form.MainNumericInputCount);
            Check("default_generation", form.CurrentProject?.Verification.Ok == true && !form.IsStale);
            form.CaptureForm(Path.Combine(folder, "01-default-1280x850.png"));
            CheckCaptions(form, "default", Check);
            Check("default_profile_stock_lengths", form.CurrentProject!.Calculations.StockLength == 155.5 && form.CurrentProject.Calculations.RawStockLength == 157.5 && form.CurrentProject.Calculations.CutLength == 117.5, form.CurrentProject.Calculations);
            Check("default_radius_04_to_scale", form.DrawingDimensionCount == 7 && form.DrawingCornerArcCount == 4 && Math.Abs(form.DrawingCornerRadiusPixels - ToolingProfile.CornerRadius * form.DrawingScale) < .0001,
                new { form.DrawingCornerRadiusPixels, form.DrawingScale, form.DrawingCornerArcCount });
            Check("default_labels_and_closed_walls", form.DrawingLabelsInside && !form.DrawingLabelsOverlap && form.RearWallsClosed && form.DrawingLabelsClearOfMaterial && form.DrawingBoreDimensionsClearOfMaterial && form.DrawingLabelsClearOfRearEdges);

            form.SetInput("length", "118");
            Check("change_marks_stale", form.IsStale);
            var staleBlocked = false;
            try { form.ExportToNewSubfolder(Path.Combine(folder, "stale-must-not-export")); }
            catch (InvalidOperationException) { staleBlocked = true; }
            Check("stale_export_blocked", staleBlocked && !Directory.Exists(Path.Combine(folder, "stale-must-not-export")));
            Check("changed_length_recalculates", form.GenerateNow() && form.CurrentProject!.Params.Length == 118 && form.CurrentProject.Calculations.TargetLength == 117.5);
            form.SetInput("length", "ошибка");
            Check("invalid_input_rejected", !form.GenerateNow() && form.IsStale && form.StatusText == "Введите число.");
            form.CaptureForm(Path.Combine(folder, "02-invalid-input.png"));

            form.SetParameters(Engine.Defaults with { BodyDiameter = 76 });
            Check("thin_wall_geometry_rejected", !form.GenerateNow() && form.IsStale && form.StatusText == "Минимальная стенка — 3 мм.");
            form.CaptureForm(Path.Combine(folder, "02b-invalid-dimensions.png"));
            var geometryBlocked = false;
            var rejectedFolder = Path.Combine(folder, "invalid-geometry-must-not-export");
            try { form.ExportToNewSubfolder(rejectedFolder); }
            catch (InvalidOperationException) { geometryBlocked = true; }
            Check("invalid_geometry_export_blocked", geometryBlocked && !Directory.Exists(rejectedFolder));
            foreach (var (name, unsupported, message) in new[]
            {
                ("length160", Engine.Defaults with { Length = 160 }, "Вылет оправки: обработка до 120 мм."),
                ("length250", Engine.Defaults with { Length = 250 }, "Вылет оправки: обработка до 120 мм."),
                ("stock_bore18", Engine.Defaults with { StockBore = 18 }, "Для оправки нужно отверстие от 40 мм."),
                ("holder_clearance", Engine.Defaults with { ClearanceX = 3 }, "Отвод оправки: диаметр от 40 мм."),
                ("grip15", Engine.Defaults with { GripLength = 15 }, "Зажим должен быть не короче 25 мм."),
                ("chuck_clearance5", Engine.Defaults with { ChuckClearance = 5 }, "Зазор до кулачков: 10…40 мм."),
                ("corner_flat", Engine.Defaults with { BodyDiameter = 118.2 }, "Недостаточно места для сопряжения."),
                ("second_setup_length20", Engine.Defaults with { Length = 20, FlangeDiameter = 200, BodyDiameter = 180, FlangeWidth = 2, BoreDiameter = 130, CounterboreDiameter = 150, CounterboreDepth = 1, StockDiameter = 204, StockBore = 128 }, "Мало длины для второго установа.")
            })
            {
                form.SetParameters(unsupported);
                Check("tool_profile_" + name + "_rejected_clearly", !form.GenerateNow() && form.IsStale && form.StatusText == message, form.StatusText);
                var blockedPath = Path.Combine(folder, "unsupported-" + name + "-must-not-export");
                var blocked = false;
                try { form.ExportToNewSubfolder(blockedPath); }
                catch (InvalidOperationException) { blocked = true; }
                Check("tool_profile_" + name + "_export_blocked", blocked && !Directory.Exists(blockedPath));
                if (name is "length160" or "length250" or "stock_bore18" or "holder_clearance" or "grip15" or "chuck_clearance5" or "second_setup_length20")
                {
                    form.Refresh(); Application.DoEvents(); RenderDrawing(form);
                    Check("tool_profile_" + name + "_keeps_nominal_drawing", form.DrawingDimensionCount == 7 &&
                        form.DimensionText("length") == MainForm.F(unsupported.Length) && form.RearWallsClosed &&
                        form.DrawingLabelsInside && !form.DrawingLabelsOverlap,
                        new { form.DrawingDimensionCount, length = form.DimensionText("length") });
                }
                if (name is "length160" or "stock_bore18" or "second_setup_length20") form.CaptureForm(Path.Combine(folder, "14-unsupported-" + name + ".png"));
            }
            form.SetParameters(Engine.Defaults);
            form.SetInput("length", "-1");
            Check("negative_length_rejected", !form.GenerateNow() && form.IsStale && form.StatusText == "Длина L: 20…250 мм.");
            form.SetParameters(Engine.Defaults with { FlangeDiameter = 98, FlangeLowerDeviation = 3, FlangeUpperDeviation = 4 });
            Check("contradictory_nominal_drawing_rejected", !form.GenerateNow() && form.IsStale && form.StatusText == "Для чертежа нужно Db < D.");
            form.CaptureForm(Path.Combine(folder, "02c-invalid-nominal.png"));
            form.SetParameters(Engine.Defaults with { BoreLowerDeviation = 1, BoreUpperDeviation = 0 });
            Check("reversed_tolerance_has_clear_error", !form.GenerateNow() && form.IsStale && form.StatusText == "Верхнее отклонение ниже нижнего.");
            form.SetParameters(Engine.Defaults);
            form.SetInput("length", "116,0001");
            Check("excess_precision_has_clear_error", !form.GenerateNow() && form.IsStale && form.StatusText == "Не более 3 знаков после запятой.");

            var shortProfile = Engine.Defaults with { Length = 80, FlangeWidth = 8, CounterboreDepth = 10, BodyDiameter = 84, FlangeDiameter = 100, StockDiameter = 104 };
            var longProfile = Engine.Defaults with { Length = 118, FlangeWidth = 20, CounterboreDepth = 60, BodyDiameter = 110, FlangeDiameter = 140, StockDiameter = 144, BoreDiameter = 50, CounterboreDiameter = 80 };
            foreach (var (name, profile) in new[] { ("03-short", shortProfile), ("04-long", longProfile) })
            {
                form.SetParameters(profile);
                Check(name + "_valid", form.GenerateNow());
                form.CaptureForm(Path.Combine(folder, name + "-1280x850.png"));
                Check(name + "_drawing", form.DrawingLabelsInside && !form.DrawingLabelsOverlap && form.RearWallsClosed && form.DrawingLabelsClearOfMaterial && form.DrawingBoreDimensionsClearOfMaterial && form.DrawingLabelsClearOfRearEdges);
            }
            var thinProfile = Engine.Defaults with { Length = 118, FlangeDiameter = 60, BodyDiameter = 55, FlangeWidth = 2, BoreDiameter = 47.3, CounterboreDiameter = 50.5, CounterboreDepth = 1, StockDiameter = 64, StockBore = 45 };
            var wideProfile = Engine.Defaults with { Length = 60, FlangeDiameter = 200, BodyDiameter = 180, FlangeWidth = 2, BoreDiameter = 130, CounterboreDiameter = 150, CounterboreDepth = 1, StockDiameter = 204, StockBore = 128 };
            foreach (var (name, profile) in new[] { ("09-extreme-thin", thinProfile), ("10-extreme-wide", wideProfile) })
            {
                form.SetParameters(profile);
                Check(name + "_valid", form.GenerateNow());
                form.CaptureForm(Path.Combine(folder, name + "-1280x850.png"));
                Check(name + "_drawing", form.DrawingLabelsInside && !form.DrawingLabelsOverlap && form.RearWallsClosed && form.DrawingLabelsClearOfMaterial && form.DrawingBoreDimensionsClearOfMaterial && form.DrawingLabelsClearOfRearEdges);
                form.Size = new Size(1100, 720);
                form.CaptureForm(Path.Combine(folder, name + "-minimum.png"));
                Check(name + "_minimum_drawing", form.DrawingLabelsInside && !form.DrawingLabelsOverlap && form.RearWallsClosed && form.DrawingLabelsClearOfMaterial && form.DrawingBoreDimensionsClearOfMaterial && form.DrawingLabelsClearOfRearEdges,
                    new { labelsInside = form.DrawingLabelsInside, labelsOverlap = form.DrawingLabelsOverlap, rearWallsClosed = form.RearWallsClosed, labelsClearOfMaterial = form.DrawingLabelsClearOfMaterial, boreDimensionsClearOfMaterial = form.DrawingBoreDimensionsClearOfMaterial, rearEdgesClear = form.DrawingLabelsClearOfRearEdges });
                form.ClientSize = new Size(1280, 850);
            }
            foreach (var length in new[] { 80D, 116D, 118D })
                foreach (var depth in new[] { 10D, 28D, 60D })
                {
                    form.SetParameters(Engine.Defaults with { Length = length, CounterboreDepth = depth });
                    var valid = form.GenerateNow();
                    form.Refresh();
                    Application.DoEvents();
                    Check($"drawing_L{length}_h{depth}", valid && form.DrawingLabelsInside && !form.DrawingLabelsOverlap && form.RearWallsClosed && form.DrawingLabelsClearOfMaterial && form.DrawingBoreDimensionsClearOfMaterial && form.DrawingLabelsClearOfRearEdges);
                }
            form.SetParameters(Engine.Defaults);
            form.GenerateNow();
            form.Size = new Size(1100, 720);
            form.CaptureForm(Path.Combine(folder, "05-minimum-1100x720.png"));
            CheckCaptions(form, "minimum", Check);
            Check("minimum_radius_not_enlarged", form.DrawingDimensionCount == 7 && (ToolingProfile.CornerRadius * form.DrawingScale < 1
                ? form.DrawingCornerArcCount == 0 && form.DrawingCornerRadiusPixels == 0
                : form.DrawingCornerArcCount == 4 && Math.Abs(form.DrawingCornerRadiusPixels - ToolingProfile.CornerRadius * form.DrawingScale) < .0001),
                new { form.DrawingCornerRadiusPixels, form.DrawingScale, form.DrawingCornerArcCount });
            Check("minimum_drawing", form.DrawingLabelsInside && !form.DrawingLabelsOverlap && form.RearWallsClosed && form.DrawingLabelsClearOfMaterial && form.DrawingBoreDimensionsClearOfMaterial && form.DrawingLabelsClearOfRearEdges, new { form.Width, form.Height });
            form.ClientSize = new Size(1280, 850);


            // Exercise the canvas MouseClick event path at rendered hit coordinates and its real owned modal editor.
            var edits = new[] { ("length", 118D), ("flangeDiameter", 122D), ("bodyDiameter", 98D),
                ("flangeWidth", 14D), ("boreDiameter", 48D), ("counterboreDiameter", 76D), ("counterboreDepth", 32D) };
            foreach (var (key, nominal) in edits)
            {
                form.SetParameters(Engine.Defaults); form.GenerateNow(); form.Refresh(); Application.DoEvents(); RenderDrawing(form);
                var before = form.CurrentProject!.Params;
                var bounds = form.DimensionHitBounds(key);
                Check("hit_area_" + key, bounds.Width >= 28 && bounds.Height >= 28 && form.HitDimension(Point.Round(new PointF(bounds.X + bounds.Width / 2, bounds.Y + bounds.Height / 2))) == key, bounds);
                EditByCanvasClick(form, key, editor =>
                {
                    editor.NominalInput.Text = MainForm.F(nominal);
                    if (key == "flangeWidth") CaptureDialog(editor, Path.Combine(folder, "11c-flange-width-editor.png"));
                    PressDialogKey(editor, Keys.Enter);
                });
                var after = form.CurrentProject!.Params;
                Check("canvas_edit_" + key + "_syncs_and_keeps_other_values",
                    !form.IsStale && form.GetInput(key) == MainForm.F(nominal) && SameOtherParameters(before, after, key), after);
            }
            form.SetParameters(Engine.Defaults); form.GenerateNow(); form.Refresh(); Application.DoEvents(); RenderDrawing(form);
            Check("seven_nominal_dimension_labels", form.DrawingDimensionCount == 7 && edits.All(pair =>
                form.DimensionText(pair.Item1) == ((pair.Item1 is "bodyDiameter" or "flangeDiameter" or "boreDiameter" or "counterboreDiameter") ? "⌀" : "") + MainForm.F((double)typeof(Parameters).GetProperty(char.ToUpperInvariant(pair.Item1[0]) + pair.Item1[1..])!.GetValue(Engine.Defaults)!)));
            var beforeEditor = JsonSerializer.Serialize(form.CurrentProject!);
            EditByCanvasClick(form, "length", editor =>
            {
                editor.NominalInput.Text = "150";
                editor.LowerInput!.Text = "-0,2";
                PressDialogKey(editor, Keys.Escape);
            });
            Check("canvas_escape_discards_changes", beforeEditor == JsonSerializer.Serialize(form.CurrentProject!) && form.GetInput("length") == "116");
            EditByCanvasClick(form, "bodyDiameter", editor =>
            {
                Check("nominal_only_dimension_has_no_fake_tolerances", editor.LowerInput is null && editor.UpperInput is null);
                editor.NominalInput.Text = "99"; CaptureDialog(editor, Path.Combine(folder, "11b-body-diameter-editor.png")); editor.CancelEditorButton.PerformClick();
            });
            Check("canvas_cancel_button_discards_changes", beforeEditor == JsonSerializer.Serialize(form.CurrentProject!));
            EditByCanvasClick(form, "boreDiameter", editor =>
            {
                editor.NominalInput.Text = "48"; editor.LowerInput!.Text = "-0,1"; editor.UpperInput!.Text = "0,5";
                CheckButtonPair("dimension_editor", editor.ApplyButton, editor.CancelEditorButton, Check);
                CaptureDialog(editor, Path.Combine(folder, "11-dimension-editor.png"));
                PressDialogKey(editor, Keys.Enter);
            });
            Check("canvas_tolerance_edit_recalculates_target", !form.IsStale && form.CurrentProject!.Calculations.TargetBoreDiameter == 48.2 && form.GetInput("boreLowerDeviation") == "-0,1" && form.GetInput("boreUpperDeviation") == "0,5");
            form.SetParameters(Engine.Defaults); form.GenerateNow(); form.Refresh(); Application.DoEvents(); RenderDrawing(form);
            beforeEditor = JsonSerializer.Serialize(form.CurrentProject!);
            foreach (var (name, key, text, error) in new[] { ("not_number", "length", "abc", "Введите число."), ("geometry", "flangeDiameter", "90", "Для чертежа нужно Db < D.") })
            {
                EditByCanvasClick(form, key, editor =>
                {
                    editor.NominalInput.Text = text; editor.ApplyButton.PerformClick();
                    Check("canvas_" + name + "_does_not_apply", editor.Visible && editor.Result is null && editor.ErrorText == error && beforeEditor == JsonSerializer.Serialize(form.CurrentProject!));
                    CaptureDialog(editor, Path.Combine(folder, "12-invalid-editor-" + name + ".png"));
                    PressDialogKey(editor, Keys.Escape);
                });
                Check("canvas_" + name + "_preserves_model_and_side_fields", beforeEditor == JsonSerializer.Serialize(form.CurrentProject!) && !form.IsStale);
            }
            EditByCanvasClick(form, "counterboreDepth", editor =>
            {
                editor.LowerInput!.Text = "1"; editor.UpperInput!.Text = "0"; editor.ApplyButton.PerformClick();
                Check("canvas_reversed_tolerance_rejected", editor.Visible && editor.Result is null && editor.ErrorText == "Верхнее отклонение ниже нижнего.");
                PressDialogKey(editor, Keys.Escape);
            });
            EditByCanvasClick(form, "length", editor =>
            {
                editor.NominalInput.Text = "160"; editor.ApplyButton.PerformClick();
                Check("canvas_overhang_limit_has_tool_message", editor.Visible && editor.Result is null && editor.ErrorText == "Вылет оправки: обработка до 120 мм.");
                CaptureDialog(editor, Path.Combine(folder, "15-editor-tool-limit.png"));
                PressDialogKey(editor, Keys.Escape);
            });
            form.Size = new Size(1100, 720); form.Refresh(); Application.DoEvents(); RenderDrawing(form);
            EditByCanvasClick(form, "counterboreDiameter", editor =>
            {
                editor.NominalInput.Text = "76"; PressDialogKey(editor, Keys.Enter);
            });
            Check("canvas_edit_after_resize", form.GetInput("counterboreDiameter") == "76" && !form.IsStale);
            form.ClientSize = new Size(1280, 850); form.SetParameters(Engine.Defaults); form.GenerateNow();

            // Regression: a real side TextChanged starts the debounce, then a
            // dimension is opened and cancelled before that timer can tick.
            foreach (var cancel in new[] { "Escape", "CancelButton" })
            {
                foreach (var (scenario, sideKey, value, drawingText, dimensionCount) in new[]
                {
                    ("valid", "length", "118", "118", 7),
                    ("invalid", "bodyDiameter", "76", "", 0)
                })
                {
                    form.SetParameters(Engine.Defaults); form.GenerateNow(); RenderDrawing(form);
                    var previousProject = JsonSerializer.Serialize(form.CurrentProject!);
                    var sideBox = Descendants(form).OfType<TextBox>().Single(box => box.Name == "parameter_" + sideKey);
                    sideBox.Text = value; // Do not use SetInput: it eagerly updates the preview.
                    EditByCanvasClick(form, "boreDiameter", editor =>
                    {
                        if (cancel == "Escape") PressDialogKey(editor, Keys.Escape);
                        else editor.CancelEditorButton.PerformClick();
                    }, refresh: false);
                    form.CaptureForm(Path.Combine(folder, "13-pending-side-" + scenario + "-after-" + cancel + ".png"));
                    Check("cancel_pending_side_" + scenario + "_" + cancel + "_updates_preview_without_applying_project",
                        form.GetInput(sideKey) == value && form.IsStale && form.StatusText == "" &&
                        JsonSerializer.Serialize(form.CurrentProject!) == previousProject &&
                        form.DimensionText("length") == drawingText && form.DrawingDimensionCount == dimensionCount,
                        new { sideKey, sideValue = form.GetInput(sideKey), form.IsStale, form.StatusText,
                            drawnLength = form.DimensionText("length"), drawnDimensions = form.DrawingDimensionCount });
                }
            }
            form.SetParameters(Engine.Defaults); form.GenerateNow();

            var json = Path.Combine(folder, "parameters.json");
            form.SaveToPath(json);
            var invalidJson = Path.Combine(folder, "invalid-import.json");
            File.WriteAllText(invalidJson, "{\"length\":-1}");
            var invalidImport = false;
            try { form.ImportFromPath(invalidJson); }
            catch (InvalidDataException) { invalidImport = true; }
            Check("invalid_import_preserves_form", invalidImport && form.GetInput("length") == MainForm.F(Engine.Defaults.Length) && !form.IsStale);
            form.SetInput("length", "118");
            form.ImportFromPath(json);
            Check("valid_import", !form.IsStale && form.CurrentProject!.Params.Length == 116);

            var beforeCancel = JsonSerializer.Serialize(form.CurrentProject!);
            var beforeCancelFiles = FileSnapshot(folder);
            foreach (var action in new[] { "ExportDialog", "OpenParameters", "SaveParameters" })
            {
                var title = CancelOwnModal(form, () => InvokeAction(form, action));
                Check("cancel_" + action + "_preserves_project_and_files",
                    !form.IsStale && beforeCancel == JsonSerializer.Serialize(form.CurrentProject!) && beforeCancelFiles == FileSnapshot(folder), title);
            }
            var originalPartName = form.CurrentProject!.Params.PartName;
            var processingTitle = CancelOwnModal(form, () => InvokeAction(form, "OpenProcessing"), dialog =>
            {
                Check("stock_profile_fields_only_in_processing", form.MainNumericInputCount == 17 &&
                    Descendants(dialog).OfType<Label>().Any(label => label.Text == "Зазор до кулачков, мм") &&
                    Descendants(dialog).OfType<Label>().Any(label => label.Text == "Припуск переднего торца, мм"));
                var pair = Descendants(dialog).OfType<Button>().ToArray();
                CheckButtonPair("processing", pair.Single(button => button.Text == "Применить"), pair.Single(button => button.Text == "Отмена"), Check);
                var partName = Descendants(dialog).OfType<TextBox>().First(box => box.Text == originalPartName);
                partName.Text = "Discarded QA edit";
            });
            Check("cancel_processing_discards_dialog_edits", !form.IsStale && beforeCancel == JsonSerializer.Serialize(form.CurrentProject!) && beforeCancelFiles == FileSnapshot(folder), processingTitle);

            var exportParent = Path.Combine(folder, "export-destination");
            Directory.CreateDirectory(exportParent);
            File.WriteAllText(Path.Combine(exportParent, "existing-user-file.txt"), "preserve this file");
            var previous = Path.Combine(exportParent, "Документы втулки");
            Directory.CreateDirectory(previous);
            File.WriteAllText(Path.Combine(previous, "previous-user-file.txt"), "preserve previous export");
            var files = form.ExportToNewSubfolder(exportParent);
            var expected = new[] { "Маршрутная карта.docx", "Технологическая карта.docx", "Управляющая программа.nc" };
            Check("exactly_three_deliverables", files.Count == 3 && files.Select(Path.GetFileName).Order().SequenceEqual(expected.Order()) && Directory.GetFiles(Path.GetDirectoryName(files[0])!).Length == 3, files);
            Check("previous_files_preserved", File.ReadAllText(Path.Combine(exportParent, "existing-user-file.txt")) == "preserve this file" && File.ReadAllText(Path.Combine(previous, "previous-user-file.txt")) == "preserve previous export" && Path.GetDirectoryName(files[0]) != previous);
            var firstExport = FileSnapshot(Path.GetDirectoryName(files[0])!);
            var fileAtNextName = Path.Combine(exportParent, "Документы втулки (3)");
            File.WriteAllText(fileAtNextName, "preserve occupied folder name");
            form.SetInput("length", "118");
            Check("repeat_export_recalculates_new_geometry", form.GenerateNow());
            var repeated = form.ExportToNewSubfolder(exportParent);
            Check("repeat_export_keeps_previous_bundle_and_skips_file_name",
                repeated.Count == 3 && Directory.GetFiles(Path.GetDirectoryName(repeated[0])!).Length == 3 &&
                Path.GetFileName(Path.GetDirectoryName(repeated[0])) == "Документы втулки (4)" &&
                firstExport == FileSnapshot(Path.GetDirectoryName(files[0])!) &&
                File.ReadAllText(fileAtNextName) == "preserve occupied folder name" &&
                File.ReadAllText(repeated[2]).Contains("L 117.5", StringComparison.Ordinal), repeated);
            form.SetParameters(Engine.Defaults);
            form.GenerateNow();
            RunV24Checks(form, folder, Check);
            RunV25Checks(form, folder, Check);
            form.CaptureProcessingDialog(Path.Combine(folder, "07-processing-dialog.png"));
            form.CaptureExportDialog(files, Path.Combine(folder, "08-export-result-dialog.png"));
        }
        catch (Exception ex) { failures.Add(ex.ToString()); }

        foreach (var targetDpi in new[] { 96, 120, 144, 192 })
        {
            try
            {
                using var simulated = new MainForm();
                simulated.Show();
                Application.DoEvents();
                simulated.SimulateDpiText(targetDpi);
                simulated.CaptureForm(Path.Combine(folder, $"06-text-scale-simulation-{targetDpi}dpi.png"));
                CheckCaptions(simulated, "scaled_" + targetDpi, Check);
                Check("text_scale_simulation_" + targetDpi, simulated.DrawingLabelsInside && !simulated.DrawingLabelsOverlap && simulated.RearWallsClosed && simulated.DrawingLabelsClearOfMaterial && simulated.DrawingBoreDimensionsClearOfMaterial && simulated.DrawingLabelsClearOfRearEdges,
                    new { simulatedDpi = targetDpi, actualWindowsDpi = simulated.DeviceDpi, scope = "application text scaling simulation; Windows DPI settings unchanged" });
                simulated.SetDrawingOptions(true, true); RenderDrawing(simulated);
                Check("v25_tolerance_scale_" + targetDpi, simulated.DrawingLabelsInside && !simulated.DrawingLabelsOverlap && simulated.DrawingLabelsClearOfRearEdges && simulated.Sketch.LabelsClearOfDimensionLines, null);
                simulated.CaptureForm(Path.Combine(folder, $"06-tolerances-simulation-{targetDpi}dpi.png"));
                simulated.SetDrawingOptions(true, false);
                foreach (var key in new[] { "length", "flangeDiameter", "bodyDiameter", "flangeWidth", "boreDiameter", "counterboreDiameter", "counterboreDepth" })
                {
                    var before = JsonSerializer.Serialize(simulated.CurrentProject!);
                    EditByCanvasClick(simulated, key, editor => PressDialogKey(editor, Keys.Escape));
                    Check("scaled_hit_" + targetDpi + "_" + key, before == JsonSerializer.Serialize(simulated.CurrentProject!) && !simulated.IsStale);
                }
                simulated.Close();
            }
            catch (Exception ex) { failures.Add("simulation " + targetDpi + ": " + ex); }
        }
        File.WriteAllText(Path.Combine(folder, "ui-smoke.json"), JsonSerializer.Serialize(new
        {
            ok = failures.Count == 0, framework = "Windows Forms", executedAtUtc = DateTime.UtcNow,
            dpiTesting = "Actual host DPI plus text/control scaling simulations. These simulations are not real Windows DPI-switch tests.",
            dimensionEditingTesting = "Rendered hit coordinates through Control.OnMouseClick into actual modal dialogs; Form.ProcessDialogKey checks Enter/Esc. No global OS input injection.",
            checks, failures
        }, new JsonSerializerOptions { WriteIndented = true }));
        Environment.ExitCode = failures.Count == 0 ? 0 : 1;
    }


    private static void CheckCaptions(MainForm form, string name, Action<string, bool, object?> check)
    {
        var labels = Descendants(form).OfType<Label>().Where(label => label.Name.StartsWith("dimension_caption_", StringComparison.Ordinal)).ToArray();
        var expected = new Dictionary<string, string>
        {
            ["length"] = "Длина", ["flangeDiameter"] = "Диаметр бурта", ["boreDiameter"] = "Диаметр отверстия",
            ["counterboreDiameter"] = "Диаметр расточки", ["counterboreDepth"] = "Глубина расточки", ["bodyDiameter"] = "Диаметр тела", ["flangeWidth"] = "Ширина бурта"
        };
        var evidence = labels.Select(label => new
        {
            key = label.Name["dimension_caption_".Length..], text = label.Text.Replace('\n', ' '),
            availableWidth = label.ClientSize.Width, availableHeight = label.ClientSize.Height,
            measuredWidth = label.Text.Split('\n').Max(line => TextRenderer.MeasureText(line, label.Font).Width),
            measuredHeight = TextRenderer.MeasureText(label.Text, label.Font).Height
        }).ToArray();
        check(name + "_full_dimension_captions_fit", evidence.Length == 7 && evidence.All(item =>
            expected.GetValueOrDefault(item.key) == item.text && item.measuredWidth <= item.availableWidth && item.measuredHeight <= item.availableHeight), evidence);
    }
    private static void CheckButtonPair(string name, Button first, Button second, Action<string, bool, object?> check)
    {
        check(name + "_buttons_align", first.Top == second.Top && first.Size == second.Size && first.Font.Equals(second.Font) &&
            first.Padding == second.Padding && first.Margin == second.Margin && first.TextAlign == second.TextAlign &&
            first.FlatStyle == second.FlatStyle && first.FlatAppearance.BorderSize == second.FlatAppearance.BorderSize &&
            first.UseCompatibleTextRendering == second.UseCompatibleTextRendering,
            new { first = ButtonEvidence(first), second = ButtonEvidence(second) });
    }
    private static object ButtonEvidence(Button button) => new
    {
        button.Text, button.Left, button.Top, button.Width, button.Height, font = button.Font.ToString(),
        padding = button.Padding.ToString(), margin = button.Margin.ToString(), textAlign = button.TextAlign.ToString(),
        borderWidth = button.FlatAppearance.BorderSize, borderColor = button.FlatAppearance.BorderColor.ToString()
    };

    private static void RenderDrawing(MainForm form)
    {
        form.PerformLayout(); form.Refresh(); Application.DoEvents();
        var drawing = form.DrawingControl;
        using var bitmap = new Bitmap(drawing.Width, drawing.Height);
        drawing.DrawToBitmap(bitmap, new Rectangle(Point.Empty, drawing.Size));
    }
    private static bool SameOtherParameters(Parameters before, Parameters after, string edited)
    {
        var name = char.ToUpperInvariant(edited[0]) + edited[1..];
        return typeof(Parameters).GetProperties().Where(property => property.Name != name).All(property => Equals(property.GetValue(before), property.GetValue(after)));
    }
    private static void CaptureDialog(Form dialog, string path)
    {
        dialog.Refresh(); Application.DoEvents();
        using var bitmap = new Bitmap(dialog.Width, dialog.Height);
        dialog.DrawToBitmap(bitmap, new Rectangle(Point.Empty, dialog.Size)); bitmap.Save(path);
    }
    private static void PressDialogKey(Form dialog, Keys key) =>
        typeof(Form).GetMethod("ProcessDialogKey", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(dialog, [key]);

    // Invoke the same control MouseClick handler used by WinForms, at an actual
    // rendered label coordinate. This is a synthetic control event, not a global
    // OS mouse click. Its owned modal and Enter/Esc handlers run normally.
    private static void EditByCanvasClick(MainForm owner, string key, Action<DimensionEditDialog> edit, bool refresh = true)
    {
        if (refresh) { owner.Refresh(); Application.DoEvents(); }
        var bounds = owner.DimensionHitBounds(key);
        if (bounds.IsEmpty) throw new InvalidOperationException("Missing hit area: " + key);
        var point = Point.Round(new PointF(bounds.X + bounds.Width / 2, bounds.Y + bounds.Height / 2));
        var edited = false;
        Exception? editFailure = null;
        using var watchdog = new System.Threading.Timer(_ => Environment.Exit(4), null, 10_000, Timeout.Infinite);
        using var timer = new System.Windows.Forms.Timer { Interval = 100 };
        timer.Tick += (_, _) =>
        {
            var editor = Application.OpenForms.OfType<DimensionEditDialog>().FirstOrDefault(dialog => dialog.Owner == owner);
            if (editor is null) return;
            timer.Stop(); edited = true;
            try { edit(editor); }
            catch (Exception ex) { editFailure = ex; }
            finally
            {
                if (editor.DialogResult == DialogResult.None)
                {
                    editFailure ??= new InvalidOperationException("Editor did not finish: " + key + ": " + editor.ErrorText);
                    editor.CancelEditorButton.PerformClick();
                }
            }
        };
        timer.Start();
        typeof(Control).GetMethod("OnMouseClick", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(owner.DrawingControl, [new MouseEventArgs(MouseButtons.Left, 1, point.X, point.Y, 0)]);
        timer.Stop();
        if (!edited) throw new InvalidOperationException("Canvas click did not open editor: " + key);
        if (editFailure is not null) throw editFailure;
    }

    private static IEnumerable<Control> Descendants(Control root)
    {
        foreach (Control child in root.Controls)
        {
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }

    private static void InvokeAction(MainForm form, string name) =>
        typeof(MainForm).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(form, null);

    private static string FileSnapshot(string folder) => string.Join("\n", Directory.GetFiles(folder, "*", SearchOption.AllDirectories)
        .Order(StringComparer.Ordinal).Select(path => Path.GetRelativePath(folder, path) + ":" + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path)))));

    // Only this smoke process's owned popup is closed. No global keyboard input is sent.
    private static string CancelOwnModal(MainForm owner, Action show, Action<Form>? edit = null)
    {
        var cancelled = false;
        var title = "";
        using var watchdog = new System.Threading.Timer(_ => Environment.Exit(3), null, 10_000, Timeout.Infinite);
        using var timer = new System.Windows.Forms.Timer { Interval = 100 };
        timer.Tick += (_, _) =>
        {
            var popup = GetLastActivePopup(owner.Handle);
            if (popup == IntPtr.Zero || popup == owner.Handle) return;
            GetWindowThreadProcessId(popup, out var processId);
            if (processId != (uint)Environment.ProcessId) return;
            var dialog = Application.OpenForms.Cast<Form>().FirstOrDefault(form => form.Handle == popup);
            if (dialog is not null) edit?.Invoke(dialog);
            var text = new System.Text.StringBuilder(256);
            GetWindowText(popup, text, text.Capacity);
            title = text.ToString();
            cancelled = PostMessage(popup, 0x0010, IntPtr.Zero, IntPtr.Zero);
            if (cancelled) timer.Stop();
        };
        timer.Start();
        try { show(); }
        finally { timer.Stop(); }
        if (!cancelled) throw new InvalidOperationException("The owned modal dialog was not cancelled: " + title);
        return title;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetLastActivePopup(IntPtr owner);
    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetWindowTextW")]
    private static extern int GetWindowText(IntPtr window, System.Text.StringBuilder text, int capacity);
    [DllImport("user32.dll", EntryPoint = "PostMessageW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
}
