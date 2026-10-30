using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using Kontur.Core;
namespace Kontur.Tests;

internal static partial class RegressionSuite
{
    private static void RegisterBlankAndMetadataTests()
    {
        Add("automatic blank preserves initial blank and follows reversible geometry edits", () =>
        {
            var p = Engine.Defaults with
            {
                AutoStock = true
            };
            var first = Generated(p);
            Near(124, first.Params.StockDiameter);
            Near(45, first.Params.StockBore);
            var smaller = Generated(p with
            {
                BoreDiameter = 46
            });
            Near(44, smaller.Params.StockBore);
            var large = Generated(p with
            {
                FlangeDiameter = 150
            });
            Near(154, large.Params.StockDiameter);
            var restored = Generated(smaller.Params with
            {
                BoreDiameter = p.BoreDiameter
            });
            Near(first.Params.StockBore, restored.Params.StockBore);
            Near(first.Params.StockDiameter, restored.Params.StockDiameter);
        });
        Add("manual blank survives geometry edits and cannot be silently repaired", () =>
        {
            var p = Engine.Defaults with
            {
                StockDiameter = 126,
                StockBore = 44,
                AutoStock = false
            };
            var q = Generated(p with
            {
                FlangeDiameter = 121
            });
            Near(126, q.Params.StockDiameter);
            Near(44, q.Params.StockBore);
            Rejected(p with
            {
                BoreDiameter = 42
            }, "stockBore");
        });
        Add("automatic blank rejects invalid geometry and incompatible tooling", () =>
        {
            Rejected(Engine.Defaults with
            {
                AutoStock = true,
                BoreDiameter = double.NaN
            });
            Rejected(Engine.Defaults with
            {
                AutoStock = true,
                BoreDiameter = 42
            }, "stockBore");
            Rejected(Engine.Defaults with
            {
                AutoStock = true,
                FlangeDiameter = double.PositiveInfinity
            });
        });
        foreach (var controller in new[] { CncProfiles.Axioma, CncProfiles.Sinumerik })
        {
            Add("custom Db and b tolerances drive plan and parsed NC " + controller, () =>
            {
                var p = Engine.Defaults with
                {
                    Controller = controller,
                    BodyTolerance = "",
                    BodyLowerDeviation = -.1,
                    BodyUpperDeviation = .3,
                    FlangeWidthTolerance = "",
                    FlangeWidthLowerDeviation = -.02,
                    FlangeWidthUpperDeviation = .08
                };
                var q = Generated(p);
                Near(100.1, q.Calculations.TargetBodyDiameter);
                Near(12.03, q.Calculations.TargetFlangeWidth);
                var body = q.Transitions.Single(t => t.Id == "010.04");
                Near(100.1, body.Diameter);
                Near(-12.03, body.ZStart);
                Check(
                    q.Verification.Moves.Any(m => m.Mode == "G1" && m.X == 100.1 && m.Z == -12.03),
                    "parsed NC contains actual body target at actual shoulder"
                );
                Check(q.Verification.Ok, "NC verifies");
                Check(
                    !q.Operations.Single(o => o.Code == "030").Description.Contains(ToolingProfile.GeneralToleranceDesignation, StringComparison.Ordinal),
                    "manual tolerances are not labelled as general m"
                );
            });
        }

        Add("new tolerances support general standard and manual modes", () =>
        {
            var general = Tolerances.Resolve(Engine.Defaults with
            {
                BodyDiameter = 130,
                FlangeWidth = 5
            });
            Near(-.5, general.BodyLowerDeviation);
            Near(.1, general.FlangeWidthUpperDeviation);
            var standard = Tolerances.SetDesignation(Engine.Defaults, "bodyDiameter", "h7");
            Near(0, standard.BodyUpperDeviation);
            Near(-.035, standard.BodyLowerDeviation);
            var axial = Tolerances.SetDesignation(Engine.Defaults, "flangeWidth", "IT10");
            Near(-.035, axial.FlangeWidthLowerDeviation);
            Rejected(
                Engine.Defaults with
                {
                    BodyTolerance = "",
                    BodyLowerDeviation = .2,
                    BodyUpperDeviation = -.2
                },
                "bodyUpperDeviation"
            );
        });
        Add("project round trip preserves blank mode custom deviations and card fields", () =>
        {
            using var temp = new TempWorkspace();
            var path = Path.Combine(temp.Root, "project.json");
            var q = Generated(Engine.Defaults with
            {
                AutoStock = true,
                Developer = "Худяков Е.",
                DrawingNumber = "ПФ.001",
                Organization = "Университет",
                BodyTolerance = "",
                BodyLowerDeviation = -.0123,
                BodyUpperDeviation = .0157
            });
            ProjectFiles.SaveParameters(q, path);
            Equal(q.Params, ProjectFiles.ImportParameters(path), "all parameters round trip");
            File.WriteAllText(path, "{}", new UTF8Encoding(false));
            var old = ProjectFiles.ImportParameters(path);
            Check(
                !old.AutoStock && old.BodyTolerance == "m" && old.FlangeWidthTolerance == "m",
                "old JSON retains manual blank and general tolerances"
            );
        });
        Add("card metadata appears in both editable Word forms", () =>
        {
            using var temp = new TempWorkspace();
            var q = Generated(Engine.Defaults with
            {
                Organization = "Университет",
                DrawingNumber = "ПФ.001",
                Developer = "Худяков Е.",
                Checker = "Петров П.",
                RateSetter = "Иванов И.",
                NormController = "Сидоров С."
            });
            foreach (var file in ProjectFiles.Export(q, Path.Combine(temp.Root, "cards")).Where(f => f.EndsWith(".docx", StringComparison.Ordinal)))
            {
                using var zip = ZipFile.OpenRead(file);
                using var stream = zip.GetEntry("word/document.xml")!.Open();
                var text = string.Join(" ", XDocument.Load(stream).Descendants().Where(e => e.Name.LocalName == "t").Select(e => e.Value));
                foreach (var field in CardFields.Fields)
                {
                    Check(text.Contains(CardFields.Value(q.Params, field.Key), StringComparison.Ordinal), field.Key + " is written");
                }
            }
            Rejected(q.Params with
            {
                Developer = "bad\nname"
            }, "developer");
        });
    }
}
