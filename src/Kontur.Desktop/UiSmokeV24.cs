using Kontur.Core;

namespace Kontur.Desktop;

internal static partial class UiSmoke
{
    private static void RunV24Checks(MainForm form, string folder, Action<string, bool, object?> check)
    {
        form.SetParameters(Engine.Defaults); form.GenerateNow(); form.SetDrawingOptions(true, false);
        form.Size = new Size(1100, 720); RenderDrawing(form);
        var smallStep = form.Sketch.HatchStepDips;
        check("v24_origin_visible_default", form.Sketch.ShowAxes && form.Sketch.WorkOrigin.X > 0 && form.Sketch.WorkOrigin.X < form.Sketch.Width, form.Sketch.WorkOrigin);
        form.ClientSize = new Size(1520, 920); RenderDrawing(form);
        check("v24_hatching_adapts_to_window", form.Sketch.HatchStepDips > smallStep && smallStep >= 8 && form.Sketch.HatchStepDips <= 18, new { smallStep, largeStep = form.Sketch.HatchStepDips });
        form.CaptureForm(Path.Combine(folder, "20-large-axes.png"));
        form.ClientSize = new Size(1280, 850); form.SetDrawingOptions(false, true); RenderDrawing(form);
        check("v24_view_toggles", !form.Sketch.ShowAxes && form.Sketch.ShowTolerances && form.DimensionText("boreDiameter").Contains('+'), form.DimensionText("boreDiameter"));
        form.SetDrawingOptions(true, true); form.CaptureForm(Path.Combine(folder, "21-manual-tolerances.png"));
        check("v24_manual_tolerances_fit", form.DrawingLabelsInside && !form.DrawingLabelsOverlap && form.DrawingLabelsClearOfMaterial, null);
        form.SetDrawingOptions(true, false);
        EditByCanvasClick(form, "boreDiameter", editor =>
        {
            editor.ModeInput.SelectedIndex = 1; editor.PositionInput.SelectedItem = "JS"; editor.GradeInput.SelectedItem = "IT7";
            check("v24_editor_half_micrometre", editor.LowerInput!.Text == "-0,0125" && editor.UpperInput!.Text == "0,0125", null);
            CaptureDialog(editor, Path.Combine(folder, "22-standard-tolerance-editor.png"));
            PressDialogKey(editor, Keys.Enter);
        });
        check("v24_editor_applies_standard", form.CurrentProject?.Params.BoreTolerance == "JS7", null);
        form.SetInput("boreDiameter", "50,001"); form.GenerateNow();
        check("v24_side_nominal_recomputes_standard", form.CurrentProject?.Params.BoreTolerance == "JS7" && form.GetInput("boreUpperDeviation") == "0,015", null);
        form.SetInput("boreUpperDeviation", "0,02"); form.GenerateNow();
        check("v24_side_deviation_selects_manual", form.CurrentProject?.Params.BoreTolerance == "" && form.CurrentProject.Params.BoreUpperDeviation == .02, null);
        var standard = Engine.Defaults with { BoreTolerance = "H7", CounterboreTolerance = "JS8", FlangeTolerance = "h7", LengthTolerance = "IT8", CounterboreDepthTolerance = "IT7" };
        form.SetParameters(standard); form.GenerateNow(); form.SetDrawingOptions(true, true);
        form.CaptureForm(Path.Combine(folder, "23-standard-drawing.png"));
        check("v24_standard_tolerances_fit", form.DrawingLabelsInside && !form.DrawingLabelsOverlap && form.DrawingLabelsClearOfMaterial, null);
        form.SetController(CncProfiles.Sinumerik);
        check("v24_profile_switch_regenerates", form.CurrentProject?.Params.Controller == CncProfiles.Sinumerik && form.CurrentProject.Verification.Ok && !form.IsStale, null);
        form.OpenInspection(); var inspection = form.Inspection!;
        inspection.Refresh(); Application.DoEvents();
        check("v24_inspection_uses_parsed_moves", inspection.TransitionCount == 8 && inspection.DisplayedMoveCount == form.CurrentProject!.Verification.Moves.Count, null);
        inspection.SelectTransitionForSmoke(7); Application.DoEvents();
        check("v24_transition_selects_blocks", inspection.SelectedMoveCount == form.CurrentProject!.Transitions[7].Blocks.Count, inspection.SelectedMoveCount);
        inspection.CaptureImage(Path.Combine(folder, "24-inspection-siemens.png"));
        inspection.Size = inspection.MinimumSize; inspection.Refresh(); Application.DoEvents();
        inspection.CaptureImage(Path.Combine(folder, "25-trajectory-minimum.png"));
        inspection.ClientSize = new Size(1200, 850); inspection.Refresh(); Application.DoEvents();
        var move = form.CurrentProject!.Verification.Moves.First(m => m.Mode == "G1");
        inspection.SelectFrameForSmoke(move.PhysicalLine); Application.DoEvents();
        check("v24_frame_selects_one_move", inspection.SelectedMoveCount == 1, inspection.SelectedMoveCount);
        form.SetInput("length", "100");
        check("v24_open_inspection_updates_after_parameters", inspection.CurrentProjectId == Engine.Generate(form.ReadForSmoke()!).Project?.Id, inspection.CurrentProjectId);
        form.SetInput("length", "ошибка");
        check("v24_invalid_parameters_clear_inspection", inspection.DisplayedMoveCount == 0 && inspection.CurrentProjectId is null, null);
        inspection.Close();
        form.SetParameters(Engine.Defaults); form.GenerateNow(); form.SetDrawingOptions(true, false);
    }
}
