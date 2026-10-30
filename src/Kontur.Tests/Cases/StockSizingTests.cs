using Kontur.Core;

namespace Kontur.Tests;

internal static partial class RegressionSuite
{
    private static void RegisterStockSizingTests()
    {
        Add(
            "minimum stock diameter includes target and rough plus finish removal",
            () => Near(120.065, Engine.MinimumStockDiameter(Engine.Defaults))
        );
        Add("large flange is valid at the computed stock boundary in both controllers", () =>
        {
            foreach (var controller in new[] { CncProfiles.Axioma, CncProfiles.Sinumerik })
            {
                var p = Engine.Defaults with
                {
                    FlangeDiameter = 140,
                    Controller = controller
                };
                var minimum = Engine.MinimumStockDiameter(p);
                var project = Generated(p with
                {
                    StockDiameter = minimum
                });
                Near(139.565, project.Calculations.TargetFlangeDiameter);
                Check(project.Verification.Ok, "NC must verify");
                Rejected(p with
                {
                    StockDiameter = minimum - .001
                }, "stockDiameter");
            }
        });
        Add("minimum stock resolves standard tolerance before choosing target", () =>
        {
            var p = Engine.Defaults with
            {
                FlangeDiameter = 140,
                FlangeTolerance = "h7"
            };
            var resolved = Tolerances.Resolve(p);
            Near(
                Engine.Target(140, resolved.FlangeLowerDeviation, resolved.FlangeUpperDeviation) + .5,
                Engine.MinimumStockDiameter(p)
            );
        });
        Add(
            "exposed machining target keeps midpoint rounding precision",
            () => Near(47.302, Engine.Target(47.3, -.0123, .0157))
        );
    }
}
