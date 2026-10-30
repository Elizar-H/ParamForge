namespace Kontur.Core;

internal static class ParameterSchema
{
    public static IReadOnlyList<FieldDefinition> Fields
    {
        get;
    } = Array.AsReadOnly(new[] { new FieldDefinition("length", "Длина L", "мм", "Геометрия", 20, 250, .1, "Номинальная длина детали после доводки заднего торца"), new("flangeDiameter", "Диаметр буртика D", "мм", "Геометрия", 30, 200, .1, "Номинальный наружный диаметр буртика"), new("bodyDiameter", "Диаметр тела Db", "мм", "Геометрия", 25, 195, .1, "Наружный диаметр тела за буртиком"), new("flangeWidth", "Ширина буртика b", "мм", "Геометрия", 2, 60, .1, "Осевой размер буртика от переднего торца"), new(
        "boreDiameter",
        "Основное отверстие d",
        "мм",
        "Геометрия",
        20,
        160,
        .1,
        "Номинальный диаметр основного сквозного отверстия"
    ), new(
    "counterboreDiameter",
    "Диаметр расточки d2",
    "мм",
    "Геометрия",
    22,
    180,
    .1,
    "Номинальный диаметр внутренней ступени"
), new(
    "counterboreDepth",
    "Глубина расточки h",
    "мм",
    "Геометрия",
    1,
    240,
    .1,
    "Глубина внутренней ступени от переднего торца"
), new(
    "stockDiameter",
    "Наружный диаметр заготовки",
    "мм",
    "Заготовка",
    32,
    240,
    .1,
    "Наружный диаметр заготовки до точения"
), new(
    "stockBore",
    "Исходное отверстие заготовки",
    "мм",
    "Заготовка",
    16,
    155,
    .1,
    "Диаметр сквозного отверстия до растачивания"
), new(
    "trimAllowance",
    "Припуск заднего торца",
    "мм",
    "Заготовка",
    .5,
    5,
    .1,
    "Припуск, снимаемый с заднего торца в операции 020"
), new(
    "gripLength",
    "Длина зажима",
    "мм",
    "Заготовка",
    ToolingProfile.ChuckMinimumGrip,
    80,
    1,
    "Фактическая длина заготовки, охваченная кулачками"
), new(
    "chuckClearance",
    "Зазор до кулачков",
    "мм",
    "Заготовка",
    ToolingProfile.ChuckMinimumClearance,
    40,
    .1,
    "От задней границы отрезного реза до кулачков: габарит проектного блока 6 мм и остаточный запас не менее 4 мм"
), new(
    "frontFacingAllowance",
    "Припуск переднего торца",
    "мм",
    "Заготовка",
    .5,
    5,
    .1,
    "Осевой припуск, снимаемый в операции 005 до задания Z0"
), new(
    "depthOutside",
    "Глубина наружного прохода ap",
    "мм",
    "Режимы",
    .1,
    5,
    .1,
    "Максимальная радиальная глубина одного наружного прохода"
), new(
    "depthInside",
    "Глубина внутреннего прохода ap",
    "мм",
    "Режимы",
    .1,
    3,
    .1,
    "Максимальная радиальная глубина одного внутреннего прохода"
), new(
    "finishAllowance",
    "Чистовой припуск a",
    "мм",
    "Режимы",
    .05,
    .5,
    .05,
    "Радиальный припуск, оставляемый перед чистовым проходом"
), new(
    "cuttingSpeed",
    "Заданная скорость v",
    "м/мин",
    "Режимы",
    10,
    150,
    1,
    "Скорость для расчёта оборотов по опорному диаметру"
), new(
    "feedRough",
    "Черновая подача f",
    "мм/об",
    "Режимы",
    .03,
    .5,
    .01,
    "Перемещение инструмента за оборот при черновой обработке"
), new(
    "feedFinish",
    "Чистовая подача f",
    "мм/об",
    "Режимы",
    .02,
    .3,
    .01,
    "Перемещение инструмента за оборот при чистовой обработке"
), new("rpmMax", "Ограничение частоты n", "об/мин", "Режимы", 100, 3000, 1, "Верхняя граница частоты вращения шпинделя"), new(
    "clearanceX",
    "Радиальный отвод",
    "мм",
    "Подходы",
    .5,
    5,
    .1,
    "Радиальный отвод; в координате X учитывается удвоенная величина"
), new(
    "clearanceZ",
    "Отвод перед торцом",
    "мм",
    "Подходы",
    1,
    10,
    .1,
    "Положительная координата Z исходной точки перед торцом"
), new(
    "boreLowerDeviation",
    "d: нижнее отклонение",
    "мм",
    "Допуски",
    -5,
    5,
    .01,
    "Отклонение от номинала d до нижней границы отверстия"
), new(
    "boreUpperDeviation",
    "d: верхнее отклонение",
    "мм",
    "Допуски",
    -5,
    5,
    .01,
    "Отклонение от номинала d до верхней границы отверстия"
), new(
    "counterboreLowerDeviation",
    "d2: нижнее отклонение",
    "мм",
    "Допуски",
    -5,
    5,
    .01,
    "Отклонение от номинала d2 до нижней границы ступени"
), new(
    "counterboreUpperDeviation",
    "d2: верхнее отклонение",
    "мм",
    "Допуски",
    -5,
    5,
    .01,
    "Отклонение от номинала d2 до верхней границы ступени"
), new(
    "flangeLowerDeviation",
    "D: нижнее отклонение",
    "мм",
    "Допуски",
    -5,
    5,
    .01,
    "Отклонение от номинала D до нижней границы буртика"
), new(
    "flangeUpperDeviation",
    "D: верхнее отклонение",
    "мм",
    "Допуски",
    -5,
    5,
    .01,
    "Отклонение от номинала D до верхней границы буртика"
), new(
    "lengthLowerDeviation",
    "L: нижнее отклонение",
    "мм",
    "Допуски",
    -5,
    5,
    .01,
    "Отклонение от номинала L до нижней границы конечной длины"
), new(
    "lengthUpperDeviation",
    "L: верхнее отклонение",
    "мм",
    "Допуски",
    -5,
    5,
    .01,
    "Отклонение от номинала L до верхней границы конечной длины"
), new(
    "counterboreDepthLowerDeviation",
    "h: нижнее отклонение",
    "мм",
    "Допуски",
    -5,
    5,
    .01,
    "Отклонение от номинала h до нижней границы глубины ступени"
), new("bodyLowerDeviation", "Db: нижнее отклонение", "мм", "Допуски", -5, 5, .01, "Нижнее отклонение диаметра тела"), new("bodyUpperDeviation", "Db: верхнее отклонение", "мм", "Допуски", -5, 5, .01, "Верхнее отклонение диаметра тела"), new(
    "flangeWidthLowerDeviation",
    "b: нижнее отклонение",
    "мм",
    "Допуски",
    -5,
    5,
    .01,
    "Нижнее отклонение ширины бурта"
), new(
    "flangeWidthUpperDeviation",
    "b: верхнее отклонение",
    "мм",
    "Допуски",
    -5,
    5,
    .01,
    "Верхнее отклонение ширины бурта"
), new(
    "counterboreDepthUpperDeviation",
    "h: верхнее отклонение",
    "мм",
    "Допуски",
    -5,
    5,
    .01,
    "Отклонение от номинала h до верхней границы глубины ступени"
) });

    // Имена полей едины для формы, JSON и сообщений валидации.
    internal static double Number(Parameters p, string key) => key switch
    {
        "length" => p.Length,
        "flangeDiameter" => p.FlangeDiameter,
        "bodyDiameter" => p.BodyDiameter,
        "flangeWidth" => p.FlangeWidth,
        "boreDiameter" => p.BoreDiameter,
        "counterboreDiameter" => p.CounterboreDiameter,
        "counterboreDepth" => p.CounterboreDepth,
        "stockDiameter" => p.StockDiameter,
        "stockBore" => p.StockBore,
        "trimAllowance" => p.TrimAllowance,
        "gripLength" => p.GripLength,
        "chuckClearance" => p.ChuckClearance,
        "frontFacingAllowance" => p.FrontFacingAllowance,
        "depthOutside" => p.DepthOutside,
        "depthInside" => p.DepthInside,
        "finishAllowance" => p.FinishAllowance,
        "cuttingSpeed" => p.CuttingSpeed,
        "feedRough" => p.FeedRough,
        "feedFinish" => p.FeedFinish,
        "rpmMax" => p.RpmMax,
        "clearanceX" => p.ClearanceX,
        "clearanceZ" => p.ClearanceZ,
        "boreLowerDeviation" => p.BoreLowerDeviation,
        "boreUpperDeviation" => p.BoreUpperDeviation,
        "counterboreLowerDeviation" => p.CounterboreLowerDeviation,
        "counterboreUpperDeviation" => p.CounterboreUpperDeviation,
        "flangeLowerDeviation" => p.FlangeLowerDeviation,
        "flangeUpperDeviation" => p.FlangeUpperDeviation,
        "lengthLowerDeviation" => p.LengthLowerDeviation,
        "lengthUpperDeviation" => p.LengthUpperDeviation,
        "counterboreDepthLowerDeviation" => p.CounterboreDepthLowerDeviation,
        "counterboreDepthUpperDeviation" => p.CounterboreDepthUpperDeviation,
        "bodyLowerDeviation" => p.BodyLowerDeviation,
        "bodyUpperDeviation" => p.BodyUpperDeviation,
        "flangeWidthLowerDeviation" => p.FlangeWidthLowerDeviation,
        "flangeWidthUpperDeviation" => p.FlangeWidthUpperDeviation,
        _ => throw new ArgumentException("Неизвестный параметр: " + key)
    };
}
