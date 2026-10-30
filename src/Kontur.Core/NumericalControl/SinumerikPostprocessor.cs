namespace Kontur.Core;

/// <summary>Formats native SINUMERIK 840D sl instructions without machining cycles.</summary>
public sealed class SinumerikPostprocessor : LinearPostprocessor
{
    protected override string ControllerHeader => "SINUMERIK 840D SL";
    protected override string GetProgramName(MachiningPlan plan) => "PF_" + plan.Id[2..];
    protected override string GetFileName(string programName) => programName + ".MPF";
    protected override List<string> CreateHeader(string programName) =>
        ["%_N_" + programName + "_MPF", ";$PATH=/_N_MPF_DIR"];
    protected override string FormatComment(string text) => "; " + text;

    protected override string? FormatInstruction(NcInstruction instruction) => instruction.Kind switch
    {
        NcInstructionKind.Prepare => "G710 G18 G90 G94 G97 G40 G54",
        NcInstructionKind.Diameter => "DIAMON",
        NcInstructionKind.Tool => "T" + instruction.Tool + " D1",
        NcInstructionKind.GeometryOffset => null,
        _ => base.FormatInstruction(instruction)
    };
}
