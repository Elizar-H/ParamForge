using Kontur.Core;

namespace Kontur.Desktop;

internal sealed partial class MainForm
{
    internal void SetToleranceView(bool visible) => toleranceView.Checked = visible;
    private void ApplyToleranceView()
    {
        if (dimensionTable is null)
        {
            return;
        }

        foreach (var readout in deviationReadouts.Values)
        {
            readout.ShowNumbers = toleranceView.Checked;
        }

        PositionDimensionFlyout();
    }

    private void EditDimension(string key)
    {
        if (dimensionEditor is { } active)
        {
            if (active.DimensionKey == key)
            {
                active.FocusNominal();
                return;
            }

            // Changing the selected dimension accepts a valid draft; invalid input stays visible.
            active.ApplyButton.PerformClick();
            if (dimensionEditor is not null)
            {
                return;
            }
        }

        var baseline = ReadParameters(false, false);
        if (baseline is null)
        {
            return;
        }

        previewTimer.Stop();
        UpdatePreview();
        var editor = new DimensionEditor(key, baseline, candidate => GenerateEditorCandidate(key, candidate));
        editor.FontChanged += (_, _) => PositionDimensionFlyout();
        editor.DpiChangedAfterParent += (_, _) => PositionDimensionFlyout();
        editor.Cancelled += CloseDimensionEditor;
        editor.Accepted += () =>
        {
            var result = editor.Result!;
            SetParameters(result.Params);
            project = result;
            if (inspection is { IsDisposed: false })
            {
                inspection.SetResult(project);
            }

            stale = false;
            sketch.Parameters = project.Params;
            status.Text = "";
        };
        dimensionEditor = editor;
        dimensionFlyout.Controls.Add(editor);
        PositionDimensionFlyout();
        dimensionFlyout.Visible = true;
        dimensionFlyout.BringToFront();
        editor.FocusNominal();
    }

    private void CloseDimensionEditor()
    {
        if (dimensionEditor is not { } editor)
        {
            return;
        }

        dimensionEditor = null;
        dimensionFlyout.Visible = false;
        dimensionFlyout.Controls.Remove(editor);
        editor.Dispose();
    }

    private void PositionDimensionFlyout()
    {
        if (dimensionEditor is null || !IsHandleCreated)
        {
            return;
        }

        var anchor = PointToClient(geometryHost.PointToScreen(new Point(geometryHost.Width + 10, 0)));
        var line = TextRenderer.MeasureText("M", dimensionEditor.Font).Height;
        var width = Math.Min(Math.Max(380, line * 12 + 24), Math.Max(300, ClientSize.Width - anchor.X - 12));
        var bounds = new Rectangle(anchor, new Size(width, Math.Min(line * 29 + 15, Math.Max(200, ClientSize.Height - anchor.Y - 18))));
        if (dimensionFlyout.Bounds != bounds)
        {
            dimensionFlyout.Bounds = bounds;
        }
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == Keys.Escape && dimensionEditor is not null)
        {
            CloseDimensionEditor();
            return true;
        }

        return base.ProcessCmdKey(ref msg, keyData);
    }

    private GenerationResult GenerateEditorCandidate(string key, Parameters draft)
    {
        var candidate = working with
        {
        };
        var dimension = Tolerances.EditableDimensions.SingleOrDefault(d => d.Key == key);
        var changed = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            key
        };
        if (dimension is not null)
        {
            changed.Add(dimension.LowerProperty);
            changed.Add(dimension.UpperProperty);
        }

        var invalid = new List<ValidationError>();
        foreach (var (name, input) in inputs)
        {
            if (changed.Contains(name))
            {
                ParameterDraftReader.Property(name).SetValue(candidate, ParameterDraftReader.Property(name).GetValue(draft));
            }
            else if (NumericText.TryParse(input.Text, out var value))
            {
                ParameterDraftReader.Property(name).SetValue(candidate, value);
            }
            else
            {
                invalid.Add(new(name, "Введите число в основном списке геометрии."));
            }
        }

        if (invalid.Count != 0)
        {
            return new(false, invalid, [], null);
        }

        if (dimension is not null)
        {
            try
            {
                candidate = Tolerances.SetDesignation(candidate, key, Tolerances.Designation(draft, dimension));
            }
            catch (ArgumentException ex)
            {
                return new(false, [new(key, ex.Message)], [], null);
            }
        }

        return Engine.Generate(candidate);
    }
}
