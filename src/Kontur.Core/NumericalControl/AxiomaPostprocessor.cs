namespace Kontur.Core;

/// <summary>Formats the explicit linear instruction subset for AxiOMA Control.</summary>
public sealed class AxiomaPostprocessor : LinearPostprocessor
{
    protected override string ControllerHeader => "AXIOMA CONTROL";
    protected override string GetProgramName(MachiningPlan plan) => plan.Id;
    protected override string GetFileName(string programName) => "Управляющая программа.nc";
    protected override List<string> CreateHeader(string programName) => ["% " + programName];
    protected override string FormatComment(string text) => "(" + text + ")";

    protected override string? FormatInstruction(NcInstruction instruction) => instruction.Kind switch
    {
        NcInstructionKind.Prepare => "G21 G18 G90 G94 G97 G40 G49 G80 G54",
        NcInstructionKind.Diameter => "G10 X1",
        NcInstructionKind.Tool => "T" + instruction.Tool + " D1 M66",
        NcInstructionKind.GeometryOffset => "G43",
        _ => base.FormatInstruction(instruction)
    };
}
