using System.Globalization;

using System.Text.Json;

using Kontur.Core;

namespace Kontur.Tests;

internal static partial class RegressionSuite
{
    private static void RegisterParameterValidationTests()
    {
        foreach (var f in Engine.Fields)
        {
            var field = f;
            foreach (var item in new[]
            {
                (Name: "below minimum", Value: f.Min - .001),
                (Name: "above maximum", Value: f.Max + .001),
                (Name: "NaN", Value: double.NaN),
                (Name: "positive infinity", Value: double.PositiveInfinity),
                (Name: "negative infinity", Value: double.NegativeInfinity),
                (Name: "excess decimal precision", Value: Value(Engine.Defaults, f.Key) + .00001)
            })
            {
                var invalid = item;
                Add(
                    "reject " + field.Key + " " + invalid.Name,
                    () => Rejected(Change(Engine.Defaults, field.Key, invalid.Value), field.Key)
                );
            }
        }

        Add("text metadata validates empty, null, long and control-character inputs", () =>
        {
            foreach (var text in new[] { "", "  ", new string('x', 101), "name\nnext", "name\0next" })
            {
                Rejected(Engine.Defaults with
                {
                    PartName = text
                }, "partName");
                Rejected(Engine.Defaults with
                {
                    Material = text
                }, "material");
            }
            Rejected(Engine.Defaults with
            {
                Material = null!
            }, "material");
            Generated(Engine.Defaults with
            {
                PartName = "Деталь Ω",
                Material = new string('m', 35)
            });
        });

        Add("single-line text boundaries reject overflow without trimming imported sources", () =>
        {
            foreach (var pair in new[]
            {
                (Part: new string('W', 45), Material: new string('W', 35)),
                (Part: new string('Ш', 45), Material: new string('Ш', 35)),
                (Part: "Втулка специальная ".PadRight(45, 'я'), Material: "Сталь конструкционная ".PadRight(35, 'я'))
            })
            {
                Equal(45, pair.Part.Length);
                Equal(35, pair.Material.Length);
                var project = Generated(Engine.Defaults with
                {
                    PartName = pair.Part,
                    Material = pair.Material
                });
                Equal(pair.Part, project.Params.PartName, "boundary part name is preserved without trimming");
                Equal(pair.Material, project.Params.Material, "boundary material is preserved without trimming");
            }
            Rejected(Engine.Defaults with
            {
                PartName = new string('W', 46)
            }, "partName");
            Rejected(Engine.Defaults with
            {
                Material = new string('W', 36)
            }, "material");
            Rejected(Engine.Defaults with
            {
                PartName = new string('W', 100)
            }, "partName");
            Rejected(Engine.Defaults with
            {
                Material = new string('W', 100)
            }, "material");
            foreach (var control in new[] { '\u007f', '\u2028', '\u2029' })
            {
                Rejected(Engine.Defaults with
                {
                    PartName = "Поле" + control + "продолжение"
                }, "partName");
                Rejected(Engine.Defaults with
                {
                    Material = "Поле" + control + "продолжение"
                }, "material");
            }
            using var temp = new TempWorkspace();
            foreach (var field in new[] { "partName", "material" })
            {
                var json = JsonSerializer.Serialize(new Dictionary<string, string> { [field] = new string('W', 100) });
                var path = temp.Write("too-long-" + field + ".json", json);
                var original = File.ReadAllBytes(path);
                Throws<InvalidDataException>(() => ProjectFiles.ImportParameters(path));
                Sequence(original, File.ReadAllBytes(path));
            }
        });

        Add("invalid geometry and machining settings produce field errors and no project", () =>
        {
            Rejected(Engine.Defaults with
            {
                FlangeWidth = 116
            }, "flangeWidth");
            Rejected(Engine.Defaults with
            {
                CounterboreDepth = 116
            }, "counterboreDepth");
            Rejected(Engine.Defaults with
            {
                BodyDiameter = 120
            }, "bodyDiameter");
            Rejected(Engine.Defaults with
            {
                BoreDiameter = 75
            }, "counterboreDiameter");
            Rejected(Engine.Defaults with
            {
                StockBore = 47.8
            }, "stockBore");
            Rejected(Engine.Defaults with
            {
                StockDiameter = 119.565
            }, "stockDiameter");
            Rejected(Engine.Defaults with
            {
                FinishAllowance = .5,
                DepthInside = .1
            }, "finishAllowance");
            Rejected(Engine.Defaults with
            {
                RpmMax = 1200.5
            }, "rpmMax");
            Rejected(Engine.Defaults with
            {
                FeedFinish = .25
            }, "feedFinish");
            Rejected(Engine.Defaults with
            {
                StockBore = 16,
                ClearanceX = 3
            }, "clearanceX");
            Rejected(Engine.Defaults with
            {
                StockDiameter = 240,
                DepthOutside = .1,
                FinishAllowance = .05
            }, "depthOutside");
        });

        Add("all five tolerance intervals reject reversed lower and upper limits", () =>
        {
            foreach (var prefix in new[] { "bore", "counterbore", "flange", "length", "counterboreDepth" })
            {
                var p = Change(Change(Engine.Defaults, prefix + "LowerDeviation", .2), prefix + "UpperDeviation", .1);
                Rejected(p, prefix + "UpperDeviation");
            }
        });

        Add("worst-case wall thickness accepts 3 mm and rejects 2.9995 mm", () =>
        {
            Generated(Engine.Defaults with
            {
                BodyDiameter = 82.04
            });
            Rejected(Engine.Defaults with
            {
                BodyDiameter = 82.039
            }, "counterboreDiameter");
            var confined = Engine.Defaults with
            {
                CounterboreDepth = 10,
                BodyDiameter = 54.6
            };
            Generated(confined);
            Rejected(confined with
            {
                BodyDiameter = 54.599
            }, "boreDiameter");
            var flangeWall = Engine.Defaults with
            {
                CounterboreDepth = 10,
                CounterboreDiameter = 112.39
            };
            Generated(flangeWall);
            Rejected(flangeWall with
            {
                CounterboreDiameter = 112.391
            }, "counterboreDiameter");
        });

        Add("counterbore topology uses maximum depth rather than midpoint depth", () =>
        {
            var confined = Engine.Defaults with
            {
                CounterboreDiameter = 110,
                CounterboreDepth = 11.8,
                CounterboreDepthLowerDeviation = 0,
                CounterboreDepthUpperDeviation = 0
            };
            Generated(confined);
            Rejected(confined with
            {
                CounterboreDepthUpperDeviation = .001
            }, "counterboreDiameter");
            Rejected(
                confined with
                {
                    CounterboreDepthLowerDeviation = -.18,
                    CounterboreDepthUpperDeviation = .18
                },
                "counterboreDiameter"
            );
        });

        Add("worst-case length and flange limits constrain topology", () =>
        {
            Rejected(Engine.Defaults with
            {
                Length = 29,
                LengthLowerDeviation = -1
            }, "counterboreDepth");
            Rejected(Engine.Defaults with
            {
                Length = 20,
                FlangeWidth = 19.5,
                CounterboreDepth = 10
            }, "flangeWidth");
            Rejected(Engine.Defaults with
            {
                BodyDiameter = 119.8
            }, "bodyDiameter");
        });

        Add("one-sided tolerance zones retain their signs and shift midpoint targets", () =>
        {
            var p = Generated(Engine.Defaults with
            {
                BoreLowerDeviation = 1,
                BoreUpperDeviation = 2,
                CounterboreLowerDeviation = -.8,
                CounterboreUpperDeviation = -.2,
                FlangeLowerDeviation = -1.5,
                FlangeUpperDeviation = -.5,
                LengthLowerDeviation = 1,
                LengthUpperDeviation = 2,
                CounterboreDepthLowerDeviation = -.4,
                CounterboreDepthUpperDeviation = -.2
            });
            Near(48.8, p.Calculations.TargetBoreDiameter);
            Near(74.5, p.Calculations.TargetCounterboreDiameter);
            Near(119, p.Calculations.TargetFlangeDiameter);
            Near(117.5, p.Calculations.TargetLength);
            Near(27.7, p.Calculations.TargetCounterboreDepth);
            TargetsWithinIntervals(p);
        });

        Add("zero tolerance widths and 0.001-mm intervals produce in-bound targets", () =>
        {
            TargetsWithinIntervals(Generated(ZeroTolerances(Engine.Defaults)));
            TargetsWithinIntervals(Generated(Engine.Defaults with
            {
                BoreLowerDeviation = .001,
                BoreUpperDeviation = .002
            }));
        });

        Add("half-micron midpoint ties round away from zero in decimal millimetres", () =>
        {
            // Fixed decimal examples found independently by auditing emitted NC/DOCX.
            var examples = new (double Lower, double Upper, double Expected)[]
            {
                (.002, .003, 47.303), (.005, .006, 47.306),
                (.008, .009, 47.309), (.011, .012, 47.312),
                (.014, .015, 47.315), (.017, .018, 47.318),
                (.020, .021, 47.321), (.023, .024, 47.324)
            };
            foreach (var example in examples)
            {
                var project = Generated(Engine.Defaults with
                {
                    BoreLowerDeviation = example.Lower,
                    BoreUpperDeviation = example.Upper
                });
                Near(example.Expected, project.Calculations.TargetBoreDiameter);
                Check(
                    project.Nc.Contains("G1 X" + example.Expected.ToString("0.000", CultureInfo.InvariantCulture) + " ", StringComparison.Ordinal),
                    "NC reaches the exact rounded bore target"
                );
            }
        });

        Add("decimal length target agrees with the independently checked NC envelope", () =>
        {
            var project = Generated(Engine.Defaults with
            {
                Length = 60.002,
                LengthLowerDeviation = 0,
                LengthUpperDeviation = .001,
                CounterboreDepth = 10
            });
            Near(60.003, project.Calculations.TargetLength);
            Near(62.003, project.Calculations.CutLength);
            Check(project.Nc.Contains("Z-62.003", StringComparison.Ordinal), "NC reaches the correct decimal length boundary");
            var verified = NcVerifier.Verify(project.Nc, project.Params);
            Check(verified.Ok, string.Join("; ", verified.Errors));
        });

        Add("sub-micron deviations are rejected while harmless binary noise is accepted", () =>
        {
            Rejected(Engine.Defaults with
            {
                BoreLowerDeviation = 1e-9
            }, "boreLowerDeviation");
            Rejected(Engine.Defaults with
            {
                BoreUpperDeviation = 2e-9
            }, "boreUpperDeviation");
            var narrow = Engine.Generate(Engine.Defaults with
            {
                BoreLowerDeviation = 1e-9,
                BoreUpperDeviation = 2e-9
            });
            Check(!narrow.Ok && narrow.Project is null, "an unrepresentable narrow tolerance interval must not produce NC");
            Check(
                narrow.Errors.Any(error => error.Field == "boreLowerDeviation") && narrow.Errors.Any(error => error.Field == "boreUpperDeviation"),
                "both overprecise deviations have diagnostics"
            );
            var harmless = Generated(Engine.Defaults with
            {
                BoreLowerDeviation = .1 + .2
            });
            Near(47.95, harmless.Calculations.TargetBoreDiameter);
        });

        Add("positive tolerance zones cannot hide contradictory nominal topology", () =>
        {
            Rejected(
                Engine.Defaults with
                {
                    FlangeDiameter = 98,
                    FlangeLowerDeviation = 3,
                    FlangeUpperDeviation = 4,
                    BodyDiameter = 100
                },
                "bodyDiameter"
            );
            Rejected(
                Engine.Defaults with
                {
                    Length = 20,
                    LengthLowerDeviation = 3,
                    LengthUpperDeviation = 4,
                    FlangeWidth = 22,
                    CounterboreDepth = 10
                },
                "flangeWidth"
            );
            Generated(Engine.Defaults with
            {
                FlangeLowerDeviation = 1,
                FlangeUpperDeviation = 2,
                LengthLowerDeviation = 1,
                LengthUpperDeviation = 2
            });
        });

    }
}
