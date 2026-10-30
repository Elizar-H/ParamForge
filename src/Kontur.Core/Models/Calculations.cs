namespace Kontur.Core;

public record Calculations(
    double StockLength,
    double CutLength,
    double ClearOut,
    double ClearIn,
    double TotalCutMinutes,
    double TargetBoreDiameter = 0,
    double TargetCounterboreDiameter = 0,
    double TargetFlangeDiameter = 0,
    double TargetLength = 0,
    double TargetCounterboreDepth = 0,
    double CutoffWidth = 3,
    double PartingFrontZ = 0,
    double PartingBackZ = 0,
    double JawsFrontZ = 0,
    double RawStockLength = 0,
    double SecondClampStart = 0,
    double SecondClampEnd = 0,
    double SoftJawPocketDiameter = 0,
    double SoftJawPocketDepth = 0,
    double TargetBodyDiameter = 0,
    double TargetFlangeWidth = 0
);
