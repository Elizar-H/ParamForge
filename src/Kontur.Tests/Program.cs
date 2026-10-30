using System.Diagnostics;
using System.Globalization;
using System.IO.Compression;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using Kontur.Core;

namespace Kontur.Tests;

internal static partial class Program
{
    private static readonly List<(string Name, Action Body)> Tests = [];
    private static readonly Dictionary<string, object?> Evidence = [];
    private static readonly JsonSerializerOptions ReportOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };
    private static int assertions;
    private static int sweepVariants;
    private static int docxFilesChecked;
    private static readonly string[] ExportNames = ["Маршрутная карта.docx", "Технологическая карта.docx", "Управляющая программа.nc"];

    public static int Main(string[] args)
    {
        Console.OutputEncoding = new UTF8Encoding(false);
        string? reportPath = null, exportPath = null, inputPath = null;
        try
        {
            for (var i = 0; i < args.Length; i++)
            {
                if (args[i] is "--help" or "-h")
                {
                    Console.WriteLine("Kontur.Tests [--report <json>] [--export <directory> [--input <parameters.json>]]");
                    return 0;
                }
                if (args[i] is not ("--report" or "--export" or "--input" or "--inputfile") || i + 1 >= args.Length)
                    throw new ArgumentException("Unknown option or missing value: " + args[i]);
                var option = args[i++];
                switch (option)
                {
                    case "--report": reportPath = Path.GetFullPath(args[i]); break;
                    case "--export": exportPath = Path.GetFullPath(args[i]); break;
                    default: inputPath = Path.GetFullPath(args[i]); break;
                }
            }
            if (inputPath is not null && exportPath is null)
                throw new ArgumentException("--input requires --export.");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }

        RegisterTests();
        if (exportPath is not null)
            Add("CLI exports the requested reproducible project", () => ExportRequested(exportPath, inputPath));
        var started = DateTimeOffset.UtcNow;
        var timer = Stopwatch.StartNew();
        var results = new List<TestResult>();
        foreach (var test in Tests)
        {
            var before = assertions;
            var testTimer = Stopwatch.StartNew();
            try
            {
                test.Body();
                results.Add(new(test.Name, true, assertions - before, testTimer.Elapsed.TotalMilliseconds, null));
            }
            catch (Exception ex)
            {
                results.Add(new(test.Name, false, assertions - before, testTimer.Elapsed.TotalMilliseconds, ex.Message));
                Console.Error.WriteLine("FAIL " + test.Name + ": " + ex.Message);
            }
        }
        var failed = results.Count(t => !t.Passed);
        var report = new
        {
            schemaVersion = 1,
            suite = "Kontur.Tests",
            engineVersion = Engine.Version,
            framework = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
            startedUtc = started,
            durationMilliseconds = timer.Elapsed.TotalMilliseconds,
            passed = failed == 0,
            totalTests = results.Count,
            passedTests = results.Count - failed,
            failedTests = failed,
            totalAssertions = assertions,
            validSweepVariants = sweepVariants,
            structurallyCheckedDocxFiles = docxFilesChecked,
            docxVisualValidation = "Not performed by this test runner; XML/ZIP checks do not validate page layout.",
            productReadiness = "Not determined by this runner. Word page comparison and GUI review are separate requirements.",
            evidence = Evidence,
            limitations = new[]
            {
                "Static verification of the generated AxiOMA Control command subset and ideal point-tool coordinate envelope.",
                "No collision, stock removal, tool geometry, fixture, controller execution or machining accuracy simulation.",
                "G1 time excludes rapids, acceleration, tool changes and manual operations."
            },
            tests = results
        };
        var json = JsonSerializer.Serialize(report, ReportOptions);
        if (reportPath is not null)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(reportPath)!);
                File.WriteAllText(reportPath, json + "\n", new UTF8Encoding(false));
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("Cannot write report: " + ex.Message);
                Console.WriteLine(json);
                return 1;
            }
        }
        Console.WriteLine(json);
        Console.Error.WriteLine(failed == 0
            ? "Regression and structural checks passed. Word page layout and GUI review remain separate."
            : $"Regression/structural checks have {failed} failure(s). See the JSON report.");
        return failed == 0 ? 0 : 1;
    }

    private static void RegisterTests()
    {
        RegisterV24Tests();
        RegisterV25Tests();
        Add("field schema exposes exactly 32 numeric parameters", () =>
        {
            Equal(32, Engine.Fields.Count, "numeric field count");
            Equal(32, Engine.Fields.Select(f => f.Key).Distinct(StringComparer.Ordinal).Count(), "unique field keys");
            var numericProperties = typeof(Parameters).GetProperties().Where(p => p.PropertyType == typeof(double)).ToArray();
            Equal(32, numericProperties.Length, "numeric property count");
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
            Near(47.3, p.Params.BoreDiameter); Near(45, p.Params.StockBore);
            Near(1, p.Params.BoreUpperDeviation); Near(.74, p.Params.CounterboreUpperDeviation);
            Near(-.87, p.Params.FlangeLowerDeviation); Near(-1, p.Params.LengthLowerDeviation);
            Near(-.18, p.Params.CounterboreDepthLowerDeviation); Near(.18, p.Params.CounterboreDepthUpperDeviation);
            Near(47.8, c.TargetBoreDiameter); Near(75.37, c.TargetCounterboreDiameter);
            Near(119.565, c.TargetFlangeDiameter); Near(115.5, c.TargetLength); Near(28, c.TargetCounterboreDepth);
            Near(117.5, c.CutLength); Near(155.5, c.StockLength); Near(157.5, c.RawStockLength); Near(128, c.ClearOut); Near(41, c.ClearIn);
            Sequence(new[] { "005", "010", "020", "030" }, p.Operations.Select(o => o.Code));
            Sequence(new[] { 2, 1, 6, 1, 2, 1, 14, 1 }, p.Transitions.Select(t => t.Passes.Count));
            Near(119.565, p.Transitions[1].Diameter); Near(47.8, p.Transitions[5].Diameter); Near(75.37, p.Transitions[7].Diameter);
            Near(-117.5, p.Transitions[2].ZEnd); Near(-117.5, p.Transitions[5].ZEnd); Near(-28, p.Transitions[7].ZEnd);
            foreach (var text in new[] { p.RouteHtml, p.TechnologyHtml, p.Nc }) Check(text.Contains(p.Id, StringComparison.Ordinal), "revision linkage");
            foreach (var t in p.Transitions)
            {
                Check(p.Nc.Contains("(TR " + t.Id + " ", StringComparison.Ordinal), "NC transition " + t.Id);
                Check(p.TechnologyHtml.Contains(t.Id, StringComparison.Ordinal), "card transition " + t.Id);
                foreach (var n in t.Blocks) Check(p.Nc.Contains("N" + n + " ", StringComparison.Ordinal), "linked NC block");
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
                Add("reject " + field.Key + " " + invalid.Name, () => Rejected(Change(Engine.Defaults, field.Key, invalid.Value), field.Key));
            }
        }
        Add("text metadata validates empty, null, long and control-character inputs", () =>
        {
            foreach (var text in new[] { "", "  ", new string('x', 101), "name\nnext", "name\0next" })
            {
                Rejected(Engine.Defaults with { PartName = text }, "partName");
                Rejected(Engine.Defaults with { Material = text }, "material");
            }
            Rejected(Engine.Defaults with { Material = null! }, "material");
            Generated(Engine.Defaults with { PartName = "Деталь Ω", Material = new string('m', 35) });
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
                Equal(45, pair.Part.Length); Equal(35, pair.Material.Length);
                var project = Generated(Engine.Defaults with { PartName = pair.Part, Material = pair.Material });
                Equal(pair.Part, project.Params.PartName, "boundary part name is preserved without trimming");
                Equal(pair.Material, project.Params.Material, "boundary material is preserved without trimming");
            }
            Rejected(Engine.Defaults with { PartName = new string('W', 46) }, "partName");
            Rejected(Engine.Defaults with { Material = new string('W', 36) }, "material");
            Rejected(Engine.Defaults with { PartName = new string('W', 100) }, "partName");
            Rejected(Engine.Defaults with { Material = new string('W', 100) }, "material");
            foreach (var control in new[] { '\u007f', '\u2028', '\u2029' })
            {
                Rejected(Engine.Defaults with { PartName = "Поле" + control + "продолжение" }, "partName");
                Rejected(Engine.Defaults with { Material = "Поле" + control + "продолжение" }, "material");
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
            Rejected(Engine.Defaults with { FlangeWidth = 116 }, "flangeWidth");
            Rejected(Engine.Defaults with { CounterboreDepth = 116 }, "counterboreDepth");
            Rejected(Engine.Defaults with { BodyDiameter = 120 }, "bodyDiameter");
            Rejected(Engine.Defaults with { BoreDiameter = 75 }, "counterboreDiameter");
            Rejected(Engine.Defaults with { StockBore = 47.8 }, "stockBore");
            Rejected(Engine.Defaults with { StockDiameter = 119.565 }, "stockDiameter");
            Rejected(Engine.Defaults with { FinishAllowance = .5, DepthInside = .1 }, "finishAllowance");
            Rejected(Engine.Defaults with { RpmMax = 1200.5 }, "rpmMax");
            Rejected(Engine.Defaults with { FeedFinish = .25 }, "feedFinish");
            Rejected(Engine.Defaults with { StockBore = 16, ClearanceX = 3 }, "clearanceX");
            Rejected(Engine.Defaults with { StockDiameter = 240, DepthOutside = .1, FinishAllowance = .05 }, "depthOutside");
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
            Generated(Engine.Defaults with { BodyDiameter = 82.04 });
            Rejected(Engine.Defaults with { BodyDiameter = 82.039 }, "counterboreDiameter");
            var confined = Engine.Defaults with { CounterboreDepth = 10, BodyDiameter = 54.6 };
            Generated(confined);
            Rejected(confined with { BodyDiameter = 54.599 }, "boreDiameter");
            var flangeWall = Engine.Defaults with { CounterboreDepth = 10, CounterboreDiameter = 112.39 };
            Generated(flangeWall);
            Rejected(flangeWall with { CounterboreDiameter = 112.391 }, "counterboreDiameter");
        });
        Add("counterbore topology uses maximum depth rather than midpoint depth", () =>
        {
            var confined = Engine.Defaults with { CounterboreDiameter = 110, CounterboreDepth = 11.8, CounterboreDepthLowerDeviation = 0, CounterboreDepthUpperDeviation = 0 };
            Generated(confined);
            Rejected(confined with { CounterboreDepthUpperDeviation = .001 }, "counterboreDiameter");
            Rejected(confined with { CounterboreDepthLowerDeviation = -.18, CounterboreDepthUpperDeviation = .18 }, "counterboreDiameter");
        });
        Add("worst-case length and flange limits constrain topology", () =>
        {
            Rejected(Engine.Defaults with { Length = 29, LengthLowerDeviation = -1 }, "counterboreDepth");
            Rejected(Engine.Defaults with { Length = 20, FlangeWidth = 19.5, CounterboreDepth = 10 }, "flangeWidth");
            Rejected(Engine.Defaults with { BodyDiameter = 119.8 }, "bodyDiameter");
        });
        Add("one-sided tolerance zones retain their signs and shift midpoint targets", () =>
        {
            var p = Generated(Engine.Defaults with
            {
                BoreLowerDeviation = 1, BoreUpperDeviation = 2,
                CounterboreLowerDeviation = -.8, CounterboreUpperDeviation = -.2,
                FlangeLowerDeviation = -1.5, FlangeUpperDeviation = -.5,
                LengthLowerDeviation = 1, LengthUpperDeviation = 2,
                CounterboreDepthLowerDeviation = -.4, CounterboreDepthUpperDeviation = -.2
            });
            Near(48.8, p.Calculations.TargetBoreDiameter); Near(74.5, p.Calculations.TargetCounterboreDiameter);
            Near(119, p.Calculations.TargetFlangeDiameter); Near(117.5, p.Calculations.TargetLength);
            Near(27.7, p.Calculations.TargetCounterboreDepth);
            TargetsWithinIntervals(p);
        });
        Add("zero tolerance widths and 0.001-mm intervals produce in-bound targets", () =>
        {
            TargetsWithinIntervals(Generated(ZeroTolerances(Engine.Defaults)));
            TargetsWithinIntervals(Generated(Engine.Defaults with { BoreLowerDeviation = .001, BoreUpperDeviation = .002 }));
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
                var project = Generated(Engine.Defaults with { BoreLowerDeviation = example.Lower, BoreUpperDeviation = example.Upper });
                Near(example.Expected, project.Calculations.TargetBoreDiameter);
                Check(project.Nc.Contains("G1 X" + example.Expected.ToString("0.000", CultureInfo.InvariantCulture) + " ", StringComparison.Ordinal), "NC reaches the exact rounded bore target");
            }
        });
        Add("decimal length target agrees with the independently checked NC envelope", () =>
        {
            var project = Generated(Engine.Defaults with
            {
                Length = 60.002, LengthLowerDeviation = 0, LengthUpperDeviation = .001,
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
            Rejected(Engine.Defaults with { BoreLowerDeviation = 1e-9 }, "boreLowerDeviation");
            Rejected(Engine.Defaults with { BoreUpperDeviation = 2e-9 }, "boreUpperDeviation");
            var narrow = Engine.Generate(Engine.Defaults with { BoreLowerDeviation = 1e-9, BoreUpperDeviation = 2e-9 });
            Check(!narrow.Ok && narrow.Project is null, "an unrepresentable narrow tolerance interval must not produce NC");
            Check(narrow.Errors.Any(error => error.Field == "boreLowerDeviation") && narrow.Errors.Any(error => error.Field == "boreUpperDeviation"), "both overprecise deviations have diagnostics");
            var harmless = Generated(Engine.Defaults with { BoreLowerDeviation = .1 + .2 });
            Near(47.95, harmless.Calculations.TargetBoreDiameter);
        });
        Add("positive tolerance zones cannot hide contradictory nominal topology", () =>
        {
            Rejected(Engine.Defaults with { FlangeDiameter = 98, FlangeLowerDeviation = 3, FlangeUpperDeviation = 4, BodyDiameter = 100 }, "bodyDiameter");
            Rejected(Engine.Defaults with { Length = 20, LengthLowerDeviation = 3, LengthUpperDeviation = 4, FlangeWidth = 22, CounterboreDepth = 10 }, "flangeWidth");
            Generated(Engine.Defaults with { FlangeLowerDeviation = 1, FlangeUpperDeviation = 2, LengthLowerDeviation = 1, LengthUpperDeviation = 2 });
        });
        Add("generation is deterministic and revisions respond to each input", () =>
        {
            var a = Generated(); var b = Generated(Engine.Defaults);
            Equal(JsonSerializer.Serialize(a), JsonSerializer.Serialize(b), "full project determinism");
            foreach (var f in Engine.Fields)
            {
                var changed = Generated(Change(Engine.Defaults, f.Key, Value(Engine.Defaults, f.Key) + (f.Key == "rpmMax" ? 1 : .001)));
                Check(a.Id != changed.Id, "revision responds to " + f.Key);
            }
            Check(a.Id != Generated(Engine.Defaults with { PartName = "Other part" }).Id, "name revision");
            Check(a.Id != Generated(Engine.Defaults with { Material = "Other material" }).Id, "material revision");
            Equal(a.Id, Generated().Id, "later generation does not mutate defaults");
        });
        Add("length and counterbore depth edits propagate to cuts, NC, cards and time", () =>
        {
            var a = Generated(); var b = Generated(Engine.Defaults with { Length = 100 });
            Near(139.5, b.Calculations.StockLength); Near(-101.5, b.Transitions[2].ZEnd);
            Check(b.Nc.Contains("Z-101.5", StringComparison.Ordinal), "NC length");
            Check(b.RouteHtml.Contains("100", StringComparison.Ordinal), "route length");
            Check(b.TechnologyHtml.Contains("101.5", StringComparison.Ordinal), "technology cut length");
            Check(b.Calculations.TotalCutMinutes < a.Calculations.TotalCutMinutes, "shorter cuts take less time");
            var deep = Generated(Engine.Defaults with { CounterboreDepth = 40 });
            Near(-40, deep.Transitions[7].ZEnd); Near(-117.5, deep.Transitions[5].ZEnd);
        });
        Add("prepared and raw stock lengths conserve each independent axial allowance", () =>
        {
            var project = Generated(); var p = project.Params; var c = project.Calculations;
            Near(2, p.TrimAllowance); Near(3, c.CutoffWidth); Near(10, p.ChuckClearance);
            Near(25, p.GripLength); Near(2, p.FrontFacingAllowance);
            Near(6, ToolingProfile.CutoffHolderAxialProjection);
            Near(4, ToolingProfile.ChuckRequiredResidualClearance);
            Near(ToolingProfile.ChuckMinimumClearance, p.ChuckClearance);
            Check(p.ChuckClearance - ToolingProfile.CutoffHolderAxialProjection >= ToolingProfile.ChuckRequiredResidualClearance,
                "10-mm axial clearance retains 4 mm beyond the selected 6-mm project holder envelope");
            Near(117.5, c.CutLength); Near(-117.5, c.PartingFrontZ);
            Near(-120.5, c.PartingBackZ); Near(-130.5, c.JawsFrontZ);
            Near(155.5, c.StockLength); Near(157.5, c.RawStockLength);
            Near(c.TargetLength + p.TrimAllowance, c.CutLength);
            Near(c.CutLength + c.CutoffWidth + p.ChuckClearance + p.GripLength, c.StockLength);
            Near(c.StockLength + p.FrontFacingAllowance, c.RawStockLength);
            Near(c.CutoffWidth, c.PartingFrontZ - c.PartingBackZ);
            Near(p.ChuckClearance, c.PartingBackZ - c.JawsFrontZ);
            Near(p.GripLength, c.StockLength + c.JawsFrontZ);
            Check(c.PartingFrontZ > c.PartingBackZ && c.PartingBackZ > c.JawsFrontZ, "kerf is outside the jaw front");
            Evidence["axialStockBudget"] = new
            {
                c.TargetLength, p.TrimAllowance, c.CutoffWidth, p.ChuckClearance, p.GripLength,
                p.FrontFacingAllowance, c.CutLength, c.StockLength, c.RawStockLength,
                c.PartingFrontZ, c.PartingBackZ, c.JawsFrontZ
            };
        });
        Add("rear trim is counted once while the right-face step references remain unchanged", () =>
        {
            var first = Generated();
            var moreTrim = Generated(Engine.Defaults with { TrimAllowance = 3 });
            Near(first.Calculations.TargetLength, moreTrim.Calculations.TargetLength);
            Near(first.Calculations.CutLength + 1, moreTrim.Calculations.CutLength);
            Near(first.Calculations.StockLength + 1, moreTrim.Calculations.StockLength);
            Near(first.Calculations.RawStockLength + 1, moreTrim.Calculations.RawStockLength);
            Near(first.Calculations.PartingFrontZ - 1, moreTrim.Calculations.PartingFrontZ);
            Near(first.Calculations.PartingBackZ - 1, moreTrim.Calculations.PartingBackZ);
            Near(first.Calculations.JawsFrontZ - 1, moreTrim.Calculations.JawsFrontZ);
            foreach (var id in new[] { "010.01", "010.02", "010.03", "010.04", "010.07", "010.08" })
            {
                var a = first.Transitions.Single(t => t.Id == id); var b = moreTrim.Transitions.Single(t => t.Id == id);
                Near(a.ZStart, b.ZStart);
                if (id is "010.01" or "010.02" or "010.07" or "010.08") Near(a.ZEnd, b.ZEnd);
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
                Equal(first.Nc, changed.Nc.Replace(changed.Id, first.Id, StringComparison.Ordinal), "non-cutting stock allowances do not move the cutting path");
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
                var p = project.Params; var c = project.Calculations;
                var expectedWidthTolerance = p.FlangeWidth <= 30 ? .2m : .3m;
                var expectedStart = (decimal)p.FlangeWidth + expectedWidthTolerance + 2m;
                Near((double)expectedStart, c.SecondClampStart);
                Near((double)(expectedStart + 25m), c.SecondClampEnd);
                Near((double)((decimal)p.FlangeDiameter + (decimal)p.FlangeUpperDeviation + 1m), c.SoftJawPocketDiameter);
                Near((double)((decimal)p.FlangeWidth + expectedWidthTolerance + 1m), c.SoftJawPocketDepth);
                Check(p.Length + p.LengthLowerDeviation - c.SecondClampEnd >= 5 - 1e-8, "minimum final length leaves the required facing gap beyond the clamping belt");
            }
            var moreRearTrim = Generated(Engine.Defaults with { TrimAllowance = 3 });
            Near(baseline.Calculations.SecondClampStart, moreRearTrim.Calculations.SecondClampStart);
            Near(baseline.Calculations.SecondClampEnd, moreRearTrim.Calculations.SecondClampEnd);
            Near(baseline.Calculations.SoftJawPocketDiameter, moreRearTrim.Calculations.SoftJawPocketDiameter);
            Near(baseline.Calculations.SoftJawPocketDepth, moreRearTrim.Calculations.SoftJawPocketDepth);
            Evidence["secondSetupGeometry"] = new
            {
                baseline.Calculations.SecondClampStart, baseline.Calculations.SecondClampEnd,
                baseline.Calculations.SoftJawPocketDiameter, baseline.Calculations.SoftJawPocketDepth
            };
        });
        Add("second-setup clamping rejects a short part and checks the minimum tolerance limit", () =>
        {
            var shortPart = Engine.Defaults with
            {
                Length = 20, LengthLowerDeviation = 0, LengthUpperDeviation = 0,
                FlangeWidth = 2, CounterboreDepth = 1,
                CounterboreDepthLowerDeviation = 0, CounterboreDepthUpperDeviation = 0
            };
            var rejected = Engine.Generate(shortPart);
            Check(!rejected.Ok && rejected.Project is null, "short but drawable geometry must not produce this process");
            Check(rejected.Errors.Any(error => error.Field == "length" && error.Message.Contains("второго установа", StringComparison.Ordinal)), "rejection identifies the unsupported second setup");
            var exact = Generated(Engine.Defaults with { Length = 44.2, LengthLowerDeviation = 0, LengthUpperDeviation = 0 });
            Near(5, exact.Params.Length - exact.Calculations.SecondClampEnd);
            Rejected(exact.Params with { Length = 44.199 }, "length");
            Rejected(exact.Params with { LengthLowerDeviation = -.001 }, "length");
        });
        Add("the installed boring bar limits process depth and minimum entry geometry", () =>
        {
            Near(32, ToolingProfile.BoringBarDiameter); Near(40, ToolingProfile.BoringMinimumDiameter);
            Near(125, ToolingProfile.BoringOverhang); Near(128, ToolingProfile.BoringMaximumOverhang);
            Near(5, ToolingProfile.HolderFrontClearance);
            Near(128, ToolingProfile.BoringClampLength); Near(300, ToolingProfile.BoringFunctionalLength);
            Near(.4, ToolingProfile.NoseRadius); Near(.5, ToolingProfile.MaximumCornerRadius);
            var exact = Generated(Engine.Defaults with { Length = 118, LengthLowerDeviation = 0, LengthUpperDeviation = 0 });
            Near(120, exact.Calculations.CutLength);
            Near(5, ToolingProfile.BoringOverhang - exact.Calculations.CutLength);
            Rejected(exact.Params with { Length = 118.001 });
            Rejected(Engine.Defaults with { Length = 250 });
            Rejected(Engine.Defaults with { BoreDiameter = 20, StockBore = 18, ClearanceX = .5 });
            Rejected(Engine.Defaults with { GripLength = 24.999 });
            Rejected(Engine.Defaults with { ChuckClearance = 9.999 });
            Generated(Engine.Defaults with { StockBore = 44 });
            Rejected(Engine.Defaults with { StockBore = 43.999 });
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
            Near(100, p.Params.BodyDiameter); Near(12, p.Params.FlangeWidth);
            Check(p.TechnologyHtml.Contains("0.4", StringComparison.Ordinal) || p.TechnologyHtml.Contains("0,4", StringComparison.Ordinal), "operation data identify the selected nose radius");
        });
        Add("maximum permitted corner radius leaves shoulder flats within all tolerance limits", () =>
        {
            Rejected(Engine.Defaults with { BodyDiameter = 117.83 }, "bodyDiameter");
            Generated(Engine.Defaults with { BodyDiameter = 117.828 });
            var shortBody = Engine.Defaults with
            {
                Length = 20, LengthLowerDeviation = 0, LengthUpperDeviation = 0,
                CounterboreDepth = 10, FlangeWidth = 19.3
            };
            Rejected(shortBody);
            Rejected(shortBody with { FlangeWidth = 19.299 }, "length");
            Rejected(Engine.Defaults with { CounterboreDepth = 1, CounterboreDepthLowerDeviation = -.5, CounterboreDepthUpperDeviation = 0 });
            Generated(Engine.Defaults with { CounterboreDepth = 1, CounterboreDepthLowerDeviation = -.499, CounterboreDepthUpperDeviation = 0 });
        });
        Add("balanced pass allocation avoids the previous tiny final roughing cut", () =>
        {
            var flange = Generated().Transitions.Single(t => t.Id == "010.01");
            Sequence(new[] { 121.983, 119.965 }, flange.Passes);
            var previous = flange.StartDiameter;
            var depths = new List<double>();
            foreach (var diameter in flange.Passes) { depths.Add((previous - diameter) / 2); previous = diameter; }
            Near(1.0085, depths[0]); Near(1.009, depths[1]);
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
            Check(p.Transitions[0].ActualMaximumDepth != p.Transitions[0].Depth, "operation-card t must not silently equal the requested 2-mm limit");
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
                    var expected = (double)Math.Round((decimal)sample.Start + ((decimal)sample.Target - (decimal)sample.Start) * (i + 1) / count, 3, MidpointRounding.AwayFromZero);
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
            var p = Generated(Engine.Defaults with { PartName = payload, Material = payload });
            foreach (var html in new[] { p.RouteHtml, p.TechnologyHtml })
            {
                Check(!html.Contains(payload, StringComparison.Ordinal), "raw payload excluded");
                Check(html.Contains("&lt;img", StringComparison.Ordinal) && html.Contains("&amp;", StringComparison.Ordinal), "HTML escaping");
            }
            Check(!p.Nc.Contains(payload, StringComparison.Ordinal), "metadata excluded from NC");
            Check(p.Nc.All(ch => ch <= 127), "NC ASCII");
        });
        Add("spindle cap and requested surface-speed bound hold for every transition", () =>
        {
            foreach (var p in new[] { Generated(), Generated(Engine.Defaults with { CuttingSpeed = 150, RpmMax = 100 }) })
                foreach (var t in p.Transitions)
                {
                    Check(t.Rpm > 0 && t.Rpm <= p.Params.RpmMax && Math.Floor(t.Rpm) == t.Rpm, "integer capped rpm");
                    Check(Math.PI * t.ReferenceDiameter * t.Rpm / 1000 <= p.Params.CuttingSpeed + 1e-9, "surface-speed cap");
                }
        });
        Add("actual pass diameters meet radial depth caps and exact final targets", () =>
        {
            foreach (var p in new[] { Generated(), Generated(Engine.Defaults with { DepthOutside = .7, DepthInside = .3, FinishAllowance = .1 }) })
                CheckPasses(p);
        });
        Add("independent NC reading reconstructs generated coordinates and G1 time", () =>
        {
            foreach (var p in new[] { Generated(), Generated(Engine.Defaults with { Length = 100 }), Generated(Engine.Defaults with { BodyDiameter = 95, DepthOutside = 1 }), Generated(Engine.Defaults with { CounterboreDepth = 40 }) })
                CheckNcAgreement(p);
        });
        Add("80 valid deterministic variants retain targets, pass caps and NC agreement", () =>
        {
            for (var i = 0; i < 80; i++)
            {
                var p = Generated(Engine.Defaults with
                {
                    Length = 60 + i % 59, FlangeWidth = 8 + i % 8, CounterboreDepth = 18 + i % 30,
                    BodyDiameter = 90 + i % 15, CuttingSpeed = 40 + i,
                    DepthOutside = .5 + i % 5 * .5, DepthInside = .3 + i % 3 * .3, FinishAllowance = .1
                });
                TargetsWithinIntervals(p); CheckPasses(p); CheckNcAgreement(p);
                sweepVariants++;
            }
        });
        RegisterVerifierTests();
        RegisterFileTests();
    }

    private static void Add(string name, Action body) => Tests.Add((name, body));
    private static void Check(bool condition, string message)
    {
        assertions++;
        if (!condition) throw new InvalidOperationException(message);
    }
    private static void Equal<T>(T expected, T actual, string message = "equality") =>
        Check(EqualityComparer<T>.Default.Equals(expected, actual), $"{message}: expected {expected}, got {actual}");
    private static void Near(double expected, double actual, double epsilon = 1e-9) =>
        Check(double.IsFinite(actual) && Math.Abs(expected - actual) <= epsilon, $"expected {expected:R} ± {epsilon:R}, got {actual:R}");
    private static void Sequence<T>(IEnumerable<T> expected, IEnumerable<T> actual) =>
        Check(expected.SequenceEqual(actual), "sequence differs: expected [" + string.Join(",", expected) + "], got [" + string.Join(",", actual) + "]");
    private static PropertyInfo ParameterProperty(string key) => typeof(Parameters).GetProperty(key, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase)
        ?? throw new InvalidOperationException("Missing parameter property: " + key);
    private static double Value(Parameters p, string key) => (double)ParameterProperty(key).GetValue(p)!;
    private static Parameters Change(Parameters p, string key, double value)
    {
        var changed = p with { };
        ParameterProperty(key).SetValue(changed, value);
        return changed;
    }
    private static Project Generated(Parameters? parameters = null)
    {
        var result = Engine.Generate(parameters);
        Check(result.Ok, "generation failed: " + string.Join("; ", result.Errors.Select(e => e.Field + ": " + e.Message)));
        Check(result.Project is not null, "successful generation must return project");
        return result.Project!;
    }
    private static void Rejected(Parameters p, string? field = null)
    {
        var result = Engine.Generate(p);
        Check(!result.Ok, "invalid input was accepted");
        Check(result.Project is null, "invalid input must not return project");
        Check(result.Errors.Count > 0, "invalid input needs diagnostics");
        if (field is not null) Check(result.Errors.Any(e => e.Field == field && e.Message.Length > 0), "missing diagnostic field " + field + ": " + string.Join("; ", result.Errors.Select(e => e.Field)));
    }
    private static Parameters ZeroTolerances(Parameters p) => p with
    {
        BoreLowerDeviation = 0, BoreUpperDeviation = 0, CounterboreLowerDeviation = 0, CounterboreUpperDeviation = 0,
        FlangeLowerDeviation = 0, FlangeUpperDeviation = 0, LengthLowerDeviation = 0, LengthUpperDeviation = 0,
        CounterboreDepthLowerDeviation = 0, CounterboreDepthUpperDeviation = 0
    };
    private static void TargetsWithinIntervals(Project project)
    {
        var p = project.Params; var c = project.Calculations;
        foreach (var item in new[]
        {
            (c.TargetBoreDiameter, p.BoreDiameter + p.BoreLowerDeviation, p.BoreDiameter + p.BoreUpperDeviation),
            (c.TargetCounterboreDiameter, p.CounterboreDiameter + p.CounterboreLowerDeviation, p.CounterboreDiameter + p.CounterboreUpperDeviation),
            (c.TargetFlangeDiameter, p.FlangeDiameter + p.FlangeLowerDeviation, p.FlangeDiameter + p.FlangeUpperDeviation),
            (c.TargetLength, p.Length + p.LengthLowerDeviation, p.Length + p.LengthUpperDeviation),
            (c.TargetCounterboreDepth, p.CounterboreDepth + p.CounterboreDepthLowerDeviation, p.CounterboreDepth + p.CounterboreDepthUpperDeviation)
        }) Check(item.Item1 >= item.Item2 - 1e-9 && item.Item1 <= item.Item3 + 1e-9, "target outside tolerance interval");
    }
    private static void CheckPasses(Project p)
    {
        foreach (var t in p.Transitions)
        {
            Check(t.Passes.Count > 0, "empty transition " + t.Id);
            var previous = t.StartDiameter;
            var direction = Math.Sign(t.Diameter - previous);
            var count = (int)decimal.Ceiling(Math.Abs((decimal)t.Diameter - (decimal)t.StartDiameter) / (2m * (decimal)t.Depth));
            Equal(count, t.Passes.Count, "minimum balanced pass count " + t.Id);
            var workingDepths = new List<double>();
            foreach (var d in t.Passes)
            {
                workingDepths.Add(Math.Abs(d - previous) / 2);
                Check(Math.Abs(d - previous) / 2 <= t.Depth + .000501, "radial depth cap including coordinate rounding " + t.Id);
                Check(Math.Sign(d - previous) == direction, "monotonic pass " + t.Id);
                previous = d;
            }
            Near(t.Diameter, previous);
            Near(IndependentMaximumDepth(t), t.ActualMaximumDepth);
            Check(workingDepths.Max() - workingDepths.Min() <= .001001, "balanced working depths avoid a tiny residual " + t.Id);
        }
    }
    private static double IndependentMaximumDepth(Transition transition)
    {
        var previous = (decimal)transition.StartDiameter;
        decimal maximum = 0;
        foreach (var pass in transition.Passes)
        {
            var current = (decimal)pass;
            maximum = Math.Max(maximum, Math.Abs(current - previous) / 2m);
            previous = current;
        }
        return (double)maximum;
    }
    private static void CheckNcAgreement(Project p)
    {
        var verified = NcVerifier.Verify(p.Nc, p.Params);
        Check(verified.Ok, string.Join("; ", verified.Errors));
        Equal(p.Moves.Count, verified.Moves.Count, "verifier move count");
        var read = ReadGeneratedNc(p.Nc);
        Equal(p.Moves.Count, read.Moves.Count, "independent move count");
        for (var i = 0; i < p.Moves.Count; i++)
        {
            var expected = p.Moves[i]; var actual = read.Moves[i]; var checkedMove = verified.Moves[i];
            Near(expected.X, actual.X); Near(expected.Z, actual.Z); Equal(expected.Mode, actual.Mode); Equal(expected.Line, actual.Line);
            Near(actual.X, checkedMove.X); Near(actual.Z, checkedMove.Z); Equal(actual.Mode, checkedMove.Mode); Equal(actual.Line, checkedMove.Line);
        }
        Near(p.Calculations.TotalCutMinutes, read.Minutes, 1e-8);
        Near(verified.CutMinutes, read.Minutes, 1e-8);
        Near(p.Transitions.Sum(t => t.CutMinutes), read.Minutes, 1e-8);
    }
    // A small test-side reader: no Engine, NcVerifier or Move timing implementation is called here.
    // It reads explicit G0/G1 and S/F words from emitted NC and integrates distances in physical millimetres.
    private static NcRead ReadGeneratedNc(string nc)
    {
        var moves = new List<ReadMove>();
        (double X, double Z)? previous = null;
        double rpm = 0, minutes = 0;
        foreach (var raw in nc.Split('\n'))
        {
            var line = Regex.Replace(raw, @"\([^()]*\)", "").Split(';')[0].Trim();
            if (line.Length == 0 || line.StartsWith("%", StringComparison.Ordinal)) continue;
            var words = Regex.Matches(line.ToUpperInvariant(), @"([A-Z])([+-]?(?:\d+(?:\.\d*)?|\.\d+))")
                .Select(m => (Letter: m.Groups[1].Value, Number: double.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture))).ToArray();
            if (words.Any(w => w.Letter == "T")) previous = null;
            var speed = words.FirstOrDefault(w => w.Letter == "S");
            if (speed.Letter is not null) rpm = speed.Number;
            var motion = words.FirstOrDefault(w => w.Letter == "G" && w.Number is 0 or 1);
            if (motion.Letter is null) continue;
            var x = words.Single(w => w.Letter == "X").Number;
            var z = words.Single(w => w.Letter == "Z").Number;
            if (motion.Number == 1)
            {
                var feed = words.Single(w => w.Letter == "F").Number;
                if (previous is null || feed <= 0 || rpm <= 0) throw new InvalidOperationException("Incomplete G1 timing state");
                var radial = .5 * (x - previous.Value.X);
                var axial = z - previous.Value.Z;
                minutes += Math.Sqrt(radial * radial + axial * axial) / feed / rpm;
            }
            moves.Add(new(x, z, motion.Number == 0 ? "G0" : "G1", (int)words.Single(w => w.Letter == "N").Number));
            previous = (x, z);
        }
        return new(moves, minutes);
    }

    private static readonly string Fixture = """
        % TEST_PROFILE
        (POINT-TOOL CHECK FIXTURE)
        N10 G21 G18 G90 G94 G97 G40 G49 G80 G54
        N11 G10 X1
        N12 M5
        N13 M9
        N20 T1 D1 M66
        N21 G43
        N22 M8
        N30 S100 M3
        N31 G95
        N40 G0 X128 Z3
        N50 G0 X120 Z3
        N60 G1 X120 Z-12 F0.2
        N70 G1 X124 Z-12 F0.2
        N75 G94
        N78 M9
        N80 M5
        N90 M30
        """.ReplaceLineEndings("\r\n");

    private static void RegisterVerifierTests()
    {
        Add("analytic AxiOMA fixture gives 15 mm axial plus 2 mm radial = 0.85 minutes", () =>
        {
            var v = NcVerifier.Verify(Fixture, Engine.Defaults);
            Check(v.Ok, string.Join("; ", v.Errors)); Equal(4, v.Moves.Count);
            Near(.85, v.CutMinutes, 1e-12); Near(.85, ReadGeneratedNc(Fixture).Minutes, 1e-12);
            Sequence(new[] { 40, 50, 60, 70 }, v.Moves.Select(m => m.Line));
            Check(v.Moves.All(move => move.Line != 11), "G10 X1 configures diameter mode rather than moving to X1");
        });
        Add("lowercase compact AxiOMA NC and ASCII comments are supported", () =>
        {
            var compact = Fixture.ToLowerInvariant().Replace("n60 g1 x120 z-12 f0.2", "n60g1x120z-12f.2 (cut)", StringComparison.Ordinal);
            Check(NcVerifier.Verify(compact, Engine.Defaults).Ok, "compact lowercase NC");
            foreach (var comment in new[] { "; cut", "// cut" })
            {
                var commented = Fixture.Replace("N60 G1 X120 Z-12 F0.2", "N60 G1 X120 Z-12 F0.2 " + comment, StringComparison.Ordinal);
                var result = NcVerifier.Verify(commented, Engine.Defaults);
                Check(result.Ok, "official ASCII line comment: " + string.Join("; ", result.Errors));
                Near(.85, result.CutMinutes, 1e-12);
            }
        });
        Add("manual and automatic tool changes both select an explicit tool edge", () =>
        {
            var automatic = Fixture.Replace("T1 D1 M66", "T1 D1 M6", StringComparison.Ordinal);
            var v = NcVerifier.Verify(automatic, Engine.Defaults);
            Check(v.Ok, string.Join("; ", v.Errors)); Near(.85, v.CutMinutes, 1e-12);
        });
        foreach (var mode in new[] { "G21", "G18", "G90", "G97", "G40", "G49", "G80", "G54" })
        {
            var required = mode;
            Add("verifier rejects omitted preparation " + required, () =>
                InvalidNc(Regex.Replace(Fixture, @"\b" + required + @"\b ?", "")));
        }
        var mutations = new (string Name, string Find, string Replace)[]
        {
            ("incremental coordinates", "G90", "G91"),
            ("inch units", "G21", "G20"),
            ("unsupported cycle", "N60 G1", "N60 G81"),
            ("coordinate expression", "X120 Z-12", "X[120] Z-12"),
            ("unknown Y word", "N50 G0", "N50 G0 Y1"),
            ("duplicate block number", "N60 ", "N50 "),
            ("descending block number", "N60 ", "N45 "),
            ("missing block number", "N60 ", ""),
            ("fractional block number", "N60 ", "N60.5 "),
            ("block number not first", "N60 G1", "G1 N60"),
            ("duplicate X", "N60 G1", "N60 G1 X119"),
            ("conflicting motion", "N60 G1", "N60 G0 G1"),
            ("missing Z", "N60 G1 X120 Z-12 F0.2", "N60 G1 X120 F0.2"),
            ("implicit modal motion", "N60 G1", "N60"),
            ("initial point outside declared corner", "N40 G0 X128 Z3", "N40 G0 X126 Z3"),
            ("initial G1", "N40 G0 X128 Z3", "N40 G1 X128 Z3 F0.2"),
            ("X above envelope", "X120 Z-12", "X129 Z-12"),
            ("negative X", "X120 Z-12", "X-1 Z-12"),
            ("Z below midpoint-length envelope", "X120 Z-12", "X120 Z-117.501"),
            ("Z above envelope", "X120 Z-12", "X120 Z4"),
            ("zero feed", "F0.2", "F0"),
            ("negative feed", "F0.2", "F-0.2"),
            ("zero spindle", "S100", "S0"),
            ("negative spindle", "S100", "S-100"),
            ("fractional spindle", "S100", "S100.5"),
            ("rpm cap exceeded", "S100", "S1201"),
            ("spindle not started", "N30 S100 M3", "N30 S100"),
            ("missing rpm", "N30 S100 M3", "N30 M3"),
            ("implicit G1 feed", "N70 G1 X124 Z-12 F0.2", "N70 G1 X124 Z-12"),
            ("unsupported tool", "T1", "T3"),
            ("tool not loaded", "T1 D1 M66", "T1 D1"),
            ("tool change with running spindle", "N70 G1 X124 Z-12 F0.2", "N70 T2 D1 M66"),
            ("missing stop before end", "N80 M5", "(stop omitted)"),
            ("commands after M30", "N90 M30", "N90 M30\r\nN100 G0 X128 Z3"),
            ("missing M30", "N90 M30", "(end omitted)"),
            ("unclosed comment", "(POINT-TOOL CHECK FIXTURE)", "(unfinished"),
            ("nested comment", "(POINT-TOOL CHECK FIXTURE)", "(outer(inner))"),
            ("non-ASCII NC comment", "(POINT-TOOL CHECK FIXTURE)", "(непроверено)"),
            ("percent inside program", "N60 G1", "% OTHER\r\nN60 G1"),
            ("missing program header", "% TEST_PROFILE", "(header omitted)"),
            ("unnamed program header", "% TEST_PROFILE", "%"),
            ("LinuxCNC diameter mode", "N11 G10 X1", "N11 G7"),
            ("radius programming instead of diameter mode", "N11 G10 X1", "N11 G10 X0"),
            ("diameter mode with no transverse-axis setting", "N11 G10 X1", "N11 G10"),
            ("diameter mode shares a block", "N11 G10 X1", "N11 G10 X1 G90"),
            ("diameter mode with Z word", "N11 G10 X1", "N11 G10 X1 Z0"),
            ("missing diameter mode", "N11 G10 X1", "(diameter mode omitted)"),
            ("missing explicit edge at tool selection", "T1 D1 M66", "T1 M66"),
            ("zero edge disables correction", "T1 D1 M66", "T1 D0 M66"),
            ("standalone edge selection", "N21 G43", "N21 D1 G43"),
            ("LinuxCNC H compensation word", "N21 G43", "N21 G43 H1"),
            ("missing tool compensation", "N21 G43", "(G43 omitted)"),
            ("conflicting tool compensation", "N21 G43", "N21 G43 G49"),
            ("G95 before spindle speed is set", "N12 M5", "N12 G95\r\nN13 G94\r\nN14 M5"),
            ("G95 without running spindle", "N31 G95", "N31 M5\r\nN32 G95"),
            ("missing feed-per-revolution mode", "N31 G95", "(G95 omitted)"),
            ("missing initial coolant-off confirmation", "N13 M9", "(initial M9 omitted)"),
            ("coolant command in a movement block", "N60 G1", "N60 M8 G1"),
            ("coolant-off command in a return movement block", "N78 M9", "N78 M9 G0 X128 Z3"),
            ("missing continuous coolant before cutting", "N22 M8", "(M8 omitted)"),
            ("coolant switched off before cutting", "N50 G0", "N49 M9\r\nN50 G0"),
            ("coolant left on at program end", "N78 M9", "(M9 omitted)"),
            ("G94 during cutting", "N60 G1", "N59 G94\r\nN60 G1"),
            ("M5 while G95 is active", "N75 G94", "(G94 omitted)"),
            ("conflicting feed modes", "N31 G95", "N31 G94 G95"),
            ("LinuxCNC program end", "N90 M30", "N90 M2"),
            ("old paired-percent terminator", "N90 M30", "N90 M30\r\n%")
        };
        foreach (var mutation in mutations)
        {
            var item = mutation;
            Add("verifier rejects " + item.Name, () =>
            {
                var changed = Fixture.Replace(item.Find, item.Replace, StringComparison.Ordinal);
                Check(changed != Fixture, "mutation must change the fixture");
                InvalidNc(changed);
            });
        }
        foreach (var malformed in new[] { "NaN", "Infinity", "1e400", "--3", "1.2.3" })
        {
            var value = malformed;
            Add("verifier rejects malformed numeric token " + value, () => InvalidNc(Fixture.Replace("X120 Z-12", "X" + value + " Z-12", StringComparison.Ordinal)));
        }
        Add("exact midpoint-length lower boundary is accepted", () =>
        {
            var program = Fixture.Replace("Z-12", "Z-117.5", StringComparison.Ordinal);
            var v = NcVerifier.Verify(program, Engine.Defaults);
            Check(v.Ok, string.Join("; ", v.Errors));
            InvalidNc(program.Replace("Z-117.5", "Z-118", StringComparison.Ordinal));
        });
        Add("tool change requires fresh G0 and resets the unknown position", () =>
        {
            var changed = Fixture.Replace("N70 G1 X124 Z-12 F0.2",
                "N61 G94\r\nN62 M5\r\nN63 M9\r\nN64 T2 D1 M66\r\nN65 G43\r\nN66 M8\r\nN67 S100 M3\r\nN68 G95\r\nN70 G1 X41 Z-12 F0.2", StringComparison.Ordinal);
            InvalidNc(changed);
            var valid = changed.Replace("N70 G1 X41 Z-12 F0.2", "N69 G0 X41 Z3\r\nN70 G1 X41 Z-12 F0.2", StringComparison.Ordinal);
            var v = NcVerifier.Verify(valid, Engine.Defaults);
            Check(v.Ok, string.Join("; ", v.Errors)); Near(1.5, v.CutMinutes, 1e-12);
            InvalidNc(valid.Replace("N65 G43", "(second G43 omitted)", StringComparison.Ordinal));
            InvalidNc(valid.Replace("N68 G95", "(second G95 omitted)", StringComparison.Ordinal));
            InvalidNc(valid.Replace("N63 M9", "(M9 before second tool omitted)", StringComparison.Ordinal));
            InvalidNc(valid.Replace("N66 M8", "(M8 for second tool omitted)", StringComparison.Ordinal));
        });
        Add("feed-mode changes require a new cutting feed after returning to G95", () =>
        {
            var changed = Fixture.Replace("N70 G1 X124 Z-12 F0.2",
                "N61 G94\r\nN62 F0.2\r\nN63 G95\r\nN70 G1 X124 Z-12", StringComparison.Ordinal);
            InvalidNc(changed);
            var valid = changed.Replace("N70 G1 X124 Z-12", "N70 G1 X124 Z-12 F0.2", StringComparison.Ordinal);
            Check(NcVerifier.Verify(valid, Engine.Defaults).Ok, "explicit F after returning to G95");
        });
        Add("empty NC and absent or nonfinite bounds fail cleanly", () =>
        {
            InvalidNc(null); InvalidNc(""); InvalidNc(Fixture, null, false);
            InvalidNc(Fixture, Engine.Defaults with { Length = double.NaN });
            InvalidNc(Fixture, Engine.Defaults with { StockDiameter = -1 });
            InvalidNc(Fixture, Engine.Defaults with { RpmMax = 0 });
            InvalidNc(Fixture, Engine.Defaults with { LengthLowerDeviation = double.PositiveInfinity });
        });
        Add("NC import size, line and diagnostic counts are bounded", () =>
        {
            InvalidNc(new string('x', 1_000_001));
            InvalidNc(new string('\n', 25_001));
            var v = InvalidNc(string.Concat(Enumerable.Repeat("invalid\n", 1000)));
            Equal(100, v.Errors.Count, "error cap");
        });
        Add("accepted in-envelope diagonal explicitly retains collision limitations", () =>
        {
            var v = NcVerifier.Verify(Fixture.Replace("N60 G1 X120 Z-12 F0.2", "N60 G1 X10 Z-100 F0.2", StringComparison.Ordinal), Engine.Defaults);
            Check(v.Ok, "in-envelope point-tool diagonal remains in the documented subset");
            Check(v.Warnings.Any(w => w.Contains("Не проверяются столкновения", StringComparison.Ordinal)), "must disclose absence of collision checks");
            Check(v.Warnings.Any(w => w.Contains("Холостые", StringComparison.Ordinal)), "must disclose G1-only timing");
        });
    }

    private static VerificationResult InvalidNc(string? program, Parameters? p = null, bool useDefaults = true)
    {
        var result = NcVerifier.Verify(program, p ?? (useDefaults ? Engine.Defaults : null));
        Check(!result.Ok, "invalid NC accepted");
        Check(result.Errors.Count > 0, "NC rejection requires diagnostics");
        Check(result.Warnings.Count >= 4, "NC check must disclose its limitations");
        return result;
    }

    private static void RegisterFileTests()
    {
        Add("JSON partial inputs and supported wrappers merge with typed defaults", () =>
        {
            using var temp = new TempWorkspace();
            foreach (var json in new[] { "{\"length\":100}", "{\"params\":{\"length\":100}}", "{\"project\":{\"params\":{\"length\":100}}}" })
            {
                var path = temp.Write("input.json", json);
                var p = ProjectFiles.ImportParameters(path);
                Near(100, p.Length); Near(47.3, p.BoreDiameter); Near(-1, p.LengthLowerDeviation);
                Near(139.5, Generated(p).Calculations.StockLength);
            }
            var empty = ProjectFiles.ImportParameters(temp.Write("empty.json", "{}"));
            Equal(Engine.Defaults, empty, "empty object has complete defaults");
        });
        foreach (var json in new[]
        {
            "null", "[]", "\"text\"", "{", "{\"misspelled\":2}", "{\"__proto__\":3}",
            "{\"length\":\"116\"}", "{\"length\":null}", "{\"length\":true}", "{\"length\":1e400}",
            "{\"length\":NaN}", "{\"length\":0}", "{\"length\":116.00001}", "{\"Length\":116}",
            "{\"material\":null}", "{\"material\":12}", "{\"length\":116,\"length\":100}",
            "{\"params\":[]}", "{\"params\":{},\"unknown\":1}", "{\"params\":{},\"params\":{}}",
            "{\"project\":null}", "{\"project\":{}}", "{\"project\":{\"params\":{},\"ignored\":1}}",
            "{\"project\":{\"params\":{}},\"ignored\":1}", "{\"params\":{\"length\":116,\"length\":100}}"
        })
        {
            var invalid = json;
            Add("JSON rejects " + invalid, () =>
            {
                using var temp = new TempWorkspace();
                Throws<InvalidDataException>(() => ProjectFiles.ImportParameters(temp.Write("invalid.json", invalid)));
            });
        }
        Add("JSON rejects oversized or invalid UTF-8 files and accepts a UTF-8 BOM", () =>
        {
            using var temp = new TempWorkspace();
            Throws<InvalidDataException>(() => ProjectFiles.ImportParameters(temp.Write("large.json", new string(' ', 1_000_001))));
            var bad = Path.Combine(temp.Root, "bad.json"); File.WriteAllBytes(bad, [0xff, 0xfe, 0x7b]);
            Throws<InvalidDataException>(() => ProjectFiles.ImportParameters(bad));
            var bom = Path.Combine(temp.Root, "bom.json"); File.WriteAllBytes(bom, [0xef, 0xbb, 0xbf, 0x7b, 0x7d]);
            Equal(Engine.Defaults, ProjectFiles.ImportParameters(bom));
        });
        Add("parameter save is separate from the exact three-file reproducible export", () =>
        {
            using var temp = new TempWorkspace();
            var p = Generated(Engine.Defaults with { PartName = "Втулка <A>&", Length = 100, DepthOutside = .7 });
            var saved = Path.Combine(temp.Root, "saved.json");
            ProjectFiles.SaveParameters(p, saved);
            var imported = ProjectFiles.ImportParameters(saved);
            Equal(p.Params, imported, "saved parameters round trip");
            var regenerated = Generated(imported);
            Equal(p.Id, regenerated.Id); Equal(p.Nc, regenerated.Nc);
            Equal(p.RouteHtml, regenerated.RouteHtml); Equal(p.TechnologyHtml, regenerated.TechnologyHtml);
            var firstDirectory = Path.Combine(temp.Root, "first");
            var secondDirectory = Path.Combine(temp.Root, "second");
            var first = ProjectFiles.Export(p, firstDirectory);
            var second = ProjectFiles.Export(regenerated, secondDirectory);
            CheckExportFiles(firstDirectory, first);
            CheckExportFiles(secondDirectory, second);
            foreach (var path in first)
            {
                Check(new FileInfo(path).Length > 0, "nonempty exported file");
                var other = second.Single(f => Path.GetFileName(f) == Path.GetFileName(path));
                Sequence(File.ReadAllBytes(path), File.ReadAllBytes(other));
            }
            var ncBytes = File.ReadAllBytes(Path.Combine(firstDirectory, "Управляющая программа.nc"));
            Check(ncBytes.All(b => b <= 127), "NC is ASCII without a UTF-8 BOM");
            var exportedNc = Encoding.ASCII.GetString(ncBytes);
            Equal(p.Nc, exportedNc, "exported NC matches the checked project");
            Check(exportedNc.StartsWith("% ", StringComparison.Ordinal), "AxiOMA program-start line");
            Check(!Regex.IsMatch(exportedNc, @"(?<!\r)\n|\r(?!\n)"), "NC uses only CRLF line separators");
            Check(exportedNc.EndsWith("\r\n", StringComparison.Ordinal), "NC ends with CRLF");
            Check(!Regex.IsMatch(exportedNc, @"\bG7\b|\bM2\b"), "LinuxCNC-only preparation and end are absent");
            var verification = NcVerifier.Verify(exportedNc, p.Params);
            Check(verification.Ok, string.Join("; ", verification.Errors));
            Near(p.Calculations.TotalCutMinutes, verification.CutMinutes);
        });
        Add("nonempty export folders are refused without overwriting existing files", () =>
        {
            using var temp = new TempWorkspace();
            var p = Generated();
            var existing = temp.Write("params.json", "existing user data");
            Throws<IOException>(() => ProjectFiles.Export(p, temp.Root));
            Equal("existing user data", File.ReadAllText(existing));
            Sequence(new[] { "params.json" }, Directory.GetFileSystemEntries(temp.Root).Select(Path.GetFileName));
        });
        Add("concurrent exports preserve exactly one complete unmixed project", () =>
        {
            using var temp = new TempWorkspace();
            var projects = new[]
            {
                Generated(Engine.Defaults with { PartName = "Concurrent A", Length = 100 }),
                Generated(Engine.Defaults with { PartName = "Concurrent B", Length = 100, CounterboreDepth = 40 })
            };
            var references = projects.Select((project, index) => ProjectFiles.Export(project, Path.Combine(temp.Root, "reference-" + index)))
                .Select(files => files.ToDictionary(file => Path.GetFileName(file)!, File.ReadAllBytes, StringComparer.Ordinal)).ToArray();
            // Repeated starts belong to one concurrency case; expected bytes come from ordinary exports.
            for (var round = 0; round < 12; round++)
            {
                var directory = Path.Combine(temp.Root, "concurrent-" + round);
                using var start = new Barrier(2);
                var attempts = Enumerable.Range(0, 2).Select(index => Task.Factory.StartNew(() =>
                {
                    if (!start.SignalAndWait(TimeSpan.FromSeconds(10))) throw new TimeoutException("Concurrent export start barrier timed out");
                    try
                    {
                        ProjectFiles.Export(projects[index], directory);
                        return (Index: index, Error: (Exception?)null);
                    }
                    catch (Exception ex) { return (Index: index, Error: (Exception?)ex); }
                }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default)).ToArray();
                var results = attempts.Select(attempt => attempt.GetAwaiter().GetResult()).ToArray();
                var winners = results.Where(result => result.Error is null).ToArray();
                Equal(1, winners.Length, "one concurrent export succeeds");
                var loser = results.Single(result => result.Error is not null);
                Check(loser.Error is IOException, "the losing export reports IOException rather than overwriting files");
                var files = Directory.GetFiles(directory);
                CheckExportFiles(directory, files);
                foreach (var file in files)
                    Sequence(references[winners[0].Index][Path.GetFileName(file)], File.ReadAllBytes(file));
            }
        });
        Add("parameter saving replaces atomically and preserves locked or invalid targets", () =>
        {
            using var temp = new TempWorkspace();
            var first = Generated();
            var replacement = Generated(Engine.Defaults with { Length = 100, CounterboreDepth = 40 });
            var path = Path.Combine(temp.Root, "saved.json");
            ProjectFiles.SaveParameters(first, path);
            Equal(first.Params, ProjectFiles.ImportParameters(path), "new parameter file imports");
            ProjectFiles.SaveParameters(replacement, path);
            Equal(replacement.Params, ProjectFiles.ImportParameters(path), "replacement parameter file imports");
            var preserved = File.ReadAllBytes(path);
            void MustFailWithoutWriting(string target)
            {
                Exception? failure = null;
                try { ProjectFiles.SaveParameters(first, target); }
                catch (Exception ex) { failure = ex; }
                Check(failure is IOException or UnauthorizedAccessException, "failed atomic save reports a filesystem exception");
            }
            using (var held = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                MustFailWithoutWriting(path);
                Sequence(preserved, File.ReadAllBytes(path));
            }
            Sequence(new[] { "saved.json" }, Directory.GetFileSystemEntries(temp.Root).Select(Path.GetFileName));
            var directoryTarget = Path.Combine(temp.Root, "directory-target");
            Directory.CreateDirectory(directoryTarget);
            var sentinel = Path.Combine(directoryTarget, "sentinel.txt");
            File.WriteAllText(sentinel, "keep this content");
            MustFailWithoutWriting(directoryTarget);
            Equal("keep this content", File.ReadAllText(sentinel), "directory target sentinel survives");
            Sequence(new[] { "sentinel.txt" }, Directory.GetFileSystemEntries(directoryTarget).Select(Path.GetFileName));
            Sequence(new[] { "directory-target", "saved.json" }, Directory.GetFileSystemEntries(temp.Root).Select(Path.GetFileName).Order(StringComparer.Ordinal));
            Sequence(preserved, File.ReadAllBytes(path));
        });
        Add("reordered JSON keys have identical revision and NC", () =>
        {
            using var temp = new TempWorkspace();
            var a = ProjectFiles.ImportParameters(temp.Write("a.json", "{\"length\":100,\"bodyDiameter\":95}"));
            var b = ProjectFiles.ImportParameters(temp.Write("b.json", "{\"bodyDiameter\":95,\"length\":100}"));
            Equal(Generated(a).Id, Generated(b).Id); Equal(Generated(a).Nc, Generated(b).Nc);
        });
        Add("Word process forms have editable OOXML, service fields and page geometry", () =>
        {
            using var temp = new TempWorkspace();
            var p = Generated(Engine.Defaults with { PartName = "Втулка <A>& \"Б\"" });
            var directory = Path.Combine(temp.Root, "docx");
            var files = ProjectFiles.Export(p, directory);
            CheckExportFiles(directory, files);
            CheckDocx(files.Single(f => Path.GetFileName(f) == "Маршрутная карта.docx"), p, route: true);
            CheckDocx(files.Single(f => Path.GetFileName(f) == "Технологическая карта.docx"), p, route: false);
        });
        Add("invariant NC and deterministic cards survive a Russian process culture", () =>
        {
            var original = CultureInfo.CurrentCulture;
            var originalUi = CultureInfo.CurrentUICulture;
            var baseline = Generated();
            try
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("ru-RU");
                CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("ru-RU");
                var localized = Generated();
                Equal(baseline.Id, localized.Id); Equal(baseline.Nc, localized.Nc);
                Equal(baseline.RouteHtml, localized.RouteHtml); Equal(baseline.TechnologyHtml, localized.TechnologyHtml);
            }
            finally { CultureInfo.CurrentCulture = original; CultureInfo.CurrentUICulture = originalUi; }
        });
        Add("export rejects tampered NC before creating its destination", () =>
        {
            using var temp = new TempWorkspace();
            var p = Generated(); var target = Path.Combine(temp.Root, "rejected");
            Throws<InvalidDataException>(() => ProjectFiles.Export(p with { Nc = p.Nc.Replace("G90", "G91", StringComparison.Ordinal) }, target));
            Check(!Directory.Exists(target), "invalid export should not create output files");
            Throws<InvalidDataException>(() => ProjectFiles.SaveParameters(p with { Params = p.Params with { Length = double.NaN } }, Path.Combine(temp.Root, "bad.json")));
        });
        Add("export rejects stale parameters, cards, moves and valid-but-edited NC", () =>
        {
            using var temp = new TempWorkspace();
            var p = Generated();
            var changes = new[]
            {
                p with { Params = p.Params with { Length = 100 } },
                p with { RouteHtml = p.RouteHtml + "<p>stale</p>" },
                p with { Nc = p.Nc.Replace("F0.2", "F0.19", StringComparison.Ordinal) },
                p with { Moves = p.Moves.Skip(1).ToList() }
            };
            for (var i = 0; i < changes.Length; i++)
            {
                var target = Path.Combine(temp.Root, "stale-" + i);
                Throws<InvalidDataException>(() => ProjectFiles.Export(changes[i], target));
                Check(!Directory.Exists(target), "stale export should not create destination");
            }
        });
    }
    private static void Throws<T>(Action action) where T : Exception
    {
        assertions++;
        try { action(); }
        catch (T) { return; }
        catch (Exception ex) { throw new InvalidOperationException("Expected " + typeof(T).Name + ", got " + ex.GetType().Name + ": " + ex.Message); }
        throw new InvalidOperationException("Expected " + typeof(T).Name + " but operation succeeded");
    }
    private static void ExportRequested(string directory, string? input)
    {
        var parameters = input is null ? Engine.Defaults : ProjectFiles.ImportParameters(input);
        var p = Generated(parameters);
        var files = ProjectFiles.Export(p, directory);
        CheckExportFiles(directory, files, p.NcFileName);
        CheckDocx(files.Single(f => Path.GetFileName(f) == "Маршрутная карта.docx"), p, route: true);
        CheckDocx(files.Single(f => Path.GetFileName(f) == "Технологическая карта.docx"), p, route: false);
        Evidence["export"] = new { directory, input, p.Id, files };
    }
    private static void CheckExportFiles(string directory, IReadOnlyList<string> files, string ncName = "Управляющая программа.nc")
    {
        Equal(3, files.Count, "exact export file count");
        string[] expectedNames = ["Маршрутная карта.docx", "Технологическая карта.docx", ncName];
        Sequence(expectedNames.Order(StringComparer.Ordinal), files.Select(Path.GetFileName).Order(StringComparer.Ordinal));
        Sequence(expectedNames.Order(StringComparer.Ordinal), Directory.GetFileSystemEntries(directory).Select(Path.GetFileName).Order(StringComparer.Ordinal));
        foreach (var file in files)
        {
            Equal(Path.GetFullPath(directory), Path.GetDirectoryName(Path.GetFullPath(file)), "export path belongs to the selected directory");
            Check(new FileInfo(file).Length > 0, "nonempty export file " + Path.GetFileName(file));
        }
    }
    private static void CheckDocx(string path, Project p, bool route)
    {
        using var package = ZipFile.OpenRead(path);
        var names = package.Entries.Select(e => e.FullName).ToHashSet(StringComparer.Ordinal);
        Equal(package.Entries.Count, names.Count, "unique DOCX part names");
        foreach (var required in new[] { "[Content_Types].xml", "_rels/.rels", "word/document.xml", "word/styles.xml", "word/_rels/document.xml.rels" })
            Check(names.Contains(required), "missing DOCX part " + required);
        var documents = new Dictionary<string, XDocument>(StringComparer.Ordinal);
        foreach (var entry in package.Entries)
        {
            Check(entry.Length > 0, "empty DOCX part " + entry.FullName);
            using var stream = entry.Open();
            if (entry.FullName.EndsWith(".xml", StringComparison.Ordinal) || entry.FullName.EndsWith(".rels", StringComparison.Ordinal))
            {
                using var reader = XmlReader.Create(stream, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
                documents.Add(entry.FullName, XDocument.Load(reader));
            }
            else stream.CopyTo(Stream.Null);
        }
        XNamespace contentTypes = "http://schemas.openxmlformats.org/package/2006/content-types";
        Check(documents["[Content_Types].xml"].Descendants(contentTypes + "Override").Any(e =>
            (string?)e.Attribute("PartName") == "/word/document.xml" &&
            (string?)e.Attribute("ContentType") == "application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"), "main DOCX content type");
        XNamespace relationships = "http://schemas.openxmlformats.org/package/2006/relationships";
        foreach (var item in documents.Where(d => d.Key.EndsWith(".rels", StringComparison.Ordinal)))
        {
            var split = item.Key.IndexOf("/_rels/", StringComparison.Ordinal);
            var baseDirectory = split < 0 ? "" : item.Key[..(split + 1)];
            foreach (var relationship in item.Value.Descendants(relationships + "Relationship"))
            {
                if ((string?)relationship.Attribute("TargetMode") == "External") continue;
                var target = (string?)relationship.Attribute("Target");
                Check(!string.IsNullOrWhiteSpace(target), "DOCX relationship target");
                var resolved = new Uri(new Uri("https://docx.invalid/" + baseDirectory), target!).AbsolutePath.TrimStart('/');
                Check(names.Contains(resolved), "unresolved DOCX relationship " + resolved);
            }
        }
        XNamespace w = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
        var document = documents["word/document.xml"];
        Equal(w + "document", document.Root?.Name, "Word document root");
        Check(document.Root?.Element(w + "body") is not null, "Word body");
        var text = string.Join(" ", document.Descendants(w + "t").Select(t => t.Value));
        Check(text.Contains(p.Id, StringComparison.Ordinal), "DOCX revision");
        Check(text.Contains(p.Params.PartName, StringComparison.Ordinal), "DOCX part name and XML escaping");
        var tables = document.Descendants(w + "tbl").ToArray();
        Check(tables.Length > 0, "native editable Word tables");
        foreach (var table in tables)
        {
            var columnCount = table.Element(w + "tblGrid")?.Elements(w + "gridCol").Count() ?? 0;
            Check(columnCount is > 0 and <= 63, "Word table uses a supported column count");
            var rows = table.Elements(w + "tr").ToArray();
            Check(rows.Length > 0, "native form rows");
            foreach (var row in rows)
            {
                Check(row.Elements(w + "tc").Any(), "native editable form cells");
                foreach (var span in row.Descendants(w + "gridSpan"))
                    Check(int.TryParse((string?)span.Attribute(w + "val"), out var count) && count > 0, "valid merged form cell");
                var coveredColumns = row.Elements(w + "tc").Sum(cell =>
                    int.Parse((string?)cell.Element(w + "tcPr")?.Element(w + "gridSpan")?.Attribute(w + "val") ?? "1", CultureInfo.InvariantCulture));
                Equal(columnCount, coveredColumns, "merged row covers its table grid");
            }
        }
        var sections = document.Descendants(w + "sectPr").ToArray();
        Check(sections.Length > 0, "explicit form page settings");
        foreach (var section in sections)
        {
            var size = section.Element(w + "pgSz") ?? throw new InvalidOperationException("Missing A4 page dimensions");
            Near(297, Millimetres(size, w + "w"), .05);
            Near(210, Millimetres(size, w + "h"), .05);
            Equal("landscape", (string?)size.Attribute(w + "orient"), "A4 landscape");
            var margins = section.Element(w + "pgMar") ?? throw new InvalidOperationException("Missing form page margins");
            Near(20, Millimetres(margins, w + "left"), .05);
            foreach (var side in new[] { "top", "right", "bottom" }) Near(5, Millimetres(margins, w + side), .05);
        }
        var pageFrame = sections.Select(s => s.Element(w + "pgBorders")).Any(borders => HasFourBorders(borders, w));
        var tableFrame = tables.Any(table =>
            HasFourBorders(table.Element(w + "tblPr")?.Element(w + "tblBorders"), w) &&
            Math.Abs(table.Element(w + "tblGrid")?.Elements(w + "gridCol").Sum(c => Millimetres(c, w + "w")) - 272 ?? double.PositiveInfinity) < .1);
        Check(pageFrame || tableFrame, "four-sided form frame spanning the 272-mm printable width");
        Check(!documents.Values.Any(d => d.Descendants(w + "documentProtection").Any()), "Word cards remain editable");
        Check(!document.Descendants(w + "altChunk").Any(), "Word content uses native paragraphs and tables");
        Check(!text.Contains("Полный набор исходных параметров", StringComparison.Ordinal), "form excludes the former parameter appendix");
        Check(!text.Contains("Ограничения учебного комплекта", StringComparison.Ordinal), "form excludes the former warning appendix");
        CheckFormContent(document, p, route);
        docxFilesChecked++;
    }
    private static double Millimetres(XElement element, XName attribute) =>
        double.Parse((string?)element.Attribute(attribute) ?? throw new InvalidOperationException("Missing page/table dimension " + attribute.LocalName), CultureInfo.InvariantCulture) * 25.4 / 1440;

    private static bool HasFourBorders(XElement? borders, XNamespace w) => borders is not null &&
        new[] { "top", "left", "bottom", "right" }.All(side => borders.Element(w + side) is { } border && (string?)border.Attribute(w + "val") is not (null or "nil" or "none"));

    private static void CheckFormContent(XDocument document, Project p, bool route)
    {
        XNamespace w = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
        var rows = document.Descendants(w + "tr").Select(row => row.Elements(w + "tc")
            .Select(cell => string.Join(" ", cell.Descendants(w + "t").Select(t => t.Value)).Trim()).ToArray()).ToArray();
        var text = string.Join(" ", document.Descendants(w + "t").Select(t => t.Value));
        Check(text.Contains("010", StringComparison.Ordinal), "NC operation 010 belongs to both cards");
        var cells = rows.SelectMany(row => row).ToArray();
        var nominalCells = cells.Where(cell => cell.StartsWith("Номиналы и отклонения", StringComparison.Ordinal)).ToArray();
        Equal(1, nominalCells.Length, "confirmed five-dimension acceptance is stated once");
        Check(!Regex.IsMatch(nominalCells[0], @"\bDb\b|(?<!\w)b\s+\d"), "body and flange-width acceptance are not duplicated as confirmed photo tolerances");
        var generalCells = cells.Where(cell => cell.StartsWith("Db ", StringComparison.Ordinal)).ToArray();
        Equal(1, generalCells.Length, "general body and flange-width acceptance is stated once");
        string Ru(double value) => value.ToString("0.###", CultureInfo.GetCultureInfo("ru-RU"));
        Check(generalCells[0].Contains("Db " + Ru(p.Params.BodyDiameter) + " ±" + Ru(ToolingProfile.GeneralLinearTolerance(p.Params.BodyDiameter)), StringComparison.Ordinal), "body general tolerance is explicit");
        Check(generalCells[0].Contains("b " + Ru(p.Params.FlangeWidth) + " ±" + Ru(ToolingProfile.GeneralLinearTolerance(p.Params.FlangeWidth)), StringComparison.Ordinal), "flange-width general tolerance is explicit");
        Check(generalCells[0].Contains("R≤0,5", StringComparison.Ordinal), "maximum allowed corner radius is explicit");
        if (route)
        {
            Check(Regex.IsMatch(text, @"Форма\s*1\b", RegexOptions.IgnoreCase), "route form 1 designation");
            foreach (var marker in new[] { "А", "Б" })
                Check(rows.Any(row => row.Length > 0 && Regex.IsMatch(row[0], "^" + marker + @"\s*\d*$")), "route service marker " + marker);
            foreach (var operation in p.Operations) Check(text.Contains(operation.Code, StringComparison.Ordinal), "route operation " + operation.Code);
            var rawHeader = Array.FindIndex(rows, row => row.Length > 0 && row[0] == "М02");
            Check(rawHeader >= 0 && rawHeader + 1 < rows.Length, "route raw-stock profile row exists");
            var rawProfile = rows[rawHeader + 1][8];
            var rawLength = Regex.Match(rawProfile, @"×\s*([0-9]+(?:,[0-9]+)?)");
            Check(rawLength.Success, "route raw-stock length is readable");
            Near(p.Calculations.RawStockLength, FormNumber(rawLength.Groups[1].Value));
            var equipmentHeader = rows.First(row => row.Length > 1 && row[0] == "Б" && row.Any(cell => NormLabel(cell) == "Тпз"));
            var timeColumns = equipmentHeader.Select((cell, index) => (Label: NormLabel(cell), Index: index))
                .Where(cell => cell.Label is "Тпз" or "Тшт").Select(cell => cell.Index).ToArray();
            Equal(2, timeColumns.Length, "route time-standard columns");
            foreach (var row in rows.Where(row => row.Length > 0 && Regex.IsMatch(row[0], @"^Б\s*\d+$")))
                foreach (var column in timeColumns) Check(column < row.Length && string.IsNullOrWhiteSpace(row[column]), "route time standards remain blank");
            return;
        }
        var preparedStock = rows.Single(row => row.Length == 8 && row[0] == "Токарная с ЧПУ");
        var preparedLength = Regex.Match(preparedStock[5], @"×\s*([0-9]+(?:,[0-9]+)?)");
        Check(preparedLength.Success, "operation prepared-stock length is readable");
        Near(p.Calculations.StockLength, FormNumber(preparedLength.Groups[1].Value));
        Check(Regex.IsMatch(text, @"Форма\s*3\b", RegexOptions.IgnoreCase), "operation form 3 designation");
        Check(Regex.IsMatch(text, @"Форма\s*2[аa]\b", RegexOptions.IgnoreCase), "operation continuation form 2a designation");
        foreach (var marker in new[] { "О", "Т", "Р" })
            Check(rows.Any(row => row.Length > 0 && Regex.IsMatch(row[0], "^" + marker + @"\s*\d+$")), "operation service marker " + marker);
        var regimeHeaders = rows.Where(row => row.Length > 0 && row[0] == "Р" && row.Contains("ПИ", StringComparer.Ordinal)).ToArray();
        Check(regimeHeaders.Length > 0, "named operation regime columns");
        foreach (var header in regimeHeaders)
            Sequence(new[] { "Р", "", "ПИ", "D или B", "L", "t", "i", "S", "n", "V" }, header);
        var normHeader = document.Descendants(w + "tr").First(row => row.Elements(w + "tc")
            .Any(cell => NormLabel(string.Concat(cell.Descendants(w + "t").Select(t => t.Value))) == "То"));
        var normLabels = normHeader.Elements(w + "tc").Select(cell => NormLabel(string.Concat(cell.Descendants(w + "t").Select(t => t.Value)))).ToArray();
        var normValues = normHeader.ElementsAfterSelf(w + "tr").First().Elements(w + "tc")
            .Select(cell => string.Concat(cell.Descendants(w + "t").Select(t => t.Value))).ToArray();
        foreach (var label in new[] { "То", "Тв", "Тпз", "Тшт" })
        {
            var column = Array.IndexOf(normLabels, label);
            Check(column >= 0 && column < normValues.Length && string.IsNullOrWhiteSpace(normValues[column]), "operation standard " + label + " remains blank");
        }
        var regimes = ReadNcTransitionRegimes(p.Nc);
        var regimeRows = rows.Where(row => row.Length > 1 && Regex.IsMatch(row[0], @"^Р\s*\d+$") && Regex.IsMatch(row[1], @"010\.\d{2}")).ToArray();
        Equal(p.Transitions.Count, regimeRows.Length, "one regime row per generated transition");
        foreach (var t in p.Transitions)
        {
            var row = regimeRows.Single(row => Regex.IsMatch(row[1], @"(?<!\d)" + Regex.Escape(t.Id) + @"(?!\d)"));
            Equal(10, row.Length, "operation regime fields");
            Check(row.Skip(2).All(cell => !Regex.IsMatch(cell, @"\d\.\d")), "Russian operation-card numeric fields use a decimal comma");
            Near(double.Parse(t.Tool[1..], CultureInfo.InvariantCulture), FormNumber(row[2]));
            Near(t.ReferenceDiameter, FormNumber(row[3]), .000501);
            Near(Math.Abs(t.ZEnd - t.ZStart), FormNumber(row[4]), .000501);
            Near(IndependentMaximumDepth(t), FormNumber(row[5]));
            Near(t.Passes.Count, FormNumber(row[6]));
            Near(t.Feed, FormNumber(row[7]), .000501);
            Near(t.Rpm, FormNumber(row[8]));
            Near(Math.PI * t.ReferenceDiameter * t.Rpm / 1000, FormNumber(row[9]), .000501);
            Check(regimes.TryGetValue(t.Id, out var ncRegimes) && ncRegimes.Count > 0, "G1 regimes exist in NC for " + t.Id);
            foreach (var nc in ncRegimes!)
            {
                Near(nc.Tool, FormNumber(row[2]));
                Near(nc.Feed, FormNumber(row[7]), .000501);
                Near(nc.Rpm, FormNumber(row[8]));
            }
        }
    }

    private static double FormNumber(string value) => double.Parse(value.Replace(',', '.').Replace('−', '-'), CultureInfo.InvariantCulture);
    private static string NormLabel(string value) => value.Trim().TrimEnd('.');

    // The card's S and n columns are checked against words read directly from NC, not only against model values.
    private static Dictionary<string, List<NcRegime>> ReadNcTransitionRegimes(string nc)
    {
        var result = new Dictionary<string, List<NcRegime>>(StringComparer.Ordinal);
        string? transition = null;
        int? tool = null;
        double rpm = 0;
        foreach (var raw in nc.Split('\n'))
        {
            var line = raw.Trim();
            var tag = Regex.Match(line, @"^(?:\(|;\s*)TR (010\.\d{2}) ");
            if (tag.Success)
            {
                transition = tag.Groups[1].Value;
                result.Add(transition, []);
            }
            if (!Regex.IsMatch(line, @"^N\d+\s")) continue;
            var words = Regex.Matches(line, @"([A-Z])([+-]?(?:\d+(?:\.\d*)?|\.\d+))")
                .Select(m => (Letter: m.Groups[1].Value, Number: double.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture))).ToArray();
            var speed = words.FirstOrDefault(word => word.Letter == "S");
            if (speed.Letter is not null) rpm = speed.Number;
            if (words.Any(word => word.Letter == "T"))
            {
                var selection = words.Single(word => word.Letter == "T").Number;
                Check(selection == Math.Floor(selection) && selection is 1 or 2, "NC tool selection is an explicit known tool");
                Near(1, words.Single(word => word.Letter == "D").Number);
                tool = (int)selection;
            }
            if (!words.Any(word => word.Letter == "G" && word.Number == 1)) continue;
            if (transition is null) throw new InvalidOperationException("G1 appears outside a tagged transition");
            var feed = words.Single(word => word.Letter == "F").Number;
            if (tool is null) throw new InvalidOperationException("G1 has no independently selected tool");
            result[transition].Add(new((int)words.Single(word => word.Letter == "N").Number, feed, rpm, tool.Value));
        }
        return result;
    }
    private sealed class TempWorkspace : IDisposable
    {
        public string Root { get; } = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "Kontur.Tests-" + Guid.NewGuid().ToString("N")));
        public TempWorkspace() => Directory.CreateDirectory(Root);
        public string Write(string name, string content)
        {
            var path = Path.Combine(Root, name);
            File.WriteAllText(path, content, new UTF8Encoding(false));
            return path;
        }
        public void Dispose()
        {
            var temporaryRoot = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!Root.StartsWith(temporaryRoot, StringComparison.OrdinalIgnoreCase) || !Path.GetFileName(Root).StartsWith("Kontur.Tests-", StringComparison.Ordinal))
                throw new InvalidOperationException("Refusing cleanup outside the test-owned temporary directory");
            Directory.Delete(Root, true);
        }
    }

    private sealed record TestResult(string Name, bool Passed, int Assertions, double DurationMilliseconds, string? Error);
    private sealed record ReadMove(double X, double Z, string Mode, int Line);
    private sealed record NcRead(List<ReadMove> Moves, double Minutes);
    private sealed record NcRegime(int Line, double Feed, double Rpm, int Tool);
}
