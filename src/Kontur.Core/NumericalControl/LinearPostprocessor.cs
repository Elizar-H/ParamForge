namespace Kontur.Core;

/// <summary>
/// Numbers NC blocks and associates emitted movements with machining transitions.
/// Controller-specific syntax belongs to the derived postprocessors.
/// </summary>
public abstract class LinearPostprocessor : INcPostprocessor
{
    protected abstract string ControllerHeader
    {
        get;
    }
    protected abstract string GetProgramName(MachiningPlan plan);
    protected abstract string GetFileName(string programName);
    protected abstract List<string> CreateHeader(string programName);
    protected abstract string FormatComment(string text);

    public PostprocessResult Process(MachiningPlan plan)
    {
        var name = GetProgramName(plan);
        var lines = CreateHeader(name);
        void Comment(string text) => lines.Add(FormatComment(text));

        Comment("PARAMFORGE " + Engine.Version + " - " + plan.Id);
        Comment("NOT VERIFIED ON A MACHINE");
        Comment(ControllerHeader + " - MM - X DIAMETER - OPERATION 010 ONLY");
        Comment("TOOL OFFSETS: VIRTUAL SHARP TIP - NOSE R" + Engine.Fmt(ToolingProfile.NoseRadius));
        var c = plan.Calculations;
        Comment($"TARGETS D {Engine.Fmt(c.TargetFlangeDiameter)} d {Engine.Fmt(c.TargetBoreDiameter)} d2 {Engine.Fmt(c.TargetCounterboreDiameter)} L {Engine.Fmt(c.TargetLength)} h {Engine.Fmt(c.TargetCounterboreDepth)}");

        var moves = new List<Move>();
        var transitions = plan.Transitions.Select(t => t with { Blocks = [] }).ToList();
        var number = 0;

        foreach (var instruction in plan.Instructions)
        {
            if (instruction.Kind == NcInstructionKind.Comment)
            {
                Comment(instruction.Text);
                continue;
            }

            var command = FormatInstruction(instruction);
            if (command is null)
            {
                continue;
            }

            number += 10;
            lines.Add("N" + number + " " + command);

            if (instruction.Motion is { } move)
            {
                moves.Add(move with
                {
                    Line = number
                });
                transitions.SingleOrDefault(t => t.Id == move.Transition)?.Blocks.Add(number);
            }
        }

        return new(
            string.Join("\r\n", lines) + "\r\n",
            GetFileName(name),
            name,
            moves,
            transitions);
    }

    protected virtual string? FormatInstruction(NcInstruction instruction) => instruction.Kind switch
    {
        NcInstructionKind.CoolantOff => "M9",
        NcInstructionKind.Stop => "M5",
        NcInstructionKind.CoolantOn => "M8",
        NcInstructionKind.Speed => "S" + Engine.Fmt(instruction.Value) + " M3",
        NcInstructionKind.FeedPerRevolution => "G95",
        NcInstructionKind.FeedPerMinute => "G94",
        NcInstructionKind.End => "M30",
        NcInstructionKind.Motion => FormatMotion(instruction.Motion!),
        _ => throw new InvalidOperationException("Неизвестная инструкция плана обработки.")
    };

    private static string FormatMotion(Move move) =>
        move.Mode + " X" + Engine.Fmt(move.X) + " Z" + Engine.Fmt(move.Z)
        + (move.Mode == "G1" ? " F" + Engine.Fmt(move.Feed!.Value) : "");
}
