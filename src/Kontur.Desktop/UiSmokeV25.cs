using Kontur.Core;

namespace Kontur.Desktop;

internal static partial class UiSmoke
{
    private static void RunV25Checks(MainForm form, string folder, Action<string, bool, object?> check)
    {
        var cases = new[]
        {
            ("default", Engine.Defaults),
            ("close_steps", Engine.Defaults with { CounterboreDepth = 12.5 }),
            ("thin", Engine.Defaults with { Length = 118, FlangeDiameter = 60, BodyDiameter = 55, FlangeWidth = 2, BoreDiameter = 47.3, CounterboreDiameter = 50.5, CounterboreDepth = 1, StockDiameter = 64, StockBore = 45 }),
            ("wide", Engine.Defaults with { Length = 60, FlangeDiameter = 200, BodyDiameter = 180, FlangeWidth = 2, BoreDiameter = 130, CounterboreDiameter = 150, CounterboreDepth = 1, StockDiameter = 204, StockBore = 128 })
        };
        foreach (var (name, value) in cases)
            foreach (var standard in new[] { false, true })
                foreach (var minimum in new[] { false, true })
                {
                    var p = standard ? value with { BoreTolerance = "H7", CounterboreTolerance = "JS8", FlangeTolerance = "h7", LengthTolerance = "IT10", CounterboreDepthTolerance = "IT12" } : value;
                    form.SetParameters(p); form.SetDrawingOptions(true, true);
                    form.Size = minimum ? new Size(1100, 720) : new Size(1302, 906); RenderDrawing(form);
                    var s = form.Sketch;
                    check($"v25_tolerances_{name}_{standard}_{minimum}", s.LabelsInside && !s.LabelsOverlap && s.LabelsClearOfMaterial && s.LabelsClearOfRearEdges && s.LabelsClearOfDimensionLines,
                        new { s.LabelsInside, s.LabelsOverlap, s.LabelsClearOfMaterial, s.LabelsClearOfRearEdges, s.LabelsClearOfDimensionLines });
                    form.CaptureForm(Path.Combine(folder, $"30-tolerances-{name}-{standard}-{minimum}.png"));
                }
        form.ClientSize = new Size(1280, 850); form.SetParameters(Engine.Defaults); form.GenerateNow();
        var button = Descendants(form).OfType<Button>().Single(b => b.Name == "dimension_edit_boreDiameter");
        var title = CancelOwnModal(form, () => button.PerformClick());
        check("v25_explicit_tolerance_button_opens_editor", title == Engine.Fields.Single(f => f.Key == "boreDiameter").Label, title);
        form.OpenInspection(); var inspection = form.Inspection!;
        inspection.SelectTransitionForSmoke(2); inspection.Refresh(); Application.DoEvents();
        var plot = inspection.Trajectory; var isolated = plot.VisibleSegmentCount;
        plot.ShowAll = true; plot.Refresh();
        check("v25_isolate_transition_and_show_context", isolated > 0 && plot.VisibleSegmentCount > isolated, new { isolated, all = plot.VisibleSegmentCount });
        var withRapids = plot.VisibleSegmentCount; plot.ShowRapids = false; plot.Refresh();
        check("v25_hide_rapids", plot.VisibleSegmentCount < withRapids, plot.VisibleSegmentCount);
        plot.ZoomAt(new(plot.Width * .6F, plot.Height * .4F), 2);
        check("v25_zoom_anchors_at_pointer", plot.Zoom == 2 && Math.Abs(plot.Pan.X + .1F * plot.Width) < .01 && Math.Abs(plot.Pan.Y - .1F * plot.Height) < .01, plot.Pan);
        var beforePan = plot.Pan;
        var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        typeof(Control).GetMethod("OnMouseDown", flags)!.Invoke(plot, [new MouseEventArgs(MouseButtons.Left, 1, 80, 80, 0)]);
        typeof(Control).GetMethod("OnMouseMove", flags)!.Invoke(plot, [new MouseEventArgs(MouseButtons.Left, 0, 100, 110, 0)]);
        typeof(Control).GetMethod("OnMouseUp", flags)!.Invoke(plot, [new MouseEventArgs(MouseButtons.Left, 1, 100, 110, 0)]);
        check("v25_drag_pans_view", Math.Abs(plot.Pan.X - beforePan.X - 20) < .01 && Math.Abs(plot.Pan.Y - beforePan.Y - 30) < .01, plot.Pan);
        plot.Fit(); check("v25_fit_resets_view", plot.Zoom == 1 && plot.Pan == PointF.Empty, null);
        plot.ShowAll = false; plot.ShowRapids = true; plot.Refresh();
        inspection.CaptureImage(Path.Combine(folder, "31-selected-transition.png"));
        inspection.Close(); form.SetDrawingOptions(true, false);
    }
}
