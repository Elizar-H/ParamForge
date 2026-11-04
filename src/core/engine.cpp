#include "model.h"
#include <QCryptographicHash>
#include <QJsonDocument>
#include <cmath>
#include <stdexcept>

namespace pf
{
Calculations calculate(const Parameters& p)
{
    Calculations c;
    c.cutoffWidth = p.n("cutoffWidth");
    c.targetLength = target(p.n("length"), p.n("lengthLowerDeviation"), p.n("lengthUpperDeviation"));
    c.cutLength = roundTo(c.targetLength + p.n("trimAllowance"));
    const auto front = p.n("frontFacingAllowance");
    c.stockOverhang = p.b("autoBlankOverhang")
                          ? roundedDimensionSum({c.cutLength, c.cutoffWidth, p.n("chuckClearance"), front})
                          : p.n("blankOverhang");
    c.rawStockLength =
        p.b("autoBlankLength") ? roundedDimensionSum({c.stockOverhang, p.n("gripLength")}) : p.n("blankLength");
    c.stockLength = roundTo(c.rawStockLength - front);
    c.gripLength = roundTo(c.rawStockLength - c.stockOverhang);
    c.chuckClearance = roundTo(c.stockOverhang - front - c.cutLength - c.cutoffWidth);
    c.clearOut = roundTo(p.n("stockDiameter") + 2 * p.n("clearanceX"));
    c.clearIn = roundTo(p.n("stockBore") - 2 * p.n("clearanceX"));
    c.targetBoreDiameter = target(p.n("boreDiameter"), p.n("boreLowerDeviation"), p.n("boreUpperDeviation"));
    c.targetCounterboreDiameter =
        target(p.n("counterboreDiameter"), p.n("counterboreLowerDeviation"), p.n("counterboreUpperDeviation"));
    c.targetFlangeDiameter = target(p.n("flangeDiameter"), p.n("flangeLowerDeviation"), p.n("flangeUpperDeviation"));
    c.targetCounterboreDepth =
        target(p.n("counterboreDepth"), p.n("counterboreDepthLowerDeviation"), p.n("counterboreDepthUpperDeviation"));
    c.targetBodyDiameter = target(p.n("bodyDiameter"), p.n("bodyLowerDeviation"), p.n("bodyUpperDeviation"));
    c.targetFlangeWidth =
        target(p.n("flangeWidth"), p.n("flangeWidthLowerDeviation"), p.n("flangeWidthUpperDeviation"));
    c.partingFrontZ = -c.cutLength;
    c.partingBackZ = -roundTo(c.cutLength + c.cutoffWidth);
    c.jawsFrontZ = roundTo(front - c.stockOverhang);
    c.secondClampStart = roundedDimensionSum({p.n("flangeWidth"), p.n("flangeWidthUpperDeviation"), 2});
    c.secondClampEnd = roundedDimensionSum({c.secondClampStart, 25});
    c.softJawPocketDiameter = roundedDimensionSum({p.n("flangeDiameter"), p.n("flangeUpperDeviation"), 1});
    c.softJawPocketDepth = roundedDimensionSum({p.n("flangeWidth"), p.n("flangeWidthUpperDeviation"), 1});
    return c;
}
static bool validText(const QJsonValue& value, int maximum, bool required)
{
    if (!value.isString())
        return false;
    const auto text = value.toString();
    if (text.size() > maximum || (required && text.trimmed().isEmpty()))
        return false;
    for (qsizetype i = 0; i < text.size(); ++i)
    {
        const auto ch = text[i];
        if (ch.isHighSurrogate())
        {
            if (++i >= text.size() || !text[i].isLowSurrogate())
                return false;
        }
        else if (ch.isLowSurrogate() || ch.category() == QChar::Other_Control || ch == QChar(0x2028) ||
                 ch == QChar(0x2029) || ch == QChar(0xfffe) || ch == QChar(0xffff))
            return false;
    }
    return true;
}
QVector<Error> validate(const Parameters& input)
{
    QVector<Error> errors;
    auto error = [&](const QString& key, const QString& message) { errors.append({key, message}); };
    for (const auto& key : {"bodyDiameter", "flangeWidth", "blankLength", "blankOverhang"})
    {
        const auto& f = field(key);
        const auto value = input.n(key);
        if (!std::isfinite(value) || value < f.minimum || value > f.maximum)
            error(key, f.label + ": размер вне диапазона.");
    }
    if (!errors.isEmpty())
        return errors;
    if (input.s("controller") != "axioma" && input.s("controller") != "sinumerik840dsl")
        error("controller", "Неизвестный профиль ЧПУ.");
    auto p = input;
    errors += validateToolSetup(p);
    if (!errors.isEmpty())
        return errors;
    try
    {
        p = resolveStock(resolveTolerances(p));
    }
    catch (const std::exception& e)
    {
        error("tolerances", QString::fromUtf8(e.what()));
    }
    if (!errors.isEmpty())
        return errors;
    for (const auto& item : {std::pair{"partName", 45}, std::pair{"material", 35}})
        if (!validText(p.json().value(item.first), item.second, true))
            error(item.first, "Введите короткое наименование без переносов строки.");
    for (const auto& item : schema()["cardFields"].toArray())
    {
        const auto f = item.toObject();
        if (!validText(p.json().value(f["key"].toString()), f["maximum"].toInt(), false))
            error(f["key"].toString(), "Реквизит слишком длинный или содержит недопустимые символы.");
    }
    for (const auto& f : fields())
    {
        const auto value = p.n(f.key);
        if (!std::isfinite(value))
            error(f.key, f.label + ": требуется конечное число.");
        else if (value < f.minimum || value > f.maximum)
            error(f.key,
                  f.label + ": диапазон " + localized(f.minimum) + "…" + localized(f.maximum) + " " + f.unit + ".");
        else
        {
            const int precision = f.key.endsWith("Deviation") ? 4 : 3;
            if (std::abs(value - roundTo(value, precision)) > 1e-10)
                error(f.key, f.label + ": слишком много знаков после запятой.");
        }
    }
    if (!errors.isEmpty())
        return errors;
    for (const auto& d : dimensions())
    {
        const auto nominal = p.n(d.nominal), lo = p.n(d.lower), hi = p.n(d.upper);
        if (hi < lo)
            error(d.upper, "Верхнее отклонение должно быть не меньше нижнего.");
        if (nominal + lo <= 0)
            error(d.upper, "Нижняя граница размера должна быть положительной.");
        const auto t = target(nominal, lo, hi);
        if (hi >= lo && (t < nominal + lo - 1e-10 || t > nominal + hi + 1e-10))
            error(d.upper, "В интервал допуска не попадает цель с точностью 0,001 мм.");
    }
    if (!errors.isEmpty())
        return errors;
    if (p.n("flangeWidth") >= p.n("length"))
        error("flangeWidth", "Требуется b < L.");
    if (p.n("counterboreDepth") >= p.n("length"))
        error("counterboreDepth", "Требуется h < L.");
    if (p.n("bodyDiameter") >= p.n("flangeDiameter"))
        error("bodyDiameter", "Требуется Db < D.");
    if (p.n("boreDiameter") >= p.n("counterboreDiameter"))
        error("counterboreDiameter", "Требуется d < d2.");
    if (p.n("boreDiameter") >= p.n("bodyDiameter"))
        error("boreDiameter", "Требуется d < Db.");
    if (p.n("counterboreDiameter") >= p.n("flangeDiameter"))
        error("counterboreDiameter", "Требуется d2 < D.");
    if (p.n("counterboreDepth") >= p.n("flangeWidth") && p.n("counterboreDiameter") >= p.n("bodyDiameter"))
        error("counterboreDiameter", "При h ≥ b требуется d2 < Db.");
    if (!errors.isEmpty())
        return errors;
    const auto c = calculate(p);
    const auto flangeMin = p.n("flangeDiameter") + p.n("flangeLowerDeviation");
    const auto boreMax = p.n("boreDiameter") + p.n("boreUpperDeviation");
    const auto cbMin = p.n("counterboreDiameter") + p.n("counterboreLowerDeviation");
    const auto cbMax = p.n("counterboreDiameter") + p.n("counterboreUpperDeviation");
    const auto lengthMin = p.n("length") + p.n("lengthLowerDeviation");
    const auto depthMax = p.n("counterboreDepth") + p.n("counterboreDepthUpperDeviation");
    const auto bodyMin = p.n("bodyDiameter") + p.n("bodyLowerDeviation");
    const auto bodyMax = p.n("bodyDiameter") + p.n("bodyUpperDeviation");
    const auto widthMin = p.n("flangeWidth") + p.n("flangeWidthLowerDeviation");
    const auto widthMax = p.n("flangeWidth") + p.n("flangeWidthUpperDeviation");
    if (p.n("rpmMax") != std::floor(p.n("rpmMax")))
        error("rpmMax", "Ограничение оборотов задаётся целым числом.");
    if (widthMax >= lengthMin)
        error("flangeWidth", "Ширина буртика по допускам должна быть меньше длины.");
    if (depthMax >= lengthMin)
        error("counterboreDepth", "Глубина расточки по допускам должна быть меньше длины.");
    if (bodyMax >= flangeMin)
        error("bodyDiameter", "Тело по допускам должно быть меньше буртика.");
    if (boreMax >= cbMin)
        error("counterboreDiameter", "Расточка по допускам должна быть шире отверстия.");
    if ((bodyMin - boreMax) / 2 < 3 - 1e-8)
        error("boreDiameter", "Стенка тела по допускам должна быть не менее 3 мм.");
    if ((flangeMin - cbMax) / 2 < 3 - 1e-8)
        error("counterboreDiameter", "Стенка буртика по допускам должна быть не менее 3 мм.");
    if (depthMax >= widthMin - 1e-8 && (bodyMin - cbMax) / 2 < 3 - 1e-8)
        error("counterboreDiameter", "Стенка тела в зоне расточки должна быть не менее 3 мм.");
    const auto minRemoval = 2 * (p.n("finishAllowance") + .05);
    if (p.n("stockDiameter") < minimumStockDiameter(p) - 1e-8)
        error("stockDiameter", "Недостаточно наружного припуска на обработку буртика.");
    if (c.targetBoreDiameter - p.n("stockBore") < minRemoval - 1e-8)
    {
        const auto selectedTool = boringTool(p);
        const auto requiredBore = selectedTool.minBore + 2 * p.n("clearanceX");
        error("stockBore", requiredBore > c.targetBoreDiameter - minRemoval
                               ? "Оправка " + selectedTool.name + " требует исходное отверстие от " +
                                     localized(requiredBore) +
                                     " мм с учётом отвода. Для этой детали нужно отверстие не больше " +
                                     localized(c.targetBoreDiameter - minRemoval) + " мм. Выберите меньшую оправку."
                               : "Недостаточно припуска на растачивание отверстия.");
    }
    if (c.targetFlangeDiameter - c.targetBodyDiameter < minRemoval - 1e-8)
        error("bodyDiameter", "Перепад наружных диаметров слишком мал.");
    if (c.targetCounterboreDiameter - c.targetBoreDiameter < minRemoval - 1e-8)
        error("counterboreDiameter", "Перепад внутренних диаметров слишком мал.");
    if (p.n("finishAllowance") > std::min(p.n("depthOutside"), p.n("depthInside")))
        error("finishAllowance", "Чистовой припуск превышает глубину прохода.");
    const auto tool = boringTool(p);
    const auto boreMessage = "Для " + tool.name + " с радиальным отводом " + localized(p.n("clearanceX")) +
                             " мм исходное отверстие должно быть не меньше " +
                             localized(tool.minBore + 2 * p.n("clearanceX")) + " мм.";
    if (p.n("stockBore") < tool.minBore)
        error("stockBore", boreMessage);
    if (c.clearIn < tool.minBore - 1e-8)
        error("clearanceX", boreMessage);
    if (c.cutLength > p.n("boringOverhang") - 5 + 1e-8)
        error("length",
              "В этом маршруте отверстие растачивается целиком в установе 010; установ 020 обрабатывает задний торец. "
              "При текущем вылете допустимая глубина с задним припуском — " +
                  localized(p.n("boringOverhang") - 5) + " мм, сейчас " + localized(c.cutLength) +
                  " мм. Двустороннее растачивание этим маршрутом не предусмотрено.");
    if (c.gripLength < 25 - 1e-8)
        error("blankLength", "Длина заготовки должна быть не меньше " + localized(c.stockOverhang + 25) +
                                 " мм: после вылета нужен участок зажима не менее 25 мм.");
    if (c.chuckClearance < 10 - 1e-8)
        error("blankOverhang", "Вылет должен быть не меньше " +
                                   localized(c.cutLength + c.cutoffWidth + p.n("frontFacingAllowance") + 10) +
                                   " мм: нужно место для детали, припусков, отрезки и зазора до кулачков 10 мм.");
    if ((flangeMin - bodyMax) / 2 - .5 < .001 - 1e-8)
        error("bodyDiameter", "Недостаточно плоской части наружного уступа при R≤0,5.");
    if ((cbMin - boreMax) / 2 - .5 < .001 - 1e-8)
        error("counterboreDiameter", "Недостаточно плоской части внутреннего уступа при R≤0,5.");
    if (lengthMin - c.secondClampEnd < 5 - 1e-8)
        error("length", "Для второго установа нужны пояс зажима 25 мм и свободный участок 5 мм.");
    if (lengthMin - widthMax - .5 < .001 - 1e-8)
        error("flangeWidth", "После галтели должна оставаться прямая часть тела.");
    if (widthMin - .5 < .001 - 1e-8)
        error("flangeWidth", "Ширина буртика недостаточна для галтели R≤0,5.");
    if (p.n("counterboreDepth") + p.n("counterboreDepthLowerDeviation") - .5 < .001 - 1e-8)
        error("counterboreDepth", "Глубина расточки недостаточна для галтели R≤0,5.");
    if (p.n("feedFinish") > p.n("feedRough"))
        error("feedFinish", "Чистовая подача превышает черновую.");
    const auto count = [&](double difference, double depth)
    {
        const auto remaining = std::llround(difference * 10000) - 2 * std::llround(p.n("finishAllowance") * 10000);
        const auto step = 2 * std::llround(depth * 10000);
        return std::max<qint64>(0, (remaining + step - 1) / step);
    };
    const auto countAll = count(p.n("stockDiameter") - c.targetFlangeDiameter, p.n("depthOutside")) +
                          count(p.n("stockDiameter") - c.targetBodyDiameter, p.n("depthOutside")) +
                          count(c.targetBoreDiameter - p.n("stockBore"), p.n("depthInside")) +
                          count(c.targetCounterboreDiameter - c.targetBoreDiameter, p.n("depthInside")) + 4;
    if (countAll > 400)
        error("depthOutside", "Слишком много проходов: более 400.");
    return errors;
}
Generation generate(const Parameters& input)
{
    Generation result;
    result.errors = validate(input);
    if (!result.errors.isEmpty())
        return result;
    Project project;
    project.parameters = resolveStock(resolveTolerances(input));
    project.calculations = calculate(project.parameters);
    project.id =
        "V-" + QString::fromLatin1(
                   QCryptographicHash::hash(QByteArray(Version) + "|" +
                                                QJsonDocument(project.parameters.json()).toJson(QJsonDocument::Compact),
                                            QCryptographicHash::Sha256)
                       .toHex()
                       .left(16)
                       .toUpper());
    for (const auto& plan : {firstSetup(project.parameters, project.calculations, project.id),
                             secondSetup(project.parameters, project.calculations, project.id)})
    {
        auto program = postprocess(plan, project.calculations, project.parameters.s("controller"));
        program.verification = verify(program.text, project.parameters, program.setup);
        for (const auto& message : program.verification.errors)
            result.errors.append({"nc", program.setupId + ": " + message});
        if (program.moves.size() != program.verification.moves.size())
            result.errors.append(Error{"nc", "Разбор УП восстановил другое число движений."});
        else
            for (qsizetype i = 0; i < program.moves.size(); ++i)
            {
                const auto& a = program.moves[i];
                const auto& b = program.verification.moves[i];
                if (std::abs(a.x - b.x) > 1e-9 || std::abs(a.z - b.z) > 1e-9 || a.mode != b.mode || a.line != b.line ||
                    (a.mode == "G1" &&
                     (!b.feed || !b.rpm || std::abs(*a.feed - *b.feed) > 1e-9 || std::abs(a.rpm - *b.rpm) > 1e-9)))
                {
                    result.errors.append(Error{"nc", "Движения расчётного плана и сформированной УП различаются."});
                    break;
                }
            }
        double plannedTime = 0;
        for (const auto& t : program.transitions)
            plannedTime += t.cutMinutes;
        if (std::abs(plannedTime - program.verification.cutMinutes) > 1e-9)
            result.errors.append(Error{"nc", "Время G1 плана и УП различается."});
        project.calculations.totalCutMinutes += program.verification.cutMinutes;
        project.programs.append(std::move(program));
    }
    if (!result.errors.isEmpty())
        return result;
    project.operations = operations(project.parameters, project.calculations);
    result.project = std::move(project);
    return result;
}
} // namespace pf
