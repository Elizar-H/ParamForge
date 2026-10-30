namespace Kontur.Core;
/// <summary>Параметры детали, заготовки и обработки.</summary>
public record Parameters
{
    public string Controller { get; init; } = CncProfiles.Axioma;
    // Empty designation means explicitly entered numerical deviations.
    public string LengthTolerance { get; init; } = "";
    public string FlangeTolerance { get; init; } = "";
    public string BoreTolerance { get; init; } = "";
    public string CounterboreTolerance { get; init; } = "";
    public string CounterboreDepthTolerance { get; init; } = "";
    public string BodyTolerance { get; init; } = "m";
    public string FlangeWidthTolerance { get; init; } = "m";
    public double BodyLowerDeviation { get; init; } = -.3;
    public double BodyUpperDeviation { get; init; } = .3;
    public double FlangeWidthLowerDeviation { get; init; } = -.2;
    public double FlangeWidthUpperDeviation { get; init; } = .2;
    // Existing JSON files keep their explicitly selected blank dimensions.
    public bool AutoStock
    {
        get; init;
    }
    public string Organization { get; init; } = "";
    public string DrawingNumber { get; init; } = "";
    public string Developer { get; init; } = "";
    public string Checker { get; init; } = "";
    public string RateSetter { get; init; } = "";
    public string NormController { get; init; } = "";
    public string PartName { get; init; } = "Втулка с буртиком";
    public string Material { get; init; } = "Сталь 45";
    public double Length { get; init; } = 116;
    public double FlangeDiameter { get; init; } = 120;
    public double BodyDiameter { get; init; } = 100;
    public double FlangeWidth { get; init; } = 12;
    public double BoreDiameter { get; init; } = 47.3;
    public double CounterboreDiameter { get; init; } = 75;
    public double CounterboreDepth { get; init; } = 28;
    public double StockDiameter { get; init; } = 124;
    public double StockBore { get; init; } = 45;
    public double TrimAllowance { get; init; } = 2;
    public double GripLength { get; init; } = 25;
    public double ChuckClearance { get; init; } = 10;
    public double FrontFacingAllowance { get; init; } = 2;
    public double DepthOutside { get; init; } = 2;
    public double DepthInside { get; init; } = 1;
    public double FinishAllowance { get; init; } = 0.2;
    public double CuttingSpeed { get; init; } = 80;
    public double FeedRough { get; init; } = 0.2;
    public double FeedFinish { get; init; } = 0.1;
    // double сохраняет дробный ввод до валидации, вместо молчаливого округления.
    public double RpmMax { get; init; } = 1200;
    public double ClearanceX { get; init; } = 2;
    public double ClearanceZ { get; init; } = 3;
    public double BoreLowerDeviation { get; init; } = 0;
    public double BoreUpperDeviation { get; init; } = 1;
    public double CounterboreLowerDeviation { get; init; } = 0;
    public double CounterboreUpperDeviation { get; init; } = 0.74;
    public double FlangeLowerDeviation { get; init; } = -0.87;
    public double FlangeUpperDeviation { get; init; } = 0;
    public double LengthLowerDeviation { get; init; } = -1;
    public double LengthUpperDeviation { get; init; } = 0;
    public double CounterboreDepthLowerDeviation { get; init; } = -0.18;
    public double CounterboreDepthUpperDeviation { get; init; } = 0.18;
}
