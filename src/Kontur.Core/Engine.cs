using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Kontur.Core;

/// <summary>Детерминированный расчёт обработки и согласованные карты/УП.</summary>
public static class Engine
{
    public const string Version = "2.4.1";
    public static Parameters Defaults => new();
    public static IReadOnlyList<FieldDefinition> Fields { get; } = Array.AsReadOnly(new[]
    {
        new FieldDefinition("length", "Длина L", "мм", "Геометрия", 20, 250, .1, "Номинальная длина детали после доводки заднего торца"),
        new("flangeDiameter", "Диаметр буртика D", "мм", "Геометрия", 30, 200, .1, "Номинальный наружный диаметр буртика"),
        new("bodyDiameter", "Диаметр тела Db", "мм", "Геометрия", 25, 195, .1, "Наружный диаметр тела за буртиком"),
        new("flangeWidth", "Ширина буртика b", "мм", "Геометрия", 2, 60, .1, "Осевой размер буртика от переднего торца"),
        new("boreDiameter", "Основное отверстие d", "мм", "Геометрия", 20, 160, .1, "Номинальный диаметр основного сквозного отверстия"),
        new("counterboreDiameter", "Диаметр расточки d2", "мм", "Геометрия", 22, 180, .1, "Номинальный диаметр внутренней ступени"),
        new("counterboreDepth", "Глубина расточки h", "мм", "Геометрия", 1, 240, .1, "Глубина внутренней ступени от переднего торца"),
        new("stockDiameter", "Наружный диаметр заготовки", "мм", "Заготовка", 32, 240, .1, "Наружный диаметр заготовки до точения"),
        new("stockBore", "Исходное отверстие заготовки", "мм", "Заготовка", 16, 155, .1, "Диаметр сквозного отверстия до растачивания"),
        new("trimAllowance", "Припуск заднего торца", "мм", "Заготовка", .5, 5, .1, "Припуск, снимаемый с заднего торца в операции 020"),
        new("gripLength", "Длина зажима", "мм", "Заготовка", ToolingProfile.ChuckMinimumGrip, 80, 1, "Фактическая длина заготовки, охваченная кулачками"),
        new("chuckClearance", "Зазор до кулачков", "мм", "Заготовка", ToolingProfile.ChuckMinimumClearance, 40, .1, "От задней границы отрезного реза до кулачков: габарит проектного блока 6 мм и остаточный запас не менее 4 мм"),
        new("frontFacingAllowance", "Припуск переднего торца", "мм", "Заготовка", .5, 5, .1, "Осевой припуск, снимаемый в операции 005 до задания Z0"),
        new("depthOutside", "Глубина наружного прохода ap", "мм", "Режимы", .1, 5, .1, "Максимальная радиальная глубина одного наружного прохода"),
        new("depthInside", "Глубина внутреннего прохода ap", "мм", "Режимы", .1, 3, .1, "Максимальная радиальная глубина одного внутреннего прохода"),
        new("finishAllowance", "Чистовой припуск a", "мм", "Режимы", .05, .5, .05, "Радиальный припуск, оставляемый перед чистовым проходом"),
        new("cuttingSpeed", "Заданная скорость v", "м/мин", "Режимы", 10, 150, 1, "Скорость для расчёта оборотов по опорному диаметру"),
        new("feedRough", "Черновая подача f", "мм/об", "Режимы", .03, .5, .01, "Перемещение инструмента за оборот при черновой обработке"),
        new("feedFinish", "Чистовая подача f", "мм/об", "Режимы", .02, .3, .01, "Перемещение инструмента за оборот при чистовой обработке"),
        new("rpmMax", "Ограничение частоты n", "об/мин", "Режимы", 100, 3000, 1, "Верхняя граница частоты вращения шпинделя"),
        new("clearanceX", "Радиальный отвод", "мм", "Подходы", .5, 5, .1, "Радиальный отвод; в координате X учитывается удвоенная величина"),
        new("clearanceZ", "Отвод перед торцом", "мм", "Подходы", 1, 10, .1, "Положительная координата Z исходной точки перед торцом"),
        new("boreLowerDeviation", "d: нижнее отклонение", "мм", "Допуски", -5, 5, .01, "Отклонение от номинала d до нижней границы отверстия"),
        new("boreUpperDeviation", "d: верхнее отклонение", "мм", "Допуски", -5, 5, .01, "Отклонение от номинала d до верхней границы отверстия"),
        new("counterboreLowerDeviation", "d2: нижнее отклонение", "мм", "Допуски", -5, 5, .01, "Отклонение от номинала d2 до нижней границы ступени"),
        new("counterboreUpperDeviation", "d2: верхнее отклонение", "мм", "Допуски", -5, 5, .01, "Отклонение от номинала d2 до верхней границы ступени"),
        new("flangeLowerDeviation", "D: нижнее отклонение", "мм", "Допуски", -5, 5, .01, "Отклонение от номинала D до нижней границы буртика"),
        new("flangeUpperDeviation", "D: верхнее отклонение", "мм", "Допуски", -5, 5, .01, "Отклонение от номинала D до верхней границы буртика"),
        new("lengthLowerDeviation", "L: нижнее отклонение", "мм", "Допуски", -5, 5, .01, "Отклонение от номинала L до нижней границы конечной длины"),
        new("lengthUpperDeviation", "L: верхнее отклонение", "мм", "Допуски", -5, 5, .01, "Отклонение от номинала L до верхней границы конечной длины"),
        new("counterboreDepthLowerDeviation", "h: нижнее отклонение", "мм", "Допуски", -5, 5, .01, "Отклонение от номинала h до нижней границы глубины ступени"),
        new("counterboreDepthUpperDeviation", "h: верхнее отклонение", "мм", "Допуски", -5, 5, .01, "Отклонение от номинала h до верхней границы глубины ступени")
    });

    // Имена полей едины для формы, JSON и сообщений валидации.
    internal static double Number(Parameters p, string key) => key switch
    {
        "length" => p.Length, "flangeDiameter" => p.FlangeDiameter, "bodyDiameter" => p.BodyDiameter,
        "flangeWidth" => p.FlangeWidth, "boreDiameter" => p.BoreDiameter, "counterboreDiameter" => p.CounterboreDiameter,
        "counterboreDepth" => p.CounterboreDepth, "stockDiameter" => p.StockDiameter, "stockBore" => p.StockBore,
        "trimAllowance" => p.TrimAllowance, "gripLength" => p.GripLength, "chuckClearance" => p.ChuckClearance,
        "frontFacingAllowance" => p.FrontFacingAllowance, "depthOutside" => p.DepthOutside,
        "depthInside" => p.DepthInside, "finishAllowance" => p.FinishAllowance, "cuttingSpeed" => p.CuttingSpeed,
        "feedRough" => p.FeedRough, "feedFinish" => p.FeedFinish, "rpmMax" => p.RpmMax,
        "clearanceX" => p.ClearanceX, "clearanceZ" => p.ClearanceZ,
        "boreLowerDeviation" => p.BoreLowerDeviation, "boreUpperDeviation" => p.BoreUpperDeviation,
        "counterboreLowerDeviation" => p.CounterboreLowerDeviation, "counterboreUpperDeviation" => p.CounterboreUpperDeviation,
        "flangeLowerDeviation" => p.FlangeLowerDeviation, "flangeUpperDeviation" => p.FlangeUpperDeviation,
        "lengthLowerDeviation" => p.LengthLowerDeviation, "lengthUpperDeviation" => p.LengthUpperDeviation,
        "counterboreDepthLowerDeviation" => p.CounterboreDepthLowerDeviation,
        "counterboreDepthUpperDeviation" => p.CounterboreDepthUpperDeviation,
        _ => throw new ArgumentException("Неизвестный параметр: " + key)
    };

    internal static double Round(double value) => Math.Round(value, 3, MidpointRounding.AwayFromZero);
    internal static string Fmt(double value) => value.ToString("0.####", CultureInfo.InvariantCulture);
    // Inputs are validated to three decimal places. Decimal keeps an exact half-micron
    // midpoint from falling below its tie through binary floating-point addition.
    internal static double Target(double nominal, double lower, double upper) =>
        (double)Math.Round((decimal)nominal + ((decimal)lower + (decimal)upper) / 2m, 3, MidpointRounding.AwayFromZero);
    internal static Calculations Calculate(Parameters p, double time = 0)
    {
        var length = Target(p.Length, p.LengthLowerDeviation, p.LengthUpperDeviation);
        var cut = Round(length + p.TrimAllowance);
        var stock = Round(cut + ToolingProfile.CutoffWidth + p.ChuckClearance + p.GripLength);
        var widthTolerance = ToolingProfile.GeneralLinearTolerance(p.FlangeWidth);
        var secondClampStart = Round(p.FlangeWidth + widthTolerance + ToolingProfile.SoftJawBeltStartClearance);
        return new(stock, cut, Round(p.StockDiameter + 2 * p.ClearanceX),
            Round(p.StockBore - 2 * p.ClearanceX), time,
            Target(p.BoreDiameter, p.BoreLowerDeviation, p.BoreUpperDeviation),
            Target(p.CounterboreDiameter, p.CounterboreLowerDeviation, p.CounterboreUpperDeviation),
            Target(p.FlangeDiameter, p.FlangeLowerDeviation, p.FlangeUpperDeviation), length,
            Target(p.CounterboreDepth, p.CounterboreDepthLowerDeviation, p.CounterboreDepthUpperDeviation),
            ToolingProfile.CutoffWidth, -cut, -Round(cut + ToolingProfile.CutoffWidth),
            -Round(cut + ToolingProfile.CutoffWidth + p.ChuckClearance), Round(stock + p.FrontFacingAllowance),
            secondClampStart, Round(secondClampStart + ToolingProfile.SecondSetupGripLength),
            Round(p.FlangeDiameter + p.FlangeUpperDeviation + ToolingProfile.SoftJawPocketDiametralClearance),
            Round(p.FlangeWidth + widthTolerance + ToolingProfile.SoftJawPocketAxialClearance));
    }

    public static List<ValidationError> Validate(Parameters p)
    {
        var errors = new List<ValidationError>();
        if (!CncProfiles.Supported(p.Controller)) errors.Add(new("controller", "Неизвестный профиль ЧПУ."));
        try { p = Tolerances.Resolve(p); }
        catch (ArgumentException ex) { errors.Add(new("tolerances", ex.Message)); }
        if (errors.Count != 0) return errors;
        void Error(string field, string message) => errors.Add(new(field, message));
        foreach (var (key, label, value, maximum) in new[]
        {
            ("partName", "Наименование детали", p.PartName, 45),
            ("material", "Материал", p.Material, 35)
        })
            if (string.IsNullOrWhiteSpace(value) || value.Length > maximum || value.Any(c => char.IsControl(c) || c is '\u2028' or '\u2029'))
                Error(key, $"{label}: от 1 до {maximum} символов без переносов строки и управляющих символов.");
        foreach (var f in Fields)
        {
            var v = Number(p, f.Key);
            if (!double.IsFinite(v)) Error(f.Key, f.Label + ": требуется конечное число.");
            else if (v < f.Min || v > f.Max) Error(f.Key, $"{f.Label}: диапазон {Fmt(f.Min)}–{Fmt(f.Max)} {f.Unit}.");
            else
            {
                var precision = f.Key.EndsWith("Deviation", StringComparison.Ordinal) ? 4 : 3;
                if ((decimal)v != Math.Round((decimal)v, precision, MidpointRounding.AwayFromZero))
                    Error(f.Key, f.Label + (precision == 4 ? ": не более четырёх знаков после запятой." : ": не более трёх знаков после запятой."));
            }
        }
        if (errors.Count != 0) return errors;
        void Tolerance(string field, double nominal, double lower, double upper)
        {
            if (upper < lower) Error(field, "Верхнее отклонение не должно быть меньше нижнего.");
            if (nominal + lower <= 0) Error(field, "Нижняя граница размера должна быть положительной.");
            if (upper >= lower)
            {
                var target = (decimal)Target(nominal, lower, upper);
                if (target < (decimal)nominal + (decimal)lower || target > (decimal)nominal + (decimal)upper)
                    Error(field, "В интервал допуска не попадает целевой размер, округлённый до 0,001 мм.");
            }
        }
        Tolerance("boreUpperDeviation", p.BoreDiameter, p.BoreLowerDeviation, p.BoreUpperDeviation);
        Tolerance("counterboreUpperDeviation", p.CounterboreDiameter, p.CounterboreLowerDeviation, p.CounterboreUpperDeviation);
        Tolerance("flangeUpperDeviation", p.FlangeDiameter, p.FlangeLowerDeviation, p.FlangeUpperDeviation);
        Tolerance("lengthUpperDeviation", p.Length, p.LengthLowerDeviation, p.LengthUpperDeviation);
        Tolerance("counterboreDepthUpperDeviation", p.CounterboreDepth, p.CounterboreDepthLowerDeviation, p.CounterboreDepthUpperDeviation);
        if (errors.Count != 0) return errors;
        // The displayed drawing uses nominal dimensions. Both that drawing and the
        // full tolerance envelope must describe the selected flanged-bushing family.
        if (p.FlangeWidth >= p.Length) Error("flangeWidth", "Для чертежа нужно b < L.");
        if (p.CounterboreDepth >= p.Length) Error("counterboreDepth", "Для чертежа нужно h < L.");
        if (p.BodyDiameter >= p.FlangeDiameter) Error("bodyDiameter", "Для чертежа нужно Db < D.");
        if (p.BoreDiameter >= p.CounterboreDiameter) Error("counterboreDiameter", "Для чертежа нужно d < d2.");
        if (p.BoreDiameter >= p.BodyDiameter) Error("boreDiameter", "Для чертежа нужно d < Db.");
        if (p.CounterboreDiameter >= p.FlangeDiameter) Error("counterboreDiameter", "Для чертежа нужно d2 < D.");
        if (p.CounterboreDepth > p.FlangeWidth && p.CounterboreDiameter >= p.BodyDiameter)
            Error("counterboreDiameter", "При h > b требуется d2 < Db.");
        if (errors.Count != 0) return errors;
        var c = Calculate(p);
        var flangeMin = p.FlangeDiameter + p.FlangeLowerDeviation;
        var boreMax = p.BoreDiameter + p.BoreUpperDeviation;
        var cbMin = p.CounterboreDiameter + p.CounterboreLowerDeviation;
        var cbMax = p.CounterboreDiameter + p.CounterboreUpperDeviation;
        var lengthMin = p.Length + p.LengthLowerDeviation;
        var depthMax = p.CounterboreDepth + p.CounterboreDepthUpperDeviation;
        var bodyTolerance = ToolingProfile.GeneralLinearTolerance(p.BodyDiameter);
        var widthTolerance = ToolingProfile.GeneralLinearTolerance(p.FlangeWidth);
        var bodyMin = p.BodyDiameter - bodyTolerance;
        var bodyMax = p.BodyDiameter + bodyTolerance;
        var widthMin = p.FlangeWidth - widthTolerance;
        var widthMax = p.FlangeWidth + widthTolerance;
        if (p.RpmMax != Math.Floor(p.RpmMax)) Error("rpmMax", "Ограничение оборотов задаётся целым числом.");
        if (widthMax >= lengthMin) Error("flangeWidth", "Максимальная ширина буртика должна быть меньше минимальной длины по допускам.");
        if (depthMax >= lengthMin) Error("counterboreDepth", "Максимальная глубина расточки должна быть меньше минимальной длины по допуску.");
        if (bodyMax >= flangeMin) Error("bodyDiameter", "Максимальный диаметр тела должен быть меньше минимального диаметра буртика по допускам.");
        if (boreMax >= cbMin) Error("counterboreDiameter", "Минимальная расточка должна быть шире максимального основного отверстия.");
        if ((bodyMin - boreMax) / 2 < 3 - 1e-8) Error("boreDiameter", "Толщина стенки тела по худшему сочетанию допусков должна быть не менее 3 мм.");
        if ((flangeMin - cbMax) / 2 < 3 - 1e-8) Error("counterboreDiameter", "Толщина стенки буртика по худшему сочетанию допусков должна быть не менее 3 мм.");
        if (depthMax > widthMin && (bodyMin - cbMax) / 2 < 3 - 1e-8)
            Error("counterboreDiameter", "Расточка заходит за буртик: стенка тела при максимальном отверстии должна быть не менее 3 мм.");
        var minRemoval = 2 * (p.FinishAllowance + .05);
        if (p.StockDiameter - c.TargetFlangeDiameter < minRemoval - 1e-8) Error("stockDiameter", "Для буртика нужен чистовой припуск и не менее 0,05 мм чернового съёма на сторону.");
        if (c.TargetBoreDiameter - p.StockBore < minRemoval - 1e-8) Error("stockBore", "Исходное отверстие должно оставлять чистовой припуск и не менее 0,05 мм чернового съёма.");
        if (c.TargetFlangeDiameter - p.BodyDiameter < minRemoval - 1e-8) Error("bodyDiameter", "Перепад наружных расчётных диаметров слишком мал для данного чистового припуска.");
        if (c.TargetCounterboreDiameter - c.TargetBoreDiameter < minRemoval - 1e-8) Error("counterboreDiameter", "Перепад внутренних расчётных диаметров слишком мал для данного чистового припуска.");
        if (p.FinishAllowance > Math.Min(p.DepthOutside, p.DepthInside)) Error("finishAllowance", "Чистовой припуск не должен превышать разрешённую глубину прохода.");
        errors.AddRange(ToolingProfile.Validate(p, c));
        if (p.FeedFinish > p.FeedRough) Error("feedFinish", "Чистовая подача не должна превышать черновую.");
        double Count(double difference, double depth) => (double)Math.Max(0m, Math.Ceiling(((decimal)difference - 2m * (decimal)p.FinishAllowance) / (2m * (decimal)depth)));
        var passes = Count(p.StockDiameter - c.TargetFlangeDiameter, p.DepthOutside) + Count(p.StockDiameter - p.BodyDiameter, p.DepthOutside)
            + Count(c.TargetBoreDiameter - p.StockBore, p.DepthInside) + Count(c.TargetCounterboreDiameter - c.TargetBoreDiameter, p.DepthInside) + 4;
        if (passes > 400) Error("depthOutside", "Слишком много проходов (более 400): увеличьте глубину или уменьшите припуск.");
        return errors;
    }

    public static List<double> PassDiameters(double start, double target, double depth)
    {
        if (!double.IsFinite(start) || !double.IsFinite(target) || !double.IsFinite(depth) || depth <= 0)
            throw new ArgumentOutOfRangeException(nameof(depth), "Параметры проходов должны быть конечными, глубина — положительной.");
        decimal first, last, step;
        try { first = (decimal)start; last = (decimal)target; step = (decimal)depth; }
        catch (OverflowException) { throw new ArgumentOutOfRangeException(nameof(depth), "Параметры проходов превышают числовой диапазон."); }
        if (step <= 0) throw new ArgumentOutOfRangeException(nameof(depth), "Глубина прохода слишком мала.");
        var delta = last - first;
        var count = Math.Ceiling(Math.Abs(delta) / (2m * step));
        if (count > 400) throw new ArgumentOutOfRangeException(nameof(depth), "Число проходов превышает предел 400.");
        var values = new List<double>();
        // Равные доли полного съёма вместо полного шага с микроскопическим остатком.
        for (var i = 1; i <= count; i++)
            values.Add((double)Math.Round(i == count ? last : first + delta * i / count, 3, MidpointRounding.AwayFromZero));
        return values;
    }

    private record Definition(string Id, string Title, string Tag, int Tool, string Kind, double Start,
        double Target, double ZStart, double ZEnd, double Depth, bool Finish, double ReferenceDiameter);

    public static GenerationResult Generate(Parameters? parameters = null)
    {
        var p = parameters ?? Defaults;
        var errors = Validate(p);
        if (errors.Count == 0) p = Tolerances.Resolve(p);
        if (errors.Count > 0) return new(false, errors, [], null);
        var c = Calculate(p);
        var id = "V-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Version + "|" + JsonSerializer.Serialize(p, ProjectFiles.JsonOptions))))[..16];
        var warnings = new List<string>();
        var controls = HtmlCards.ControlDescription(p, c);
        var operations = new List<Operation>
        {
            new("005", "Подготовка заготовки", "manual", $"Принять предварительно обработанный полый полуфабрикат Ø{Fmt(p.StockDiameter)} / Ø{Fmt(p.StockBore)} × {Fmt(c.RawStockLength)} мм. Снять с переднего торца {Fmt(p.FrontFacingAllowance)} мм до длины {Fmt(c.StockLength)} мм; этот торец принять исходным. Отверстие сквозное, способ его получения находится вне данного процесса."),
            new("010", "Токарная с ЧПУ", "nc", $"Один установ: длина зажима {Fmt(p.GripLength)} мм; отрезная полоса {Fmt(c.CutoffWidth)} мм и отдельный осевой зазор до кулачков {Fmt(p.ChuckClearance)} мм. Передняя плоскость кулачков Z={Fmt(c.JawsFrontZ)} мм. Z0 — подготовленный передний торец, Z− — к зажиму, X — диаметр. Обработать буртик, тело, отверстие и расточку на длине {Fmt(c.CutLength)} мм с задним припуском {Fmt(p.TrimAllowance)} мм. Цели D={Fmt(c.TargetFlangeDiameter)}, d={Fmt(c.TargetBoreDiameter)}, d2={Fmt(c.TargetCounterboreDiameter)}, h={Fmt(c.TargetCounterboreDepth)} мм. Галтели R≤{Fmt(ToolingProfile.MaximumCornerRadius)} при пластине R{Fmt(ToolingProfile.NoseRadius)}; непрерывная СОЖ."),
            new("020", "Отделение и доводка", "manual", $"Отрезать лезвием {ToolingProfile.CutoffTool} с пластиной {ToolingProfile.CutoffInsert}: полоса Z={Fmt(c.PartingFrontZ)}…{Fmt(c.PartingBackZ)} мм, ширина {Fmt(c.CutoffWidth)} мм. Сохранить задний припуск {Fmt(p.TrimAllowance)} мм. Переустановить в мягкие кулачки по Db={Fmt(p.BodyDiameter)} мм с карманом Ø{Fmt(c.SoftJawPocketDiameter)} глубиной {Fmt(c.SoftJawPocketDepth)} мм и осевым упором по первому торцу. Контактный пояс от {Fmt(c.SecondClampStart)} до {Fmt(c.SecondClampEnd)} мм от первого торца; перед обработкой заднего торца требуется свободный участок не меньше {Fmt(ToolingProfile.SecondSetupFaceClearance)} мм. Снять задний припуск до длины {Fmt(c.TargetLength)} мм (границы {Fmt(p.Length + p.LengthLowerDeviation)}…{Fmt(p.Length + p.LengthUpperDeviation)}). Сохранить h={Fmt(c.TargetCounterboreDepth)} и b={Fmt(p.FlangeWidth)} мм от первого торца; удалить заусенцы."),
            new("030", "Контроль", "manual", controls + $" Проверить Db={Fmt(p.BodyDiameter)} ±{Fmt(ToolingProfile.GeneralLinearTolerance(p.BodyDiameter))} и b={Fmt(p.FlangeWidth)} ±{Fmt(ToolingProfile.GeneralLinearTolerance(p.FlangeWidth))} мм ({ToolingProfile.GeneralToleranceDesignation}). Глубину h измерять от первого торца на плоской части уступа вне галтели R≤{Fmt(ToolingProfile.MaximumCornerRadius)}. Средства: микрометр/штангенциркуль, нутромер и глубиномер.")
        };
        var definitions = new[]
        {
            new Definition("010.01", "Буртик — черновое точение", "FLANGE ROUGH", 1, "outer", p.StockDiameter, c.TargetFlangeDiameter + 2 * p.FinishAllowance, 0, p.FlangeWidth, p.DepthOutside, false, p.StockDiameter),
            new("010.02", "Буртик — чистовое точение", "FLANGE FINISH", 1, "outer", c.TargetFlangeDiameter + 2 * p.FinishAllowance, c.TargetFlangeDiameter, 0, p.FlangeWidth, p.FinishAllowance, true, c.TargetFlangeDiameter),
            new("010.03", "Тело — черновое точение", "BODY ROUGH", 1, "body", p.StockDiameter, p.BodyDiameter + 2 * p.FinishAllowance, p.FlangeWidth, c.CutLength, p.DepthOutside, false, p.StockDiameter),
            new("010.04", "Тело — чистовое точение", "BODY FINISH", 1, "body", p.BodyDiameter + 2 * p.FinishAllowance, p.BodyDiameter, p.FlangeWidth, c.CutLength, p.FinishAllowance, true, p.BodyDiameter),
            new("010.05", "Основное отверстие — черновое растачивание", "BORE ROUGH", 2, "inner", p.StockBore, c.TargetBoreDiameter - 2 * p.FinishAllowance, 0, c.CutLength, p.DepthInside, false, c.TargetBoreDiameter),
            new("010.06", "Основное отверстие — чистовое растачивание", "BORE FINISH", 2, "inner", c.TargetBoreDiameter - 2 * p.FinishAllowance, c.TargetBoreDiameter, 0, c.CutLength, p.FinishAllowance, true, c.TargetBoreDiameter),
            new("010.07", "Внутренняя ступень — черновое растачивание", "STEP ROUGH", 2, "inner", c.TargetBoreDiameter, c.TargetCounterboreDiameter - 2 * p.FinishAllowance, 0, c.TargetCounterboreDepth, p.DepthInside, false, c.TargetCounterboreDiameter),
            new("010.08", "Внутренняя ступень — чистовое растачивание", "STEP FINISH", 2, "inner", c.TargetCounterboreDiameter - 2 * p.FinishAllowance, c.TargetCounterboreDiameter, 0, c.TargetCounterboreDepth, p.FinishAllowance, true, c.TargetCounterboreDiameter)
        };
        var transitions = new List<Transition>();
        var moves = new List<Move>();
        var instructions = new List<NcInstruction>();
        void Emit(NcInstructionKind kind, int tool = 0, double value = 0, string text = "") =>
            instructions.Add(new(kind, Tool: tool, Value: value, Text: text));
        (double X, double Z)? position = null;
        var activeTool = 0;
        void Move(string mode, double x, double z, Transition? t)
        {
            x = Round(x); z = Round(z);
            var distance = position is { } a ? Math.Sqrt(Math.Pow((x - a.X) / 2, 2) + Math.Pow(z - a.Z, 2)) : 0;
            if (mode == "G1") t!.CutMinutes += distance / (t.Feed * t.Rpm);
            var move = new Move(x, z, mode, t?.Id ?? "", 0, mode == "G1" ? t!.Feed : null, t?.Rpm ?? 0);
            instructions.Add(new(NcInstructionKind.Motion, Motion: move));
            position = (x, z);
        }
        Emit(NcInstructionKind.Prepare);
        Emit(NcInstructionKind.Diameter);
        Emit(NcInstructionKind.CoolantOff);
        Emit(NcInstructionKind.Stop);
        foreach (var d in definitions)
        {
            var t = new Transition
            {
                Id = d.Id, Title = d.Title, Tool = "T" + d.Tool, Kind = d.Kind, Diameter = Round(d.Target), StartDiameter = Round(d.Start),
                Rpm = Math.Min(p.RpmMax, Math.Floor(1000 * p.CuttingSpeed / (Math.PI * d.ReferenceDiameter))),
                Feed = d.Finish ? p.FeedFinish : p.FeedRough, Depth = d.Depth, ReferenceDiameter = d.ReferenceDiameter,
                Passes = PassDiameters(d.Start, d.Target, d.Depth), ZStart = -d.ZStart, ZEnd = -d.ZEnd
            };
            if (d.Tool != activeTool)
            {
                if (activeTool != 0) { Move("G0", c.ClearOut, p.ClearanceZ, null); Emit(NcInstructionKind.FeedPerMinute); Emit(NcInstructionKind.CoolantOff); Emit(NcInstructionKind.Stop); }
                Emit(NcInstructionKind.Tool, tool: d.Tool);
                Emit(NcInstructionKind.GeometryOffset);
                Emit(NcInstructionKind.CoolantOn);
                activeTool = d.Tool;
            }
            Emit(NcInstructionKind.Comment, text: $"TR {d.Id} {d.Tag}");
            Emit(NcInstructionKind.Speed, value: t.Rpm);
            Emit(NcInstructionKind.FeedPerRevolution);
            // После смены инструмента исходная точка задаётся явным G0.
            Move("G0", d.Kind == "inner" ? c.ClearIn : c.ClearOut, p.ClearanceZ, t);
            foreach (var diameter in t.Passes)
            {
                if (d.Kind == "body")
                {
                    Move("G0", c.ClearOut, -d.ZStart, t); Move("G1", diameter, -d.ZStart, t);
                    Move("G1", diameter, -d.ZEnd, t); Move("G0", c.ClearOut, -d.ZEnd, t); Move("G0", c.ClearOut, p.ClearanceZ, t);
                }
                else if (d.Kind == "outer")
                {
                    Move("G0", diameter, p.ClearanceZ, t); Move("G1", diameter, -d.ZEnd, t);
                    Move("G0", c.ClearOut, -d.ZEnd, t); Move("G0", c.ClearOut, p.ClearanceZ, t);
                }
                else
                {
                    Move("G0", diameter, p.ClearanceZ, t); Move("G1", diameter, -d.ZEnd, t);
                    Move("G1", c.ClearIn, -d.ZEnd, t); Move("G0", c.ClearIn, p.ClearanceZ, t);
                }
            }
            transitions.Add(t);
        }
        Move("G0", c.ClearIn, p.ClearanceZ, null); Move("G0", c.ClearOut, p.ClearanceZ, null); Emit(NcInstructionKind.FeedPerMinute); Emit(NcInstructionKind.CoolantOff); Emit(NcInstructionKind.Stop); Emit(NcInstructionKind.End);
        c = c with { TotalCutMinutes = transitions.Sum(t => t.CutMinutes) };
        var output = CncProfiles.Postprocessor(p.Controller).Process(new(id, c, transitions, instructions));
        var nc = output.Text;
        moves = output.Moves;
        transitions = output.Transitions;
        var verification = NcVerifier.Verify(nc, p);
        if (!verification.Ok) errors.AddRange(verification.Errors.Select(e => new ValidationError("nc", e)));
        if (verification.Moves.Count != moves.Count) errors.Add(new("nc", "Независимая проверка восстановила другое число движений."));
        else for (var i = 0; i < moves.Count; i++)
        {
            var a = moves[i]; var b = verification.Moves[i];
            if (a.X != b.X || a.Z != b.Z || a.Mode != b.Mode || a.Line != b.Line ||
                (a.Mode == "G1" && (a.Feed != b.Feed || a.Rpm != b.Rpm)))
            { errors.Add(new("nc", $"Независимая проверка движения N{a.Line} не совпала с расчётной моделью.")); break; }
        }
        if (Math.Abs(c.TotalCutMinutes - verification.CutMinutes) > 1e-9)
            errors.Add(new("nc", "Время G1 независимой проверки не совпало с расчётной моделью."));
        if (errors.Count > 0) return new(false, errors, warnings, null);
        var project = new Project { Id = id, Params = p, Calculations = c, Operations = operations, Transitions = transitions,
            Moves = moves, Nc = nc, NcFileName = output.FileName, ProgramName = output.ProgramName, Warnings = warnings, Verification = verification };
        project = project with { RouteHtml = HtmlCards.Route(project), TechnologyHtml = HtmlCards.Technology(project) };
        return new(true, [], warnings, project);
    }
}
