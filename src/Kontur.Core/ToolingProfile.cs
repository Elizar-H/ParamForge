namespace Kontur.Core;

/// <summary>Фиксированная инструментальная сборка и границы её применения.</summary>
public static class ToolingProfile
{
    public const string ExternalTool = "SCLCR 2525M12";
    public const string BoringTool = "A32T-SCLCR 12";
    public const string Insert = "CCMT 12 04 04-PF 4425";
    public const string CutoffTool = "QD-NN2G60C25A";
    public const string CutoffInsert = "QD-NG-0300-0002-CM 1125";
    public const string CutoffHolder = "ПФ-02, проектный блок (задний выступ ≤6 мм)";
    public const string GeneralToleranceDesignation = "ГОСТ 30893.1-m";
    public const string MaterialCondition = "Отожжённая, HB ≤197";
    public const double NoseRadius = .4;
    public const double CornerRadius = NoseRadius;
    public const double MaximumCornerRadius = .5;
    public const double MinimumFlatLength = .001;
    public const double CutoffWidth = 3;
    public const double ChuckMinimumGrip = 25;
    public const double CutoffHolderAxialProjection = 6;
    public const double ChuckRequiredResidualClearance = 4;
    public const double ChuckMinimumClearance = CutoffHolderAxialProjection + ChuckRequiredResidualClearance;
    public const double BoringBarDiameter = 32;
    public const double BoringMinimumDiameter = 40;
    public const double BoringOverhang = 125;
    public const double BoringMaximumOverhang = 128;
    public const double BoringClampLength = 128;
    public const double BoringFunctionalLength = 300;
    public const double BoringFunctionalWidth = 22;
    public const double HolderFrontClearance = 5;
    public const double SecondSetupGripLength = 25;
    public const double SoftJawBeltStartClearance = 2;
    public const double SoftJawPocketAxialClearance = 1;
    public const double SoftJawPocketDiametralClearance = 1;
    public const double SecondSetupFaceClearance = 5;
    public const double MaximumBoringDepth = BoringOverhang - HolderFrontClearance;

    // Симметричные предельные отклонения линейных размеров, класс m.
    // В этом проекте применяются только к Db и b, без редактируемых полей допуска.
    public static double GeneralLinearTolerance(double nominal) => nominal switch
    {
        >= .5 and <= 6 => .1,
        > 6 and <= 30 => .2,
        > 30 and <= 120 => .3,
        > 120 and <= 400 => .5,
        _ => throw new ArgumentOutOfRangeException(nameof(nominal), "Для размера нет общего допуска выбранного профиля.")
    };

    internal static IEnumerable<ValidationError> Validate(Parameters p, Calculations c)
    {
        if (p.StockBore < BoringMinimumDiameter)
            yield return new("stockBore", "Для оправки A32T-SCLCR 12 исходное отверстие должно быть не меньше 40 мм.");
        if (c.ClearIn < BoringMinimumDiameter - 1e-8)
            yield return new("clearanceX", "Точка отвода X должна быть не меньше 40 мм для выбранной расточной оправки.");
        if (c.CutLength > MaximumBoringDepth + 1e-8)
            yield return new("length", "Длина обработки превышает 120 мм: вылет оправки 125 мм и зазор держателя 5 мм.");
        if (p.GripLength < ChuckMinimumGrip)
            yield return new("gripLength", "Длина зажима должна быть не меньше 25 мм для выбранного приспособления.");
        if (p.ChuckClearance < ChuckMinimumClearance)
            yield return new("chuckClearance", "Осевой зазор до кулачков должен быть не меньше 10 мм: габарит блока 6 мм и запас 4 мм.");

        var dbTolerance = GeneralLinearTolerance(p.BodyDiameter);
        var widthTolerance = GeneralLinearTolerance(p.FlangeWidth);
        var flangeMinimum = p.FlangeDiameter + p.FlangeLowerDeviation;
        var bodyMaximum = p.BodyDiameter + dbTolerance;
        var boreMaximum = p.BoreDiameter + p.BoreUpperDeviation;
        var counterboreMinimum = p.CounterboreDiameter + p.CounterboreLowerDeviation;
        if ((flangeMinimum - bodyMaximum) / 2 - MaximumCornerRadius < MinimumFlatLength - 1e-8)
            yield return new("bodyDiameter", "На наружном уступе недостаточно места для плоской части и галтели R≤0,5 с учётом допусков.");
        if ((counterboreMinimum - boreMaximum) / 2 - MaximumCornerRadius < MinimumFlatLength - 1e-8)
            yield return new("counterboreDiameter", "На внутреннем уступе недостаточно места для плоской части и галтели R≤0,5 с учётом допусков.");
        var lengthMinimum = p.Length + p.LengthLowerDeviation;
        if (lengthMinimum - c.SecondClampEnd < SecondSetupFaceClearance - 1e-8)
            yield return new("length", "Недостаточно длины для второго установа: нужны пояс зажима 25 мм и свободный участок 5 мм под торцевание.");
        if (lengthMinimum - (p.FlangeWidth + widthTolerance) - MaximumCornerRadius < MinimumFlatLength - 1e-8)
            yield return new("flangeWidth", "После галтели R≤0,5 должна оставаться прямая часть наружного тела.");
        if (p.FlangeWidth - widthTolerance - MaximumCornerRadius < MinimumFlatLength - 1e-8)
            yield return new("flangeWidth", "Ширина буртика должна оставлять прямую часть после галтели R≤0,5.");
        if (p.CounterboreDepth + p.CounterboreDepthLowerDeviation - MaximumCornerRadius < MinimumFlatLength - 1e-8)
            yield return new("counterboreDepth", "Глубина расточки должна оставлять прямую часть перед галтелью R≤0,5.");
    }
}
