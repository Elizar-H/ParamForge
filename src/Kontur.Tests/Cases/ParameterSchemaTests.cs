using Kontur.Core;

namespace Kontur.Tests;

internal static partial class RegressionSuite
{
    private static void RegisterParameterSchemaTests()
    {
        Add("field schema exposes exactly 36 numeric parameters", () =>
        {
            Equal(36, Engine.Fields.Count, "numeric field count");
            Equal(36, Engine.Fields.Select(f => f.Key).Distinct(StringComparer.Ordinal).Count(), "unique field keys");
            var numericProperties = typeof(Parameters).GetProperties().Where(p => p.PropertyType == typeof(double)).ToArray();
            Equal(36, numericProperties.Length, "numeric property count");
            foreach (var f in Engine.Fields)
            {
                var property = ParameterProperty(f.Key);
                Equal(typeof(double), property.PropertyType, f.Key);
                Check(f.Min < f.Max && f.Step > 0, f.Key + " has a valid editable range");
            }
        });

        Add("confirmed photo dimensions and midpoint targets propagate into all outputs", () =>
        {
            var p = Generated();
            var c = p.Calculations;
            Near(47.3, p.Params.BoreDiameter);
            Near(45, p.Params.StockBore);
            Near(1, p.Params.BoreUpperDeviation);
            Near(.74, p.Params.CounterboreUpperDeviation);
            Near(-.87, p.Params.FlangeLowerDeviation);
            Near(-1, p.Params.LengthLowerDeviation);
            Near(-.18, p.Params.CounterboreDepthLowerDeviation);
            Near(.18, p.Params.CounterboreDepthUpperDeviation);
            Near(47.8, c.TargetBoreDiameter);
            Near(75.37, c.TargetCounterboreDiameter);
            Near(119.565, c.TargetFlangeDiameter);
            Near(115.5, c.TargetLength);
            Near(28, c.TargetCounterboreDepth);
            Near(117.5, c.CutLength);
            Near(155.5, c.StockLength);
            Near(157.5, c.RawStockLength);
            Near(128, c.ClearOut);
            Near(41, c.ClearIn);
            Sequence(new[] { "005", "010", "020", "030" }, p.Operations.Select(o => o.Code));
            Sequence(new[] { 2, 1, 6, 1, 2, 1, 14, 1 }, p.Transitions.Select(t => t.Passes.Count));
            Near(119.565, p.Transitions[1].Diameter);
            Near(47.8, p.Transitions[5].Diameter);
            Near(75.37, p.Transitions[7].Diameter);
            Near(-117.5, p.Transitions[2].ZEnd);
            Near(-117.5, p.Transitions[5].ZEnd);
            Near(-28, p.Transitions[7].ZEnd);
            foreach (var text in new[] { p.RouteHtml, p.TechnologyHtml, p.Nc })
            {
                Check(text.Contains(p.Id, StringComparison.Ordinal), "revision linkage");
            }

            foreach (var t in p.Transitions)
            {
                Check(p.Nc.Contains("(TR " + t.Id + " ", StringComparison.Ordinal), "NC transition " + t.Id);
                Check(p.TechnologyHtml.Contains(t.Id, StringComparison.Ordinal), "card transition " + t.Id);
                foreach (var n in t.Blocks)
                {
                    Check(p.Nc.Contains("N" + n + " ", StringComparison.Ordinal), "linked NC block");
                }
            }
            Evidence["defaultProject"] = new
            {
                p.Id,
                targetBoreDiameter = c.TargetBoreDiameter,
                targetCounterboreDiameter = c.TargetCounterboreDiameter,
                targetFlangeDiameter = c.TargetFlangeDiameter,
                targetLength = c.TargetLength,
                targetCounterboreDepth = c.TargetCounterboreDepth,
                c.CutLength,
                c.StockLength,
                passesByTransition = p.Transitions.Select(t => t.Passes.Count).ToArray(),
                totalPasses = p.Transitions.Sum(t => t.Passes.Count),
                moves = p.Moves.Count,
                c.TotalCutMinutes
            };
        });

    }
}
