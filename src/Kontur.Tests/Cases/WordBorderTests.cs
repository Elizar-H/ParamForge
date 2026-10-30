using System.IO.Compression;
using System.Xml.Linq;
using Kontur.Core;

namespace Kontur.Tests;

internal static partial class RegressionSuite
{
    private static void RegisterWordBorderTests()
    {
        Add("Word nested form borders fit inside the parent content width", () =>
        {
            using var temp = new TempWorkspace();
            var files = ProjectFiles.Export(Generated(Engine.Defaults), Path.Combine(temp.Root, "cards"));
            XNamespace w = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
            foreach (var doc in files.Where(f => f.EndsWith(".docx", StringComparison.Ordinal)))
            {
                using var zip = ZipFile.OpenRead(doc);
                using var stream = zip.GetEntry("word/document.xml")!.Open();
                var xml = XDocument.Load(stream);
                var nested = xml.Descendants(w + "tc").Where(c => c.Element(w + "tbl") is not null).ToArray();
                Check(nested.Length > 0, "header blocks present");
                foreach (var cell in nested)
                {
                    var width = (int)cell.Element(w + "tcPr")!.Element(w + "tcW")!.Attribute(w + "w")!;
                    var parentDefault = cell.Parent!.Parent!.Element(w + "tblPr")!.Element(w + "tblCellMar");
                    int Margin(string side) => (int?)(cell.Element(w + "tcPr")!.Element(w + "tcMar")?.Element(w + side)?.Attribute(w + "w")
                        ?? parentDefault?.Element(w + side)?.Attribute(w + "w")) ?? 0;
                    var table = cell.Element(w + "tbl")!;
                    var childWidth = (int)table.Element(w + "tblPr")!.Element(w + "tblW")!.Attribute(w + "w")!;
                    Check(childWidth + Margin("left") + Margin("right") <= width, "nested border must fit, including cell padding");
                    Equal(
                        "single",
                        (string)table.Element(w + "tblPr")!.Element(w + "tblBorders")!.Element(w + "right")!.Attribute(w + "val")!
                    );
                }
            }
        });
    }
}
