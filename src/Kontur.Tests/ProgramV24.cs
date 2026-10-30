using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using Kontur.Core;

namespace Kontur.Tests;

internal static partial class Program
{
    private static void RegisterV24Tests()
    {
        foreach (var (size, grade, expected) in new[] { (3d,6,.006), (3.001,6,.008), (6d,7,.012), (6.001,7,.015),
            (18d,8,.027), (30d,7,.021), (30.001,7,.025), (50d,7,.025), (50.001,7,.030), (75d,14,.740),
            (120d,14,.870), (120.001,14,1d), (180d,16,2.5), (180.001,16,2.9), (250d,7,.046) })
            Add($"ISO IT reference size {size} grade {grade}", () => Near(expected, Tolerances.Width(size, grade)));
        Add("standard tolerance lookup rejects unsupported ranges", () =>
        {
            foreach (var size in new[] {0d, -1, 250.001, double.NaN, double.PositiveInfinity}) Throws<ArgumentException>(() => Tolerances.Width(size, 7));
            Throws<ArgumentException>(() => Tolerances.Width(30, 5)); Throws<ArgumentException>(() => Tolerances.Width(30, 17));
        });
        Add("JS odd micrometre width survives rounding serialization and Word", () =>
        {
            var parameters = Tolerances.SetDesignation(Engine.Defaults, "boreDiameter", "JS7");
            Near(-.0125, parameters.BoreLowerDeviation); Near(.0125, parameters.BoreUpperDeviation);
            var project = Generated(parameters); TargetsWithinIntervals(project);
            using var temp = new TempWorkspace();
            var path = Path.Combine(temp.Root, "project.json"); ProjectFiles.SaveParameters(project, path);
            var restored = ProjectFiles.ImportParameters(path); Equal("JS7", restored.BoreTolerance);
            Equal(project.Id, Generated(restored).Id); Near(-.0125, restored.BoreLowerDeviation);
            var files = ProjectFiles.Export(project, Path.Combine(temp.Root, "cards"));
            foreach (var doc in files.Where(f => f.EndsWith(".docx", StringComparison.Ordinal)))
            {
                using var zip = ZipFile.OpenRead(doc); using var reader = new StreamReader(zip.GetEntry("word/document.xml")!.Open());
                var xml = reader.ReadToEnd(); Check(xml.Contains("0,0125", StringComparison.Ordinal), "no half-micrometre truncation"); Check(xml.Contains("JS7", StringComparison.Ordinal), "designation in card");
            }
        });
        Add("nominal changes recompute standards without changing manual defaults", () =>
        {
            var h = Tolerances.SetDesignation(Engine.Defaults, "boreDiameter", "H7"); Near(.025, h.BoreUpperDeviation);
            var changed = Tolerances.Resolve(h with { BoreDiameter = 50.001 }); Near(.030, changed.BoreUpperDeviation);
            var manual = Tolerances.SetDesignation(h, "boreDiameter", "");
            Near(.025, Tolerances.Resolve(manual with { BoreDiameter = 60 }).BoreUpperDeviation);
            Equal("", Engine.Defaults.BoreTolerance); Near(1, Engine.Defaults.BoreUpperDeviation);
        });
        Add("standard position must match the size type", () =>
        {
            foreach (var p in new[] { Engine.Defaults with { BoreTolerance = "h7" }, Engine.Defaults with { FlangeTolerance = "H7" },
                Engine.Defaults with { LengthTolerance = "JS7" }, Engine.Defaults with { BoreTolerance = "H5" }, Engine.Defaults with { BoreTolerance = null! } }) Rejected(p);
        });
        Add("four-decimal deviations are accepted but unrepresentable NC targets are rejected", () =>
        {
            Generated(Engine.Defaults with { BoreLowerDeviation = .0001, BoreUpperDeviation = .0251 });
            Rejected(Engine.Defaults with { BoreLowerDeviation = .0001, BoreUpperDeviation = .0002 }, "boreUpperDeviation");
            Rejected(Engine.Defaults with { BoreUpperDeviation = .02501 }, "boreUpperDeviation");
        });
        Add("old parameter JSON defaults to manual AxiOMA; metadata roundtrips", () =>
        {
            using var temp = new TempWorkspace();
            var old = ProjectFiles.ImportParameters(temp.Write("old.json", "{\"length\":100}"));
            Equal(CncProfiles.Axioma, old.Controller); Equal("", old.LengthTolerance);
            var p = Generated(Engine.Defaults with { Controller = CncProfiles.Sinumerik, BoreTolerance = "H7", FlangeTolerance = "js8", LengthTolerance = "IT9" });
            var path = Path.Combine(temp.Root, "new.json"); ProjectFiles.SaveParameters(p, path);
            Equal(p.Params, ProjectFiles.ImportParameters(path));
            foreach (var json in new[] { "{\"controller\":\"unknown\"}", "{\"boreTolerance\":7}", "{\"boreTolerance\":\"H17\"}" })
                Throws<InvalidDataException>(() => ProjectFiles.ImportParameters(temp.Write("bad.json", json)));
        });
        foreach (var parameters in new[] { Engine.Defaults, Engine.Defaults with { Length = 80, CounterboreDepth = 10 },
            Engine.Defaults with { BoreTolerance = "H7", CounterboreTolerance = "JS8", FlangeTolerance = "h7", LengthTolerance = "IT8", CounterboreDepthTolerance = "IT7" } })
            Add("both postprocessors restore the same geometry " + parameters.Length + " " + parameters.BoreTolerance, () =>
            {
                var a = Generated(parameters with { Controller = CncProfiles.Axioma });
                var b = Generated(parameters with { Controller = CncProfiles.Sinumerik });
                Check(a.Id != b.Id, "profile belongs to revision identity");
                Equal(a.Verification.Moves.Count, b.Verification.Moves.Count); Near(a.Verification.CutMinutes, b.Verification.CutMinutes);
                for (var i = 0; i < a.Verification.Moves.Count; i++)
                {
                    var x = a.Verification.Moves[i]; var y = b.Verification.Moves[i];
                    Equal(x.X, y.X); Equal(x.Z, y.Z); Equal(x.Mode, y.Mode); Equal(x.Tool, y.Tool); Equal(x.Feed, y.Feed); Equal(x.Rpm, y.Rpm);
                }
                TargetsWithinIntervals(b); CheckNcAgreement(b);
                foreach (var transition in b.Transitions)
                    foreach (var block in transition.Blocks) Check(b.Verification.Moves.Any(m => m.Line == block), "Siemens block map points to emitted motion");
            });
        var siemens = Generated(Engine.Defaults with { Controller = CncProfiles.Sinumerik });
        foreach (var (name, mutation) in new (string, Func<string,string>)[]
        {
            ("missing diameter mode", s => s.Replace("DIAMON", "")), ("AxiOMA units", s => s.Replace("G710", "G21")),
            ("wrong corrector", s => s.Replace("T1 D1", "T1 D0")), ("foreign tool change", s => s.Replace("T1 D1", "T1 D1 M66")),
            ("missing coolant", s => Regex.Replace(s, @"N\d+ M8\r\n", "")), ("missing feed mode", s => s.Replace("G95", "G94")),
            ("missing stop", s => Regex.Replace(s, @"N\d+ M5\r\n", "")),
            ("bad header", s => s.Replace("%_N_PF_", "%_N_PF-")), ("motion after end", s => s + "N99999 G0 X128 Z3\r\n"),
            ("coordinate overflow", s => s.Replace("X128 Z3", "X129 Z3")),
            ("diameter command mixed with motion", s => s.Replace("DIAMON", "G0 X128 Z3 DIAMON"))
        }) Add("Siemens verifier rejects " + name, () => Check(!NcVerifier.Verify(mutation(siemens.Nc), siemens.Params).Ok, name));
        Add("dialect verifiers reject the other dialect", () =>
        {
            Check(!new AxiomaVerifier().Verify(siemens.Nc, siemens.Params).Ok, "AxiOMA must reject Siemens");
            Check(!new SinumerikVerifier().Verify(Generated().Nc, Engine.Defaults).Ok, "Siemens must reject AxiOMA");
        });
        Add("Siemens exports exactly two cards and one ASCII MPF with matching names", () =>
        {
            using var temp = new TempWorkspace(); var directory = Path.Combine(temp.Root, "siemens");
            var files = ProjectFiles.Export(siemens, directory); CheckExportFiles(directory, files, siemens.NcFileName);
            Check(Regex.IsMatch(siemens.NcFileName, @"^PF_[0-9A-F]{16}\.MPF$"), "portable program file name");
            var bytes = File.ReadAllBytes(Path.Combine(directory, siemens.NcFileName)); Check(bytes.All(b => b <= 127), "ASCII no BOM");
            Check(siemens.Nc.StartsWith("%_N_" + siemens.ProgramName + "_MPF\r\n", StringComparison.Ordinal), "matching internal header");
            Check(!siemens.Nc.Replace("\r\n", "").Contains('\n'), "CRLF");
            foreach (var doc in files.Where(f => f.EndsWith(".docx", StringComparison.Ordinal)))
            {
                using var zip = ZipFile.OpenRead(doc); using var reader = new StreamReader(zip.GetEntry("word/document.xml")!.Open()); var xml = reader.ReadToEnd();
                Check(xml.Contains("SINUMERIK 840D sl", StringComparison.Ordinal), "profile in card");
                Check(xml.Contains(siemens.NcFileName, StringComparison.Ordinal), "file designation in card");
            }
            Throws<IOException>(() => ProjectFiles.Export(siemens, directory));
        });
    }
}
