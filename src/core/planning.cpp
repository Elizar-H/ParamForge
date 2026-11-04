#include "model.h"
#include <cmath>
#include <numbers>
#include <stdexcept>

namespace pf
{
QVector<double> passes(double start, double finish, double depth)
{
    if (!std::isfinite(start) || !std::isfinite(finish) || !std::isfinite(depth) || depth <= 0 ||
        std::abs(start) > 10000 || std::abs(finish) > 10000)
        throw std::invalid_argument("Некорректные параметры проходов.");
    const auto a = std::llround(start * 10000), b = std::llround(finish * 10000),
               step = 2 * std::llround(depth * 10000);
    if (step <= 0)
        throw std::invalid_argument("Глубина прохода слишком мала.");
    const auto count = (std::abs(b - a) + step - 1) / step;
    if (count > 400)
        throw std::invalid_argument("Число проходов превышает 400.");
    QVector<double> result;
    for (qint64 i = 1; i <= count; ++i)
    {
        const auto numerator = a * count + (b - a) * i, denominator = count * 10;
        const auto rounded = (std::abs(numerator) + denominator / 2) / denominator;
        result.append(static_cast<double>(numerator < 0 ? -rounded : rounded) / 1000.);
    }
    return result;
}
namespace
{
class Builder
{
  public:
    Plan plan;
    std::optional<std::pair<double, double>> position;
    void emit(InstructionKind kind, int tool = 0, double value = 0, const QString& text = {})
    {
        plan.instructions.append({kind, {}, tool, value, text});
    }
    void move(const QString& mode, double x, double z, Transition* t)
    {
        x = roundTo(x);
        z = roundTo(z);
        if (mode == "G1" && position)
            t->cutMinutes += std::hypot((x - position->first) / 2, z - position->second) / (t->feed * t->rpm);
        Move motion{x,
                    z,
                    mode,
                    t ? t->id : QString(),
                    0,
                    mode == "G1" ? std::optional<double>(t->feed) : std::nullopt,
                    t ? t->rpm : 0};
        plan.instructions.append({InstructionKind::Motion, motion});
        position = {{x, z}};
    }
    void prepare()
    {
        emit(InstructionKind::Prepare);
        emit(InstructionKind::Diameter);
        emit(InstructionKind::FeedPerMinute);
        emit(InstructionKind::CoolantOff);
        emit(InstructionKind::Stop);
    }
    void end()
    {
        emit(InstructionKind::FeedPerMinute);
        emit(InstructionKind::CoolantOff);
        emit(InstructionKind::Stop);
        emit(InstructionKind::End);
    }
};
} // namespace
Plan firstSetup(const Parameters& p, const Calculations& c, const QString& id)
{
    struct Definition
    {
        QString id, title, tag, kind;
        int tool;
        double start, target, zStart, zEnd, depth, reference;
        bool finish;
    };
    const auto allowance = p.n("finishAllowance");
    const QVector<Definition> definitions = {
        {"010.01", "Буртик — черновое точение", "FLANGE ROUGH", "outer", 1, p.n("stockDiameter"),
         c.targetFlangeDiameter + 2 * allowance, 0, c.targetFlangeWidth, p.n("depthOutside"), p.n("stockDiameter"),
         false},
        {"010.02", "Буртик — чистовое точение", "FLANGE FINISH", "outer", 1, c.targetFlangeDiameter + 2 * allowance,
         c.targetFlangeDiameter, 0, c.targetFlangeWidth, allowance, c.targetFlangeDiameter, true},
        {"010.03", "Тело — черновое точение", "BODY ROUGH", "body", 1, p.n("stockDiameter"),
         c.targetBodyDiameter + 2 * allowance, c.targetFlangeWidth, c.cutLength, p.n("depthOutside"),
         p.n("stockDiameter"), false},
        {"010.04", "Тело — чистовое точение", "BODY FINISH", "body", 1, c.targetBodyDiameter + 2 * allowance,
         c.targetBodyDiameter, c.targetFlangeWidth, c.cutLength, allowance, c.targetBodyDiameter, true},
        {"010.05", "Основное отверстие — черновое растачивание", "BORE ROUGH", "inner", 2, p.n("stockBore"),
         c.targetBoreDiameter - 2 * allowance, 0, c.cutLength, p.n("depthInside"), c.targetBoreDiameter, false},
        {"010.06", "Основное отверстие — чистовое растачивание", "BORE FINISH", "inner", 2,
         c.targetBoreDiameter - 2 * allowance, c.targetBoreDiameter, 0, c.cutLength, p.n("finishAllowance"),
         c.targetBoreDiameter, true},
        {"010.07", "Внутренняя ступень — черновое растачивание", "STEP ROUGH", "inner", 2, c.targetBoreDiameter,
         c.targetCounterboreDiameter - 2 * allowance, 0, c.targetCounterboreDepth, p.n("depthInside"),
         c.targetCounterboreDiameter, false},
        {"010.08", "Внутренняя ступень — чистовое растачивание", "STEP FINISH", "inner", 2,
         c.targetCounterboreDiameter - 2 * allowance, c.targetCounterboreDiameter, 0, c.targetCounterboreDepth,
         allowance, c.targetCounterboreDiameter, true}};
    Builder b;
    b.plan.id = id;
    b.plan.setup = {"010",        "Первый установ",     false,         0, c.jawsFrontZ,
                    c.gripLength, p.n("stockDiameter"), -c.stockLength};
    b.plan.blank = Plan::Blank{0, -c.stockLength, c.jawsFrontZ, p.n("stockDiameter"), p.n("stockBore")};
    b.prepare();
    int activeTool = 0;
    for (const auto& d : definitions)
    {
        Transition t;
        t.id = d.id;
        t.title = d.title;
        t.tool = "T" + QString::number(d.tool);
        t.kind = d.kind;
        t.diameter = roundTo(d.target);
        t.startDiameter = roundTo(d.start);
        t.rpm = std::min(p.n("rpmMax"), std::floor(1000 * p.n("cuttingSpeed") / (std::numbers::pi * d.reference)));
        t.feed = p.n(d.finish ? "feedFinish" : "feedRough");
        t.depth = d.depth;
        t.referenceDiameter = d.reference;
        t.passes = passes(d.start, d.target, d.depth);
        t.zStart = -d.zStart;
        t.zEnd = -d.zEnd;
        if (d.tool != activeTool)
        {
            if (activeTool != 0)
            {
                b.move("G0", c.clearOut, p.n("clearanceZ"), nullptr);
                b.emit(InstructionKind::FeedPerMinute);
                b.emit(InstructionKind::CoolantOff);
                b.emit(InstructionKind::Stop);
            }
            b.emit(InstructionKind::Tool, d.tool);
            if (d.tool == 2 &&
                (p.s("boringTool") != "a32t" || p.n("boringOverhang") != 125 || p.n("boringClamp") != 128))
            {
                const auto tool = boringTool(p);
                b.emit(InstructionKind::Comment, 0, 0,
                       (tool.id == "custom" ? QString("CUSTOM BORING BAR") : tool.name) + " BD" +
                           QString::number(tool.diameter, 'f', 3) + " DMIN" + QString::number(tool.minBore, 'f', 3) +
                           " OH" + QString::number(p.n("boringOverhang"), 'f', 3) + " CLAMP" +
                           QString::number(p.n("boringClamp"), 'f', 3));
            }
            b.emit(InstructionKind::GeometryOffset);
            b.emit(InstructionKind::CoolantOn);
            activeTool = d.tool;
        }
        b.emit(InstructionKind::Comment, 0, 0, "TR " + d.id + " " + d.tag);
        b.emit(InstructionKind::Speed, 0, t.rpm);
        b.emit(InstructionKind::FeedPerRevolution);
        b.move("G0", d.kind == "inner" ? c.clearIn : c.clearOut, p.n("clearanceZ"), &t);
        for (const auto diameter : t.passes)
        {
            if (d.kind == "body")
            {
                b.move("G0", c.clearOut, -d.zStart, &t);
                b.move("G1", diameter, -d.zStart, &t);
                b.move("G1", diameter, -d.zEnd, &t);
                b.move("G0", c.clearOut, -d.zEnd, &t);
                b.move("G0", c.clearOut, p.n("clearanceZ"), &t);
            }
            else if (d.kind == "outer")
            {
                b.move("G0", diameter, p.n("clearanceZ"), &t);
                b.move("G1", diameter, -d.zEnd, &t);
                b.move("G0", c.clearOut, -d.zEnd, &t);
                b.move("G0", c.clearOut, p.n("clearanceZ"), &t);
            }
            else
            {
                b.move("G0", diameter, p.n("clearanceZ"), &t);
                b.move("G1", diameter, -d.zEnd, &t);
                b.move("G1", c.clearIn, -d.zEnd, &t);
                b.move("G0", c.clearIn, p.n("clearanceZ"), &t);
            }
        }
        b.plan.transitions.append(t);
    }
    b.move("G0", c.clearIn, p.n("clearanceZ"), nullptr);
    b.move("G0", c.clearOut, p.n("clearanceZ"), nullptr);
    b.end();
    return b.plan;
}
Plan secondSetup(const Parameters& p, const Calculations& c, const QString& id)
{
    Builder b;
    b.plan.id = id;
    b.plan.setup = {
        "020", "Второй установ",     true,           p.n("trimAllowance"), roundTo(-c.targetLength + c.secondClampEnd),
        25,    c.targetBodyDiameter, -c.targetLength};
    const auto safeX = roundTo(c.targetBodyDiameter + 2 * p.n("clearanceX"));
    const auto insideX = roundTo(c.targetBoreDiameter - 2 * p.n("clearanceX"));
    const auto safeZ = roundTo(p.n("trimAllowance") + p.n("clearanceZ"));
    b.prepare();
    b.emit(InstructionKind::Tool, 1);
    b.emit(InstructionKind::GeometryOffset);
    b.emit(InstructionKind::CoolantOn);
    const auto allowance = p.n("finishAllowance");
    const auto trimTicks = dimensionTicks(p.n("trimAllowance")), finishTicks = dimensionTicks(allowance);
    const auto depthTicks = dimensionTicks(p.n("depthOutside"));
    const int roughCount =
        static_cast<int>((std::max<qint64>(0, trimTicks - finishTicks) + depthTicks - 1) / depthTicks);
    for (int stage = 0; stage < 2; ++stage)
    {
        if (stage == 0 && roughCount == 0)
            continue;
        Transition t;
        t.id = stage == 0 ? "020.01" : "020.02";
        t.title = stage == 0 ? "Задний торец — черновое торцевание" : "Задний торец — чистовое торцевание";
        t.tool = "T1";
        t.kind = "face";
        t.diameter = c.targetBodyDiameter;
        t.startDiameter = c.targetBodyDiameter;
        t.rpm =
            std::min(p.n("rpmMax"), std::floor(1000 * p.n("cuttingSpeed") / (std::numbers::pi * c.targetBodyDiameter)));
        t.feed = p.n(stage == 0 ? "feedRough" : "feedFinish");
        t.depth = stage == 0 ? p.n("depthOutside") : allowance;
        t.referenceDiameter = c.targetBodyDiameter;
        t.zStart = stage == 0 ? p.n("trimAllowance") : allowance;
        t.zEnd = stage == 0 ? allowance : 0;
        if (stage == 0)
            for (int i = 1; i <= roughCount; ++i)
            {
                const auto numerator = trimTicks * roughCount - (trimTicks - finishTicks) * i;
                const auto denominator = roughCount * 10;
                t.passes.append(static_cast<double>((numerator + denominator / 2) / denominator) / 1000.);
            }
        else
            t.passes.append(0);
        b.emit(InstructionKind::Comment, 0, 0, "TR " + t.id + (stage == 0 ? " BACK FACE ROUGH" : " BACK FACE FINISH"));
        b.emit(InstructionKind::Speed, 0, t.rpm);
        b.emit(InstructionKind::FeedPerRevolution);
        b.move("G0", safeX, safeZ, &t);
        for (const auto z : t.passes)
        {
            // The entire face cut proceeds at constant Z; retraction first enters the bore.
            b.move("G0", safeX, z, &t);
            b.move("G1", insideX, z, &t);
            b.move("G0", insideX, safeZ, &t);
            b.move("G0", safeX, safeZ, &t);
        }
        b.plan.transitions.append(t);
    }
    b.end();
    return b.plan;
}
} // namespace pf
