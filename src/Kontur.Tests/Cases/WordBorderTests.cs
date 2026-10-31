using System.IO.Compression;
using System.Xml.Linq;
using Kontur.Core;

namespace Kontur.Tests;

internal static partial class RegressionSuite
{
    private static void RegisterWordBorderTests()
    {
        Add("Word upper blocks have closed borders and standard dimensions without nested-table clipping", () =>
        {
            using var temp = new TempWorkspace();
            var files = ProjectFiles.Export(Generated(), Path.Combine(temp.Root, "cards"));
            XNamespace w = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
            foreach (var doc in files.Where(f => f.EndsWith(".docx", StringComparison.Ordinal)))
            {
                using var zip = ZipFile.OpenRead(doc);
                using var stream = zip.GetEntry("word/document.xml")!.Open();
                var xml = XDocument.Load(stream);
                Check(!xml.Descendants(w + "tc").Any(c => c.Element(w + "tbl") is not null), "no nested tables can clip a header's right rule");
                var tables = xml.Root!.Element(w + "body")!.Elements(w + "tbl").ToArray();
                Equal(4, tables.Length, "upper blocks and body on each of two sheets");
                foreach (var top in tables.Where((_, index) => index % 2 == 0))
                {
                    var rows = top.Elements(w + "tr").ToArray();
                    Equal(3, rows.Length, "B4f1 and B3f1b have three rows");
                    foreach (var row in rows)
                    {
                        var boundaries = new List<double>();
                        double x = 0;
                        foreach (var cell in row.Elements(w + "tc"))
                        {
                            x += Millimetres(cell.Element(w + "tcPr")!.Element(w + "tcW")!, w + "w");
                            boundaries.Add(x);
                            var borders = cell.Element(w + "tcPr")!.Element(w + "tcBorders");
                            if (borders?.Element(w + "left")?.Attribute(w + "val")?.Value == "single")
                            {
                                Check(HasFourBorders(borders, w), "all four sides of every visible block cell");
                            }
                        }
                        foreach (var edge in new[] { 75.4, 124.8, 205.4, 286.0 })
                        {
                            Check(boundaries.Any(b => Math.Abs(b - edge) < .05), "standard block edge " + edge);
                        }
                        Near(4.25, Millimetres(row.Element(w + "trPr")!.Element(w + "trHeight")!, w + "val"), .02);
                        Equal("exact", (string?)row.Element(w + "trPr")!.Element(w + "trHeight")!.Attribute(w + "hRule"));
                    }
                    var replacement = rows[1].Elements(w + "tc").ToArray();
                    Near(57.2, Millimetres(replacement[1].Element(w + "tcPr")!.Element(w + "tcW")!, w + "w"), .05);
                }
            }
        });
        Add("Word Form 3 regime columns and transition time fields follow the original form", () =>
        {
            using var temp = new TempWorkspace();
            var path = ProjectFiles.Export(Generated(), Path.Combine(temp.Root, "cards")).Single(p => Path.GetFileName(p) == "Технологическая карта.docx");
            using var zip = ZipFile.OpenRead(path);
            using var stream = zip.GetEntry("word/document.xml")!.Open();
            var xml = XDocument.Load(stream);
            XNamespace w = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
            double[] expected = [13, 109.2, 18.2, 28.6, 23.4, 13, 15.6, 26, 18.2, 20.8];
            var regimeRows = xml.Descendants(w + "tr").Where(r => r.Elements(w + "tc").Count() == 10).ToArray();
            Equal(10, regimeRows.Length, "two headings and eight transitions");
            foreach (var row in regimeRows)
            {
                var cells = row.Elements(w + "tc").ToArray();
                for (var i = 0; i < expected.Length; i++)
                {
                    Near(expected[i], Millimetres(cells[i].Element(w + "tcPr")!.Element(w + "tcW")!, w + "w"), .05);
                }
            }
            var descriptions = xml.Descendants(w + "tr").Where(r => r.Elements(w + "tc").Count() == 4
                && string.Concat(r.Elements(w + "tc").First().Descendants(w + "t").Select(t => t.Value)).StartsWith("О", StringComparison.Ordinal)).ToArray();
            Equal(8, descriptions.Length);
            foreach (var row in descriptions)
            {
                var times = row.Elements(w + "tc").Skip(2).ToArray();
                Near(18.2, Millimetres(times[0].Element(w + "tcPr")!.Element(w + "tcW")!, w + "w"), .05);
                Near(20.8, Millimetres(times[1].Element(w + "tcPr")!.Element(w + "tcW")!, w + "w"), .05);
                Check(times.All(c => !c.Descendants(w + "t").Any(t => t.Value.Length != 0)), "unknown normative times remain blank");
            }
        });
    }
}
