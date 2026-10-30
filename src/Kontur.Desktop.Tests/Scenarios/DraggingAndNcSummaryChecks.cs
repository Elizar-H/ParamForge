using System.Reflection;
using System.Text.Json;
using Kontur.Core;

namespace Kontur.Desktop;

internal static partial class UiSmoke
{
    private static void RunDraggingAndNcSummaryChecks(MainForm form, string folder, Action<string, bool, object?> check)
    {
        form.SetParameters(Engine.Defaults);
        form.GenerateNow();
        var baseline = JsonSerializer.Serialize(form.CurrentProject);
        form.SetToleranceView(true);
        RenderDrawing(form);
        var visible = Descendants(form).OfType<TextBox>().Count(c => c.Visible && c.Name.StartsWith("parameter_", StringComparison.Ordinal));
        check(
            "v26_inline_deviations_preserve_values",
            visible == 7 && Descendants(form).OfType<DeviationReadout>().Count(c => c.Visible && c.ShowNumbers) == 7 && baseline == JsonSerializer.Serialize(form.CurrentProject) && !form.IsStale,
            visible
        );
        form.CaptureForm(Path.Combine(folder, "40-inline-deviations.png"));
        form.Size = new Size(1100, 720);
        RenderDrawing(form);
        CheckCaptions(form, "v26_inline_minimum", check);
        check(
            "v26_inline_minimum_drawing",
            form.Sketch.LabelsInside && !form.Sketch.LabelsOverlap && form.Sketch.LabelsClearOfDimensionLines && form.Sketch.LabelsClearOfRearEdges,
            null
        );
        form.CaptureForm(Path.Combine(folder, "41-inline-minimum.png"));
        form.ClientSize = new Size(1280, 850);
        form.SetParameters(Engine.Defaults with
        {
            BoreTolerance = "JS7"
        });
        form.GenerateNow();
        RenderDrawing(form);
        var precise = form.DeviationView("boreDiameter");
        check(
            "v26_inline_four_decimals_fit",
            precise.TextFits && form.GetInput("boreLowerDeviation") == "-0,0125",
            new
            {
                precise.Text,
                precise.Width
            }
        );
        form.CaptureForm(Path.Combine(folder, "44-inline-standard-tolerance.png"));
        form.SetParameters(Engine.Defaults);
        form.GenerateNow();
        form.SetToleranceView(false);
        form.ClientSize = new Size(1280, 850);
        RenderDrawing(form);
        check(
            "v26_toggle_hides_only_deviations",
            Descendants(form).OfType<TextBox>().Count(c => c.Visible && c.Name.StartsWith("parameter_", StringComparison.Ordinal)) == 7 && baseline == JsonSerializer.Serialize(form.CurrentProject),
            null
        );
        EditToleranceFromPanel(form, "boreDiameter", editor =>
        {
            check("v26_matrix_opens_in_manual_mode", !editor.UsesStandard && editor.Picker!.SelectedDesignation is null, null);
            var picker = editor.Picker!;
            picker.ChoiceButton("JS7").PerformClick();
            check(
                "v26_matrix_click_selects_JS7",
                editor.UsesStandard && editor.LowerInput!.Text == "-0,0125" && editor.UpperInput!.Text == "0,0125",
                null
            );
            CaptureDialog(editor, Path.Combine(folder, "42-tolerance-matrix.png"));
            var keyboardChoice = picker.ChoiceButton("H7");
            keyboardChoice.Focus();
            typeof(Control).GetMethod("OnKeyDown", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(keyboardChoice, [new KeyEventArgs(Keys.Down)]);
            check(
                "v26_matrix_keyboard_selection",
                editor.UsesStandard && editor.LowerInput!.Text == "0" && editor.UpperInput!.Text == "0,039",
                null
            );
            editor.SelectStandard("JS7");
            editor.NominalInput.Text = "50,001";
            check(
                "v26_matrix_nominal_recomputes_values",
                editor.UpperInput!.Text == "0,015" && picker.SelectedDesignation == "JS7",
                null
            );
            editor.LowerInput!.Text = "0";
            check("v26_matrix_manual_override_clears_choice", !editor.UsesStandard && picker.SelectedDesignation is null, null);
            PressDialogKey(editor, Keys.Escape);
        });
        foreach (var (key, designation, rows) in new[] { ("flangeDiameter", "js8", 2), ("length", "IT10", 1) })
        {
            EditToleranceFromPanel(form, key, editor =>
            {
                editor.SelectStandard(designation);
                check(
                    "v26_matrix_" + designation,
                    editor.UsesStandard && editor.Picker!.ChoiceCount == 11 * rows && editor.Picker.SelectedDesignation == designation,
                    null
                );
                PressDialogKey(editor, Keys.Escape);
            });
        }

        form.OpenInspection();
        var inspection = form.Inspection!;
        inspection.SelectTransitionForSmoke(2);
        form.OpenInspection();
        check(
            "v26_reopen_reuses_inspection",
            ReferenceEquals(inspection, form.Inspection) && inspection.SelectedMoveCount > 0,
            null
        );
        inspection.WindowState = FormWindowState.Minimized;
        form.OpenInspection();
        Application.DoEvents();
        check(
            "v26_reopen_restores_minimized_window",
            inspection.WindowState == FormWindowState.Normal && ReferenceEquals(inspection, form.Inspection),
            null
        );
        var project = form.CurrentProject!;
        var metadataChanged = project with
        {
            Id = project.Id + "-metadata",
            Transitions = project.Transitions.Select(t => t with { Feed = 9, Rpm = 9, Tool = "T9", CutMinutes = 999, Passes = [] }).ToList()
        };
        inspection.SetResult(metadataChanged);
        inspection.SelectTransitionForSmoke(0);
        var summary = inspection.TransitionSummary(0);
        check(
            "v26_statistics_ignore_plan_metadata",
            summary.Tools == "T1" && summary.Feeds == NumericText.Format(project.Transitions[0].Feed) && summary.Passes == project.Transitions[0].Passes.Count && Math.Abs(summary.CutMinutes - project.Transitions[0].CutMinutes) < .000001,
            summary
        );
        var nc = project.Nc.Replace("F0.2", "F0.3", StringComparison.Ordinal);
        var verification = NcVerifier.Verify(nc, project.Params);
        inspection.SetResult(project with
        {
            Id = project.Id + "-nc",
            Nc = nc,
            Verification = verification
        });
        check(
            "v26_statistics_follow_changed_gcode",
            verification.Ok && inspection.TransitionSummary(0).Feeds == "0,3",
            verification.Errors
        );
        inspection.SelectFrameForSmoke(1);
        check(
            "v26_statistics_remain_without_move_selection",
            inspection.DetailText.Contains("Подача", StringComparison.Ordinal),
            inspection.DetailText
        );
        inspection.SetResult(project);
        check(
            "v26_parsed_time_sums_to_verifier",
            Math.Abs(Enumerable.Range(0, project.Transitions.Count).Sum(i => inspection.TransitionSummary(i).CutMinutes) - project.Verification.CutMinutes) < .000001,
            null
        );
        check(
            "v26_parsed_passes_match_cut_segments",
            Enumerable.Range(0, project.Transitions.Count).All(i => inspection.TransitionSummary(i).Passes == project.Transitions[i].Passes.Count),
            null
        );
        var siemens = Engine.Generate(Engine.Defaults with
        {
            Controller = CncProfiles.Sinumerik
        }).Project!;
        inspection.SetResult(siemens);
        check("v26_siemens_parsed_statistics", Enumerable.Range(0, siemens.Transitions.Count).All(i => inspection.TransitionSummary(i).Passes == siemens.Transitions[i].Passes.Count)
            && Math.Abs(Enumerable.Range(0, siemens.Transitions.Count).Sum(i => inspection.TransitionSummary(i).CutMinutes) - siemens.Verification.CutMinutes) < .000001, null);
        inspection.SetResult(project);
        inspection.Trajectory.ShowAll = true;
        inspection.SelectTransitionForSmoke(2);
        inspection.Refresh();
        Application.DoEvents();
        inspection.CaptureImage(Path.Combine(folder, "43-contrast-trajectories.png"));
        inspection.Close();

        foreach (var key in new[] { "length", "flangeDiameter", "bodyDiameter", "flangeWidth", "boreDiameter", "counterboreDiameter", "counterboreDepth" })
        {
            form.SetParameters(Engine.Defaults);
            form.GenerateNow();
            var initial = ParameterValue(form.CurrentProject!.Params, key);
            var end = BeginSizeDrag(form, key, 2);
            check(
                "v26_drag_previews_" + key,
                form.Sketch.IsDragging && form.IsStale && ParameterValue(form.Sketch.Parameters!, key) == initial + 2 && ParameterValue(form.CurrentProject.Params, key) == initial,
                null
            );
            SendMouse(form.Sketch, "OnMouseUp", end);
            check(
                "v26_drag_commits_" + key,
                !form.IsStale && form.CurrentProject!.Verification.Ok && ParameterValue(form.CurrentProject.Params, key) == initial + 2,
                form.GetInput(key)
            );
        }
        form.SetParameters(Engine.Defaults with
        {
            BoreTolerance = "JS7"
        });
        form.GenerateNow();
        var standardEnd = BeginSizeDrag(form, "boreDiameter", 4);
        SendMouse(form.Sketch, "OnMouseUp", standardEnd);
        check(
            "v26_drag_recomputes_standard_tolerance",
            form.CurrentProject!.Params.BoreTolerance == "JS7" && form.GetInput("boreUpperDeviation") == "0,015",
            null
        );
        form.SetParameters(Engine.Defaults);
        form.GenerateNow();
        baseline = JsonSerializer.Serialize(form.CurrentProject);
        BeginSizeDrag(form, "length", 2);
        typeof(SketchCanvas).GetMethod("ProcessCmdKey", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(form.Sketch, [new Message(), Keys.Escape]);
        check(
            "v26_drag_escape_rolls_back",
            !form.Sketch.IsDragging && !form.IsStale && baseline == JsonSerializer.Serialize(form.CurrentProject) && form.GetInput("length") == "116",
            null
        );
        var invalidEnd = BeginSizeDrag(form, "flangeDiameter", 500);
        SendMouse(form.Sketch, "OnMouseUp", invalidEnd);
        check(
            "v26_drag_rejects_invalid_geometry",
            baseline == JsonSerializer.Serialize(form.CurrentProject) && form.GetInput("flangeDiameter") == "120",
            null
        );
        var shortEnd = BeginSizeDrag(form, "counterboreDepth", 76);
        SendMouse(form.Sketch, "OnMouseUp", shortEnd);
        check(
            "v26_drag_short_passage_repositions_dimensions",
            form.CurrentProject!.Params.CounterboreDepth == 104 && form.CurrentProject.Verification.Ok && !form.IsStale,
            form.GetInput("counterboreDepth")
        );
        form.SetParameters(Engine.Defaults);
        form.GenerateNow();
        baseline = JsonSerializer.Serialize(form.CurrentProject);
        BeginSizeDrag(form, "bodyDiameter", 2);
        typeof(Control).GetMethod("OnMouseCaptureChanged", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(form.Sketch, [EventArgs.Empty]);
        check(
            "v26_capture_loss_rolls_back_drag",
            !form.Sketch.IsDragging && baseline == JsonSerializer.Serialize(form.CurrentProject),
            null
        );
        form.SetToleranceView(false);
        form.SetParameters(Engine.Defaults);
        form.GenerateNow();
    }
    private static double ParameterValue(Parameters p, string key) => (double)typeof(Parameters).GetProperty(char.ToUpperInvariant(key[0]) + key[1..])!.GetValue(p)!;
    private static Point BeginSizeDrag(MainForm form, string key, double change)
    {
        RenderDrawing(form);
        var start = Point.Round(form.Sketch.DragHandle(key));
        var diameter = key.Contains("Diameter", StringComparison.OrdinalIgnoreCase);
        var pixels = (int)Math.Round(-change * form.DrawingScale / (diameter ? 2 : 1));
        var end = new Point(start.X + (diameter ? 0 : pixels), start.Y + (diameter ? pixels : 0));
        SendMouse(form.Sketch, "OnMouseDown", start);
        SendMouse(form.Sketch, "OnMouseMove", end);
        RenderDrawing(form);
        return end;
    }
    private static void SendMouse(Control control, string method, Point point) =>
        typeof(Control).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(control, [new MouseEventArgs(MouseButtons.Left, 1, point.X, point.Y, 0)]);
}
