#include "model.h"
#include <QJsonDocument>

namespace pf
{
QString inspectionDescription(const Parameters& p, const Calculations& c)
{
    const QMap<QString, double> targets = {{"length", c.targetLength},
                                           {"flangeDiameter", c.targetFlangeDiameter},
                                           {"bodyDiameter", c.targetBodyDiameter},
                                           {"flangeWidth", c.targetFlangeWidth},
                                           {"boreDiameter", c.targetBoreDiameter},
                                           {"counterboreDiameter", c.targetCounterboreDiameter},
                                           {"counterboreDepth", c.targetCounterboreDepth}};
    QStringList text;
    for (const auto& d : dimensions())
    {
        const auto lo = p.n(d.lower), hi = p.n(d.upper);
        const auto designation = p.s(d.designation);
        text.append(field(d.nominal).label + ": " + localized(p.n(d.nominal)) +
                    (designation.isEmpty() ? QString() : " " + designation) + "; границы " +
                    localized(p.n(d.nominal) + lo) + "…" + localized(p.n(d.nominal) + hi) + " мм; цель " +
                    localized(targets.value(d.key)) + " мм");
    }
    return "Проверить: " + text.join("; ") + ".";
}
QVector<Operation> operations(const Parameters& p, const Calculations& c)
{
    return {{"005", "Подготовка заготовки", "manual",
             "Принять полый полуфабрикат Ø" + localized(p.n("stockDiameter")) + " / Ø" + localized(p.n("stockBore")) +
                 " × " + localized(c.rawStockLength) + " мм. Снять передний припуск " +
                 localized(p.n("frontFacingAllowance")) + " мм до длины " + localized(c.stockLength) +
                 " мм. Подготовленный торец принять исходным. Способ получения сквозного отверстия — вне данного "
                 "процесса."},
            {"010", "Токарная с ЧПУ — первый установ", "nc",
             "Закрепить за наружную поверхность заготовки на длине " + localized(c.gripLength) +
                 " мм. Передняя плоскость кулачков Z=" + localized(c.jawsFrontZ) + " мм, осевой зазор " +
                 localized(c.chuckClearance) +
                 " мм. G54: Z0 — подготовленный передний торец, X0 — ось вращения; X задаётся по диаметру. Обработать "
                 "буртик, тело, сквозное отверстие и внутреннюю ступень. Длина до отделения " +
                 localized(c.cutLength) + " мм, задний припуск " + localized(p.n("trimAllowance")) +
                 " мм. Инструменты: SCLCR 2525M12 и " + boringTool(p).name +
                 ", пластина CCMT 12 04 04-PF 4425, R0,4; галтели "
                 "R≤0,5. Непрерывная СОЖ."},
            {"020", "Токарная с ЧПУ — второй установ", "nc",
             "До запуска УП отделить деталь: отрезная полоса Z=" + localized(c.partingFrontZ) + "…" +
                 localized(c.partingBackZ) + " мм, ширина " + localized(c.cutoffWidth) +
                 " мм. Отрезка — отдельный ручной шаг, инструмент " + partingToolName(p) +
                 ". Перевернуть "
                 "деталь и закрепить в мягких кулачках за обработанное тело Ø" +
                 localized(c.targetBodyDiameter) + " мм. Контактный пояс " + localized(c.secondClampStart) + "…" +
                 localized(c.secondClampEnd) + " мм от первого торца; длина зажима 25 мм. Карман под буртик Ø" +
                 localized(c.softJawPocketDiameter) + " глубиной " + localized(c.softJawPocketDepth) +
                 " мм, упор по первому торцу. Для этого установа заново задать G54: Z0 — окончательный задний торец; "
                 "исходный торец находится в Z+" +
                 localized(p.n("trimAllowance")) + " мм. УП снимает припуск до Z0, конечная длина " +
                 localized(c.targetLength) + " мм. Удалить заусенцы без изменения контролируемых размеров."},
            {"030", "Контроль", "manual", inspectionDescription(p, c)}};
}
QJsonObject projectJson(const Project& project)
{
    QJsonArray programs, operationsArray;
    for (const auto& p : project.programs)
    {
        QJsonArray transitions, moves, parsed;
        for (const auto& t : p.transitions)
        {
            QJsonArray passArray, blocks;
            for (auto v : t.passes)
                passArray.append(v);
            for (auto v : t.blocks)
                blocks.append(v);
            transitions.append(QJsonObject{{"id", t.id},
                                           {"title", t.title},
                                           {"tool", t.tool},
                                           {"kind", t.kind},
                                           {"diameter", t.diameter},
                                           {"startDiameter", t.startDiameter},
                                           {"rpm", t.rpm},
                                           {"feed", t.feed},
                                           {"depth", t.depth},
                                           {"referenceDiameter", t.referenceDiameter},
                                           {"passes", passArray},
                                           {"blocks", blocks},
                                           {"zStart", t.zStart},
                                           {"zEnd", t.zEnd},
                                           {"cutMinutes", t.cutMinutes}});
        }
        for (const auto& m : p.moves)
            moves.append(QJsonObject{{"x", m.x},
                                     {"z", m.z},
                                     {"mode", m.mode},
                                     {"transition", m.transition},
                                     {"line", m.line},
                                     {"feed", m.feed ? QJsonValue(*m.feed) : QJsonValue(QJsonValue::Null)},
                                     {"rpm", m.rpm}});
        for (const auto& m : p.verification.moves)
            parsed.append(QJsonObject{{"x", m.x},
                                      {"z", m.z},
                                      {"mode", m.mode},
                                      {"line", m.line},
                                      {"physicalLine", m.physicalLine},
                                      {"tool", m.tool},
                                      {"feed", m.feed ? QJsonValue(*m.feed) : QJsonValue(QJsonValue::Null)},
                                      {"rpm", m.rpm ? QJsonValue(*m.rpm) : QJsonValue(QJsonValue::Null)}});
        programs.append(QJsonObject{{"setupId", p.setupId},
                                    {"name", p.name},
                                    {"fileName", p.fileName},
                                    {"nc", p.text},
                                    {"transitions", transitions},
                                    {"moves", moves},
                                    {"parsedMoves", parsed},
                                    {"cutMinutes", p.verification.cutMinutes},
                                    {"setup", QJsonObject{{"flipped", p.setup.flipped},
                                                          {"stockFrontZ", p.setup.stockFrontZ},
                                                          {"jawsFrontZ", p.setup.jawsFrontZ},
                                                          {"gripLength", p.setup.gripLength},
                                                          {"gripDiameter", p.setup.gripDiameter},
                                                          {"rearZ", p.setup.rearZ}}}});
    }
    for (const auto& o : project.operations)
        operationsArray.append(
            QJsonObject{{"code", o.code}, {"title", o.title}, {"kind", o.kind}, {"description", o.description}});
    return {{"id", project.id},
            {"version", Version},
            {"params", project.parameters.json()},
            {"calculations", project.calculations.json()},
            {"operations", operationsArray},
            {"programs", programs}};
}
} // namespace pf
