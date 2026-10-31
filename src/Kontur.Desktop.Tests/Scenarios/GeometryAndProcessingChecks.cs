using System.Reflection;
using System.Text.Json;
using Kontur.Core;
namespace Kontur.Desktop;

internal static partial class UiSmoke
{
    // 2.10 replaces the temporary on-canvas nominal field with focus in geometry.
    private static void RunGeometryAndProcessingChecks(MainForm form, string folder, Action<string, bool, object?> check)
    {
        form.SetParameters(Engine.Defaults);
        form.GenerateNow();
        form.SetToleranceView(false);
        form.Size = form.MinimumSize;
        RenderDrawing(form);
        var positions = Descendants(form).Where(c => c.Name.StartsWith("parameter_", StringComparison.Ordinal) || c.Name.StartsWith("dimension_edit_", StringComparison.Ordinal)).ToDictionary(c => c.Name, c => c.PointToScreen(Point.Empty));
        var size = form.Size;
        var canvas = form.Sketch.Size;
        var origin = form.Sketch.WorkOrigin;
        form.SetToleranceView(true);
        RenderDrawing(form);
        form.SetToleranceView(false);
        RenderDrawing(form);
        check(
            "v210_tolerance_visibility_keeps_geometry_still",
            form.Size == size && form.Sketch.Size == canvas && form.Sketch.WorkOrigin == origin && positions.All(pair => Descendants(form).Single(c => c.Name == pair.Key).PointToScreen(Point.Empty) == pair.Value),
            null
        );
        check("v210_no_redundant_tolerance_checkbox", !Descendants(form.Workspace.TabPages[0]).OfType<CheckBox>().Any(), null);
        check(
            "v210_processing_action_visible_at_minimum",
            FullyVisible(Descendants(form).OfType<Button>().Single(b => b.Text == "Заготовка и обработка…"), form),
            form.Size
        );
        foreach (var key in new[] { "length", "flangeDiameter", "bodyDiameter", "flangeWidth", "boreDiameter", "counterboreDiameter", "counterboreDepth" })
        {
            var input = form.NominalField(key);
            input.Focus();
            input.Select(input.TextLength, 0);
            SendMouse(input.Parent!, "OnMouseDown", new Point(1, input.Parent!.Height - 1));
            check(
                "v210_entire_nominal_cell_selects_" + key,
                input.SelectedText == input.Text && input.Focused && input.BackColor == Theme.Surface,
                null
            );
            ClickCaption(form, key);
            check(
                "v210_caption_focuses_geometry_without_popup_" + key,
                input.Focused && input.SelectedText == input.Text && form.ActiveDimensionEditor is null && !form.Sketch.Controls.OfType<TextBox>().Any(),
                null
            );
        }
        form.ClientSize = new Size(1280, 850);
        form.SetToleranceView(true);
        form.SetParameters(Engine.Defaults);
        form.GenerateNow();
        RenderDrawing(form);
        var baseline = JsonSerializer.Serialize(form.CurrentProject);
        var point = CaptionPoint(form, "boreDiameter");
        SendMouse(form.Sketch, "OnMouseDown", point);
        SendMouse(form.Sketch, "OnMouseMove", new Point(point.X + 30, point.Y + 20));
        SendMouse(form.Sketch, "OnMouseUp", point);
        check(
            "v210_number_drag_does_not_change_geometry",
            !form.Sketch.IsDragging && JsonSerializer.Serialize(form.CurrentProject) == baseline,
            null
        );
        var near = form.DimensionHitBounds("boreDiameter");
        var protectedPoint = Point.Round(new PointF(near.Right + 2 * form.DrawingDpi / 96F, near.Top + near.Height / 2));
        SendMouse(form.Sketch, "OnMouseMove", protectedPoint);
        SendMouse(form.Sketch, "OnMouseDown", protectedPoint);
        SendMouse(form.Sketch, "OnMouseMove", new Point(protectedPoint.X, protectedPoint.Y + 10));
        check(
            "v210_space_beside_number_is_not_a_drag_handle",
            !form.Sketch.IsDragging && form.Sketch.Cursor == Cursors.Hand,
            null
        );
        SendMouse(form.Sketch, "OnMouseUp", protectedPoint);
        ClickCaption(form, "boreDiameter");
        form.NominalField("boreDiameter").Text = "48,125";
        PressNominalKey(form, "boreDiameter", Keys.Enter);
        check(
            "v210_nominal_preserves_manual_tolerance",
            form.CurrentProject!.Params.BoreDiameter == 48.125 && form.CurrentProject.Params.BoreLowerDeviation == 0 && form.CurrentProject.Params.BoreUpperDeviation == 1,
            null
        );
        form.CaptureForm(Path.Combine(folder, "80-compact-geometry.png"));
        ClickCaption(form, "bodyDiameter");
        form.NominalField("bodyDiameter").Text = "abc";
        PressNominalKey(form, "bodyDiameter", Keys.Enter);
        PressNominalKey(form, "bodyDiameter", Keys.Escape);
        check(
            "v210_nominal_escape_restores_value",
            form.GetInput("bodyDiameter") == "100" && form.NominalField("bodyDiameter").BackColor == Theme.Surface,
            null
        );
        form.SetParameters(Engine.Defaults with
        {
            BoreTolerance = "JS7"
        });
        form.GenerateNow();
        RenderDrawing(form);
        check(
            "v210_standard_tolerances_display_numbers",
            form.DeviationView("boreDiameter").Text == "±0,0125" && !form.DimensionText("boreDiameter").Contains("JS7", StringComparison.Ordinal),
            form.DimensionText("boreDiameter")
        );
        ClickCaption(form, "boreDiameter");
        form.NominalField("boreDiameter").Text = "50,001";
        PressNominalKey(form, "boreDiameter", Keys.Enter);
        check(
            "v210_standard_tolerance_recomputed",
            form.CurrentProject!.Params.BoreTolerance == "JS7" && form.GetInput("boreUpperDeviation") == "0,015",
            null
        );
        form.SetParameters(Engine.Defaults);
        form.GenerateNow();
        RenderDrawing(form);
        point = CaptionPoint(form, "length");
        var wheel = typeof(Control).GetMethod("OnMouseWheel", BindingFlags.Instance | BindingFlags.NonPublic)!;
        wheel.Invoke(form.Sketch, [new MouseEventArgs(MouseButtons.None, 0, point.X, point.Y, 120)]);
        check(
            "v211_wheel_changes_dimensions_by_default",
            form.GetInput("length") == "116,01" && form.Sketch.WheelEditingEnabled,
            null
        );
        form.Sketch.WheelEditingEnabled = true;
        foreach (var key in new[] { "length", "flangeDiameter", "bodyDiameter", "flangeWidth", "boreDiameter", "counterboreDiameter", "counterboreDepth" })
        {
            form.SetParameters(Engine.Defaults);
            form.GenerateNow();
            RenderDrawing(form);
            var initial = ParameterValue(form.CurrentProject!.Params, key);
            point = CaptionPoint(form, key);
            wheel.Invoke(form.Sketch, [new MouseEventArgs(MouseButtons.None, 0, point.X, point.Y, 120)]);
            check(
                "v210_optional_wheel_fine_step_" + key,
                Math.Abs(ParameterValue(form.CurrentProject!.Params, key) - initial - .01) < .0000001 && form.ActiveDimensionEditor is null,
                null
            );
        }
        form.SetParameters(Engine.Defaults);
        form.GenerateNow();
        form.Sketch.AdjustNominalWheel("length", 120, Keys.Shift);
        form.Sketch.AdjustNominalWheel("length", 120, Keys.Control);
        check("v210_wheel_modifiers", form.GetInput("length") == "116,101", null);
        form.Sketch.WheelEditingEnabled = true;
        form.SetParameters(Engine.Defaults);
        form.GenerateNow();
        RenderDrawing(form);
        var end = BeginContourDrag(form, "bodyDiameter", 2);
        SendMouse(form.Sketch, "OnMouseUp", end);
        SendMouse(form.Sketch, "OnMouseClick", end);
        check(
            "v210_drag_release_does_not_open_popup",
            form.CurrentProject!.Params.BodyDiameter == 102 && form.ActiveDimensionEditor is null && !form.Sketch.Controls.OfType<TextBox>().Any(),
            null
        );
        form.SetParameters(Engine.Defaults with
        {
            AutoStock = true
        });
        form.GenerateNow();
        form.SetInput("boreDiameter", "46");
        form.GenerateNow();
        check(
            "v210_auto_stock_tracks_smaller_hole",
            form.CurrentProject!.Params.StockBore == 44 && form.CurrentProject.Verification.Ok,
            form.CurrentProject.Params.StockBore
        );
        form.SetInput("boreDiameter", "47,3");
        form.GenerateNow();
        check("v210_auto_stock_restores_with_geometry", form.CurrentProject!.Params.StockBore == 45, null);
        form.SetInput("flangeDiameter", "150");
        form.GenerateNow();
        check(
            "v210_auto_stock_tracks_flange",
            !form.IsStale && form.CurrentProject!.Params.StockDiameter == 154,
            form.StatusText
        );
        var processing = OpenProcessingForSmoke(form);
        {
            Application.DoEvents();
            check(
                "v210_processing_has_cards_tab",
                processing.Sections.Pages.Count == 7 && processing.Sections.Pages[6].Text == "Карты",
                null
            );
            CaptureDialog(processing, Path.Combine(folder, "81-stock-mode.png"));
            processing.StockMode.SelectedIndex = 1;
            processing.Field("stockDiameter").Text = "156";
            processing.Field("stockBore").Text = "44";
            processing.StockMode.SelectedIndex = 0;
            check(
                "v210_calculated_mode_updates_both_blank_dimensions",
                processing.Field("stockDiameter").ReadOnly && processing.Field("stockDiameter").Text == "154" && processing.Field("stockBore").Text == "45",
                null
            );
            processing.StockMode.SelectedIndex = 1;
            check(
                "v210_manual_blank_is_restored_after_temporary_auto_preview",
                processing.Field("stockDiameter").Text == "156" && processing.Field("stockBore").Text == "44" && !processing.Field("stockBore").ReadOnly,
                null
            );
            processing.Sections.SelectedIndex = 4;
            processing.Field("bodyDiameter").Text = "98";
            processing.Field("bodyLowerDeviation").Text = "-0,1";
            processing.Field("bodyUpperDeviation").Text = "0,3";
            CaptureDialog(processing, Path.Combine(folder, "82-editable-targets.png"));
            processing.Sections.SelectedIndex = 6;
            processing.Field("developer").Text = "Худяков Е.";
            processing.Field("drawingNumber").Text = "ПФ.001";
            CaptureDialog(processing, Path.Combine(folder, "83-card-fields.png"));
            Descendants(processing).OfType<Button>().Single(b => b.Text == "Применить").PerformClick();
            check(
                "v210_editable_targets_and_card_fields_apply_together",
                processing.Result is { BodyDiameter: 98, BodyTolerance: "", BodyLowerDeviation: -.1, BodyUpperDeviation: .3, Developer: "Худяков Е.", DrawingNumber: "ПФ.001" },
                processing.ErrorText
            );
            if (processing.Result is { } result)
            {
                form.SetParameters(result);
                form.GenerateNow();
            }
            processing.Close();
        }
        check(
            "v210_body_tolerance_reaches_actual_machining_target",
            form.CurrentProject!.Calculations.TargetBodyDiameter == 98.1 && form.CurrentProject.Verification.Ok,
            null
        );
        form.OpenInspection();
        var plot = form.Inspection!.Trajectory;
        check("v210_trajectory_profile_uses_operation_targets", plot.TargetProfile.Any(q => q.X == 98.1 && q.Z == -form.CurrentProject.Calculations.TargetFlangeWidth)
            && plot.TargetProfile.First().Z == -form.CurrentProject.Calculations.CutLength, null);
        form.Inspection.SelectTransitionForSmoke(3);
        form.Inspection.Refresh();
        Application.DoEvents();
        form.Inspection.CaptureImage(Path.Combine(folder, "87-target-trajectory.png"));
        form.Inspection.Close();
        form.SetParameters(Engine.Defaults with
        {
            FlangeWidth = 40,
            CounterboreDepth = 28
        });
        form.GenerateNow();
        RenderDrawing(form);
        check(
            "v210_larger_flange_width_dimension_is_above_depth",
            form.DimensionHitBounds("flangeWidth").Bottom < form.DimensionHitBounds("counterboreDepth").Top && form.Sketch.LabelsClearOfDimensionLines,
            null
        );
        form.CaptureForm(Path.Combine(folder, "84-reordered-dimensions.png"));
        form.SetParameters(Engine.Defaults);
        form.GenerateNow();
        form.SetToleranceView(true);
        form.Size = form.MinimumSize;
        RenderDrawing(form);
        form.CaptureForm(Path.Combine(folder, "85-compact-minimum.png"));
        form.WindowState = FormWindowState.Maximized;
        Application.DoEvents();
        RenderDrawing(form);
        check(
            "v210_geometry_rows_do_not_stretch_in_maximized_window",
            form.NominalField("length").Parent!.Height == form.NominalField("flangeWidth").Parent!.Height && form.NominalField("length").Parent!.Height <= TextRenderer.MeasureText("0", form.NominalField("length").Font).Height + 12,
            null
        );
        form.CaptureForm(Path.Combine(folder, "86-maximized-main.png"));
        form.WindowState = FormWindowState.Normal;
        form.ClientSize = new Size(1280, 850);
        form.SetParameters(Engine.Defaults with
        {
            AutoStock = true
        });
        form.GenerateNow();
    }
    private static Point CaptionPoint(MainForm form, string key)
    {
        RenderDrawing(form);
        var box = form.DimensionHitBounds(key);
        return Point.Round(new PointF(box.X + box.Width / 2, box.Y + box.Height / 2));
    }
    private static void ClickCaption(MainForm form, string key)
    {
        var point = CaptionPoint(form, key);
        foreach (var method in new[] { "OnMouseDown", "OnMouseUp", "OnMouseClick" })
        {
            SendMouse(form.Sketch, method, point);
        }

        Application.DoEvents();
    }
    private static void PressNominalKey(MainForm form, string key, Keys pressed) => typeof(Control).GetMethod("OnKeyDown", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(form.NominalField(key), [new KeyEventArgs(pressed)]);
}
