using System.Diagnostics;

using System.Text;

using System.Text.Json;

using Kontur.Core;

namespace Kontur.Tests;

internal static partial class RegressionSuite
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


    public static int Run(string[] args)
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
                {
                    throw new ArgumentException("Unknown option or missing value: " + args[i]);
                }

                var option = args[i++];
                switch (option)
                {
                    case "--report":
                        reportPath = Path.GetFullPath(args[i]);
                        break;
                    case "--export":
                        exportPath = Path.GetFullPath(args[i]);
                        break;
                    default:
                        inputPath = Path.GetFullPath(args[i]);
                        break;
                }
            }
            if (inputPath is not null && exportPath is null)
            {
                throw new ArgumentException("--input requires --export.");
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }

        RegisterTests();
        if (exportPath is not null)
        {
            Add("CLI exports the requested reproducible project", () => ExportRequested(exportPath, inputPath));
        }

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

    private static void ExportRequested(string directory, string? input)
    {
        var parameters = input is null ? Engine.Defaults : ProjectFiles.ImportParameters(input);
        var p = Generated(parameters);
        var files = ProjectFiles.Export(p, directory);
        CheckExportFiles(directory, files, p.NcFileName);
        CheckDocx(files.Single(f => Path.GetFileName(f) == "Маршрутная карта.docx"), p, route: true);
        CheckDocx(files.Single(f => Path.GetFileName(f) == "Технологическая карта.docx"), p, route: false);
        Evidence["export"] = new
        {
            directory,
            input,
            p.Id,
            files
        };
    }


    private sealed record TestResult(string Name, bool Passed, int Assertions, double DurationMilliseconds, string? Error);

}
