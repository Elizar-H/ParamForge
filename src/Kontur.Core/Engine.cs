using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Kontur.Core;

/// <summary>Детерминированная учебная технология и согласованные карты/УП.</summary>
public static class Engine
{
    public const string Version = "2.1.1";
    public static Parameters Defaults => new();
    public static IReadOnlyList<FieldDefinition> Fields { get; } = Array.AsReadOnly(new[]
    {
        new FieldDefinition("length", "Длина L", "мм", "Геометрия", 20, 250, .1, "Номинал 116 и отклонения −1/0 подтверждены; цель — середина допуска"),
        new("flangeDiameter", "Диаметр буртика D", "мм", "Геометрия", 30, 200, .1, "Номинал 120 и отклонения −0,87/0 подтверждены"),
        new("bodyDiameter", "Диаметр тела Db", "мм", "Геометрия", 25, 195, .1, "100 — предложенное учебное значение; допуск не задан"),
        new("flangeWidth", "Ширина буртика b", "мм", "Геометрия", 2, 60, .1, "12 — предложенное учебное значение; допуск не задан"),
        new("boreDiameter", "Основное отверстие d", "мм", "Геометрия", 20, 160, .1, "Номинал 47,3 и отклонения 0/+1 подтверждены"),
        new("counterboreDiameter", "Диаметр расточки d2", "мм", "Геометрия", 22, 180, .1, "Номинал 75 и отклонения 0/+0,74 подтверждены"),
        new("counterboreDepth", "Глубина расточки h", "мм", "Геометрия", 1, 240, .1, "Номинал 28 и отклонения ±0,18 подтверждены"),
        new("stockDiameter", "Наружный диаметр заготовки", "мм", "Заготовка", 32, 240, .1, "Учебная цилиндрическая заготовка с готовым исходным отверстием"),
        new("stockBore", "Исходное отверстие заготовки", "мм", "Заготовка", 16, 155, .1, "45 — изменяемое предложение; подготовка относится к операции 005"),
        new("trimAllowance", "Припуск заднего торца", "мм", "Заготовка", .5, 5, .1, "После УП снимается отдельно в операции 020"),
        new("gripLength", "Длина хвостовика под зажим", "мм", "Заготовка", 15, 80, 1, "Учебный запас длины, расчёт сил закрепления не выполнен"),
        new("depthOutside", "Глубина наружного прохода ap", "мм", "Режимы", .1, 5, .1, "Радиальная величина, не изменение диаметра"),
        new("depthInside", "Глубина внутреннего прохода ap", "мм", "Режимы", .1, 3, .1, "Радиальная величина"),
        new("finishAllowance", "Чистовой припуск a", "мм", "Режимы", .05, .5, .05, "На сторону, сохраняется после черновых проходов"),
        new("cuttingSpeed", "Заданная скорость v", "м/мин", "Режимы", 10, 150, 1, "Демонстрационная настройка; материал — текстовый реквизит"),
        new("feedRough", "Черновая подача f", "мм/об", "Режимы", .03, .5, .01, "Требуется подбор под реальный инструмент"),
        new("feedFinish", "Чистовая подача f", "мм/об", "Режимы", .02, .3, .01, "Допуски и шероховатость расчётом не гарантируются"),
        new("rpmMax", "Ограничение частоты n", "об/мин", "Режимы", 100, 3000, 1, "Предел учебного профиля, не паспорт станка"),
        new("clearanceX", "Радиальный отвод", "мм", "Подходы", .5, 5, .1, "По X в коде добавляется удвоенный отвод"),
        new("clearanceZ", "Отвод перед торцом", "мм", "Подходы", 1, 10, .1, "Z положителен вне правого торца"),
        new("boreLowerDeviation", "d: нижнее отклонение", "мм", "Допуски", -5, 5, .01, "Граница основного отверстия"),
        new("boreUpperDeviation", "d: верхнее отклонение", "мм", "Допуски", -5, 5, .01, "Граница основного отверстия"),
        new("counterboreLowerDeviation", "d2: нижнее отклонение", "мм", "Допуски", -5, 5, .01, "Граница внутренней ступени"),
        new("counterboreUpperDeviation", "d2: верхнее отклонение", "мм", "Допуски", -5, 5, .01, "Граница внутренней ступени"),
        new("flangeLowerDeviation", "D: нижнее отклонение", "мм", "Допуски", -5, 5, .01, "Граница наружного буртика"),
        new("flangeUpperDeviation", "D: верхнее отклонение", "мм", "Допуски", -5, 5, .01, "Граница наружного буртика"),
        new("lengthLowerDeviation", "L: нижнее отклонение", "мм", "Допуски", -5, 5, .01, "Граница конечной длины после операции 020"),
        new("lengthUpperDeviation", "L: верхнее отклонение", "мм", "Допуски", -5, 5, .01, "Граница конечной длины после операции 020"),
        new("counterboreDepthLowerDeviation", "h: нижнее отклонение", "мм", "Допуски", -5, 5, .01, "Граница глубины внутренней ступени"),
        new("counterboreDepthUpperDeviation", "h: верхнее отклонение", "мм", "Допуски", -5, 5, .01, "Граница глубины внутренней ступени")
    });

    // Имена полей едины для формы, JSON и сообщений валидации.
    internal static double Number(Parameters p, string key) => key switch
    {
        "length" => p.Length, "flangeDiameter" => p.FlangeDiameter, "bodyDiameter" => p.BodyDiameter,
        "flangeWidth" => p.FlangeWidth, "boreDiameter" => p.BoreDiameter, "counterboreDiameter" => p.CounterboreDiameter,
        "counterboreDepth" => p.CounterboreDepth, "stockDiameter" => p.StockDiameter, "stockBore" => p.StockBore,
        "trimAllowance" => p.TrimAllowance, "gripLength" => p.GripLength, "depthOutside" => p.DepthOutside,
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
    internal static string Fmt(double value) => Round(value).ToString("0.###", CultureInfo.InvariantCulture);
    // Inputs are validated to three decimal places. Decimal keeps an exact half-micron
    // midpoint from falling below its tie through binary floating-point addition.
    internal static double Target(double nominal, double lower, double upper) =>
        (double)Math.Round((decimal)nominal + ((decimal)lower + (decimal)upper) / 2m, 3, MidpointRounding.AwayFromZero);
    internal static Calculations Calculate(Parameters p, double time = 0)
    {
        var length = Target(p.Length, p.LengthLowerDeviation, p.LengthUpperDeviation);
        var cut = Round(length + p.TrimAllowance);
        return new(Round(cut + p.GripLength), cut, Round(p.StockDiameter + 2 * p.ClearanceX),
            Round(p.StockBore - 2 * p.ClearanceX), time,
            Target(p.BoreDiameter, p.BoreLowerDeviation, p.BoreUpperDeviation),
            Target(p.CounterboreDiameter, p.CounterboreLowerDeviation, p.CounterboreUpperDeviation),
            Target(p.FlangeDiameter, p.FlangeLowerDeviation, p.FlangeUpperDeviation), length,
            Target(p.CounterboreDepth, p.CounterboreDepthLowerDeviation, p.CounterboreDepthUpperDeviation));
    }

    public static List<ValidationError> Validate(Parameters p)
    {
        var errors = new List<ValidationError>();
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
            else if ((decimal)v != Math.Round((decimal)v, 3, MidpointRounding.AwayFromZero))
                Error(f.Key, f.Label + ": не более трёх знаков после запятой.");
        }
        if (errors.Count != 0) return errors;
        void Tolerance(string field, double nominal, double lower, double upper)
        {
            if (upper < lower) Error(field, "Верхнее отклонение не должно быть меньше нижнего.");
            if (nominal + lower <= 0) Error(field, "Нижняя граница размера должна быть положительной.");
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
        if (p.RpmMax != Math.Floor(p.RpmMax)) Error("rpmMax", "Ограничение оборотов задаётся целым числом.");
        if (p.FlangeWidth >= lengthMin) Error("flangeWidth", "Ширина буртика должна быть меньше минимальной длины по допуску.");
        if (depthMax >= lengthMin) Error("counterboreDepth", "Максимальная глубина расточки должна быть меньше минимальной длины по допуску.");
        if (p.BodyDiameter >= flangeMin) Error("bodyDiameter", "Диаметр тела должен быть меньше минимального диаметра буртика по допуску.");
        if (boreMax >= cbMin) Error("counterboreDiameter", "Минимальная расточка должна быть шире максимального основного отверстия.");
        if ((p.BodyDiameter - boreMax) / 2 < 3 - 1e-8) Error("boreDiameter", "Толщина стенки тела по худшему сочетанию допусков должна быть не менее 3 мм.");
        if ((flangeMin - cbMax) / 2 < 3 - 1e-8) Error("counterboreDiameter", "Толщина стенки буртика по худшему сочетанию допусков должна быть не менее 3 мм.");
        if (depthMax > p.FlangeWidth && (p.BodyDiameter - cbMax) / 2 < 3 - 1e-8)
            Error("counterboreDiameter", "Расточка заходит за буртик: стенка тела при максимальном отверстии должна быть не менее 3 мм.");
        var minRemoval = 2 * (p.FinishAllowance + .05);
        if (p.StockDiameter - c.TargetFlangeDiameter < minRemoval - 1e-8) Error("stockDiameter", "Для буртика нужен чистовой припуск и не менее 0,05 мм чернового съёма на сторону.");
        if (c.TargetBoreDiameter - p.StockBore < minRemoval - 1e-8) Error("stockBore", "Исходное отверстие должно оставлять чистовой припуск и не менее 0,05 мм чернового съёма.");
        if (c.TargetFlangeDiameter - p.BodyDiameter < minRemoval - 1e-8) Error("bodyDiameter", "Перепад наружных расчётных диаметров слишком мал для данного чистового припуска.");
        if (c.TargetCounterboreDiameter - c.TargetBoreDiameter < minRemoval - 1e-8) Error("counterboreDiameter", "Перепад внутренних расчётных диаметров слишком мал для данного чистового припуска.");
        if (p.FinishAllowance > Math.Min(p.DepthOutside, p.DepthInside)) Error("finishAllowance", "Чистовой припуск не должен превышать разрешённую глубину прохода.");
        if (p.StockBore - 2 * p.ClearanceX < 12) Error("clearanceX", "В исходном отверстии недостаточно места для учебного отвода (минимальный X 12 мм).");
        if (p.FeedFinish > p.FeedRough) Error("feedFinish", "В учебном профиле чистовая подача не превышает черновую.");
        double Count(double difference, double depth) => Math.Max(0, Math.Ceiling((difference - 2 * p.FinishAllowance - 1e-8) / (2 * depth)));
        var passes = Count(p.StockDiameter - c.TargetFlangeDiameter, p.DepthOutside) + Count(p.StockDiameter - p.BodyDiameter, p.DepthOutside)
            + Count(c.TargetBoreDiameter - p.StockBore, p.DepthInside) + Count(c.TargetCounterboreDiameter - c.TargetBoreDiameter, p.DepthInside) + 4;
        if (passes > 400) Error("depthOutside", "Слишком много проходов (более 400): увеличьте глубину или уменьшите припуск.");
        return errors;
    }

    public static List<double> PassDiameters(double start, double target, double depth)
    {
        if (!double.IsFinite(start) || !double.IsFinite(target) || !double.IsFinite(depth) || depth <= 0)
            throw new ArgumentOutOfRangeException(nameof(depth), "Параметры проходов должны быть конечными, глубина — положительной.");
        var delta = target - start;
        var count = Math.Max(0, Math.Ceiling((Math.Abs(delta) - 1e-8) / (2 * depth)));
        if (count > 400) throw new ArgumentOutOfRangeException(nameof(depth), "Число проходов превышает предел 400.");
        var values = new List<double>();
        for (var i = 1; i <= count; i++) values.Add(Round(i == count ? target : start + Math.Sign(delta) * Math.Min(2 * depth * i, Math.Abs(delta))));
        return values;
    }

    private record Definition(string Id, string Title, string Tag, int Tool, string Kind, double Start,
        double Target, double ZStart, double ZEnd, double Depth, bool Finish, double ReferenceDiameter);

    public static GenerationResult Generate(Parameters? parameters = null)
    {
        var p = parameters ?? Defaults;
        var errors = Validate(p);
        if (errors.Count > 0) return new(false, errors, [], null);
        var c = Calculate(p);
        var id = "V-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Version + "|" + JsonSerializer.Serialize(p, ProjectFiles.JsonOptions))))[..16];
        var warnings = new List<string>
        {
            "Учебный профиль. Db=100, b=12, материал, заготовка и режимы — изменяемые предложения разработчика. Материал не выбирает режимы автоматически.",
            "Расчётные размеры выбраны в середине заданных полей допусков и округлены до 0,001 мм. Это проектное решение; фактическая точность обработки не гарантируется.",
            "Диалект LinuxCNC 2.9, G7: X задаётся диаметром. Привязка к реальному станку и его инструментальной таблице отсутствует.",
            "Модель идеальной точки инструмента: радиус вершины, державка, патрон, силы закрепления, прогиб, столкновения и достижимость допусков не проверяются.",
            "Операция 005 подготавливает торец и сквозное исходное отверстие. УП покрывает только 010. Отрезка и доводка заднего торца выполняются отдельно в 020.",
            "Время рассчитано только для G1, включая подводы на рабочей подаче. Быстрые перемещения, смена инструмента, ручные операции и разгон не нормируются."
        };
        var controls = HtmlCards.ControlDescription(p, c);
        var operations = new List<Operation>
        {
            new("005", "Подготовка заготовки", "manual", $"Получить цилиндрическую заготовку Ø{Fmt(p.StockDiameter)} / Ø{Fmt(p.StockBore)} × {Fmt(c.StockLength)} мм со сквозным отверстием и подготовленным правым торцом. Метод получения отверстия и контроль выбираются технологом. Длина указана после подготовки торца."),
            new("010", "Токарная с ЧПУ — учебная", "nc", $"Один установ за хвостовик {Fmt(p.GripLength)} мм. Z0 — правый подготовленный торец, Z− — к зажиму. Обработать буртик, тело, отверстие и расточку. Длина обработки {Fmt(c.CutLength)} мм включает задний припуск. Расчётные диаметры D={Fmt(c.TargetFlangeDiameter)}, d={Fmt(c.TargetBoreDiameter)}, d2={Fmt(c.TargetCounterboreDiameter)}; глубина h={Fmt(c.TargetCounterboreDepth)} мм."),
            new("020", "Отделение и доводка", "manual", $"Отделить деталь с сохранением припуска {Fmt(p.TrimAllowance)} мм; переустановить в мягкие кулачки и довести задний торец до расчётной длины {Fmt(c.TargetLength)} мм (номинал {Fmt(p.Length)}, границы {Fmt(p.Length + p.LengthLowerDeviation)}…{Fmt(p.Length + p.LengthUpperDeviation)} мм). Удалить заусенцы. Фаски не назначаются: размеры отсутствуют. Закрепление и отрезной инструмент требуют уточнения."),
            new("030", "Контроль", "manual", controls + $" Db={Fmt(p.BodyDiameter)}, b={Fmt(p.FlangeWidth)} мм — учебные размеры без заданных допусков. Средства: микрометр/штангенциркуль, нутромер и глубиномер по согласованной методике. Расчётная цель не гарантирует фактическую точность; шероховатость пока не утверждена.")
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
        var lines = new List<string>
        {
            $"(PARAMFORGE {Version} - {id})",
            "(EDUCATIONAL - NOT VERIFIED ON A MACHINE)",
            "(LINUXCNC 2.9 - MM - X DIAMETER - OPERATION 010 ONLY)",
            $"(TARGETS D {Fmt(c.TargetFlangeDiameter)} d {Fmt(c.TargetBoreDiameter)} d2 {Fmt(c.TargetCounterboreDiameter)} L {Fmt(c.TargetLength)} h {Fmt(c.TargetCounterboreDepth)})"
        };
        var line = 0;
        (double X, double Z)? position = null;
        var activeTool = 0;
        int Emit(string text) { line += 10; lines.Add("N" + line.ToString(CultureInfo.InvariantCulture) + " " + text); return line; }
        void Move(string mode, double x, double z, Transition? t)
        {
            x = Round(x); z = Round(z);
            var number = Emit($"{mode} X{Fmt(x)} Z{Fmt(z)}" + (mode == "G1" ? " F" + Fmt(t!.Feed) : ""));
            var distance = position is { } a ? Math.Sqrt(Math.Pow((x - a.X) / 2, 2) + Math.Pow(z - a.Z, 2)) : 0;
            if (mode == "G1") t!.CutMinutes += distance / (t.Feed * t.Rpm);
            moves.Add(new(x, z, mode, t?.Id ?? "", number, mode == "G1" ? t!.Feed : null, t?.Rpm ?? 0));
            t?.Blocks.Add(number);
            position = (x, z);
        }
        Emit("G21 G18 G7 G90 G95 G97 G40 G49 G80 G54");
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
                if (activeTool != 0) { Move("G0", c.ClearOut, p.ClearanceZ, null); Emit("M5"); }
                Emit($"T{d.Tool} M6"); activeTool = d.Tool;
            }
            lines.Add($"(TR {d.Id} {d.Tag})");
            Emit($"S{Fmt(t.Rpm)} M3");
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
        Move("G0", c.ClearIn, p.ClearanceZ, null); Move("G0", c.ClearOut, p.ClearanceZ, null); Emit("M5"); Emit("M2");
        c = c with { TotalCutMinutes = transitions.Sum(t => t.CutMinutes) };
        var nc = string.Join('\n', lines) + "\n";
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
            Moves = moves, Nc = nc, Warnings = warnings, Verification = verification };
        project = project with { RouteHtml = HtmlCards.Route(project), TechnologyHtml = HtmlCards.Technology(project) };
        return new(true, [], warnings, project);
    }
}
