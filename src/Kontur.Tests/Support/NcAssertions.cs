using System.Globalization;

using System.Text.RegularExpressions;

using Kontur.Core;

namespace Kontur.Tests;

internal static partial class RegressionSuite
{
    private static void CheckNcAgreement(Project p)
    {
        var verified = NcVerifier.Verify(p.Nc, p.Params);
        Check(verified.Ok, string.Join("; ", verified.Errors));
        Equal(p.Moves.Count, verified.Moves.Count, "verifier move count");
        var read = ReadGeneratedNc(p.Nc);
        Equal(p.Moves.Count, read.Moves.Count, "independent move count");
        for (var i = 0; i < p.Moves.Count; i++)
        {
            var expected = p.Moves[i];
            var actual = read.Moves[i];
            var checkedMove = verified.Moves[i];
            Near(expected.X, actual.X);
            Near(expected.Z, actual.Z);
            Equal(expected.Mode, actual.Mode);
            Equal(expected.Line, actual.Line);
            Near(actual.X, checkedMove.X);
            Near(actual.Z, checkedMove.Z);
            Equal(actual.Mode, checkedMove.Mode);
            Equal(actual.Line, checkedMove.Line);
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
            if (line.Length == 0 || line.StartsWith("%", StringComparison.Ordinal))
            {
                continue;
            }

            var words = Regex.Matches(line.ToUpperInvariant(), @"([A-Z])([+-]?(?:\d+(?:\.\d*)?|\.\d+))")
                .Select(m => (Letter: m.Groups[1].Value, Number: double.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture))).ToArray();
            if (words.Any(w => w.Letter == "T"))
            {
                previous = null;
            }

            var speed = words.FirstOrDefault(w => w.Letter == "S");
            if (speed.Letter is not null)
            {
                rpm = speed.Number;
            }

            var motion = words.FirstOrDefault(w => w.Letter == "G" && w.Number is 0 or 1);
            if (motion.Letter is null)
            {
                continue;
            }

            var x = words.Single(w => w.Letter == "X").Number;
            var z = words.Single(w => w.Letter == "Z").Number;
            if (motion.Number == 1)
            {
                var feed = words.Single(w => w.Letter == "F").Number;
                if (previous is null || feed <= 0 || rpm <= 0)
                {
                    throw new InvalidOperationException("Incomplete G1 timing state");
                }

                var radial = .5 * (x - previous.Value.X);
                var axial = z - previous.Value.Z;
                minutes += Math.Sqrt(radial * radial + axial * axial) / feed / rpm;
            }
            moves.Add(new(x, z, motion.Number == 0 ? "G0" : "G1", (int)words.Single(w => w.Letter == "N").Number));
            previous = (x, z);
        }
        return new(moves, minutes);
    }


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
            if (!Regex.IsMatch(line, @"^N\d+\s"))
            {
                continue;
            }

            var words = Regex.Matches(line, @"([A-Z])([+-]?(?:\d+(?:\.\d*)?|\.\d+))")
                .Select(m => (Letter: m.Groups[1].Value, Number: double.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture))).ToArray();
            var speed = words.FirstOrDefault(word => word.Letter == "S");
            if (speed.Letter is not null)
            {
                rpm = speed.Number;
            }

            if (words.Any(word => word.Letter == "T"))
            {
                var selection = words.Single(word => word.Letter == "T").Number;
                Check(selection == Math.Floor(selection) && selection is 1 or 2, "NC tool selection is an explicit known tool");
                Near(1, words.Single(word => word.Letter == "D").Number);
                tool = (int)selection;
            }
            if (!words.Any(word => word.Letter == "G" && word.Number == 1))
            {
                continue;
            }

            if (transition is null)
            {
                throw new InvalidOperationException("G1 appears outside a tagged transition");
            }

            var feed = words.Single(word => word.Letter == "F").Number;
            if (tool is null)
            {
                throw new InvalidOperationException("G1 has no independently selected tool");
            }

            result[transition].Add(new((int)words.Single(word => word.Letter == "N").Number, feed, rpm, tool.Value));
        }
        return result;
    }

    private sealed record ReadMove(double X, double Z, string Mode, int Line);

    private sealed record NcRead(List<ReadMove> Moves, double Minutes);

    private sealed record NcRegime(int Line, double Feed, double Rpm, int Tool);

}
