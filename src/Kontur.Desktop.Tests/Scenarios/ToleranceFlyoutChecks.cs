using System.Reflection;
using System.Text.Json;
using Kontur.Core;

namespace Kontur.Desktop;

internal static partial class UiSmoke
{
    private static void RunToleranceFlyoutChecks(MainForm form, string folder, Action<string, bool, object?> check)
    {
        form.SetToleranceView(true);
        form.ClientSize = new Size(1440, 900);
        form.SetParameters(Engine.Defaults);
        form.GenerateNow();
        RenderDrawing(form);
        var drawingSize = form.Sketch.Size;
        var origin = form.Sketch.WorkOrigin;
        var nominalPositions = Descendants(form).OfType<TextBox>().Where(c => c.Visible && c.Name.StartsWith("parameter_", StringComparison.Ordinal))
            .ToDictionary(c => c.Name, c => c.PointToScreen(Point.Empty));
        OpenSideEditor(form, "boreDiameter");
        var editor = form.ActiveDimensionEditor!;
        var popup = editor.Parent!;
        var popupBounds = popup.Bounds;
        check("v28_geometry_stays_visible_and_still", nominalPositions.Count == 7 && nominalPositions.All(pair =>
            Descendants(form).Single(c => c.Name == pair.Key) is { Visible: true } input && input.PointToScreen(Point.Empty) == pair.Value), null);
        check(
            "v28_flyout_is_adjacent_and_on_top",
            form.Controls.GetChildIndex(popup) == 0 && popup.Left >= form.PointToClient(Descendants(form).Single(c => c.Name == "dimension_edit_length").PointToScreen(Point.Empty)).X,
            popupBounds
        );
        check(
            "v28_opening_flyout_does_not_resize_drawing",
            form.Sketch.Size == drawingSize && form.Sketch.WorkOrigin == origin,
            null
        );
        check("v28_no_manual_standard_switch", !Descendants(editor).OfType<RadioButton>().Any(), null);
        editor.SelectStandard("JS7");
        var lowerPosition = editor.LowerInput!.PointToScreen(Point.Empty);
        var scroll = Descendants(editor).OfType<Panel>().Single(p => p.AutoScroll);
        var scrollPosition = scroll.AutoScrollPosition;
        foreach (var text in new[] { "-", "-0", "-0,", "-0,0", "-0,01", "-0,012", "-0,0123" })
        {
            editor.LowerInput.Text = text;
            check(
                "v28_manual_keystroke_keeps_" + text,
                editor.LowerInput.Text == text && editor.LowerInput.PointToScreen(Point.Empty) == lowerPosition && popup.Bounds == popupBounds && scroll.AutoScrollPosition == scrollPosition,
                null
            );
        }
        editor.UpperInput!.Text = "0,0157";
        check("v28_unmatched_numbers_are_manual", !editor.UsesStandard && editor.Picker!.SelectedDesignation is null, null);
        editor.ApplyButton.PerformClick();
        check(
            "v28_manual_values_reach_project",
            form.CurrentProject!.Params.BoreTolerance == "" && form.GetInput("boreLowerDeviation") == "-0,0123" && form.GetInput("boreUpperDeviation") == "0,0157" && !form.IsStale,
            null
        );
        RenderDrawing(form);
        check(
            "v210_main_deviations_use_compact_numeric_notation",
            form.DeviationView("boreDiameter").Text == "+0,0157\n-0,0123" && form.DeviationView("boreDiameter").TextFits,
            null
        );
        form.CaptureForm(Path.Combine(folder, "60-stacked-deviations.png"));
        OpenSideEditor(form, "boreDiameter");
        editor = form.ActiveDimensionEditor!;
        editor.LowerInput!.Text = "0";
        editor.UpperInput!.Text = "0,0250";
        check(
            "v28_matching_numbers_recognize_H7_without_rewriting",
            editor.UsesStandard && editor.Picker!.SelectedDesignation == "H7" && editor.UpperInput.Text == "0,0250",
            null
        );
        var stable = editor.Parent!.Bounds;
        var stableScroll = Descendants(editor).OfType<Panel>().Single(p => p.AutoScroll).AutoScrollPosition;
        foreach (var designation in new[] { "H8", "JS7", "H10", "JS8" })
        {
            editor.Picker!.ChoiceButton(designation).PerformClick();
            check(
                "v28_class_click_does_not_move_editor_" + designation,
                editor.Parent.Bounds == stable && Descendants(editor).OfType<Panel>().Single(p => p.AutoScroll).AutoScrollPosition == stableScroll,
                null
            );
        }
        form.CaptureForm(Path.Combine(folder, "61-adjacent-tolerance-flyout.png"));
        form.Size = new Size(1180, 720);
        RenderDrawing(form);
        check(
            "v28_resizing_open_flyout_keeps_inputs_inside",
            FullyVisible(editor.LowerInput!, form) && FullyVisible(editor.ApplyButton, form) && editor.Parent.Bottom <= form.ClientSize.Height,
            editor.Parent.Bounds
        );
        form.ClientSize = new Size(1440, 900);
        RenderDrawing(form);
        editor.LowerInput!.Text = "-0,0123";
        editor.UpperInput!.Text = "0,0157";
        form.SetInput("length", "118");
        editor.ApplyButton.PerformClick();
        check(
            "v28_applying_flyout_preserves_other_geometry_edits",
            form.CurrentProject!.Params.Length == 118 && form.CurrentProject.Params.BoreLowerDeviation == -.0123,
            null
        );

        foreach (var key in new[] { "length", "flangeDiameter", "bodyDiameter", "flangeWidth", "boreDiameter", "counterboreDiameter", "counterboreDepth" })
        {
            foreach (var lower in new[] { false, true })
            {
                form.SetParameters(Engine.Defaults);
                form.GenerateNow();
                RenderDrawing(form);
                var initial = ParameterValue(form.CurrentProject!.Params, key);
                var start = Point.Round(form.Sketch.ContourHandle(key, lower));
                SendMouse(form.Sketch, "OnMouseMove", start);
                check(
                    $"v28_contour_cursor_{key}_{lower}",
                    form.Sketch.Cursor == (key.Contains("Diameter", StringComparison.OrdinalIgnoreCase) ? Cursors.SizeNS : Cursors.SizeWE),
                    null
                );
                var end = BeginContourDrag(form, key, 2, lower);
                check(
                    $"v28_contour_preview_{key}_{lower}",
                    form.Sketch.IsDragging && ParameterValue(form.Sketch.Parameters!, key) == initial + 2 && ParameterValue(form.CurrentProject.Params, key) == initial,
                    form.GetInput(key)
                );
                SendMouse(form.Sketch, "OnMouseUp", end);
                RenderDrawing(form);
                var top = form.Sketch.ContourHandle(key);
                var bottom = form.Sketch.ContourHandle(key, true);
                check(
                    $"v28_contour_commit_and_symmetry_{key}_{lower}",
                    !form.IsStale && form.CurrentProject!.Verification.Ok && ParameterValue(form.CurrentProject.Params, key) == initial + 2 && Math.Abs(top.Y + bottom.Y - 2 * form.Sketch.WorkOrigin.Y) < .001,
                    null
                );
            }
        }

        form.SetParameters(Engine.Defaults);
        form.GenerateNow();
        OpenSideEditor(form, "length");
        form.ActiveDimensionEditor!.NominalInput.Text = "118";
        var endWithFlyout = BeginContourDrag(form, "bodyDiameter", 2);
        SendMouse(form.Sketch, "OnMouseUp", endWithFlyout);
        check(
            "v28_contour_drag_works_with_flyout_open",
            form.ActiveDimensionEditor is null && form.CurrentProject!.Params.BodyDiameter == 102 && form.CurrentProject.Params.Length == 116,
            null
        );
        form.SetParameters(Engine.Defaults);
        form.GenerateNow();
        var baseline = JsonSerializer.Serialize(form.CurrentProject);
        BeginContourDrag(form, "boreDiameter", 2);
        typeof(SketchCanvas).GetMethod("ProcessCmdKey", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(form.Sketch, [new Message(), Keys.Escape]);
        check(
            "v28_contour_escape_restores_project",
            baseline == JsonSerializer.Serialize(form.CurrentProject) && !form.Sketch.IsDragging,
            null
        );
        var badEnd = BeginContourDrag(form, "flangeDiameter", 500);
        SendMouse(form.Sketch, "OnMouseUp", badEnd);
        check("v28_contour_invalid_geometry_is_rejected", baseline == JsonSerializer.Serialize(form.CurrentProject), null);
        form.SetToleranceView(false);
        form.SetParameters(Engine.Defaults);
        form.GenerateNow();
        var hover = Point.Round(form.Sketch.ContourHandle("bodyDiameter"));
        SendMouse(form.Sketch, "OnMouseMove", hover);
        RenderDrawing(form);
        form.CaptureForm(Path.Combine(folder, "62-contour-highlight.png"));
        form.SetToleranceView(true);
        form.Size = new Size(1180, 720);
        OpenSideEditor(form, "boreDiameter");
        form.ActiveDimensionEditor!.SelectStandard("JS7");
        form.CaptureForm(Path.Combine(folder, "63-flyout-minimum.png"));
        check(
            "v28_minimum_geometry_and_flyout_visible",
            nominalPositions.Keys.All(name => Descendants(form).Single(c => c.Name == name).Visible) && FullyVisible(form.ActiveDimensionEditor.LowerInput!, form) && FullyVisible(form.ActiveDimensionEditor.ApplyButton, form),
            null
        );
        form.ActiveDimensionEditor.CancelEditorButton.PerformClick();
        OpenSideEditor(form, "length");
        Descendants(form).OfType<TextBox>().Single(c => c.Name == "parameter_bodyDiameter").Focus();
        typeof(MainForm).GetMethod("ProcessCmdKey", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(form, [new Message(), Keys.Escape]);
        check("v28_escape_outside_editor_closes_flyout", form.ActiveDimensionEditor is null, null);
        form.SetToleranceView(false);
        form.ClientSize = new Size(1280, 850);
        form.SetParameters(Engine.Defaults);
        form.GenerateNow();
    }
    private static Point BeginContourDrag(MainForm form, string key, double change, bool lower = false)
    {
        RenderDrawing(form);
        var start = Point.Round(form.Sketch.ContourHandle(key, lower));
        var diameter = key.Contains("Diameter", StringComparison.OrdinalIgnoreCase);
        var pixels = (int)Math.Round(change * form.DrawingScale / (diameter ? 2 : 1));
        var end = new Point(start.X + (diameter ? 0 : -pixels), start.Y + (diameter ? (lower ? pixels : -pixels) : 0));
        SendMouse(form.Sketch, "OnMouseDown", start);
        SendMouse(form.Sketch, "OnMouseMove", end);
        RenderDrawing(form);
        return end;
    }
}
