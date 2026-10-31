using Kontur.Core;

namespace Kontur.Desktop;

internal sealed partial class MainForm
{
    private void OpenProcessing() => ShowProcessing(false);
    private void ShowProcessing(bool targets, string? focusKey = null)
    {
        var value = ReadParameters(true, false);
        if (value is null)
        {
            return;
        }

        if (processing is null || processing.IsDisposed)
        {
            processing = new ProcessingView(value, targets) { Dock = DockStyle.Fill };
            processingPage = new TabPage("Заготовка и обработка") { BackColor = Theme.Surface, Padding = Padding.Empty };
            processingPage.Controls.Add(processing);
            workspace.TabPages.Add(processingPage);
            workspace.SetClosable(processingPage, true);
            processing.GenerateCandidate = ApplyProcessingCandidate;
            processing.Accepted += result => { SetParameters(result.Params); GenerateNow(); };
            processing.Cancelled += CloseProcessing;
        }
        else
        {
            processing.Rebase(value);
        }
        workspace.SelectedTab = processingPage;
        if (focusKey is not null)
        {
            processing.FocusField(focusKey);
        }
        else if (targets)
        {
            processing.Sections.SelectedIndex = 4;
            processing.FocusField("length");
        }
        else
        {
            processing.Sections.Navigation.Focus();
        }
    }

    private GenerationResult ApplyProcessingCandidate(Parameters draft)
    {
        var current = ReadParameters(false, false);
        if (current is null)
        {
            return new(false, [new("length", "Проверьте введённые размеры в геометрии.")], [], null);
        }
        foreach (var key in processing!.EditedProperties)
        {
            ParameterDraftReader.Property(key).SetValue(current, ParameterDraftReader.Property(key).GetValue(draft));
        }
        return Engine.Generate(current);
    }

    private void CloseProcessing()
    {
        if (processingPage is not { } page)
        {
            return;
        }
        if (workspace.SelectedTab == page)
        {
            workspace.SelectedTab = geometryPage;
        }
        workspace.SetClosable(page, false);
        workspace.TabPages.Remove(page);
        processingPage = null;
        processing = null;
        page.Dispose();
    }

    internal void OpenInspection()
    {
        if (!GenerateNow())
        {
            return;
        }

        if (inspection is null || inspection.IsDisposed)
        {
            inspection = new ResultInspectionView { Dock = DockStyle.Fill };
            inspectionPage = new TabPage("Траектория УП") { BackColor = Theme.Surface, Padding = Padding.Empty };
            inspectionPage.Controls.Add(inspection);
            workspace.TabPages.Add(inspectionPage);
            workspace.SetClosable(inspectionPage, true);
            inspection.ControllerRequested += controller =>
            {
                if (!GenerateNow())
                {
                    return false;
                }

                SetController(controller);
                return !stale;
            };
            inspection.ParametersRequested += () => ShowProcessing(true);
            inspection.CloseRequested += CloseInspection;
        }

        inspection.SetResult(project);
        workspace.SelectedTab = inspectionPage;
        inspection.Focus();
    }

    private void CloseInspection()
    {
        if (inspectionPage is not { } page)
        {
            return;
        }
        if (workspace.SelectedTab == page)
        {
            workspace.SelectedTab = geometryPage;
        }
        workspace.SetClosable(page, false);
        workspace.TabPages.Remove(page);
        inspectionPage = null;
        inspection = null;
        page.Dispose();
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

    private void ShowTargets() => ShowProcessing(true);
}
