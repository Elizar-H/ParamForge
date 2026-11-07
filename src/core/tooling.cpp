#include "tooling.h"
#include <cmath>
#include <stdexcept>

namespace pf
{
QString turningToolName(const Parameters& p)
{
    return p.s("outerTool") == "custom" ? p.s("customOuterName") : QString("SCLCR 2525M12");
}
QString bodyToolName(const Parameters& p)
{
    return p.s("bodyTool") == "custom" ? p.s("customBodyName") : QString("SCLCL 2525M12");
}
const QVector<BoringTool>& boringTools()
{
    static const QVector<BoringTool> tools = {{"a32t", "A32T-SCLCR 12", 32, 40, 22, 300, 48, 128},
                                              {"a40t", "A40T-SCLCR 12", 40, 50, 27, 300, 60, 160}};
    return tools;
}
QString partingToolName(const Parameters& p)
{
    return p.n("cutoffWidth") == 3 ? QString("QD-NN2G60C25A / QD-NG-0300-0002-CM 1125")
                                   : "Отрезной инструмент под рез " + localized(p.n("cutoffWidth")) + " мм";
}
BoringTool boringTool(const Parameters& p)
{
    if (p.s("boringTool") == "custom")
        return {"custom",
                p.s("customBoringName"),
                p.n("customBoringDiameter"),
                p.n("customBoringMinBore"),
                p.n("customBoringOffset"),
                p.n("customBoringLength"),
                1,
                p.n("customBoringMaxOverhang")};
    for (const auto& tool : boringTools())
        if (tool.id == p.s("boringTool"))
            return tool;
    throw std::invalid_argument("Неизвестная расточная оправка.");
}
QVector<Error> validateToolSetup(const Parameters& p)
{
    QVector<Error> errors;
    BoringTool tool;
    try
    {
        tool = boringTool(p);
    }
    catch (const std::exception& e)
    {
        return {{"boringTool", QString::fromUtf8(e.what())}};
    }
    const auto fail = [&](const QString& key, const QString& text) { errors.append({key, text}); };
    if (p.s("outerTool") != "sclcr" && p.s("outerTool") != "custom")
        fail("outerTool", "Неизвестный наружный резец.");
    if (p.s("bodyTool") != "sclcl" && p.s("bodyTool") != "custom")
        fail("bodyTool", "Неизвестный обратный наружный резец.");
    for (const auto& prefix : {QString("Outer"), QString("Body")})
    {
        if (p.s(prefix.toLower() + "Tool") != "custom")
            continue;
        const auto key = "custom" + prefix + "Name";
        const auto name = p.s(key);
        bool printable = true;
        for (const auto ch : name)
            printable &= ch.isPrint();
        if (name.trimmed().isEmpty() || name.size() > 35 || !printable)
            fail(key, "Введите название резца в одну строку, до 35 знаков.");
    }
    for (const auto& key : {"outerToolNumber", "outerCorrector", "bodyToolNumber", "bodyCorrector",
                            "boringToolNumber", "boringCorrector"})
    {
        const bool corrector = QString(key).endsWith("Corrector");
        const int maximum = corrector && p.s("controller") == "axioma" ? 4 : 99;
        if (!std::isfinite(p.n(key)) || p.n(key) < 1 || p.n(key) > maximum || p.n(key) != std::floor(p.n(key)))
            fail(key, QString(corrector ? "Номер корректора" : "Номер инструмента") +
                          " должен быть целым числом от 1 до " + QString::number(maximum) + ".");
    }
    if (p.n("outerToolNumber") == p.n("boringToolNumber"))
        fail("boringToolNumber", "Наружный резец и расточная оправка должны иметь разные номера T.");
    if (p.n("bodyToolNumber") == p.n("outerToolNumber") || p.n("bodyToolNumber") == p.n("boringToolNumber"))
        fail("bodyToolNumber", "Обратный резец, резец для буртика и расточная оправка должны иметь разные номера T.");
    for (const auto& key : {"boringOverhang", "boringClamp", "customBoringDiameter", "customBoringMinBore",
                            "customBoringOffset", "customBoringLength", "customBoringMaxOverhang"})
        if (!std::isfinite(p.n(key)) || p.n(key) <= 0 || p.n(key) > 1000)
            fail(key, "Размер инструмента должен быть положительным и не больше 1000 мм.");
    if (!errors.isEmpty())
        return errors;
    if (tool.id == "custom")
    {
        if (tool.name.trimmed().isEmpty() || tool.name.size() > 35)
            fail("customBoringName", "Введите название оправки длиной до 35 знаков.");
        for (const auto ch : tool.name)
            if (!ch.isPrint())
            {
                fail("customBoringName", "Название оправки должно быть в одну строку.");
                break;
            }
        if (tool.minBore < tool.diameter)
            fail("customBoringMinBore", "Минимальное отверстие должно быть не меньше диаметра оправки.");
        if (tool.offset < tool.diameter / 2 || tool.offset > tool.minBore - tool.diameter / 2)
            fail("customBoringOffset",
                 "При диаметре оправки " + localized(tool.diameter) + " мм и минимальном отверстии " +
                     localized(tool.minBore) + " мм вершина от оси должна быть " + localized(tool.diameter / 2) +
                     "…" + localized(tool.minBore - tool.diameter / 2) + " мм. Сейчас " + localized(tool.offset) + " мм.");
    }
    if (p.n("boringOverhang") < tool.minOverhang || p.n("boringOverhang") > tool.maxOverhang)
        fail("boringOverhang", "Для " + tool.name + " вылет должен быть " + localized(tool.minOverhang) + "…" +
                                   localized(tool.maxOverhang) + " мм.");
    if (p.n("boringClamp") < 4 * tool.diameter)
        fail("boringClamp",
             "Длина зажима оправки должна быть не меньше " + localized(4 * tool.diameter) + " мм (4 диаметра).");
    if (p.n("boringOverhang") + p.n("boringClamp") > tool.length + 1e-8)
        fail("boringOverhang", "Сумма вылета и зажима превышает длину оправки " + localized(tool.length) + " мм.");
    return errors;
}
} // namespace pf
