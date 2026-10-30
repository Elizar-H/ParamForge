using System.Text.Json;

using Kontur.Core;

namespace Kontur.Tests;

internal static partial class RegressionSuite
{
    private static void RegisterProcessSetupTests()
    {
        Add("generation is deterministic and revisions respond to each input", () =>
        {
            var a = Generated();
            var b = Generated(Engine.Defaults);
            Equal(JsonSerializer.Serialize(a), JsonSerializer.Serialize(b), "full project determinism");
            foreach (var f in Engine.Fields)
            {
                var changed = Generated(Change(Engine.Defaults, f.Key, Value(Engine.Defaults, f.Key) + (f.Key == "rpmMax" ? 1 : .001)));
                Check(a.Id != changed.Id, "revision responds to " + f.Key);
            }
            Check(a.Id != Generated(Engine.Defaults with
            {
                PartName = "Other part"
            }).Id, "name revision");
            Check(a.Id != Generated(Engine.Defaults with
            {
                Material = "Other material"
            }).Id, "material revision");
            Equal(a.Id, Generated().Id, "later generation does not mutate defaults");
        });

        Add("length and counterbore depth edits propagate to cuts, NC, cards and time", () =>
        {
            var a = Generated();
            var b = Generated(Engine.Defaults with
            {
                Length = 100
            });
            Near(139.5, b.Calculations.StockLength);
            Near(-101.5, b.Transitions[2].ZEnd);
            Check(b.Nc.Contains("Z-101.5", StringComparison.Ordinal), "NC length");
            Check(b.RouteHtml.Contains("100", StringComparison.Ordinal), "route length");
            Check(b.TechnologyHtml.Contains("101.5", StringComparison.Ordinal), "technology cut length");
            Check(b.Calculations.TotalCutMinutes < a.Calculations.TotalCutMinutes, "shorter cuts take less time");
            var deep = Generated(Engine.Defaults with
            {
                CounterboreDepth = 40
            });
            Near(-40, deep.Transitions[7].ZEnd);
            Near(-117.5, deep.Transitions[5].ZEnd);
        });

        Add("prepared and raw stock lengths conserve each independent axial allowance", () =>
        {
            var project = Generated();
            var p = project.Params;
            var c = project.Calculations;
            Near(2, p.TrimAllowance);
            Near(3, c.CutoffWidth);
            Near(10, p.ChuckClearance);
            Near(25, p.GripLength);
            Near(2, p.FrontFacingAllowance);
            Near(6, ToolingProfile.CutoffHolderAxialProjection);
            Near(4, ToolingProfile.ChuckRequiredResidualClearance);
            Near(ToolingProfile.ChuckMinimumClearance, p.ChuckClearance);
            Check(p.ChuckClearance - ToolingProfile.CutoffHolderAxialProjection >= ToolingProfile.ChuckRequiredResidualClearance,
                "10-mm axial clearance retains 4 mm beyond the selected 6-mm project holder envelope");
            Near(117.5, c.CutLength);
            Near(-117.5, c.PartingFrontZ);
            Near(-120.5, c.PartingBackZ);
            Near(-130.5, c.JawsFrontZ);
            Near(155.5, c.StockLength);
            Near(157.5, c.RawStockLength);
            Near(c.TargetLength + p.TrimAllowance, c.CutLength);
            Near(c.CutLength + c.CutoffWidth + p.ChuckClearance + p.GripLength, c.StockLength);
            Near(c.StockLength + p.FrontFacingAllowance, c.RawStockLength);
            Near(c.CutoffWidth, c.PartingFrontZ - c.PartingBackZ);
            Near(p.ChuckClearance, c.PartingBackZ - c.JawsFrontZ);
            Near(p.GripLength, c.StockLength + c.JawsFrontZ);
            Check(c.PartingFrontZ > c.PartingBackZ && c.PartingBackZ > c.JawsFrontZ, "kerf is outside the jaw front");
            Evidence["axialStockBudget"] = new
            {
                c.TargetLength,
                p.TrimAllowance,
                c.CutoffWidth,
                p.ChuckClearance,
                p.GripLength,
                p.FrontFacingAllowance,
                c.CutLength,
                c.StockLength,
                c.RawStockLength,
                c.PartingFrontZ,
                c.PartingBackZ,
                c.JawsFrontZ
            };
        });

        Add("rear trim is counted once while the right-face step references remain unchanged", () =>
        {
            var first = Generated();
            var moreTrim = Generated(Engine.Defaults with
            {
                TrimAllowance = 3
            });
            Near(first.Calculations.TargetLength, moreTrim.Calculations.TargetLength);
            Near(first.Calculations.CutLength + 1, moreTrim.Calculations.CutLength);
            Near(first.Calculations.StockLength + 1, moreTrim.Calculations.StockLength);
            Near(first.Calculations.RawStockLength + 1, moreTrim.Calculations.RawStockLength);
            Near(first.Calculations.PartingFrontZ - 1, moreTrim.Calculations.PartingFrontZ);
            Near(first.Calculations.PartingBackZ - 1, moreTrim.Calculations.PartingBackZ);
            Near(first.Calculations.JawsFrontZ - 1, moreTrim.Calculations.JawsFrontZ);
            foreach (var id in new[] { "010.01", "010.02", "010.03", "010.04", "010.07", "010.08" })
            {
                var a = first.Transitions.Single(t => t.Id == id);
                var b = moreTrim.Transitions.Single(t => t.Id == id);
                Near(a.ZStart, b.ZStart);
                if (id is "010.01" or "010.02" or "010.07" or "010.08")
                {
                    Near(a.ZEnd, b.ZEnd);
                }
            }
            foreach (var changed in new[]
            {
                Generated(Engine.Defaults with { ChuckClearance = 11 }),
                Generated(Engine.Defaults with { GripLength = 26 }),
                Generated(Engine.Defaults with { FrontFacingAllowance = 3 })
            })
            {
                Near(first.Calculations.CutLength, changed.Calculations.CutLength);
                Near(first.Calculations.RawStockLength + 1, changed.Calculations.RawStockLength);
                Equal(
                    first.Nc,
                    changed.Nc.Replace(changed.Id, first.Id, StringComparison.Ordinal),
                    "non-cutting stock allowances do not move the cutting path"
                );
            }
        });

        Add("second setup has a defined clamping belt and flange pocket from the first-face datum", () =>
        {
            Near(25, ToolingProfile.SecondSetupGripLength);
            Near(2, ToolingProfile.SoftJawBeltStartClearance);
            Near(1, ToolingProfile.SoftJawPocketAxialClearance);
            Near(1, ToolingProfile.SoftJawPocketDiametralClearance);
            Near(5, ToolingProfile.SecondSetupFaceClearance);
            var baseline = Generated();
            Near(14.2, baseline.Calculations.SecondClampStart);
            Near(39.2, baseline.Calculations.SecondClampEnd);
            Near(121, baseline.Calculations.SoftJawPocketDiameter);
            Near(13.2, baseline.Calculations.SoftJawPocketDepth);
            foreach (var project in new[]
            {
                baseline,
                Generated(Engine.Defaults with { FlangeWidth = 30 }),
                Generated(Engine.Defaults with { FlangeWidth = 30.001 }),
                Generated(Engine.Defaults with { FlangeDiameter = 121, FlangeUpperDeviation = .2 })
            })
            {
                var p = project.Params;
                var c = project.Calculations;
                var expectedWidthTolerance = p.FlangeWidth <= 30 ? .2m : .3m;
                var expectedStart = (decimal)p.FlangeWidth + expectedWidthTolerance + 2m;
                Near((double)expectedStart, c.SecondClampStart);
                Near((double)(expectedStart + 25m), c.SecondClampEnd);
                Near((double)((decimal)p.FlangeDiameter + (decimal)p.FlangeUpperDeviation + 1m), c.SoftJawPocketDiameter);
                Near((double)((decimal)p.FlangeWidth + expectedWidthTolerance + 1m), c.SoftJawPocketDepth);
                Check(
                    p.Length + p.LengthLowerDeviation - c.SecondClampEnd >= 5 - 1e-8,
                    "minimum final length leaves the required facing gap beyond the clamping belt"
                );
            }
            var moreRearTrim = Generated(Engine.Defaults with
            {
                TrimAllowance = 3
            });
            Near(baseline.Calculations.SecondClampStart, moreRearTrim.Calculations.SecondClampStart);
            Near(baseline.Calculations.SecondClampEnd, moreRearTrim.Calculations.SecondClampEnd);
            Near(baseline.Calculations.SoftJawPocketDiameter, moreRearTrim.Calculations.SoftJawPocketDiameter);
            Near(baseline.Calculations.SoftJawPocketDepth, moreRearTrim.Calculations.SoftJawPocketDepth);
            Evidence["secondSetupGeometry"] = new
            {
                baseline.Calculations.SecondClampStart,
                baseline.Calculations.SecondClampEnd,
                baseline.Calculations.SoftJawPocketDiameter,
                baseline.Calculations.SoftJawPocketDepth
            };
        });

        Add("second-setup clamping rejects a short part and checks the minimum tolerance limit", () =>
        {
            var shortPart = Engine.Defaults with
            {
                Length = 20,
                LengthLowerDeviation = 0,
                LengthUpperDeviation = 0,
                FlangeWidth = 2,
                CounterboreDepth = 1,
                CounterboreDepthLowerDeviation = 0,
                CounterboreDepthUpperDeviation = 0
            };
            var rejected = Engine.Generate(shortPart);
            Check(!rejected.Ok && rejected.Project is null, "short but drawable geometry must not produce this process");
            Check(
                rejected.Errors.Any(error => error.Field == "length" && error.Message.Contains("второго установа", StringComparison.Ordinal)),
                "rejection identifies the unsupported second setup"
            );
            var exact = Generated(Engine.Defaults with
            {
                Length = 44.2,
                LengthLowerDeviation = 0,
                LengthUpperDeviation = 0
            });
            Near(5, exact.Params.Length - exact.Calculations.SecondClampEnd);
            Rejected(exact.Params with
            {
                Length = 44.199
            }, "length");
            Rejected(exact.Params with
            {
                LengthLowerDeviation = -.001
            }, "length");
        });

        Add("the installed boring bar limits process depth and minimum entry geometry", () =>
        {
            Near(32, ToolingProfile.BoringBarDiameter);
            Near(40, ToolingProfile.BoringMinimumDiameter);
            Near(125, ToolingProfile.BoringOverhang);
            Near(128, ToolingProfile.BoringMaximumOverhang);
            Near(5, ToolingProfile.HolderFrontClearance);
            Near(128, ToolingProfile.BoringClampLength);
            Near(300, ToolingProfile.BoringFunctionalLength);
            Near(.4, ToolingProfile.NoseRadius);
            Near(.5, ToolingProfile.MaximumCornerRadius);
            var exact = Generated(Engine.Defaults with
            {
                Length = 118,
                LengthLowerDeviation = 0,
                LengthUpperDeviation = 0
            });
            Near(120, exact.Calculations.CutLength);
            Near(5, ToolingProfile.BoringOverhang - exact.Calculations.CutLength);
            Rejected(exact.Params with
            {
                Length = 118.001
            });
            Rejected(Engine.Defaults with
            {
                Length = 250
            });
            Rejected(Engine.Defaults with
            {
                BoreDiameter = 20,
                StockBore = 18,
                ClearanceX = .5
            });
            Rejected(Engine.Defaults with
            {
                GripLength = 24.999
            });
            Rejected(Engine.Defaults with
            {
                ChuckClearance = 9.999
            });
            Generated(Engine.Defaults with
            {
                StockBore = 44
            });
            Rejected(Engine.Defaults with
            {
                StockBore = 43.999
            });
        });

        Add("general class m tolerances cover the body diameter and flange width", () =>
        {
            Near(.3, ToolingProfile.GeneralLinearTolerance(100));
            Near(.2, ToolingProfile.GeneralLinearTolerance(12));
            Near(.2, ToolingProfile.GeneralLinearTolerance(30));
            Near(.3, ToolingProfile.GeneralLinearTolerance(30.001));
            Near(.3, ToolingProfile.GeneralLinearTolerance(120));
            Near(.5, ToolingProfile.GeneralLinearTolerance(120.001));
            var p = Generated();
            Near(100, p.Params.BodyDiameter);
            Near(12, p.Params.FlangeWidth);
            Check(
                p.TechnologyHtml.Contains("0.4", StringComparison.Ordinal) || p.TechnologyHtml.Contains("0,4", StringComparison.Ordinal),
                "operation data identify the selected nose radius"
            );
        });

        Add("maximum permitted corner radius leaves shoulder flats within all tolerance limits", () =>
        {
            Rejected(Engine.Defaults with
            {
                BodyDiameter = 117.83
            }, "bodyDiameter");
            Generated(Engine.Defaults with
            {
                BodyDiameter = 117.828
            });
            var shortBody = Engine.Defaults with
            {
                Length = 20,
                LengthLowerDeviation = 0,
                LengthUpperDeviation = 0,
                CounterboreDepth = 10,
                FlangeWidth = 19.3
            };
            Rejected(shortBody);
            Rejected(shortBody with
            {
                FlangeWidth = 19.299
            }, "length");
            Rejected(Engine.Defaults with
            {
                CounterboreDepth = 1,
                CounterboreDepthLowerDeviation = -.5,
                CounterboreDepthUpperDeviation = 0
            });
            Generated(Engine.Defaults with
            {
                CounterboreDepth = 1,
                CounterboreDepthLowerDeviation = -.499,
                CounterboreDepthUpperDeviation = 0
            });
        });

    }
}
