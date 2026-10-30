namespace Kontur.Core;

/// <summary>Calculation of a prepared hollow blank for the current tooling profile.
/// The 4/2 mm diametral margins reproduce the initial project, not a stock catalogue.</summary>
public static class StockSelection
{
    public static Parameters Resolve(Parameters parameters)
    {
        if (!parameters.AutoStock)
        {
            return parameters;
        }

        var p = Tolerances.Resolve(parameters);
        // Invalid numeric input belongs to Engine.Validate; do not cast it to
        // decimal in the target calculation before its field can be diagnosed.
        if (new[] { p.BoreDiameter, p.FlangeDiameter, p.BoreLowerDeviation, p.BoreUpperDeviation, p.FlangeLowerDeviation, p.FlangeUpperDeviation, p.FinishAllowance, p.ClearanceX }
            .Any(v => !double.IsFinite(v) || Math.Abs(v) > 10000))
        {
            return p;
        }

        var removal = 2 * (p.FinishAllowance + .05);
        var diameter = Math.Ceiling(Math.Max(p.FlangeDiameter + 4, Engine.MinimumStockDiameter(p)));
        var maximumBore = Engine.Target(p.BoreDiameter, p.BoreLowerDeviation, p.BoreUpperDeviation) - removal;
        var minimumBore = ToolingProfile.BoringMinimumDiameter + 2 * p.ClearanceX;
        var bore = Math.Max(minimumBore, Math.Floor(Math.Min(p.BoreDiameter - 2, maximumBore)));
        return p with
        {
            StockDiameter = diameter,
            StockBore = bore
        };
    }
}
