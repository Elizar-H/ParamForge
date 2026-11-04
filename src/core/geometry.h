#pragma once
#include "model.h"
#include <QLineF>
#include <QPointF>
#include <cmath>

namespace pf
{
inline bool drawablePart(const Parameters& p)
{
    for (const auto& d : dimensions())
    {
        const auto& f = field(d.nominal);
        if (!std::isfinite(p.n(d.nominal)) || p.n(d.nominal) < f.minimum || p.n(d.nominal) > f.maximum ||
            !std::isfinite(p.n(d.lower)) || !std::isfinite(p.n(d.upper)) || p.n(d.lower) > p.n(d.upper) ||
            p.n(d.nominal) + p.n(d.lower) <= 0)
            return false;
    }
    const auto lo = [&](const QString& key)
    {
        const auto& d = dimension(key);
        return p.n(key) + p.n(d.lower);
    };
    const auto hi = [&](const QString& key)
    {
        const auto& d = dimension(key);
        return p.n(key) + p.n(d.upper);
    };
    return hi("flangeWidth") < lo("length") && hi("counterboreDepth") < lo("length") &&
           hi("bodyDiameter") < lo("flangeDiameter") && hi("boreDiameter") < lo("counterboreDiameter") &&
           lo("bodyDiameter") - hi("boreDiameter") >= 6 - 1e-8 &&
           lo("flangeDiameter") - hi("counterboreDiameter") >= 6 - 1e-8 &&
           (hi("counterboreDepth") < lo("flangeWidth") || lo("bodyDiameter") - hi("counterboreDiameter") >= 6 - 1e-8) &&
           lo("flangeWidth") > .5 && lo("counterboreDepth") > .5;
}
inline bool drawableSetup(const Parameters& p)
{
    return drawablePart(p) && std::isfinite(p.n("stockDiameter")) && std::isfinite(p.n("stockBore")) &&
           p.n("stockDiameter") > p.n("stockBore") && p.n("stockBore") > 0;
}
inline QVector<QPointF> axisContour(const Parameters& p, bool calculated = false)
{
    const auto c = calculate(p);
    const auto length = calculated ? c.targetLength : p.n("length");
    const auto flange = calculated ? c.targetFlangeDiameter : p.n("flangeDiameter");
    const auto body = calculated ? c.targetBodyDiameter : p.n("bodyDiameter");
    const auto bore = calculated ? c.targetBoreDiameter : p.n("boreDiameter");
    const auto cb = calculated ? c.targetCounterboreDiameter : p.n("counterboreDiameter");
    const auto width = calculated ? c.targetFlangeWidth : p.n("flangeWidth");
    const auto depth = calculated ? c.targetCounterboreDepth : p.n("counterboreDepth");
    return {{0, flange / 2},     {-width, flange / 2}, {-width, body / 2}, {-length, body / 2},
            {-length, bore / 2}, {-depth, bore / 2},   {-depth, cb / 2},   {0, cb / 2}};
}
inline QVector<QLineF> sectionProjectionEdges(const Parameters& p, bool calculated = false)
{
    const auto c = calculate(p);
    const auto length = calculated ? c.targetLength : p.n("length");
    const auto depth = calculated ? c.targetCounterboreDepth : p.n("counterboreDepth");
    const auto bore = (calculated ? c.targetBoreDiameter : p.n("boreDiameter")) / 2;
    const auto counterbore = (calculated ? c.targetCounterboreDiameter : p.n("counterboreDiameter")) / 2;
    return {
        {{-length, bore}, {-length, -bore}}, {{-depth, bore}, {-depth, -bore}}, {{0, counterbore}, {0, -counterbore}}};
}
} // namespace pf
