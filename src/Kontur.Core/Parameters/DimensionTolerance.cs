namespace Kontur.Core;

public record DimensionTolerance(
    string Key,
    string NominalProperty,
    string LowerProperty,
    string UpperProperty,
    string DesignationProperty,
    string[] Positions);
