namespace Kontur.Core;

/// <summary>Параметры детали, заготовки и обработки.</summary>
public record Parameters
{
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

public record FieldDefinition(string Key, string Label, string Unit, string Group,
    double Min, double Max, double Step, string Note);
public record ValidationError(string Field, string Message);
public record GenerationResult(bool Ok, List<ValidationError> Errors, List<string> Warnings, Project? Project);
public record Calculations(double StockLength, double CutLength, double ClearOut, double ClearIn, double TotalCutMinutes,
    double TargetBoreDiameter = 0, double TargetCounterboreDiameter = 0, double TargetFlangeDiameter = 0,
    double TargetLength = 0, double TargetCounterboreDepth = 0,
    double CutoffWidth = 3, double PartingFrontZ = 0, double PartingBackZ = 0,
    double JawsFrontZ = 0, double RawStockLength = 0,
    double SecondClampStart = 0, double SecondClampEnd = 0,
    double SoftJawPocketDiameter = 0, double SoftJawPocketDepth = 0);
public record Operation(string Code, string Title, string Kind, string Description);
public record Transition
{
    public string Id { get; init; } = "";
    public string Title { get; init; } = "";
    public string Tool { get; init; } = "";
    public string Kind { get; init; } = "";
    public double Diameter { get; init; }
    public double StartDiameter { get; init; }
    public double Rpm { get; init; }
    public double Feed { get; init; }
    public double Depth { get; init; }
    public double ReferenceDiameter { get; init; }
    public List<double> Passes { get; init; } = [];
    /// <summary>Фактический наибольший радиальный съём по округлённым диаметрам проходов.</summary>
    public double ActualMaximumDepth
    {
        get
        {
            var previous = (decimal)StartDiameter;
            decimal maximum = 0;
            foreach (var diameter in Passes)
            {
                var current = (decimal)diameter;
                maximum = Math.Max(maximum, Math.Abs(current - previous) / 2m);
                previous = current;
            }
            return (double)Math.Round(maximum, 4, MidpointRounding.AwayFromZero);
        }
    }
    public double ZStart { get; init; }
    public double ZEnd { get; init; }
    public double CutMinutes { get; set; }
    public List<int> Blocks { get; init; } = [];
}
public record Move(double X, double Z, string Mode, string Transition, int Line, double? Feed, double Rpm);
public record ParsedMove(double X, double Z, string Mode, int Line, double? Feed, double? Rpm, int PhysicalLine, int? Tool);
public record VerificationResult(bool Ok, List<string> Errors, List<string> Warnings, List<ParsedMove> Moves, double CutMinutes);
public record Project
{
    public string Id { get; init; } = "";
    public string Version { get; init; } = Engine.Version;
    public Parameters Params { get; init; } = new();
    public Calculations Calculations { get; init; } = new(0, 0, 0, 0, 0);
    public List<Operation> Operations { get; init; } = [];
    public List<Transition> Transitions { get; init; } = [];
    public List<Move> Moves { get; init; } = [];
    public string Nc { get; init; } = "";
    public string RouteHtml { get; init; } = "";
    public string TechnologyHtml { get; init; } = "";
    public List<string> Warnings { get; init; } = [];
    public VerificationResult Verification { get; init; } = new(false, [], [], [], 0);
}
