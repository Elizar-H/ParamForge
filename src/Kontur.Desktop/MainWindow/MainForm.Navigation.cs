using Kontur.Core;

namespace Kontur.Desktop;

internal sealed partial class MainForm
{
    private string? DiagnosticTarget()
    {
        var draft = ParameterDraftReader.Read(working, inputs.Select(i => KeyValuePair.Create(i.Key, i.Value.Text)), false);
        if (draft.Value is not { } candidate)
        {
            return draft.Errors.FirstOrDefault()?.Field;
        }
        var invalid = Engine.Validate(candidate);
        return invalid.FirstOrDefault(e => e.Field == "stockDiameter")?.Field
            ?? invalid.FirstOrDefault(e => e.Field == "stockBore")?.Field
            ?? invalid.FirstOrDefault()?.Field;
    }

    private void NavigateDiagnostic(string key)
    {
        if (key == "controller")
        {
            workspace.SelectedTab = geometryPage;
            controllerInput.Focus();
            return;
        }
        if (inputs.TryGetValue(key, out var input))
        {
            workspace.SelectedTab = geometryPage;
            var dimension = Tolerances.EditableDimensions.FirstOrDefault(d =>
                string.Equals(d.LowerProperty, key, StringComparison.OrdinalIgnoreCase)
                || string.Equals(d.UpperProperty, key, StringComparison.OrdinalIgnoreCase));
            if (dimension is not null)
            {
                EditDimension(dimension.Key);
                var field = string.Equals(dimension.LowerProperty, key, StringComparison.OrdinalIgnoreCase)
                    ? dimensionEditor?.LowerInput : dimensionEditor?.UpperInput;
                field?.Focus();
                field?.SelectAll();
            }
            else
            {
                input.Focus();
                input.SelectAll();
            }
            return;
        }
        ShowProcessing(false, key);
    }
}
