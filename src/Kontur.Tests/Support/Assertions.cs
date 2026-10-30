using System.Reflection;

using Kontur.Core;

namespace Kontur.Tests;

internal static partial class RegressionSuite
{

    private static void Add(string name, Action body) => Tests.Add((name, body));

    private static void Check(bool condition, string message)
    {
        assertions++;
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private static void Equal<T>(T expected, T actual, string message = "equality") =>
        Check(EqualityComparer<T>.Default.Equals(expected, actual), $"{message}: expected {expected}, got {actual}");

    private static void Near(double expected, double actual, double epsilon = 1e-9) =>
        Check(
            double.IsFinite(actual) && Math.Abs(expected - actual) <= epsilon,
            $"expected {expected:R} ± {epsilon:R}, got {actual:R}"
        );

    private static void Sequence<T>(IEnumerable<T> expected, IEnumerable<T> actual) =>
        Check(
            expected.SequenceEqual(actual),
            "sequence differs: expected [" + string.Join(",", expected) + "], got [" + string.Join(",", actual) + "]"
        );

    private static PropertyInfo ParameterProperty(string key) => typeof(Parameters).GetProperty(key, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase)
        ?? throw new InvalidOperationException("Missing parameter property: " + key);

    private static double Value(Parameters p, string key) => (double)ParameterProperty(key).GetValue(p)!;

    private static Parameters Change(Parameters p, string key, double value)
    {
        var changed = p with
        {
        };
        if (key is "bodyLowerDeviation" or "bodyUpperDeviation")
        {
            changed = changed with
            {
                BodyTolerance = ""
            };
        }

        if (key is "flangeWidthLowerDeviation" or "flangeWidthUpperDeviation")
        {
            changed = changed with
            {
                FlangeWidthTolerance = ""
            };
        }

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
        if (field is not null)
        {
            Check(
                result.Errors.Any(e => e.Field == field && e.Message.Length > 0),
                "missing diagnostic field " + field + ": " + string.Join("; ", result.Errors.Select(e => e.Field))
            );
        }
    }

    private static Parameters ZeroTolerances(Parameters p) => p with
    {
        BoreLowerDeviation = 0,
        BoreUpperDeviation = 0,
        CounterboreLowerDeviation = 0,
        CounterboreUpperDeviation = 0,
        FlangeLowerDeviation = 0,
        FlangeUpperDeviation = 0,
        LengthLowerDeviation = 0,
        LengthUpperDeviation = 0,
        CounterboreDepthLowerDeviation = 0,
        CounterboreDepthUpperDeviation = 0
    };

    private static void TargetsWithinIntervals(Project project)
    {
        var p = project.Params;
        var c = project.Calculations;
        foreach (var item in new[]
        {
            (c.TargetBoreDiameter, p.BoreDiameter + p.BoreLowerDeviation, p.BoreDiameter + p.BoreUpperDeviation),
            (c.TargetCounterboreDiameter, p.CounterboreDiameter + p.CounterboreLowerDeviation, p.CounterboreDiameter + p.CounterboreUpperDeviation),
            (c.TargetFlangeDiameter, p.FlangeDiameter + p.FlangeLowerDeviation, p.FlangeDiameter + p.FlangeUpperDeviation),
            (c.TargetLength, p.Length + p.LengthLowerDeviation, p.Length + p.LengthUpperDeviation),
            (c.TargetCounterboreDepth, p.CounterboreDepth + p.CounterboreDepthLowerDeviation, p.CounterboreDepth + p.CounterboreDepthUpperDeviation)
        })
        {
            Check(item.Item1 >= item.Item2 - 1e-9 && item.Item1 <= item.Item3 + 1e-9, "target outside tolerance interval");
        }
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

    private static void Throws<T>(Action action) where T : Exception
    {
        assertions++;
        try
        {
            action();
        }
        catch (T) { return; }
        catch (Exception ex) { throw new InvalidOperationException("Expected " + typeof(T).Name + ", got " + ex.GetType().Name + ": " + ex.Message); }
        throw new InvalidOperationException("Expected " + typeof(T).Name + " but operation succeeded");
    }

    private static void CheckExportFiles(string directory, IReadOnlyList<string> files, string ncName = "Управляющая программа.nc")
    {
        Equal(3, files.Count, "exact export file count");
        string[] expectedNames = ["Маршрутная карта.docx", "Технологическая карта.docx", ncName];
        Sequence(expectedNames.Order(StringComparer.Ordinal), files.Select(Path.GetFileName).Order(StringComparer.Ordinal));
        Sequence(
            expectedNames.Order(StringComparer.Ordinal),
            Directory.GetFileSystemEntries(directory).Select(Path.GetFileName).Order(StringComparer.Ordinal)
        );
        foreach (var file in files)
        {
            Equal(
                Path.GetFullPath(directory),
                Path.GetDirectoryName(Path.GetFullPath(file)),
                "export path belongs to the selected directory"
            );
            Check(new FileInfo(file).Length > 0, "nonempty export file " + Path.GetFileName(file));
        }
    }

}
