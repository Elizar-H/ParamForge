#include "process_view.h"
#include "core/geometry.h"
#include <QFormLayout>
#include <QKeyEvent>
#include <QLabel>
#include <QMouseEvent>
#include <QPainterPath>
#include <QScrollArea>
#include <QSignalBlocker>
#include <QSplitter>
#include <QVBoxLayout>
#include <cmath>
#include <numbers>

namespace pf::ui
{
namespace
{
constexpr double SetupTopMargin = 44, SetupBottomMargin = 60;
}
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
            if (setup.flipped)
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
        if (!setup.flipped)
        {
            const QColor kerf = highlight == "cutoffWidth" ? (dark() ? QColor("#bd805e") : QColor("#b36d42"))
                                                           : (dark() ? QColor("#555555") : QColor("#cccccc"));
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
    const auto jawRadius = setup.gripDiameter / 2;
    p.setPen(QPen(muted(), 1.2));
    for (int sign : {1, -1})
    {
        if (positiveOnly && sign < 0)
            continue;
        const QRectF jaw = QRectF(map(setup.jawsFrontZ - setup.gripLength, sign * (jawRadius + 15)),
                                  map(setup.jawsFrontZ, sign * jawRadius))
                               .normalized();
        p.fillRect(jaw, dark() ? QColor("#536774") : QColor("#b8cad5"));
        QPainterPath jawPath;
        jawPath.addRect(jaw);
        drawHatching(p, jawPath, map(setup.jawsFrontZ, 0), m.scale, dark() ? QColor("#8497a4") : QColor("#8093a0"));
        p.drawRect(jaw);
    }
    drawCenterLine(p, map(left, 0), map(right, 0), m.scale);
    p.setPen(muted());
    p.drawText(QRectF(map(setup.jawsFrontZ - setup.gripLength / 2, radius + 19) - QPointF(70, 24), QSizeF(140, 24)),
               Qt::AlignCenter, "Кулачки");
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
            const auto depth = highlight == "finishAllowance"
                                   ? parameters.n("finishAllowance")
                                   : std::min(parameters.n("depthOutside"),
                                              parameters.n("trimAllowance") - parameters.n("finishAllowance"));
            const auto start = highlight == "finishAllowance" ? 0 : parameters.n("finishAllowance");
            const auto y = map(0, c.targetBodyDiameter / 2 + 12).y();
            extension(map(start, c.targetBodyDiameter / 2), {map(start, 0).x(), y});
            extension(map(start + depth, c.targetBodyDiameter / 2), {map(start + depth, 0).x(), y});
            label({map(start, 0).x(), y}, {map(start + depth, 0).x(), y}, "Осевой припуск " + localized(depth) + " мм");
        }
        else
        {
            const bool inside = highlight == "depthInside";
            const auto r = inside ? c.targetBoreDiameter / 2 : c.targetBodyDiameter / 2;
            const auto start = inside ? std::max(parameters.n("stockBore") / 2, r - parameters.n(highlight)) : r;
            const auto end = inside ? r : std::min(radius, r + parameters.n(highlight));
            const auto surfaceZ = inside ? -c.targetCounterboreDepth : -c.targetFlangeWidth;
            const auto text =
                (highlight == "finishAllowance" ? QString("a = ") : QString("ap = ")) + localized(end - start) + " мм";
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
    if (highlight == "tooling" && !positiveOnly)
    {
        const QColor toolColor = dark() ? QColor("#79babb") : QColor("#256e73");
        p.setPen(QPen(toolColor, 1.3));
        p.setBrush(dark() ? QColor("#315d60") : QColor("#a3d1d2"));
        const auto z = setup.flipped ? 0. : -c.targetFlangeWidth - 6, r = c.targetBodyDiameter / 2;
        p.drawPolygon(QPolygonF{map(z, r), map(z + 9, r + 17), map(z - 10, r + 17)});
        p.drawText(map(z - 5, r + 23), "Резец T1");
        if (!setup.flipped)
        {
            const auto tool = boringTool(parameters);
            const auto tipZ = -c.cutLength + 3, tipR = c.targetBoreDiameter / 2, centerR = tipR - tool.offset;
            p.drawRect(QRectF(map(tipZ, centerR + tool.diameter / 2),
                              map(tipZ + parameters.n("boringOverhang"), centerR - tool.diameter / 2))
                           .normalized());
            p.drawPolygon(QPolygonF{map(tipZ, tipR), map(tipZ + 6, centerR + tool.diameter / 2), map(tipZ + 8, tipR)});
            p.drawText(map(4, centerR - tool.diameter / 2 - 3), "Оправка T2  Ø" + localized(tool.diameter));
        }
    }
    m.axesOrigin = QPointF(bounds.left() + (m.zDirection > 0 ? 20 : 78), bounds.bottom() - 16);
    drawCoordinateAxes(p, m.axesOrigin, m.zDirection);
    p.restore();
    return m;
}
ProcessDiagram::ProcessDiagram(QWidget* parent) : QWidget(parent)
{
    setObjectName("processDiagram");
    setMinimumSize(300, 300);
    setSizePolicy(QSizePolicy::Expanding, QSizePolicy::Expanding);
}
void ProcessDiagram::setParameters(const Parameters& p)
{
    if (!drawableSetup(p))
        return;
    parameters = p;
    update();
}
void ProcessDiagram::setSetup(int index)
{
    if (setupIndex != index)
        viewport.reset();
    setupIndex = index;
    update();
}
void ProcessDiagram::highlight(const QString& key)
{
    focusedKey = key;
    update();
}
void ProcessDiagram::paintEvent(QPaintEvent*)
{
    QPainter painter(this);
    painter.fillRect(rect(), paper());
    const auto c = calculate(parameters);
    const Setup setup = setupIndex == 0 ? Setup{"010",
                                                "Первый установ",
                                                false,
                                                0,
                                                c.jawsFrontZ,
                                                c.gripLength,
                                                parameters.n("stockDiameter"),
                                                -c.stockLength}
                                        : Setup{"020",
                                                "Второй установ",
                                                true,
                                                parameters.n("trimAllowance"),
                                                -c.targetLength + c.secondClampEnd,
                                                25,
                                                c.targetBodyDiameter,
                                                -c.targetLength};
    const auto bounds = QRectF(rect().adjusted(8, 8, -8, -8));
    if (!viewport)
        viewport = fitSetup(bounds, parameters, setup, false, true);
    else
    {
        const auto radius = std::max(parameters.n("stockDiameter"), parameters.n("flangeDiameter")) / 2;
        const auto left = std::min(-c.stockLength - 14, -c.targetLength - parameters.n("trimAllowance") - 12);
        const auto right = std::max(20., parameters.n("frontFacingAllowance") + 12);
        viewport->zDirection = setup.flipped ? -1 : 1;
        viewport->zOffset = setup.flipped ? -c.targetLength : 0;
        const auto room = std::max(1., std::min(viewport->origin.y() - bounds.top() - SetupTopMargin,
                                                bounds.bottom() - SetupBottomMargin - viewport->origin.y()));
        const auto nextScale =
            std::max(.1, std::min({viewport->scale, (bounds.width() - 170) / (right - left), room / (radius + 15)}));
        if (nextScale < viewport->scale - 1e-8)
            viewport->scale = nextScale;
        viewport->origin.setX(bounds.center().x() - 25 - (left + right) / 2 * viewport->scale);
    }
    drawSetup(painter, rect().adjusted(8, 8, -8, -8), parameters, setup, focusedKey, false, viewport);
}
void ProcessDiagram::resizeEvent(QResizeEvent* e)
{
    viewport.reset();
    QWidget::resizeEvent(e);
}
ProcessView::ProcessView(QWidget* parent) : QWidget(parent)
{
    auto layout = new QHBoxLayout(this);
    layout->setContentsMargins(0, 0, 0, 0);
    layout->setSpacing(6);
    sections = new QListWidget(this);
    sections->setObjectName("processSections");
    sections->addItems({"Подготовка", "Инструмент", "Режимы", "Реквизиты"});
    sections->setFixedWidth(128);
    sections->setItemDelegate(new ItemDelegate(sections));
    sections->installEventFilter(this);
    layout->addWidget(sections);
    pages = new QStackedWidget(this);
    pages->setObjectName("processParameters");
    pages->installEventFilter(this);
    pages->setFixedWidth(320);
    layout->addWidget(pages);
    const QVector<QStringList> keys = {{"stockDiameter", "stockBore", "blankLength", "blankOverhang", "trimAllowance",
                                        "frontFacingAllowance", "cutoffWidth", "gripLength", "chuckClearance"},
                                       {"boringTool", "boringOverhang", "boringClamp", "customBoringName",
                                        "customBoringDiameter", "customBoringMinBore", "customBoringOffset",
                                        "customBoringLength", "customBoringMaxOverhang"},
                                       {"depthOutside", "depthInside", "finishAllowance", "cuttingSpeed", "feedRough",
                                        "feedFinish", "rpmMax", "clearanceX", "clearanceZ"},
                                       {"partName", "material", "organization", "drawingNumber", "developer", "checker",
                                        "rateSetter", "normController"}};
    for (int index = 0; index < keys.size(); ++index)
    {
        auto scroll = new QScrollArea(pages);
        scroll->installEventFilter(this);
        scroll->viewport()->installEventFilter(this);
        scroll->setWidgetResizable(true);
        scroll->setHorizontalScrollBarPolicy(Qt::ScrollBarAlwaysOff);
        auto content = new QWidget(scroll);
        content->installEventFilter(this);
        auto form = new QVBoxLayout(content);
        form->setContentsMargins(14, 12, 14, 12);
        form->setSpacing(3);
        QVector<ParameterRow*> group;
        QVBoxLayout* rowForm = form;
        QWidget* rowHost = content;
        const auto parameterGroup = [this, content, form, &rowForm, &rowHost](const QString& title, const QString& name)
        {
            if (form->count())
                form->addSpacing(8);
            rowHost = new QWidget(content);
            rowHost->setObjectName(name);
            rowHost->installEventFilter(this);
            rowForm = new QVBoxLayout(rowHost);
            rowForm->setContentsMargins(0, 0, 0, 8);
            rowForm->setSpacing(3);
            auto heading = new QLabel(title, rowHost);
            auto font = heading->font();
            font.setBold(true);
            heading->setFont(font);
            rowForm->addWidget(heading);
            form->addWidget(rowHost);
            return heading;
        };
        if (index == Preparation)
        {
            auto heading = parameterGroup("Заготовка", "stockParameters");
            secondStock = new QLabel(rowHost);
            secondStock->setObjectName("secondStock");
            secondStock->setWordWrap(true);
            secondStock->setMargin(6);
            rowForm->addWidget(secondStock);
            resetStock = new QToolButton(rowHost);
            resetStock->setObjectName("resetStock");
            resetStock->setText("Сбросить");
            resetStock->setToolTip("Вернуть исходные торцевые припуски и автоматический расчёт заготовки");
            rowForm->removeWidget(heading);
            auto header = new QHBoxLayout;
            header->addWidget(heading);
            header->addStretch();
            header->addWidget(resetStock);
            rowForm->insertLayout(0, header);
            connect(resetStock, &QToolButton::clicked, this, &ProcessView::stockResetRequested);
        }
        if (index == Tooling)
        {
            const auto toolCard = [content, form](const QString& title, const QStringList& facts)
            {
                auto card = new QFrame(content);
                card->setObjectName("toolCard");
                auto group = new QVBoxLayout(card);
                group->setContentsMargins(10, 10, 10, 10);
                group->setSpacing(5);
                auto heading = new QLabel(title, card);
                auto font = heading->font();
                font.setBold(true);
                heading->setFont(font);
                heading->setWordWrap(true);
                group->addWidget(heading);
                for (const auto& text : facts)
                {
                    auto label = new QLabel(text, card);
                    label->setWordWrap(true);
                    group->addWidget(label);
                }
                form->addWidget(card);
                return card;
            };
            toolCard("Наружное точение и торцы", {"Инструмент 1 (T1)", "Корректор 1 (D1)", "Державка SCLCR 2525M12"});
            auto plate = new QLabel("Пластина CCMT 12 04 04-PF 4425\nРадиус вершины 0,4 мм", content);
            plate->setWordWrap(true);
            plate->setMargin(5);
            form->addWidget(plate);
            boringToolCard = toolCard("Растачивание", {"Инструмент 2 (T2)", "Корректор 1 (D1)"});
            boringToolCard->installEventFilter(this);
            rowHost = boringToolCard;
            rowForm = qobject_cast<QVBoxLayout*>(boringToolCard->layout());
            rowForm->setContentsMargins(2, 8, 2, 8);
        }
        if (index == Regimes)
            parameterGroup("Обработка", "cuttingParameters");
        for (const auto& key : keys[index])
        {
            if (key == "gripLength")
            {
                parameterGroup("Закрепление", "fixtureParameters");
                secondFixture = new QListWidget(rowHost);
                secondFixture->setWordWrap(true);
                secondFixture->setHorizontalScrollBarPolicy(Qt::ScrollBarAlwaysOff);
                secondFixture->setObjectName("secondFixtureFacts");
                secondFixture->setItemDelegate(new ItemDelegate(secondFixture));
                secondFixture->setVisible(false);
                secondFixture->installEventFilter(this);
                rowForm->addWidget(secondFixture);
            }
            if (key == "clearanceX")
                parameterGroup("Подходы", "approachParameters");
            Field f;
            if (schema()["defaults"].toObject()[key].isString())
            {
                f.key = key;
                f.label = key == "partName"           ? "Наименование"
                          : key == "material"         ? "Материал"
                          : key == "boringTool"       ? "Оправка"
                          : key == "customBoringName" ? "Название"
                                                      : QString();
                for (const auto& entry : schema()["cardFields"].toArray())
                    if (entry.toObject()["key"].toString() == key)
                        f.label = entry.toObject()["label"].toString();
            }
            else
                f = field(key);
            auto row = new ParameterRow(f, false, rowHost);
            group.append(row);
            rows.append(row);
            fieldSections[key] = index;
            rowForm->addWidget(row);
            connect(row, &ParameterRow::edited, this, &ProcessView::valueEdited);
            connect(row, &ParameterRow::parentRequested, this, [this] { sections->setFocus(); });
            connect(row, &ParameterRow::navigate, this,
                    [this, row, index](int delta)
                    {
                        if (index == Preparation && setupChoice->currentIndex() == 1)
                        {
                            secondFixture->setCurrentRow(delta > 0 ? 0 : secondFixture->count() - 1);
                            secondFixture->setFocus();
                            return;
                        }
                        const auto& group = pageRows[index];
                        int next = static_cast<int>(group.indexOf(row));
                        for (int visited = 0; visited < group.size(); ++visited)
                        {
                            next = (next + delta + group.size()) % group.size();
                            if (group[next]->input->isEnabled() && !group[next]->isHidden())
                            {
                                group[next]->setFocus();
                                break;
                            }
                        }
                    });
        }
        if (index == Tooling)
        {
            boringToolFacts = new QLabel(boringToolCard);
            boringToolFacts->setWordWrap(true);
            boringToolFacts->setMargin(6);
            rowForm->addWidget(boringToolFacts);
        }
        form->addStretch();
        pageRows.append(group);
        scroll->setWidget(content);
        pages->addWidget(scroll);
    }
    auto preview = new QWidget(this);
    layout->addWidget(preview, 1);
    auto previewLayout = new QVBoxLayout(preview);
    previewLayout->setContentsMargins(10, 10, 10, 10);
    setupChoice = new ChoiceList(preview);
    setupChoice->setObjectName("fixtureSetup");
    setupChoice->addItem("010", "010");
    setupChoice->addItem("020", "020");
    setupChoice->setAccessibleName("Установ");
    previewLayout->addWidget(setupChoice, 0, Qt::AlignLeft);
    auto sequence = new QLabel("010 → отрезка и ручная переустановка → 020", preview);
    sequence->setObjectName("setupSequence");
    sequence->setWordWrap(true);
    previewLayout->addWidget(sequence);
    diagram = new ProcessDiagram(preview);
    previewLayout->addWidget(diagram, 1);
    connect(setupChoice, &ChoiceList::currentIndexChanged, this, [this] { updateContext(); });
    for (auto row : rows)
        connect(row, &ParameterRow::activated, this,
                [this, row](const QString& key)
                {
                    focusedField = key;
                    diagram->highlight(fieldSections.value(key) == Tooling ? "tooling" : key);
                    if (auto scroll = qobject_cast<QScrollArea*>(pages->currentWidget()))
                        scroll->ensureWidgetVisible(row, 0, 12);
                });
    connect(sections, &QListWidget::currentRowChanged, this,
            [this](int i)
            {
                pages->setCurrentIndex(i);
                updateContext();
            });
    connect(sections, &QListWidget::itemClicked, this, [this] { focusFirst(); });
    connect(secondFixture, &QListWidget::currentRowChanged, this,
            [this](int i) { diagram->highlight(i == 0 ? "gripLength" : ""); });
    sections->setCurrentRow(0);
    setParameters(Parameters());
}
void ProcessView::setParameters(const Parameters& p)
{
    parameters = p;
    auto display = p;
    const auto sizes = calculate(p);
    display.set("blankLength", sizes.rawStockLength);
    display.set("blankOverhang", sizes.stockOverhang);
    display.set("gripLength", sizes.gripLength);
    display.set("chuckClearance", sizes.chuckClearance);
    for (auto row : rows)
    {
        row->setParameters(display);
    }
    diagram->setParameters(p);
    try
    {
        const auto tool = boringTool(p);
        const auto allowedOverhang =
            std::max(0., std::min({p.n("boringOverhang"), tool.maxOverhang,
                                   tool.length - std::max(p.n("boringClamp"), 4 * tool.diameter)}));
        const auto reach = std::max(0., allowedOverhang - 5);
        boringToolFacts->setText(
            "Диаметр оправки " + localized(tool.diameter) + " мм\nИсходное отверстие от " +
            localized(tool.minBore + 2 * p.n("clearanceX")) + " мм с учётом отвода\nГлубина обработки до " +
            localized(reach) + " мм\nРасчётная длина детали до " +
            localized(std::max(0., reach - p.n("trimAllowance"))) + " мм\n010 — растачивание; 020 — задний торец");
    }
    catch (const std::exception&)
    {
        boringToolFacts->clear();
    }
    const auto c = calculate(parameters);
    secondStock->setText("После отделения и переворота\nДлина " + localized(c.cutLength) +
                         " мм\nЗадний торец — до длины " + localized(c.targetLength) + " мм");
    const QSignalBlocker factsBlocker(secondFixture);
    const int selected = secondFixture->currentRow();
    secondFixture->clear();
    secondFixture->addItems(
        {"Длина зажима: 25 мм",
         "Пояс: " + localized(c.secondClampStart) + "…" + localized(c.secondClampEnd) + " мм от первого торца",
         "Карман: Ø" + localized(c.softJawPocketDiameter) + " × " + localized(c.softJawPocketDepth) + " мм",
         "Упор по первому торцу"});
    secondFixture->setCurrentRow(std::max(0, selected));
    updateContext();
}
void ProcessView::updateContext()
{
    const int section = sections->currentRow();
    if (section < 0)
        return;
    const bool second = setupChoice->currentIndex() == 1;
    resetStock->setVisible(!second);
    secondStock->setVisible(second);
    boringToolCard->setVisible(!second);
    diagram->setSetup(second ? 1 : 0);
    secondFixture->setVisible(second);
    for (auto row : rows)
    {
        if (fieldSections.value(row->key) == Tooling)
            row->setVisible(!second &&
                            (!row->key.startsWith("customBoring") || parameters.s("boringTool") == "custom"));
        if (row->key == "blankLength" || row->key == "blankOverhang" || row->key == "gripLength" ||
            row->key == "chuckClearance" || row->key == "stockDiameter" || row->key == "stockBore" ||
            row->key == "frontFacingAllowance" || row->key == "cutoffWidth")
            row->setVisible(!second);
        if (row->key == "depthInside")
            row->setVisible(!second);
        if (row->key == "depthOutside")
        {
            row->setCaption(second ? "Глубина торцевого прохода ap, мм" : field(row->key).label + ", мм");
            row->setToolTip(second ? "Осевой съём за один черновой проход заднего торца." : field(row->key).note);
        }
    }
    if (section == Tooling)
        diagram->highlight("tooling");
    else
    {
        QString key;
        for (auto row : pageRows[section])
            if (!row->isHidden() && row->input->isEnabled())
            {
                if (key.isEmpty())
                    key = row->key;
                if (row->key == focusedField)
                {
                    key = focusedField;
                    break;
                }
            }
        diagram->highlight(key);
    }
}
void ProcessView::setErrors(const QVector<Error>& errors)
{
    for (auto row : rows)
    {
        bool bad = false;
        for (const auto& e : errors)
            if (e.field == row->key)
                bad = true;
        row->setError(bad);
    }
}
void ProcessView::focusField(const QString& key)
{
    if (!fieldSections.contains(key))
        return;
    if (fieldSections.value(key) == Tooling ||
        QStringList{"depthInside", "blankLength", "blankOverhang", "gripLength", "chuckClearance", "stockDiameter",
                    "stockBore", "frontFacingAllowance"}
            .contains(key))
        setupChoice->setCurrentIndex(0);
    sections->setCurrentRow(fieldSections[key]);
    for (auto row : rows)
        if (row->key == key)
        {
            (row->choices && !row->choices->isEditable() ? static_cast<QWidget*>(row) : row->input)->setFocus();
            return;
        }
}
void ProcessView::focusFirst()
{
    for (auto row : pageRows[sections->currentRow()])
        if (row->input->isEnabled() && !row->isHidden())
        {
            row->setFocus();
            return;
        }
    sections->setFocus();
}
bool ProcessView::eventFilter(QObject* object, QEvent* e)
{
    if (object == secondFixture && e->type() == QEvent::FocusIn)
    {
        diagram->highlight(secondFixture->currentRow() == 0 ? "gripLength" : "");
        if (auto scroll = qobject_cast<QScrollArea*>(pages->currentWidget()))
            scroll->ensureWidgetVisible(secondFixture, 0, 12);
    }
    if (e->type() == QEvent::MouseButtonPress && object != sections && object != secondFixture &&
        static_cast<QMouseEvent*>(e)->button() == Qt::LeftButton)
    {
        if (const auto background = qobject_cast<QWidget*>(object))
        {
            for (auto row : background->findChildren<ParameterRow*>())
                if (row->isVisible() && row->input->isEnabled())
                {
                    row->setFocus(Qt::MouseFocusReason);
                    return true;
                }
            if (background->isAncestorOf(secondFixture) && secondFixture->isVisible())
            {
                secondFixture->setFocus(Qt::MouseFocusReason);
                return true;
            }
        }
        focusFirst();
        return true;
    }
    if (object == sections && e->type() == QEvent::KeyPress)
    {
        const auto key = static_cast<QKeyEvent*>(e)->key();
        if (key == Qt::Key_Up || key == Qt::Key_Down)
        {
            sections->setCurrentRow((sections->currentRow() + (key == Qt::Key_Down ? 1 : -1) + sections->count()) %
                                    sections->count());
            return true;
        }
        if (key == Qt::Key_Right || key == Qt::Key_Return)
        {
            focusFirst();
            return true;
        }
        if (key == Qt::Key_Left)
        {
            Q_EMIT parentRequested();
            return true;
        }
    }
    if (object == secondFixture && e->type() == QEvent::KeyPress && static_cast<QKeyEvent*>(e)->key() == Qt::Key_Left)
    {
        sections->setFocus();
        return true;
    }
    if (object == secondFixture && e->type() == QEvent::KeyPress)
    {
        const auto key = static_cast<QKeyEvent*>(e)->key();
        if ((key == Qt::Key_Up && secondFixture->currentRow() <= 0) ||
            (key == Qt::Key_Down && secondFixture->currentRow() == secondFixture->count() - 1))
        {
            focusFirst();
            return true;
        }
    }
    return QWidget::eventFilter(object, e);
}
} // namespace pf::ui
