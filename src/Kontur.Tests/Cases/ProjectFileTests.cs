using System.Globalization;

using System.Text;

using System.Text.RegularExpressions;

using Kontur.Core;

namespace Kontur.Tests;

internal static partial class RegressionSuite
{

    private static void RegisterFileTests()
    {
        Add("JSON partial inputs and supported wrappers merge with typed defaults", () =>
        {
            using var temp = new TempWorkspace();
            foreach (var json in new[] { "{\"length\":100}", "{\"params\":{\"length\":100}}", "{\"project\":{\"params\":{\"length\":100}}}" })
            {
                var path = temp.Write("input.json", json);
                var p = ProjectFiles.ImportParameters(path);
                Near(100, p.Length);
                Near(47.3, p.BoreDiameter);
                Near(-1, p.LengthLowerDeviation);
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
            var bad = Path.Combine(temp.Root, "bad.json");
            File.WriteAllBytes(bad, [0xff, 0xfe, 0x7b]);
            Throws<InvalidDataException>(() => ProjectFiles.ImportParameters(bad));
            var bom = Path.Combine(temp.Root, "bom.json");
            File.WriteAllBytes(bom, [0xef, 0xbb, 0xbf, 0x7b, 0x7d]);
            Equal(Engine.Defaults, ProjectFiles.ImportParameters(bom));
        });
        Add("parameter save is separate from the exact three-file reproducible export", () =>
        {
            using var temp = new TempWorkspace();
            var p = Generated(Engine.Defaults with
            {
                PartName = "Втулка <A>&",
                Length = 100,
                DepthOutside = .7
            });
            var saved = Path.Combine(temp.Root, "saved.json");
            ProjectFiles.SaveParameters(p, saved);
            var imported = ProjectFiles.ImportParameters(saved);
            Equal(p.Params, imported, "saved parameters round trip");
            var regenerated = Generated(imported);
            Equal(p.Id, regenerated.Id);
            Equal(p.Nc, regenerated.Nc);
            Equal(p.RouteHtml, regenerated.RouteHtml);
            Equal(p.TechnologyHtml, regenerated.TechnologyHtml);
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
                    if (!start.SignalAndWait(TimeSpan.FromSeconds(10)))
                    {
                        throw new TimeoutException("Concurrent export start barrier timed out");
                    }

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
                {
                    Sequence(references[winners[0].Index][Path.GetFileName(file)], File.ReadAllBytes(file));
                }
            }
        });
        Add("parameter saving replaces atomically and preserves locked or invalid targets", () =>
        {
            using var temp = new TempWorkspace();
            var first = Generated();
            var replacement = Generated(Engine.Defaults with
            {
                Length = 100,
                CounterboreDepth = 40
            });
            var path = Path.Combine(temp.Root, "saved.json");
            ProjectFiles.SaveParameters(first, path);
            Equal(first.Params, ProjectFiles.ImportParameters(path), "new parameter file imports");
            ProjectFiles.SaveParameters(replacement, path);
            Equal(replacement.Params, ProjectFiles.ImportParameters(path), "replacement parameter file imports");
            var preserved = File.ReadAllBytes(path);
            void MustFailWithoutWriting(string target)
            {
                Exception? failure = null;
                try
                {
                    ProjectFiles.SaveParameters(first, target);
                }
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
            Sequence(
                new[] { "directory-target", "saved.json" },
                Directory.GetFileSystemEntries(temp.Root).Select(Path.GetFileName).Order(StringComparer.Ordinal)
            );
            Sequence(preserved, File.ReadAllBytes(path));
        });
        Add("reordered JSON keys have identical revision and NC", () =>
        {
            using var temp = new TempWorkspace();
            var a = ProjectFiles.ImportParameters(temp.Write("a.json", "{\"length\":100,\"bodyDiameter\":95}"));
            var b = ProjectFiles.ImportParameters(temp.Write("b.json", "{\"bodyDiameter\":95,\"length\":100}"));
            Equal(Generated(a).Id, Generated(b).Id);
            Equal(Generated(a).Nc, Generated(b).Nc);
        });
        Add("Word process forms have editable OOXML, service fields and page geometry", () =>
        {
            using var temp = new TempWorkspace();
            var p = Generated(Engine.Defaults with
            {
                PartName = "Втулка <A>& \"Б\""
            });
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
                Equal(baseline.Id, localized.Id);
                Equal(baseline.Nc, localized.Nc);
                Equal(baseline.RouteHtml, localized.RouteHtml);
                Equal(baseline.TechnologyHtml, localized.TechnologyHtml);
            }
            finally { CultureInfo.CurrentCulture = original; CultureInfo.CurrentUICulture = originalUi; }
        });
        Add("export rejects tampered NC before creating its destination", () =>
        {
            using var temp = new TempWorkspace();
            var p = Generated();
            var target = Path.Combine(temp.Root, "rejected");
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

}
