namespace Kontur.Core;

public record VerificationResult(bool Ok, List<string> Errors, List<string> Warnings, List<ParsedMove> Moves, double CutMinutes);
