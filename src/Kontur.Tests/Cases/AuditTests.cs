using System.Globalization;
using Kontur.Core;

namespace Kontur.Tests;

internal static partial class RegressionSuite
{
    private static void RegisterAuditTests()
    {
        Add("every numeric input rejects nonfinite and out-of-range values without throwing", () =>
        {
            foreach (var field in Engine.Fields)
            {
                var property = typeof(Parameters).GetProperty(char.ToUpperInvariant(field.Key[0]) + field.Key[1..])!;
                foreach (var value in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity, double.MaxValue, -double.MaxValue, field.Min - .001, field.Max + .001 })
                {
                    var p = Engine.Defaults with
                    {
                        BodyTolerance = "",
                        FlangeWidthTolerance = ""
                    };
                    property.SetValue(p, value);
                    var result = Engine.Generate(p);
                    Check(!result.Ok && result.Project is null && result.Errors.Count > 0, field.Key + " rejects " + value);
                }
            }
        });

        Add("400 reproducible variants agree across controllers and independent NC reading", () =>
        {
            var random = new Random(2132);
            for (var i = 0; i < 400; i++)
            {
                var body = random.Next(90, 161);
                var bore = random.Next(46, 66);
                var p = Engine.Defaults with
                {
                    AutoStock = true,
                    Length = random.Next(70, 116),
                    BodyDiameter = body,
                    FlangeDiameter = body + random.Next(10, 26),
                    BoreDiameter = bore,
                    CounterboreDiameter = bore + random.Next(6, 19),
                    FlangeWidth = random.Next(6, 23),
                    CounterboreDepth = random.Next(5, 41),
                    DepthOutside = random.Next(5, 31) / 10d,
                    DepthInside = random.Next(3, 20) / 10d,
                    FinishAllowance = random.Next(5, 21) / 100d,
                    CuttingSpeed = random.Next(25, 151),
                    FeedRough = random.Next(15, 31) / 100d,
                    FeedFinish = random.Next(5, 15) / 100d,
                    RpmMax = random.Next(400, 1501)
                };
                if (i % 2 == 0)
                {
                    p = p with
                    {
                        BoreTolerance = "H7",
                        CounterboreTolerance = "JS9",
                        FlangeTolerance = "h8",
                        LengthTolerance = "IT10"
                    };
                }
                var a = Generated(p);
                var b = Generated(p with
                {
                    Controller = CncProfiles.Sinumerik
                });
                TargetsWithinIntervals(a);
                TargetsWithinIntervals(b);
                CheckNcAgreement(a);
                CheckNcAgreement(b);
                CheckPasses(a);
                Near(a.Verification.CutMinutes, b.Verification.CutMinutes);
                Equal(a.Verification.Moves.Count, b.Verification.Moves.Count);
                for (var n = 0; n < a.Verification.Moves.Count; n++)
                {
                    var x = a.Verification.Moves[n];
                    var y = b.Verification.Moves[n];
                    Equal((x.X, x.Z, x.Mode, x.Tool, x.Feed, x.Rpm), (y.X, y.Z, y.Mode, y.Tool, y.Feed, y.Rpm));
                }
            }
            Evidence["auditControllerVariants"] = 800;
        });

        Add("NC and project identity are independent of the Windows number culture", () =>
        {
            var before = CultureInfo.CurrentCulture;
            try
            {
                var expected = Generated();
                foreach (var name in new[] { "ru-RU", "en-US", "de-DE", "ar-SA" })
                {
                    CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(name);
                    var actual = Generated();
                    Equal(expected.Id, actual.Id);
                    Equal(expected.Nc, actual.Nc);
                }
            }
            finally
            {
                CultureInfo.CurrentCulture = before;
            }
        });

        Add("invalid XML characters in document metadata are rejected before export", () =>
        {
            foreach (var text in new[] { "bad\uFFFF", "bad\uFFFE", "bad\uD800" })
            {
                foreach (var key in new[] { "PartName", "Material", "Organization", "DrawingNumber", "Developer", "Checker", "RateSetter", "NormController" })
                {
                    var p = Engine.Defaults with
                    {
                    };
                    typeof(Parameters).GetProperty(key)!.SetValue(p, text);
                    Check(!Engine.Generate(p).Ok, key + " rejects invalid XML text");
                }
            }
            Generated(Engine.Defaults with
            {
                PartName = "Втулка \U0001F527"
            });
        });
    }
}
