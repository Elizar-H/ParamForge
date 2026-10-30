using Kontur.Core;

namespace Kontur.Desktop;

internal sealed partial class MainForm
{
    private TextBox Input(string key)
    {
        var field = Engine.Fields.First(f => f.Key == key);
        var box = new SelectAllTextBox
        {
            Name = "parameter_" + key,
            Font = Theme.Numeric,
            BorderStyle = BorderStyle.None,
            BackColor = Theme.Surface,
            Dock = DockStyle.Fill,
            Margin = new Padding(3, 11, 5, 7),
            AccessibleName = field.Label
        };
        box.FontChanged += (_, _) => FitGeometryMetrics();
        box.Enter += (_, _) => box.BackColor = Theme.Surface;
        box.Leave += (_, _) => box.BackColor = Theme.Surface;
        box.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter)
            {
                GenerateNow();
                box.SelectAll();
                e.Handled = e.SuppressKeyPress = true;
            }
            else if (e.KeyCode == Keys.Escape)
            {
                loading = true;
                box.Text = NumericText.Format((double)ParameterDraftReader.Property(key).GetValue(working)!);
                loading = false;
                errors.SetError(box, "");
                box.BackColor = Theme.Surface;
                UpdatePreview();
                box.SelectAll();
                e.Handled = e.SuppressKeyPress = true;
            }
        };
        inputs.Add(key, box);
        tips.SetToolTip(box, field.Note);
        errors.SetIconAlignment(box, ErrorIconAlignment.MiddleLeft);
        errors.SetIconPadding(box, 1);
        box.TextChanged += (_, _) =>
        {
            if (loading)
            {
                return;
            }

            var dimension = Tolerances.EditableDimensions.FirstOrDefault(d => string.Equals(d.LowerProperty, key, StringComparison.OrdinalIgnoreCase) || string.Equals(d.UpperProperty, key, StringComparison.OrdinalIgnoreCase));
            if (dimension is not null)
            {
                working = Tolerances.SetDesignation(working, dimension.Key, "");
            }

            stale = true;
            errors.SetError(box, "");
            box.BackColor = Theme.Surface;
            status.Text = "";
            previewTimer.Stop();
            previewTimer.Start();
        };
        return box;
    }

    internal TextBox NominalField(string key) => inputs[key];
    private void FocusNominal(string key)
    {
        CloseDimensionEditor();
        if (inputs.TryGetValue(key, out var input))
        {
            input.Focus();
            input.SelectAll();
        }
    }
    internal void SetParameters(Parameters value)
    {
        CloseDimensionEditor();
        loading = true;
        previewTimer.Stop();
        try
        {
            working = StockSelection.Resolve(Tolerances.Resolve(value));
            value = working;
            controllerInput.SelectedIndex = value.Controller == CncProfiles.Sinumerik ? 1 : 0;
            errors.Clear();
            foreach (var input in inputs)
            {
                input.Value.Text = NumericText.Format((double)ParameterDraftReader.Property(input.Key).GetValue(value)!);
                input.Value.BackColor = Theme.Surface;
            }
        }
        finally
        {
            loading = false;
        }

        stale = true;
        status.Text = "";
        UpdatePreview();
    }

    internal void SetInput(string key, string value)
    {
        inputs[key].Text = value;
        UpdatePreview();
    }

    internal string GetInput(string key) => inputs[key].Text;
    private Parameters? ReadParameters(bool showErrors, bool validate = true)
    {
        var draft = ParameterDraftReader.Read(working,
            inputs.Select(input => KeyValuePair.Create(input.Key, input.Value.Text)), validate);
        if (draft.Value is not null)
        {
            return draft.Value;
        }

        if (showErrors)
        {
            ShowErrors(draft.Errors);
        }

        return null;
    }
}
