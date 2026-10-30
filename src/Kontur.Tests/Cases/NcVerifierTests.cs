using System.Text.RegularExpressions;

using Kontur.Core;

namespace Kontur.Tests;

internal static partial class RegressionSuite
{

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
            Check(v.Ok, string.Join("; ", v.Errors));
            Equal(4, v.Moves.Count);
            Near(.85, v.CutMinutes, 1e-12);
            Near(.85, ReadGeneratedNc(Fixture).Minutes, 1e-12);
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
            Check(v.Ok, string.Join("; ", v.Errors));
            Near(.85, v.CutMinutes, 1e-12);
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
            Add(
                "verifier rejects malformed numeric token " + value,
                () => InvalidNc(Fixture.Replace("X120 Z-12", "X" + value + " Z-12", StringComparison.Ordinal))
            );
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
            Check(v.Ok, string.Join("; ", v.Errors));
            Near(1.5, v.CutMinutes, 1e-12);
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
            InvalidNc(null);
            InvalidNc("");
            InvalidNc(Fixture, null, false);
            InvalidNc(Fixture, Engine.Defaults with
            {
                Length = double.NaN
            });
            InvalidNc(Fixture, Engine.Defaults with
            {
                StockDiameter = -1
            });
            InvalidNc(Fixture, Engine.Defaults with
            {
                RpmMax = 0
            });
            InvalidNc(Fixture, Engine.Defaults with
            {
                LengthLowerDeviation = double.PositiveInfinity
            });
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
            Check(
                v.Warnings.Any(w => w.Contains("Не проверяются столкновения", StringComparison.Ordinal)),
                "must disclose absence of collision checks"
            );
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

}
