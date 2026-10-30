using System.Text.Json;

namespace Kontur.Desktop;

internal static partial class UiSmoke
{
    private static void RunDpiChecks(string folder, Action<string, bool, object?> check, List<string> failures)
    {
        void Check(string name, bool passed, object? evidence = null) => check(name, passed, evidence);

        foreach (var targetDpi in new[] { 96, 120, 144, 192 })
        {
            try
            {
                using var simulated = new MainForm();
                simulated.Sketch.SimulatedModifiers = Keys.None;
                simulated.Sketch.SyntheticInput = true;
                simulated.Show();
                Application.DoEvents();
                simulated.SimulateDpiText(targetDpi);
                simulated.CaptureForm(Path.Combine(folder, $"06-text-scale-simulation-{targetDpi}dpi.png"));
                CheckCaptions(simulated, "scaled_" + targetDpi, Check);
                Check("text_scale_simulation_" + targetDpi, simulated.DrawingLabelsInside && !simulated.DrawingLabelsOverlap && simulated.RearWallsClosed && simulated.DrawingLabelsClearOfMaterial && simulated.DrawingBoreDimensionsClearOfMaterial && simulated.DrawingLabelsClearOfRearEdges,
                    new
                    {
                        simulatedDpi = targetDpi,
                        actualWindowsDpi = simulated.DeviceDpi,
                        scope = "application text scaling simulation; Windows DPI settings unchanged"
                    });
                simulated.SetDrawingOptions(true, true);
                RenderDrawing(simulated);
                Check(
                    "v25_tolerance_scale_" + targetDpi,
                    simulated.DrawingLabelsInside && !simulated.DrawingLabelsOverlap && simulated.DrawingLabelsClearOfRearEdges && simulated.Sketch.LabelsClearOfDimensionLines,
                    null
                );
                simulated.CaptureForm(Path.Combine(folder, $"06-tolerances-simulation-{targetDpi}dpi.png"));
                simulated.SetDrawingOptions(true, false);
                ClickCaption(simulated, "bodyDiameter");
                Check(
                    "v29_scaled_inline_" + targetDpi,
                    simulated.NominalField("bodyDiameter").Focused && FullyVisible(simulated.NominalField("bodyDiameter"), simulated) && simulated.ActiveDimensionEditor is null
                );
                simulated.NominalField("bodyDiameter").Text = "98";
                PressNominalKey(simulated, "bodyDiameter", Keys.Enter);
                Check(
                    "v29_scaled_inline_commit_" + targetDpi,
                    simulated.CurrentProject!.Params.BodyDiameter == 98 && !simulated.IsStale
                );
                foreach (var key in new[] { "length", "flangeDiameter", "bodyDiameter", "flangeWidth", "boreDiameter", "counterboreDiameter", "counterboreDepth" })
                {
                    var before = JsonSerializer.Serialize(simulated.CurrentProject!);
                    EditToleranceFromPanel(simulated, key, editor => PressDialogKey(editor, Keys.Escape));
                    Check(
                        "scaled_hit_" + targetDpi + "_" + key,
                        before == JsonSerializer.Serialize(simulated.CurrentProject!) && !simulated.IsStale
                    );
                }
                var end = BeginSizeDrag(simulated, "length", 2);
                SendMouse(simulated.Sketch, "OnMouseUp", end);
                Check("v26_drag_scale_" + targetDpi, simulated.CurrentProject?.Params.Length == 118 && !simulated.IsStale, null);
                simulated.Close();
            }
            catch (Exception ex) { failures.Add("simulation " + targetDpi + ": " + ex); }
        }

    }
}
