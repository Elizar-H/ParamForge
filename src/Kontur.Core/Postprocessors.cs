namespace Kontur.Core;

public static class CncProfiles
{
    public const string Axioma = "axioma";
    public const string Sinumerik = "sinumerik840dsl";
    public static bool Supported(string profile) => profile is Axioma or Sinumerik;
    public static string Name(string profile) => profile switch
    {
        Axioma => "AxiOMA Control", Sinumerik => "SINUMERIK 840D sl", _ => throw new ArgumentException("Неизвестный профиль ЧПУ.")
    };
    public static INcPostprocessor Postprocessor(string profile) => profile switch
    {
        Axioma => new AxiomaPostprocessor(), Sinumerik => new SinumerikPostprocessor(), _ => throw new ArgumentException("Неизвестный профиль ЧПУ.")
    };
    public static INcVerifier Verifier(string profile) => profile switch
    {
        Axioma => new AxiomaVerifier(), Sinumerik => new SinumerikVerifier(), _ => throw new ArgumentException("Неизвестный профиль ЧПУ.")
    };
}

public enum NcInstructionKind { Prepare, Diameter, CoolantOff, Stop, Tool, GeometryOffset, CoolantOn, Speed,
    FeedPerRevolution, FeedPerMinute, Comment, Motion, End }
public record NcInstruction(NcInstructionKind Kind, Move? Motion = null, int Tool = 0, double Value = 0, string Text = "");
public record MachiningPlan(string Id, Calculations Calculations, List<Transition> Transitions, List<NcInstruction> Instructions);
public record PostprocessResult(string Text, string FileName, string ProgramName, List<Move> Moves, List<Transition> Transitions);
public interface INcPostprocessor { PostprocessResult Process(MachiningPlan plan); }
public interface INcVerifier { VerificationResult Verify(string? nc, Parameters? parameters); }

public abstract class LinearPostprocessor : INcPostprocessor
{
    protected abstract bool Siemens { get; }
    public PostprocessResult Process(MachiningPlan plan)
    {
        var name = Siemens ? "PF_" + plan.Id[2..] : plan.Id;
        var lines = Siemens ? new List<string> { "%_N_" + name + "_MPF", ";$PATH=/_N_MPF_DIR" } : new List<string> { "% " + name };
        void Comment(string text) => lines.Add(Siemens ? "; " + text : "(" + text + ")");
        Comment("PARAMFORGE " + Engine.Version + " - " + plan.Id);
        Comment("NOT VERIFIED ON A MACHINE");
        Comment((Siemens ? "SINUMERIK 840D SL" : "AXIOMA CONTROL") + " - MM - X DIAMETER - OPERATION 010 ONLY");
        Comment("TOOL OFFSETS: VIRTUAL SHARP TIP - NOSE R" + Engine.Fmt(ToolingProfile.NoseRadius));
        var c = plan.Calculations;
        Comment($"TARGETS D {Engine.Fmt(c.TargetFlangeDiameter)} d {Engine.Fmt(c.TargetBoreDiameter)} d2 {Engine.Fmt(c.TargetCounterboreDiameter)} L {Engine.Fmt(c.TargetLength)} h {Engine.Fmt(c.TargetCounterboreDepth)}");
        var moves = new List<Move>();
        var transitions = plan.Transitions.Select(t => t with { Blocks = [] }).ToList();
        var number = 0;
        foreach (var instruction in plan.Instructions)
        {
            if (instruction.Kind == NcInstructionKind.Comment) { Comment(instruction.Text); continue; }
            var command = instruction.Kind switch
            {
                NcInstructionKind.Prepare => Siemens ? "G710 G18 G90 G94 G97 G40 G54" : "G21 G18 G90 G94 G97 G40 G49 G80 G54",
                NcInstructionKind.Diameter => Siemens ? "DIAMON" : "G10 X1",
                NcInstructionKind.CoolantOff => "M9", NcInstructionKind.Stop => "M5",
                NcInstructionKind.Tool => "T" + instruction.Tool + " D1" + (Siemens ? "" : " M66"),
                NcInstructionKind.GeometryOffset => Siemens ? null : "G43",
                NcInstructionKind.CoolantOn => "M8", NcInstructionKind.Speed => "S" + Engine.Fmt(instruction.Value) + " M3",
                NcInstructionKind.FeedPerRevolution => "G95", NcInstructionKind.FeedPerMinute => "G94",
                NcInstructionKind.End => "M30",
                NcInstructionKind.Motion => instruction.Motion!.Mode + " X" + Engine.Fmt(instruction.Motion.X) + " Z" + Engine.Fmt(instruction.Motion.Z)
                    + (instruction.Motion.Mode == "G1" ? " F" + Engine.Fmt(instruction.Motion.Feed!.Value) : ""),
                _ => throw new InvalidOperationException("Неизвестная инструкция плана обработки.")
            };
            if (command is null) continue;
            number += 10;
            lines.Add("N" + number + " " + command);
            if (instruction.Motion is { } move)
            {
                moves.Add(move with { Line = number });
                transitions.SingleOrDefault(t => t.Id == move.Transition)?.Blocks.Add(number);
            }
        }
        return new(string.Join("\r\n", lines) + "\r\n", Siemens ? name + ".MPF" : "Управляющая программа.nc", name, moves, transitions);
    }
}
public sealed class AxiomaPostprocessor : LinearPostprocessor { protected override bool Siemens => false; }
public sealed class SinumerikPostprocessor : LinearPostprocessor { protected override bool Siemens => true; }

public sealed class AxiomaVerifier : INcVerifier
{
    public VerificationResult Verify(string? nc, Parameters? parameters) => NcSubsetVerifier.Verify(nc, parameters, false);
}
public sealed class SinumerikVerifier : INcVerifier
{
    public VerificationResult Verify(string? nc, Parameters? parameters) => NcSubsetVerifier.Verify(nc, parameters, true);
}
