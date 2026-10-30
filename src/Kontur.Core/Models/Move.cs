namespace Kontur.Core;

public record Move(double X, double Z, string Mode, string Transition, int Line, double? Feed, double Rpm);
