namespace Kontur.Core;

public record NcInstruction(NcInstructionKind Kind, Move? Motion = null, int Tool = 0, double Value = 0, string Text = "");
