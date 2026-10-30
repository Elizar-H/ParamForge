using Kontur.Core;

namespace Kontur.Desktop;

internal sealed partial class MainForm
{
    private sealed record DragTransaction(Parameters Baseline, Parameters Working, Project? Project, bool Stale, Dictionary<string, string> Texts);
    private DragTransaction? dragTransaction;
    private bool dragChanged;
    private bool BeginDimensionDrag(string key)
    {
        // A contour drag is a new edit; dismiss an unapplied flyout without moving the drawing.
        CloseDimensionEditor();
        var baseline = ReadParameters(false, false);
        if (baseline is null || Engine.Validate(baseline).Any(IsGeometryError))
        {
            return false;
        }

        previewTimer.Stop();
        dragTransaction = new(baseline, working, project, stale, inputs.ToDictionary(p => p.Key, p => p.Value.Text));
        dragChanged = false;
        if (inspection is { IsDisposed: false })
        {
            inspection.SetResult(null);
        }

        return true;
    }

    private bool PreviewDimensionDrag(string key, double nominal)
    {
        if (dragTransaction is null)
        {
            return false;
        }

        var candidate = dragTransaction.Baseline with
        {
        };
        ParameterDraftReader.Property(key).SetValue(candidate, nominal);
        try
        {
            candidate = Tolerances.Resolve(candidate);
        }
        catch (ArgumentException)
        {
            return false;
        }

        var invalid = Engine.Validate(candidate);
        if (invalid.Any(IsGeometryError))
        {
            status.ForeColor = Theme.Error;
            status.Text = ShortError(invalid);
            return false;
        }

        loading = true;
        try
        {
            working = candidate;
            foreach (var pair in inputs)
            {
                pair.Value.Text = NumericText.Format((double)ParameterDraftReader.Property(pair.Key).GetValue(candidate)!);
            }
        }
        finally
        {
            loading = false;
        }

        dragChanged = nominal != (double)ParameterDraftReader.Property(key).GetValue(dragTransaction.Baseline)!;
        stale = true;
        status.Text = invalid.Count == 0 ? "" : InteractionError(invalid, candidate);
        RefreshGeneralDeviations(candidate);
        sketch.Parameters = candidate;
        return true;
    }

    private bool IsGeometryError(ValidationError error) => inputs.ContainsKey(error.Field) && !IsSetupLengthError(error);
    private string InteractionError(IReadOnlyList<ValidationError> invalid, Parameters candidate)
    {
        if (invalid.Any(e => e.Field == "stockDiameter"))
        {
            return $"Заготовка Ø{NumericText.Format(candidate.StockDiameter)}. Для этого D нужна от Ø{NumericText.Format(Engine.MinimumStockDiameter(candidate))} мм.";
        }

        if (invalid.Any(e => e.Field == "stockBore") && candidate.StockBore > Engine.Target(candidate.BoreDiameter, candidate.BoreLowerDeviation, candidate.BoreUpperDeviation) - 2 * (candidate.FinishAllowance + .05))
        {
            var maximum = Engine.Target(candidate.BoreDiameter, candidate.BoreLowerDeviation, candidate.BoreUpperDeviation) - 2 * (candidate.FinishAllowance + .05);
            if (candidate.AutoStock && maximum < ToolingProfile.BoringMinimumDiameter + 2 * candidate.ClearanceX)
            {
                return "Для этого d не помещаются текущая оправка и отвод.\nНужны другой инструмент или меньший отвод.";
            }

            return $"Отверстие заготовки Ø{NumericText.Format(candidate.StockBore)} слишком велико.\nДля этого d нужно не больше Ø{NumericText.Format(maximum)} мм.";
        }

        return ShortError(invalid);
    }

    private bool CommitNominal(string key, double nominal)
    {
        var candidate = ReadParameters(false, false);
        if (candidate is null)
        {
            status.ForeColor = Theme.Error;
            status.Text = "Проверьте введённые размеры.";
            return false;
        }

        ParameterDraftReader.Property(key).SetValue(candidate, nominal);
        try
        {
            candidate = Tolerances.Resolve(candidate);
        }
        catch (ArgumentException ex)
        {
            status.Text = ex.Message;
            return false;
        }

        var invalid = Engine.Validate(candidate);
        if (invalid.Any(IsGeometryError))
        {
            status.ForeColor = Theme.Error;
            status.Text = InteractionError(invalid, candidate);
            return false;
        }

        SetParameters(candidate);
        if (invalid.Count != 0)
        {
            stale = true;
            status.ForeColor = Theme.Error;
            status.Text = InteractionError(invalid, candidate);
            if (inspection is { IsDisposed: false })
            {
                inspection.SetResult(null);
            }

            return true;
        }

        return GenerateNow();
    }

    private void FinishDimensionDrag(bool commit)
    {
        if (dragTransaction is not { } transaction)
        {
            return;
        }

        dragTransaction = null;
        if (commit && dragChanged)
        {
            GenerateNow();
            return;
        }

        loading = true;
        try
        {
            working = transaction.Working;
            project = transaction.Project;
            foreach (var pair in transaction.Texts)
            {
                inputs[pair.Key].Text = pair.Value;
            }
        }
        finally
        {
            loading = false;
        }

        stale = transaction.Stale;
        status.Text = "";
        UpdatePreview();
    }
}
