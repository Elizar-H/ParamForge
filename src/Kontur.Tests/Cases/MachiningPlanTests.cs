using Kontur.Core;

namespace Kontur.Tests;

internal static partial class RegressionSuite
{
    private static void RegisterMachiningPlanTests()
    {
        Add("balanced pass allocation avoids the previous tiny final roughing cut", () =>
        {
            var flange = Generated().Transitions.Single(t => t.Id == "010.01");
            Sequence(new[] { 121.983, 119.965 }, flange.Passes);
            var previous = flange.StartDiameter;
            var depths = new List<double>();
            foreach (var diameter in flange.Passes)
            {
                depths.Add((previous - diameter) / 2);
                previous = diameter;
            }
            Near(1.0085, depths[0]);
            Near(1.009, depths[1]);
            Check(depths.Min() > 1, "no 0.0175-mm residual roughing pass remains");
        });

        Add("actual maximum depths come from rounded passes and remain distinct from requested limits", () =>
        {
            var p = Generated();
            var expected = new[] { 1.009, .2, 1.967, .2, .6, .2, .9705, .2 };
            for (var i = 0; i < p.Transitions.Count; i++)
            {
                var transition = p.Transitions[i];
                Near(expected[i], transition.ActualMaximumDepth);
                Near(IndependentMaximumDepth(transition), transition.ActualMaximumDepth);
                Check(transition.ActualMaximumDepth <= transition.Depth + .000501, "actual depth respects the requested pass limit");
            }
            Near(2, p.Transitions[0].Depth);
            Near(1.0085, (double)(((decimal)p.Transitions[0].StartDiameter - (decimal)p.Transitions[0].Passes[0]) / 2m));
            Check(
                p.Transitions[0].ActualMaximumDepth != p.Transitions[0].Depth,
                "operation-card t must not silently equal the requested 2-mm limit"
            );
        });

        Add("balanced pass allocation uses decimal ceil and rounded equal spacing in both directions", () =>
        {
            foreach (var sample in new[]
            {
                (Start: 124d, Target: 119.965, Depth: 2d),
                (Start: 45d, Target: 47.4, Depth: 1d),
                (Start: 47.8, Target: 74.97, Depth: 1d),
                (Start: 100d, Target: 99.4, Depth: .1),
                (Start: 99.4, Target: 100d, Depth: .1),
                (Start: 100d, Target: 99.399, Depth: .1),
                (Start: 100d, Target: 99.401, Depth: .1),
                (Start: 100d, Target: 99.995, Depth: .001),
                (Start: 99.995, Target: 100d, Depth: .001)
            })
            {
                var pass = Engine.PassDiameters(sample.Start, sample.Target, sample.Depth);
                var delta = Math.Abs((decimal)sample.Target - (decimal)sample.Start);
                var count = (int)decimal.Ceiling(delta / (2m * (decimal)sample.Depth));
                Equal(count, pass.Count, "minimum pass count without binary ceil overcounting");
                Near(sample.Target, pass[^1]);
                var previous = sample.Start;
                for (var i = 0; i < pass.Count; i++)
                {
                    var expected = (double)Math.Round(
                        (decimal)sample.Start + ((decimal)sample.Target - (decimal)sample.Start) * (i + 1) / count,
                        3,
                        MidpointRounding.AwayFromZero
                    );
                    Near(expected, pass[i]);
                    Check(Math.Abs(pass[i] - previous) / 2 <= sample.Depth + .000501, "rounded physical depth is bounded");
                    Check(Math.Sign(pass[i] - previous) == Math.Sign(sample.Target - sample.Start), "passes remain monotonic");
                    previous = pass[i];
                }
            }
            Sequence(Array.Empty<double>(), Engine.PassDiameters(100, 100, 1));
        });

        Add("HTML metadata is escaped and cannot become NC instructions", () =>
        {
            const string payload = "<img src=x onerror=alert(1)>&\"'";
            var p = Generated(Engine.Defaults with
            {
                PartName = payload,
                Material = payload
            });
            foreach (var html in new[] { p.RouteHtml, p.TechnologyHtml })
            {
                Check(!html.Contains(payload, StringComparison.Ordinal), "raw payload excluded");
                Check(
                    html.Contains("&lt;img", StringComparison.Ordinal) && html.Contains("&amp;", StringComparison.Ordinal),
                    "HTML escaping"
                );
            }
            Check(!p.Nc.Contains(payload, StringComparison.Ordinal), "metadata excluded from NC");
            Check(p.Nc.All(ch => ch <= 127), "NC ASCII");
        });

        Add("spindle cap and requested surface-speed bound hold for every transition", () =>
        {
            foreach (var p in new[] { Generated(), Generated(Engine.Defaults with { CuttingSpeed = 150, RpmMax = 100 }) })
            {
                foreach (var t in p.Transitions)
                {
                    Check(t.Rpm > 0 && t.Rpm <= p.Params.RpmMax && Math.Floor(t.Rpm) == t.Rpm, "integer capped rpm");
                    Check(Math.PI * t.ReferenceDiameter * t.Rpm / 1000 <= p.Params.CuttingSpeed + 1e-9, "surface-speed cap");
                }
            }
        });

        Add("actual pass diameters meet radial depth caps and exact final targets", () =>
        {
            foreach (var p in new[] { Generated(), Generated(Engine.Defaults with { DepthOutside = .7, DepthInside = .3, FinishAllowance = .1 }) })
            {
                CheckPasses(p);
            }
        });

        Add("independent NC reading reconstructs generated coordinates and G1 time", () =>
        {
            foreach (var p in new[] { Generated(), Generated(Engine.Defaults with { Length = 100 }), Generated(Engine.Defaults with { BodyDiameter = 95, DepthOutside = 1 }), Generated(Engine.Defaults with { CounterboreDepth = 40 }) })
            {
                CheckNcAgreement(p);
            }
        });

        Add("80 valid deterministic variants retain targets, pass caps and NC agreement", () =>
        {
            for (var i = 0; i < 80; i++)
            {
                var p = Generated(Engine.Defaults with
                {
                    Length = 60 + i % 59,
                    FlangeWidth = 8 + i % 8,
                    CounterboreDepth = 18 + i % 30,
                    BodyDiameter = 90 + i % 15,
                    CuttingSpeed = 40 + i,
                    DepthOutside = .5 + i % 5 * .5,
                    DepthInside = .3 + i % 3 * .3,
                    FinishAllowance = .1
                });
                TargetsWithinIntervals(p);
                CheckPasses(p);
                CheckNcAgreement(p);
                sweepVariants++;
            }
        });

    }
}
