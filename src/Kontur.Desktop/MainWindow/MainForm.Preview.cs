using Kontur.Core;

namespace Kontur.Desktop;

internal sealed partial class MainForm
{
    private void UpdatePreview()
    {
        var value = ReadParameters(false, false);
        if (value is not null && Engine.Validate(value).Any(e => inputs.ContainsKey(e.Field) && !IsSetupLengthError(e)))
        {
            value = null;
        }

        if (value is not null)
        {
            loading = true;
            try
            {
                foreach (var dimension in Tolerances.EditableDimensions.Where(d => Tolerances.Designation(value, d).Length != 0))
                {
                    foreach (var property in new[]
                    {
                        dimension.LowerProperty,
                        dimension.UpperProperty
                    }

                    )
                    {
                        var key = char.ToLowerInvariant(property[0]) + property[1..];
                        inputs[key].Text = NumericText.Format(Tolerances.Number(value, property));
                    }
                }
            }
            finally
            {
                loading = false;
            }
        }

        if (value is not null)
        {
            RefreshGeneralDeviations(value);
            var invalid = Engine.Validate(value);
            status.ForeColor = invalid.Count == 0 ? Theme.Muted : Theme.Error;
            status.Text = invalid.Count == 0 ? StockSummary(value) : InteractionError(invalid, value);
        }

        sketch.Parameters = value;
        sketch.Invalidate();
        if (inspection is { IsDisposed: false })
        {
            inspection.SetResult(value is null ? null : Engine.Generate(value).Project);
        }
    }

    private void RefreshGeneralDeviations(Parameters value)
    {
        foreach (var descriptor in Dimensions)
        {
            var dimension = Tolerances.EditableDimensions.SingleOrDefault(d => d.Key == descriptor.Key);
            var tolerance = ToolingProfile.GeneralLinearTolerance((double)ParameterDraftReader.Property(descriptor.Key).GetValue(value)!);
            var low = dimension is null ? -tolerance : Tolerances.Number(value, dimension.LowerProperty);
            var high = dimension is null ? tolerance : Tolerances.Number(value, dimension.UpperProperty);
            deviationReadouts[descriptor.Key].SetValues(low, high, dimension is null ? "" : Tolerances.Designation(value, dimension));
            tips.SetToolTip(deviationReadouts[descriptor.Key], $"{Tolerances.Signed(low)} / {Tolerances.Signed(high)} мм");
        }

        FitGeometryMetrics();
    }

    internal bool GenerateNow()
    {
        previewTimer.Stop();
        var value = ReadParameters(true);
        if (value is null)
        {
            return false;
        }

        var result = Engine.Generate(value);
        if (!result.Ok || result.Project is null)
        {
            ShowErrors(result.Errors);
            return false;
        }

        working = result.Project.Params;
        project = result.Project;
        loading = true;
        try
        {
            foreach (var d in Tolerances.EditableDimensions)
            {
                foreach (var property in new[]
                {
                    d.LowerProperty,
                    d.UpperProperty
                }

                )
                {
                    inputs[char.ToLowerInvariant(property[0]) + property[1..]].Text = NumericText.Format(Tolerances.Number(working, property));
                }
            }
        }
        finally
        {
            loading = false;
        }

        RefreshGeneralDeviations(working);
        stale = false;
        sketch.Parameters = value;
        status.ForeColor = Theme.Muted;
        status.Text = StockSummary(working);
        errors.Clear();
        if (inspection is { IsDisposed: false })
        {
            inspection.SetResult(project);
        }

        return true;
    }

    private static string StockSummary(Parameters p) => $"Заготовка Ø{NumericText.Format(p.StockDiameter)} / Ø{NumericText.Format(p.StockBore)}\n" + (p.AutoStock ? "Расчётная" : "Задана вручную");
}
