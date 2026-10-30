using Kontur.Core;

namespace Kontur.Desktop;

internal sealed partial class MainForm
{
    private void OpenProcessing() => ShowProcessing(this, false);
    private void ShowProcessing(Form owner, bool targets)
    {
        var value = ReadParameters(true, false);
        if (value is null)
        {
            return;
        }

        using var dialog = new ProcessingDialog(value, inputs.Keys.ToHashSet(), targets);
        if (dialog.ShowDialog(owner) == DialogResult.OK)
        {
            SetParameters(dialog.Result!);
            GenerateNow();
        }
    }

    internal void OpenInspection()
    {
        if (!GenerateNow())
        {
            return;
        }

        if (inspection is null || inspection.IsDisposed)
        {
            inspection = new ResultInspectionForm();
            inspection.ControllerRequested += controller =>
            {
                if (!GenerateNow())
                {
                    return false;
                }

                SetController(controller);
                return !stale;
            };
            inspection.ParametersRequested += () => ShowProcessing(inspection, true);
        }

        inspection.SetResult(project);
        if (!inspection.Visible)
        {
            inspection.Show(this);
        }

        if (inspection.WindowState == FormWindowState.Minimized)
        {
            inspection.WindowState = FormWindowState.Normal;
        }

        inspection.BringToFront();
        inspection.Activate();
    }

    internal void SetController(string controller)
    {
        controllerInput.SelectedIndex = controller == CncProfiles.Sinumerik ? 1 : 0;
    }

    internal void SetDrawingOptions(bool axes, bool tolerances)
    {
        sketch.ShowAxes = axes;
        sketch.ShowTolerances = tolerances;
        sketch.Invalidate();
    }

    private void ShowTargets()
    {
        var value = ReadParameters(true, false);
        if (value is null)
        {
            return;
        }

        using var dialog = new ProcessingDialog(value, inputs.Keys.ToHashSet(), true);
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            SetParameters(dialog.Result!);
            GenerateNow();
        }
    }
}
