using static Kontur.Core.Engine;

namespace Kontur.Core;

internal static class ParameterValidator
{
    public static List<ValidationError> Validate(Parameters p)
    {
        var errors = new List<ValidationError>();
        foreach (var key in new[]
        {
            "bodyDiameter",
            "flangeWidth"
        }

        )
        {
            var field = Fields.Single(f => f.Key == key);
            var number = Number(p, key);
            if (!double.IsFinite(number) || number < field.Min || number > field.Max)
            {
                errors.Add(new(key, field.Label + ": размер вне диапазона."));
            }
        }

        if (errors.Count != 0)
        {
            return errors;
        }

        if (!CncProfiles.Supported(p.Controller))
        {
            errors.Add(new("controller", "Неизвестный профиль ЧПУ."));
        }

        try
        {
            p = StockSelection.Resolve(Tolerances.Resolve(p));
        }
        catch (ArgumentException ex)
        {
            errors.Add(new("tolerances", ex.Message));
        }

        if (errors.Count != 0)
        {
            return errors;
        }

        void Error(string field, string message) => errors.Add(new(field, message));
        foreach (var (key, label, value, maximum) in new[]
        {
            ("partName", "Наименование детали", p.PartName, 45),
            ("material", "Материал", p.Material, 35)
        }

        )
        {
            if (string.IsNullOrWhiteSpace(value) || value.Length > maximum || value.Any(c => char.IsControl(c) || c is '\u2028' or '\u2029'))
            {
                Error(key, $"{label}: от 1 до {maximum} символов без переносов строки и управляющих символов.");
            }
        }

        foreach (var (key, label, maximum) in CardFields.Fields)
        {
            var value = CardFields.Value(p, key);
            if (value is null || value.Length > maximum || value.Any(c => char.IsControl(c) || c is '\u2028' or '\u2029'))
            {
                Error(key, $"{label}: не более {maximum} символов без переносов строки.");
            }
        }

        foreach (var f in Fields)
        {
            var v = Number(p, f.Key);
            if (!double.IsFinite(v))
            {
                Error(f.Key, f.Label + ": требуется конечное число.");
            }
            else if (v < f.Min || v > f.Max)
            {
                Error(f.Key, $"{f.Label}: диапазон {Fmt(f.Min)}–{Fmt(f.Max)} {f.Unit}.");
            }
            else
            {
                var precision = f.Key.EndsWith("Deviation", StringComparison.Ordinal) ? 4 : 3;
                if ((decimal)v != Math.Round((decimal)v, precision, MidpointRounding.AwayFromZero))
                {
                    Error(
                        f.Key,
                        f.Label + (precision == 4 ? ": не более четырёх знаков после запятой." : ": не более трёх знаков после запятой.")
                    );
                }
            }
        }

        if (errors.Count != 0)
        {
            return errors;
        }

        void Tolerance(string field, double nominal, double lower, double upper)
        {
            if (upper < lower)
            {
                Error(field, "Верхнее отклонение не должно быть меньше нижнего.");
            }

            if (nominal + lower <= 0)
            {
                Error(field, "Нижняя граница размера должна быть положительной.");
            }

            if (upper >= lower)
            {
                var target = (decimal)Target(nominal, lower, upper);
                if (target < (decimal)nominal + (decimal)lower || target > (decimal)nominal + (decimal)upper)
                {
                    Error(field, "В интервал допуска не попадает целевой размер, округлённый до 0,001 мм.");
                }
            }
        }

        Tolerance("bodyUpperDeviation", p.BodyDiameter, p.BodyLowerDeviation, p.BodyUpperDeviation);
        Tolerance("flangeWidthUpperDeviation", p.FlangeWidth, p.FlangeWidthLowerDeviation, p.FlangeWidthUpperDeviation);
        Tolerance("boreUpperDeviation", p.BoreDiameter, p.BoreLowerDeviation, p.BoreUpperDeviation);
        Tolerance("counterboreUpperDeviation", p.CounterboreDiameter, p.CounterboreLowerDeviation, p.CounterboreUpperDeviation);
        Tolerance("flangeUpperDeviation", p.FlangeDiameter, p.FlangeLowerDeviation, p.FlangeUpperDeviation);
        Tolerance("lengthUpperDeviation", p.Length, p.LengthLowerDeviation, p.LengthUpperDeviation);
        Tolerance(
            "counterboreDepthUpperDeviation",
            p.CounterboreDepth,
            p.CounterboreDepthLowerDeviation,
            p.CounterboreDepthUpperDeviation
        );
        if (errors.Count != 0)
        {
            return errors;
        }
        // The displayed drawing uses nominal dimensions. Both that drawing and the
        // full tolerance envelope must describe the selected flanged-bushing family.
        if (p.FlangeWidth >= p.Length)
        {
            Error("flangeWidth", "Для чертежа нужно b < L.");
        }

        if (p.CounterboreDepth >= p.Length)
        {
            Error("counterboreDepth", "Для чертежа нужно h < L.");
        }

        if (p.BodyDiameter >= p.FlangeDiameter)
        {
            Error("bodyDiameter", "Для чертежа нужно Db < D.");
        }

        if (p.BoreDiameter >= p.CounterboreDiameter)
        {
            Error("counterboreDiameter", "Для чертежа нужно d < d2.");
        }

        if (p.BoreDiameter >= p.BodyDiameter)
        {
            Error("boreDiameter", "Для чертежа нужно d < Db.");
        }

        if (p.CounterboreDiameter >= p.FlangeDiameter)
        {
            Error("counterboreDiameter", "Для чертежа нужно d2 < D.");
        }

        if (p.CounterboreDepth > p.FlangeWidth && p.CounterboreDiameter >= p.BodyDiameter)
        {
            Error("counterboreDiameter", "При h > b требуется d2 < Db.");
        }

        if (errors.Count != 0)
        {
            return errors;
        }

        var c = Calculate(p);
        var flangeMin = p.FlangeDiameter + p.FlangeLowerDeviation;
        var boreMax = p.BoreDiameter + p.BoreUpperDeviation;
        var cbMin = p.CounterboreDiameter + p.CounterboreLowerDeviation;
        var cbMax = p.CounterboreDiameter + p.CounterboreUpperDeviation;
        var lengthMin = p.Length + p.LengthLowerDeviation;
        var depthMax = p.CounterboreDepth + p.CounterboreDepthUpperDeviation;
        var bodyMin = p.BodyDiameter + p.BodyLowerDeviation;
        var bodyMax = p.BodyDiameter + p.BodyUpperDeviation;
        var widthMin = p.FlangeWidth + p.FlangeWidthLowerDeviation;
        var widthMax = p.FlangeWidth + p.FlangeWidthUpperDeviation;
        if (p.RpmMax != Math.Floor(p.RpmMax))
        {
            Error("rpmMax", "Ограничение оборотов задаётся целым числом.");
        }

        if (widthMax >= lengthMin)
        {
            Error("flangeWidth", "Максимальная ширина буртика должна быть меньше минимальной длины по допускам.");
        }

        if (depthMax >= lengthMin)
        {
            Error("counterboreDepth", "Максимальная глубина расточки должна быть меньше минимальной длины по допуску.");
        }

        if (bodyMax >= flangeMin)
        {
            Error("bodyDiameter", "Максимальный диаметр тела должен быть меньше минимального диаметра буртика по допускам.");
        }

        if (boreMax >= cbMin)
        {
            Error("counterboreDiameter", "Минимальная расточка должна быть шире максимального основного отверстия.");
        }

        if ((bodyMin - boreMax) / 2 < 3 - 1e-8)
        {
            Error("boreDiameter", "Толщина стенки тела по худшему сочетанию допусков должна быть не менее 3 мм.");
        }

        if ((flangeMin - cbMax) / 2 < 3 - 1e-8)
        {
            Error("counterboreDiameter", "Толщина стенки буртика по худшему сочетанию допусков должна быть не менее 3 мм.");
        }

        if (depthMax > widthMin && (bodyMin - cbMax) / 2 < 3 - 1e-8)
        {
            Error(
                "counterboreDiameter",
                "Расточка заходит за буртик: стенка тела при максимальном отверстии должна быть не менее 3 мм."
            );
        }

        var minRemoval = 2 * (p.FinishAllowance + .05);
        if (p.StockDiameter < MinimumStockDiameter(p) - 1e-8)
        {
            Error("stockDiameter", "Для буртика нужен чистовой припуск и не менее 0,05 мм чернового съёма на сторону.");
        }

        if (c.TargetBoreDiameter - p.StockBore < minRemoval - 1e-8)
        {
            Error("stockBore", "Исходное отверстие должно оставлять чистовой припуск и не менее 0,05 мм чернового съёма.");
        }

        if (c.TargetFlangeDiameter - c.TargetBodyDiameter < minRemoval - 1e-8)
        {
            Error("bodyDiameter", "Перепад наружных расчётных диаметров слишком мал для данного чистового припуска.");
        }

        if (c.TargetCounterboreDiameter - c.TargetBoreDiameter < minRemoval - 1e-8)
        {
            Error("counterboreDiameter", "Перепад внутренних расчётных диаметров слишком мал для данного чистового припуска.");
        }

        if (p.FinishAllowance > Math.Min(p.DepthOutside, p.DepthInside))
        {
            Error("finishAllowance", "Чистовой припуск не должен превышать разрешённую глубину прохода.");
        }

        errors.AddRange(ToolingProfile.Validate(p, c));
        if (p.FeedFinish > p.FeedRough)
        {
            Error("feedFinish", "Чистовая подача не должна превышать черновую.");
        }

        double Count(double difference, double depth) => (double)Math.Max(0m, Math.Ceiling(((decimal)difference - 2m * (decimal)p.FinishAllowance) / (2m * (decimal)depth)));
        var passes = Count(p.StockDiameter - c.TargetFlangeDiameter, p.DepthOutside) + Count(p.StockDiameter - c.TargetBodyDiameter, p.DepthOutside) + Count(c.TargetBoreDiameter - p.StockBore, p.DepthInside) + Count(c.TargetCounterboreDiameter - c.TargetBoreDiameter, p.DepthInside) + 4;
        if (passes > 400)
        {
            Error("depthOutside", "Слишком много проходов (более 400): увеличьте глубину или уменьшите припуск.");
        }

        return errors;
    }
}
