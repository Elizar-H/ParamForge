namespace Kontur.Core;

public record ParsedMove(double X, double Z, string Mode, int Line, double? Feed, double? Rpm, int PhysicalLine, int? Tool);
