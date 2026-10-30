using System.Globalization;

using System.IO.Compression;

using System.Text.RegularExpressions;

using System.Xml;

using System.Xml.Linq;

using Kontur.Core;

namespace Kontur.Tests;

internal static partial class RegressionSuite
{
    private static void CheckDocx(string path, Project p, bool route)
    {
        using var package = ZipFile.OpenRead(path);
        var names = package.Entries.Select(e => e.FullName).ToHashSet(StringComparer.Ordinal);
        Equal(package.Entries.Count, names.Count, "unique DOCX part names");
        foreach (var required in new[] { "[Content_Types].xml", "_rels/.rels", "word/document.xml", "word/styles.xml", "word/_rels/document.xml.rels" })
        {
            Check(names.Contains(required), "missing DOCX part " + required);
        }

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
            else
            {
                stream.CopyTo(Stream.Null);
            }
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
                if ((string?)relationship.Attribute("TargetMode") == "External")
                {
                    continue;
                }

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
                {
                    Check(int.TryParse((string?)span.Attribute(w + "val"), out var count) && count > 0, "valid merged form cell");
                }

                var coveredColumns = row.Elements(w + "tc").Sum(cell =>
                    int.Parse(
                        (string?)cell.Element(w + "tcPr")?.Element(w + "gridSpan")?.Attribute(w + "val") ?? "1",
                        CultureInfo.InvariantCulture
                    ));
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
            foreach (var side in new[] { "top", "right", "bottom" })
            {
                Near(5, Millimetres(margins, w + side), .05);
            }
        }
        var pageFrame = sections.Select(s => s.Element(w + "pgBorders")).Any(borders => HasFourBorders(borders, w));
        var tableFrame = tables.Any(table =>
            HasFourBorders(table.Element(w + "tblPr")?.Element(w + "tblBorders"), w) &&
            Math.Abs(table.Element(w + "tblGrid")?.Elements(w + "gridCol").Sum(c => Millimetres(c, w + "w")) - 272 ?? double.PositiveInfinity) < .1);
        Check(pageFrame || tableFrame, "four-sided form frame spanning the 272-mm printable width");
        Check(!documents.Values.Any(d => d.Descendants(w + "documentProtection").Any()), "Word cards remain editable");
        Check(!document.Descendants(w + "altChunk").Any(), "Word content uses native paragraphs and tables");
        Check(
            !text.Contains("Полный набор исходных параметров", StringComparison.Ordinal),
            "form excludes the former parameter appendix"
        );
        Check(
            !text.Contains("Ограничения учебного комплекта", StringComparison.Ordinal),
            "form excludes the former warning appendix"
        );
        CheckFormContent(document, p, route);
        docxFilesChecked++;
    }

    private static double Millimetres(XElement element, XName attribute) =>
        double.Parse(
            (string?)element.Attribute(attribute) ?? throw new InvalidOperationException("Missing page/table dimension " + attribute.LocalName),
            CultureInfo.InvariantCulture
        ) * 25.4 / 1440;


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
        Check(
            !Regex.IsMatch(nominalCells[0], @"\bDb\b|(?<!\w)b\s+\d"),
            "body and flange-width acceptance are not duplicated as confirmed photo tolerances"
        );
        var generalCells = cells.Where(cell => cell.StartsWith("Db ", StringComparison.Ordinal)).ToArray();
        Equal(1, generalCells.Length, "general body and flange-width acceptance is stated once");
        string Ru(double value) => value.ToString("0.####", CultureInfo.GetCultureInfo("ru-RU"));
        string Acceptance(double lo, double hi) => lo == -hi ? "±" + Ru(hi) : "(" + Tolerances.Signed(lo) + "/" + Tolerances.Signed(hi) + ")";
        Check(generalCells[0].Contains(
            "Db " + Ru(p.Params.BodyDiameter) + " " + Acceptance(p.Params.BodyLowerDeviation, p.Params.BodyUpperDeviation),
            StringComparison.Ordinal
        ), "actual body tolerance is explicit");
        Check(generalCells[0].Contains(
            "b " + Ru(p.Params.FlangeWidth) + " " + Acceptance(p.Params.FlangeWidthLowerDeviation, p.Params.FlangeWidthUpperDeviation),
            StringComparison.Ordinal
        ), "actual flange-width tolerance is explicit");
        Check(generalCells[0].Contains("R≤0,5", StringComparison.Ordinal), "maximum allowed corner radius is explicit");
        if (route)
        {
            Check(Regex.IsMatch(text, @"Форма\s*1\b", RegexOptions.IgnoreCase), "route form 1 designation");
            foreach (var marker in new[] { "А", "Б" })
            {
                Check(
                    rows.Any(row => row.Length > 0 && Regex.IsMatch(row[0], "^" + marker + @"\s*\d*$")),
                    "route service marker " + marker
                );
            }

            foreach (var operation in p.Operations)
            {
                Check(text.Contains(operation.Code, StringComparison.Ordinal), "route operation " + operation.Code);
            }

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
            {
                foreach (var column in timeColumns)
                {
                    Check(column < row.Length && string.IsNullOrWhiteSpace(row[column]), "route time standards remain blank");
                }
            }

            return;
        }
        var preparedStock = rows.Single(row => row.Length == 8 && row[0] == "Токарная с ЧПУ");
        var preparedLength = Regex.Match(preparedStock[5], @"×\s*([0-9]+(?:,[0-9]+)?)");
        Check(preparedLength.Success, "operation prepared-stock length is readable");
        Near(p.Calculations.StockLength, FormNumber(preparedLength.Groups[1].Value));
        Check(Regex.IsMatch(text, @"Форма\s*3\b", RegexOptions.IgnoreCase), "operation form 3 designation");
        Check(Regex.IsMatch(text, @"Форма\s*2[аa]\b", RegexOptions.IgnoreCase), "operation continuation form 2a designation");
        foreach (var marker in new[] { "О", "Т", "Р" })
        {
            Check(
                rows.Any(row => row.Length > 0 && Regex.IsMatch(row[0], "^" + marker + @"\s*\d+$")),
                "operation service marker " + marker
            );
        }

        var regimeHeaders = rows.Where(row => row.Length > 0 && row[0] == "Р" && row.Contains("ПИ", StringComparer.Ordinal)).ToArray();
        Check(regimeHeaders.Length > 0, "named operation regime columns");
        foreach (var header in regimeHeaders)
        {
            Sequence(new[] { "Р", "", "ПИ", "D или B", "L", "t", "i", "S", "n", "V" }, header);
        }

        var normHeader = document.Descendants(w + "tr").First(row => row.Elements(w + "tc")
            .Any(cell => NormLabel(string.Concat(cell.Descendants(w + "t").Select(t => t.Value))) == "То"));
        var normLabels = normHeader.Elements(w + "tc").Select(cell => NormLabel(string.Concat(cell.Descendants(w + "t").Select(t => t.Value)))).ToArray();
        var normValues = normHeader.ElementsAfterSelf(w + "tr").First().Elements(w + "tc")
            .Select(cell => string.Concat(cell.Descendants(w + "t").Select(t => t.Value))).ToArray();
        foreach (var label in new[] { "То", "Тв", "Тпз", "Тшт" })
        {
            var column = Array.IndexOf(normLabels, label);
            Check(
                column >= 0 && column < normValues.Length && string.IsNullOrWhiteSpace(normValues[column]),
                "operation standard " + label + " remains blank"
            );
        }
        var regimes = ReadNcTransitionRegimes(p.Nc);
        var regimeRows = rows.Where(row => row.Length > 1 && Regex.IsMatch(row[0], @"^Р\s*\d+$") && Regex.IsMatch(row[1], @"010\.\d{2}")).ToArray();
        Equal(p.Transitions.Count, regimeRows.Length, "one regime row per generated transition");
        foreach (var t in p.Transitions)
        {
            var row = regimeRows.Single(row => Regex.IsMatch(row[1], @"(?<!\d)" + Regex.Escape(t.Id) + @"(?!\d)"));
            Equal(10, row.Length, "operation regime fields");
            Check(
                row.Skip(2).All(cell => !Regex.IsMatch(cell, @"\d\.\d")),
                "Russian operation-card numeric fields use a decimal comma"
            );
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

}
