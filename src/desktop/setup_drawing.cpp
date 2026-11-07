#include "setup_drawing.h"
#include "core/engine.h"
#include "core/geometry.h"
#include "core/planning.h"
#include "core/tooling.h"
#include "drawing.h"
#include "theme.h"
#include <QPainter>
#include <QPainterPath>
#include <algorithm>
#include <cmath>
#include <numbers>

namespace pf::ui
{
SetupMapping fitSetup(const QRectF& bounds, const Parameters& parameters, const Setup& setup, bool positiveOnly,
                      bool keepPartOrientation)
{
    const auto c = calculate(parameters);
    const auto radius = std::max(parameters.n("stockDiameter"), parameters.n("flangeDiameter")) / 2;
    const auto left = keepPartOrientation
                          ? std::min(-c.stockLength - 14, -c.targetLength - parameters.n("trimAllowance") - 12)
                      : setup.flipped ? -c.targetLength - 20
                                      : -c.stockLength - 14;
    const auto right = keepPartOrientation ? std::max(20., parameters.n("frontFacingAllowance") + 12)
                       : setup.flipped     ? parameters.n("trimAllowance") + 12
                                           : parameters.n("frontFacingAllowance") + 12;
    SetupMapping m;
    if (keepPartOrientation && setup.flipped)
    {
        m.zDirection = -1;
        m.zOffset = -c.targetLength;
    }
    m.scale = std::max(.1, std::min((bounds.width() - (positiveOnly ? 90 : 170)) / (right - left),
                                    (bounds.height() - (positiveOnly ? 70 : SetupTopMargin + SetupBottomMargin)) /
                                        (positiveOnly ? radius + 25 : 2 * radius + 30)));
    m.origin = {positiveOnly ? bounds.left() + 45 - left * m.scale
                             : bounds.center().x() - 25 - (left + right) / 2 * m.scale,
                positiveOnly ? bounds.bottom() - 35 : bounds.top() + SetupTopMargin + (radius + 15) * m.scale};
    return m;
}
SetupMapping drawSetup(QPainter& p, const QRectF& bounds, const Parameters& parameters, const Setup& setup,
                       const QString& highlight, bool positiveOnly, std::optional<SetupMapping> viewport)
{
    const auto c = calculate(parameters);
    const auto radius = std::max(parameters.n("stockDiameter"), parameters.n("flangeDiameter")) / 2;
    const auto left = setup.flipped ? -c.targetLength - 20 : -c.stockLength - 14;
    const auto right = setup.flipped ? parameters.n("trimAllowance") + 12 : parameters.n("frontFacingAllowance") + 12;
    auto m = viewport.value_or(fitSetup(bounds, parameters, setup, positiveOnly));
    const auto map = [&](double z, double r) { return m.map(z, r); };
    const bool depthView = highlight == "depthOutside" || highlight == "depthInside";
    const bool inside = highlight == "depthInside";
    const auto passStart = setup.flipped ? parameters.n("trimAllowance")
                           : inside      ? c.targetBoreDiameter / 2
                                         : parameters.n("stockDiameter") / 2;
    const auto passFinish = setup.flipped ? parameters.n("finishAllowance")
                            : inside      ? c.targetCounterboreDiameter / 2 - parameters.n("finishAllowance")
                                          : c.targetBodyDiameter / 2 + parameters.n("finishAllowance");
    QVector<double> levels;
    if (depthView)
        try
        {
            for (const auto value : passes(2 * passStart, 2 * passFinish, parameters.n(highlight)))
                levels.append(setup.flipped ? roundTo(value / 2) : value / 2);
        }
        catch (const std::exception&)
        {
        }
    p.save();
    p.setRenderHint(QPainter::Antialiasing);
    p.setPen(QPen(border(), 1, Qt::DashLine));
    if (!setup.flipped)
    {
        const QRectF stock(map(-c.stockLength, radius), map(0, positiveOnly ? 0 : -radius));
        p.fillRect(stock, QColor(dark() ? "#303030" : "#f0f0f0"));
        p.drawRect(stock);
        const QRectF hole(map(-c.stockLength, parameters.n("stockBore") / 2),
                          map(0, positiveOnly ? 0 : -parameters.n("stockBore") / 2));
        p.fillRect(hole, paper());
        p.drawRect(hole);
        const auto raw =
            QRectF(map(0, radius), map(parameters.n("frontFacingAllowance"), positiveOnly ? 0 : -radius)).normalized();
        p.setBrush(Qt::NoBrush);
        p.drawRect(raw);
    }
    else
    {
        for (const int sign : {1, -1})
        {
            if (positiveOnly && sign < 0)
                continue;
            const auto allowance = QRectF(map(0, sign * c.targetBodyDiameter / 2),
                                          map(parameters.n("trimAllowance"), sign * c.targetBoreDiameter / 2))
                                       .normalized();
            p.fillRect(allowance, selection());
            p.drawRect(allowance);
        }
    }
    // Removed material is shown before the finished contour, so the latter stays readable.
    if (!positiveOnly)
    {
        const QColor allowance = dark() ? QColor("#b7863d") : QColor("#efc57e");
        const auto region = [&](double z1, double z2, double inner, double outer)
        {
            if (outer <= inner || qFuzzyCompare(z1, z2))
                return;
            for (const int sign : {1, -1})
            {
                const auto area = QRectF(map(z1, sign * inner), map(z2, sign * outer)).normalized();
                p.fillRect(area, allowance);
            }
        };
        if (highlight == "trimAllowance")
            region(setup.flipped ? 0 : -c.cutLength, setup.flipped ? parameters.n("trimAllowance") : -c.targetLength,
                   c.targetBoreDiameter / 2, c.targetBodyDiameter / 2);
        else if (highlight == "frontFacingAllowance" && !setup.flipped)
            region(0, parameters.n("frontFacingAllowance"), parameters.n("stockBore") / 2, radius);
        else if (highlight == "stockDiameter" && !setup.flipped)
        {
            region(-c.targetFlangeWidth, 0, c.targetFlangeDiameter / 2, radius);
            region(-c.cutLength, -c.targetFlangeWidth, c.targetBodyDiameter / 2, radius);
        }
        else if (highlight == "stockBore" && !setup.flipped)
        {
            region(-c.cutLength, -c.targetCounterboreDepth, parameters.n("stockBore") / 2, c.targetBoreDiameter / 2);
            region(-c.targetCounterboreDepth, 0, parameters.n("stockBore") / 2, c.targetCounterboreDiameter / 2);
        }
        else if (highlight == "depthOutside" || highlight == "depthInside" || highlight == "finishAllowance")
        {
            if (depthView && !levels.isEmpty())
            {
                if (setup.flipped)
                    region(levels.first(), passStart, c.targetBoreDiameter / 2, c.targetBodyDiameter / 2);
                else
                    region(inside ? 0 : -c.targetFlangeWidth, inside ? -c.targetCounterboreDepth : -c.cutLength,
                           std::min(passStart, levels.first()), std::max(passStart, levels.first()));
            }
            else if (setup.flipped)
            {
                const auto start = highlight == "finishAllowance" ? 0 : parameters.n("finishAllowance");
                const auto end = std::min(parameters.n("trimAllowance"), start + parameters.n(highlight));
                region(start, end, c.targetBoreDiameter / 2, c.targetBodyDiameter / 2);
            }
            else if (highlight == "depthInside")
                region(-c.cutLength, -c.targetCounterboreDepth,
                       std::max(parameters.n("stockBore") / 2, c.targetBoreDiameter / 2 - parameters.n(highlight)),
                       c.targetBoreDiameter / 2);
            else
                region(-c.cutLength, -c.targetFlangeWidth, c.targetBodyDiameter / 2,
                       std::min(radius, c.targetBodyDiameter / 2 + parameters.n(highlight)));
        }
        if (!setup.flipped && highlight == "cutoffWidth")
        {
            const QColor kerf = dark() ? QColor("#bd805e") : QColor("#b36d42");
            for (const int sign : {1, -1})
                p.fillRect(QRectF(map(c.partingBackZ, sign * parameters.n("stockBore") / 2),
                                  map(c.partingFrontZ, sign * radius))
                               .normalized(),
                           kerf);
        }
    }
    for (int sign : {1, -1})
    {
        if (positiveOnly && sign < 0)
            continue;
        const auto contour = axisContour(parameters, true);
        QPainterPath path;
        for (qsizetype i = 0; i < contour.size(); ++i)
        {
            const auto point =
                map(setup.flipped ? -c.targetLength - contour[i].x() : contour[i].x(), contour[i].y() * sign);
            if (i == 0)
                path.moveTo(point);
            else
                path.lineTo(point);
        }
        path.closeSubpath();
        p.fillPath(path, paper());
        drawHatching(p, path, map(setup.flipped ? -c.targetLength : 0, 0), m.scale);
        p.setPen(QPen(positiveOnly ? contourColor() : ink(), 1.5));
        p.drawPath(path);
    }
    if (!positiveOnly)
    {
        p.setPen(QPen(ink(), 1.5));
        for (const auto& edge : sectionProjectionEdges(parameters, true))
        {
            const auto z1 = setup.flipped ? -c.targetLength - edge.x1() : edge.x1();
            const auto z2 = setup.flipped ? -c.targetLength - edge.x2() : edge.x2();
            p.drawLine(map(z1, edge.y1()), map(z2, edge.y2()));
        }
    }
    if (depthView)
    {
        p.setPen(QPen(dark() ? QColor("#79babb") : QColor("#256e73"), 1.1));
        for (const auto level : levels)
        {
            const QLineF line = setup.flipped
                                    ? QLineF(map(level, c.targetBodyDiameter / 2), map(level, c.targetBoreDiameter / 2))
                                    : QLineF(map(inside ? 0 : -c.cutLength, level),
                                             map(inside ? -c.targetCounterboreDepth : -c.targetFlangeWidth, level));
            m.cuttingPasses.append(line);
            p.drawLine(line);
        }
    }
    const auto jawRadius = setup.gripDiameter / 2;
    p.setPen(QPen(muted(), 1.2));
    for (const int sign : {1, -1})
    {
        if (positiveOnly && sign < 0)
            continue;
        const auto jaw = QRectF(map(setup.jawsFrontZ - setup.gripLength, sign * (jawRadius + 15)),
                                map(setup.jawsFrontZ, sign * jawRadius))
                             .normalized();
        p.fillRect(jaw, dark() ? QColor("#536774") : QColor("#b8cad5"));
        QPainterPath path;
        path.addRect(jaw);
        drawHatching(p, path, map(setup.jawsFrontZ, 0), m.scale, dark() ? QColor("#8497a4") : QColor("#8093a0"));
        p.drawRect(jaw);
    }
    drawCenterLine(p, map(left, 0), map(right, 0), m.scale);
    p.setPen(muted());
    auto jawCaption =
        QRectF(map(setup.jawsFrontZ - setup.gripLength / 2, radius + 19) - QPointF(100, 28), QSizeF(200, 24));
    jawCaption.moveLeft(
        std::clamp(jawCaption.left(), bounds.left(), std::max(bounds.left(), bounds.right() - jawCaption.width())));
    p.drawText(jawCaption, Qt::AlignCenter, "Кулачки патрона");
    drawWorkOrigin(p, map(0, 0));
    const auto label = [&](QPointF a, QPointF b, const QString& text, bool centered = false)
    {
        m.parameterIndicator = QLineF(a, b);
        p.setPen(QPen(ink(), 1.3));
        drawDimensionArrow(p, a, b);
        const auto mid = (a + b) / 2;
        auto rectangle = QRectF(mid + QPointF(-p.fontMetrics().horizontalAdvance(text) / 2. - 7, -28),
                                QSizeF(p.fontMetrics().horizontalAdvance(text) + 14, 24));
        if (!centered && std::abs(a.x() - b.x()) > .01 && rectangle.width() + 12 > std::abs(a.x() - b.x()))
        {
            const double right = std::max(a.x(), b.x()) + 12;
            const double left = std::min(a.x(), b.x()) - rectangle.width() - 12;
            if (right + rectangle.width() <= bounds.right())
                rectangle.moveLeft(right);
            else if (left >= bounds.left())
                rectangle.moveLeft(left);
        }
        if (rectangle.left() < bounds.left())
            rectangle.moveLeft(bounds.left());
        if (rectangle.right() > bounds.right())
            rectangle.moveRight(bounds.right());
        m.parameterLabel = rectangle;
        p.fillRect(rectangle, paper());
        p.setPen(ink());
        p.drawText(rectangle, Qt::AlignCenter, text);
    };
    const auto bottom = std::min(bounds.bottom() - 33, map(0, -std::max(radius, jawRadius) - 15).y() + 36);
    const auto extension = [&](QPointF from, QPointF end)
    {
        const auto vector = end - from;
        const auto length = QLineF(from, end).length();
        if (length < .1)
            return;
        const auto direction = vector / length;
        p.setPen(QPen(ink(), .8));
        p.drawLine(from + direction * 2, end + direction * 4);
        m.extensionLines.append(QLineF(from, end));
    };
    const auto axialDimension =
        [&](double a, double b, double radiusA, double radiusB, const QString& text, bool underJaws = false)
    {
        const auto y = underJaws ? std::min(bounds.bottom() - 24, map(0, -jawRadius - 15).y() + 36) : bottom;
        const QPointF endA(map(a, 0).x(), y), endB(map(b, 0).x(), y);
        extension(map(a, radiusA), endA);
        extension(map(b, radiusB), endB);
        label(endA, endB, text, underJaws);
    };
    const auto radialDimension =
        [&](double z, double fromRadius, double toRadius, const QString& text, bool outsidePart = false)
    {
        const auto x = outsidePart ? std::max(map(0, 0).x(), map(setup.stockFrontZ, 0).x()) + 26
                                   : map(z, 0).x() + 22 * m.zDirection;
        const QPointF a(x, map(0, fromRadius).y()), b(x, map(0, toRadius).y());
        extension(map(z, fromRadius), a);
        extension(map(z, toRadius), b);
        m.parameterIndicator = QLineF(a, b);
        p.setPen(QPen(ink(), 1.3));
        drawDimensionArrow(p, a, b);
        const auto textWidth = p.fontMetrics().horizontalAdvance(text) + 8;
        const QRectF caption(std::clamp(outsidePart || m.zDirection > 0 ? x + 10 : x - 10 - textWidth, bounds.left(),
                                        std::max(bounds.left(), bounds.right() - textWidth)),
                             (a.y() + b.y()) / 2 - 12, textWidth, 24);
        p.fillRect(caption, paper());
        p.setPen(ink());
        p.drawText(caption, Qt::AlignVCenter | Qt::AlignLeft, text);
    };
    if (highlight == "gripLength")
        axialDimension(setup.jawsFrontZ - setup.gripLength, setup.jawsFrontZ, -jawRadius - 15, -jawRadius - 15,
                       localized(setup.gripLength) + " мм", true);
    else if (highlight == "chuckClearance" && !setup.flipped)
    {
        p.setPen(QPen(ink(), .9));
        p.drawLine(map(c.partingBackZ, -radius), map(c.partingBackZ, -parameters.n("stockBore") / 2));
        axialDimension(c.partingBackZ, c.jawsFrontZ, -radius, -radius, "Зазор " + localized(c.chuckClearance) + " мм");
    }
    else if (highlight == "blankOverhang" && !setup.flipped)
        axialDimension(c.jawsFrontZ, parameters.n("frontFacingAllowance"), -radius, -radius,
                       "Вылет " + localized(c.stockOverhang) + " мм");
    else if (highlight == "cutoffWidth" && !setup.flipped)
        axialDimension(c.partingBackZ, c.partingFrontZ, -radius, -radius,
                       "Ширина реза " + localized(c.cutoffWidth) + " мм");
    else if (highlight == "trimAllowance")
    {
        const auto a = setup.flipped ? 0 : -c.targetLength,
                   b = setup.flipped ? parameters.n("trimAllowance") : -c.cutLength;
        axialDimension(a, b, -c.targetBodyDiameter / 2, -c.targetBodyDiameter / 2,
                       "Задний припуск " + localized(parameters.n("trimAllowance")) + " мм");
    }
    else if (highlight == "frontFacingAllowance")
        axialDimension(0, parameters.n("frontFacingAllowance"), -radius, -radius,
                       "Передний припуск " + localized(parameters.n("frontFacingAllowance")) + " мм");
    else if (highlight == "stockDiameter" || highlight == "stockBore")
    {
        const auto r = parameters.n(highlight) / 2;
        radialDimension(0, r, -r, "Ø" + localized(2 * r));
    }
    else if (highlight == "clearanceZ" || highlight == "clearanceX")
    {
        const auto workR = setup.flipped ? c.targetBodyDiameter / 2 : radius;
        const auto safeR = workR + parameters.n("clearanceX");
        const auto face = setup.stockFrontZ, safeZ = face + parameters.n("clearanceZ");
        p.setPen(QPen(rapidColor(), 1.1, Qt::DashLine));
        p.drawPolyline(QPolygonF{map(face, safeR), map(safeZ, safeR), map(safeZ, workR)});
        const auto tip = map(safeZ, safeR);
        const QPolygonF tool{tip, tip + QPointF(12 * m.zDirection, -10), tip + QPointF(12 * m.zDirection, 2)};
        p.setPen(QPen(rapidColor(), 1.2));
        p.setBrush(paper());
        p.drawPolygon(tool);
        p.setBrush(Qt::NoBrush);
        // Keep the description separate from the measured distance.
        p.setPen(ink());
        p.drawText(bounds.adjusted(12, 8, -12, 0), Qt::AlignTop | Qt::AlignRight,
                   highlight == "clearanceX" ? "Радиальный отвод резца" : "Осевой отвод резца");
        if (highlight == "clearanceZ")
        {
            const auto y = map(face, safeR).y() - 24;
            const QPointF a(map(face, 0).x(), y), b(map(safeZ, 0).x(), y);
            extension(map(face, safeR), a);
            extension(map(safeZ, safeR), b);
            label(a, b, "ΔZ = " + localized(parameters.n("clearanceZ")) + " мм");
        }
        else
            radialDimension(safeZ, workR, safeR, "ΔR = " + localized(parameters.n("clearanceX")) + " мм");
    }
    else if (highlight == "feedRough" || highlight == "feedFinish")
    {
        const auto a = setup.flipped ? map(setup.stockFrontZ + 6, c.targetBodyDiameter / 2)
                                     : map(-c.targetLength * .2, radius + 8);
        const auto b = setup.flipped ? map(setup.stockFrontZ + 6, c.targetBoreDiameter / 2)
                                     : map(-c.targetLength * .7, radius + 8);
        m.parameterIndicator = QLineF(a, b);
        p.setPen(QPen(ink(), 1.3));
        drawDimensionArrow(p, a, b, false);
        const auto text = "f = " + localized(parameters.n(highlight)) + " мм/об";
        const auto width = p.fontMetrics().horizontalAdvance(text) + 14;
        auto rectangle =
            QRectF(QPointF(setup.flipped ? a.x() - width : (a.x() + b.x() - width) / 2, a.y() - 28), QSizeF(width, 24));
        if (rectangle.left() < bounds.left())
            rectangle.moveLeft(bounds.left());
        if (rectangle.right() > bounds.right())
            rectangle.moveRight(bounds.right());
        p.fillRect(rectangle, paper());
        p.setPen(ink());
        p.drawText(rectangle, Qt::AlignCenter, text);
    }
    else if (highlight == "depthOutside" || highlight == "depthInside" || highlight == "finishAllowance")
    {
        if (setup.flipped)
        {
            const auto depth = depthView && !levels.isEmpty() ? passStart - levels.first()
                               : highlight == "finishAllowance"
                                   ? parameters.n("finishAllowance")
                                   : std::min(parameters.n("depthOutside"),
                                              parameters.n("trimAllowance") - parameters.n("finishAllowance"));
            const auto start = depthView && !levels.isEmpty()   ? levels.first()
                               : highlight == "finishAllowance" ? 0
                                                                : parameters.n("finishAllowance");
            const auto y = map(0, c.targetBodyDiameter / 2 + 12).y();
            extension(map(start, c.targetBodyDiameter / 2), {map(start, 0).x(), y});
            extension(map(start + depth, c.targetBodyDiameter / 2), {map(start + depth, 0).x(), y});
            label({map(start, 0).x(), y}, {map(start + depth, 0).x(), y},
                  (depthView ? "За проход " : "Осевой припуск ") + localized(depth) + " мм");
        }
        else
        {
            const auto r = inside ? c.targetBoreDiameter / 2 : c.targetBodyDiameter / 2;
            const auto start = depthView && !levels.isEmpty() ? passStart : r;
            const auto end =
                depthView && !levels.isEmpty() ? levels.first() : std::min(radius, r + parameters.n(highlight));
            const auto surfaceZ = inside ? 0 : -c.targetFlangeWidth;
            const auto text = (highlight == "finishAllowance" ? QString("a = ") : QString("За проход ")) +
                              localized(std::abs(end - start)) + " мм";
            radialDimension(surfaceZ, start, end, text, true);
        }
    }
    else if (highlight == "cuttingSpeed" || highlight == "rpmMax")
    {
        const auto center = QPointF(map(0, 0).x() - 24, std::max(bounds.top() + 64, map(0, radius).y() - 40));
        p.setPen(QPen(ink(), 1.3));
        const QRectF circle(center - QPointF(18, 18), QSizeF(36, 36));
        p.drawArc(circle, 35 * 16, 290 * 16);
        const double angle = 325 * std::numbers::pi / 180;
        const auto tip = center + QPointF(18 * std::cos(angle), -18 * std::sin(angle));
        const auto tangent = QPointF(-std::sin(angle), -std::cos(angle));
        const QPointF normal(-tangent.y(), tangent.x());
        p.setBrush(ink());
        p.setPen(Qt::NoPen);
        p.drawPolygon(QPolygonF{tip, tip - tangent * 9 + normal * 2.2, tip - tangent * 9 - normal * 2.2});
        const auto text = highlight == "rpmMax" ? "n max = " + localized(parameters.n(highlight)) + " об/мин"
                                                : "v = " + localized(parameters.n(highlight)) + " м/мин";
        p.setPen(ink());
        const auto textWidth = p.fontMetrics().horizontalAdvance(text) + 12;
        p.drawText(QRectF(std::clamp(center.x() - textWidth / 2, bounds.left(), bounds.right() - textWidth),
                          center.y() - 50, textWidth, 24),
                   Qt::AlignCenter, text);
    }
    else if (!positiveOnly)
        axialDimension(setup.flipped ? -c.targetLength : -c.stockLength,
                       setup.flipped ? setup.stockFrontZ : parameters.n("frontFacingAllowance"), -radius, -radius,
                       setup.flipped ? "После отделения " + localized(c.cutLength) + " мм"
                                     : "Заготовка " + localized(c.rawStockLength) + " мм");
    if ((highlight == "tooling" || highlight == "outerTooling" || highlight == "bodyTooling" ||
         highlight == "boringTooling") &&
        !positiveOnly)
    {
        const QColor toolColor = dark() ? QColor("#79babb") : QColor("#256e73");
        p.setPen(QPen(toolColor, 1.3));
        p.setBrush(dark() ? QColor("#315d60") : QColor("#a3d1d2"));
        if (highlight != "boringTooling")
        {
            const bool body = highlight == "bodyTooling" && !setup.flipped;
            const auto z = body ? -c.targetFlangeWidth : setup.flipped ? setup.stockFrontZ : 0.;
            const auto r = (body || setup.flipped ? c.targetBodyDiameter : c.targetFlangeDiameter) / 2;
            const double direction = body ? -1 : 1;
            const auto point = [&](double dz, double dr) { return map(z + direction * dz, r + dr); };
            const QPolygonF holder{point(0, 2), point(1, 10), point(3, 29), point(18, 29), point(18, 8), point(7, 2)};
            p.drawPolygon(holder);
            p.setBrush(dark() ? QColor("#dab66d") : QColor("#bf9344"));
            p.drawPolygon(QPolygonF{point(0, 0), point(1, 7), point(6, 10), point(7, 3)});
            p.setBrush(toolColor);
            p.drawEllipse(point(3.5, 5), 1.7, 1.7);
            const auto title = "Резец T" + number(parameters.n(body ? "bodyToolNumber" : "outerToolNumber"));
            const auto textWidth = p.fontMetrics().horizontalAdvance(title);
            auto caption = map(z - 5, r + 35);
            if (caption.y() < bounds.top() + p.fontMetrics().ascent())
            {
                caption.setY(bounds.top() + p.fontMetrics().ascent());
                caption.setX(std::min(caption.x(), holder.boundingRect().left() - textWidth - 8));
            }
            caption.setX(std::clamp(caption.x(), bounds.left(), std::max(bounds.left(), bounds.right() - textWidth)));
            p.drawText(caption, title);
        }
        if (!setup.flipped && highlight != "outerTooling" && highlight != "bodyTooling")
        {
            const auto tool = boringTool(parameters);
            const auto tipZ = -c.cutLength + 3, tipR = c.targetBoreDiameter / 2, centerR = tipR - tool.offset;
            p.setBrush(dark() ? QColor("#315d60") : QColor("#a3d1d2"));
            const auto shaft = QRectF(map(tipZ, centerR + tool.diameter / 2),
                                      map(tipZ + parameters.n("boringOverhang"), centerR - tool.diameter / 2))
                                   .normalized();
            p.drawRect(shaft);
            const auto insertHeight = std::min(6., tool.offset - tool.diameter / 2);
            p.setBrush(dark() ? QColor("#dab66d") : QColor("#bf9344"));
            p.drawPolygon(QPolygonF{map(tipZ, tipR), map(tipZ + 1.5, tipR - insertHeight),
                                    map(tipZ + 8, tipR - insertHeight), map(tipZ + 6.5, tipR)});
            const auto title = "Оправка T" + number(parameters.n("boringToolNumber"));
            const auto subtitle = "Ø" + localized(tool.diameter) + " мм";
            const auto captionWidth =
                std::max(p.fontMetrics().horizontalAdvance(title), p.fontMetrics().horizontalAdvance(subtitle)) + 8;
            QRectF caption(shaft.right() + 10, shaft.center().y() - p.fontMetrics().height(), captionWidth,
                           2 * p.fontMetrics().height());
            if (caption.right() > bounds.right())
                caption.moveTopLeft({bounds.right() - captionWidth, shaft.bottom() + 6});
            p.drawText(caption, Qt::AlignLeft | Qt::AlignVCenter, title + "\n" + subtitle);
        }
    }
    m.axesOrigin = QPointF(bounds.left() + (m.zDirection > 0 ? 20 : 78), bounds.bottom() - 16);
    drawCoordinateAxes(p, m.axesOrigin, m.zDirection);
    p.restore();
    return m;
}
} // namespace pf::ui
