#include "drawing.h"
#include "theme.h"
#include "core/model.h"
#include <QLineF>
#include <QPainter>
#include <QPainterPath>
#include <algorithm>
#include <cmath>

namespace pf::ui
{
QString toleranceText(double lower, double upper)
{
    const auto sign = [](double v)
    { return (v > 0 ? QString("+") : QString()) + localized(v).replace('-', QChar(0x2212)); };
    if (lower == -upper)
        return upper == 0 ? QString("0") : "±" + localized(upper);
    return sign(upper) + "\n" + sign(lower);
}
void drawDeviation(QPainter& p, const QRectF& bounds, double lower, double upper, const QColor& color)
{
    p.save();
    p.setPen(color);
    const auto text = toleranceText(lower, upper);
    if (text.contains('\n'))
    {
        auto font = p.font();
        font.setPointSizeF(font.pointSizeF() * .95);
        p.setFont(font);
        const auto lines = text.split('\n');
        const auto height = p.fontMetrics().height();
        const auto top = bounds.center().y() - height;
        p.drawText(QRectF(bounds.left(), top, bounds.width(), height), Qt::AlignLeft | Qt::AlignVCenter, lines[0]);
        p.drawText(QRectF(bounds.left(), top + height, bounds.width(), height), Qt::AlignLeft | Qt::AlignVCenter,
                   lines[1]);
    }
    else
        p.drawText(bounds, Qt::AlignLeft | Qt::AlignVCenter, text);
    p.restore();
}
void drawDimensionArrow(QPainter& p, QPointF a, QPointF b, bool bothEnds, double headLength)
{
    const QLineF line(a, b);
    const auto length = line.length();
    if (length < .1)
        return;
    const auto u = (b - a) / length;
    const QPointF normal(-u.y(), u.x());
    const bool outside = bothEnds && length < 24;
    const double direction = outside ? -1 : 1;
    p.save();
    auto pen = p.pen();
    pen.setCapStyle(Qt::FlatCap);
    p.setPen(pen);
    p.drawLine(a - (outside ? u * 17 : QPointF()), b + (outside ? u * 17 : QPointF()));
    p.setBrush(pen.color());
    p.setPen(Qt::NoPen);
    const double head = headLength, halfWidth = headLength * .18;
    if (bothEnds)
        p.drawPolygon(
            QPolygonF{a, a + direction * u * head + normal * halfWidth, a + direction * u * head - normal * halfWidth});
    p.drawPolygon(
        QPolygonF{b, b - direction * u * head + normal * halfWidth, b - direction * u * head - normal * halfWidth});
    p.restore();
}
void drawCenterLine(QPainter& p, QPointF a, QPointF b, double scale)
{
    // Long dash / dot, with equal gaps and full dashes at the ends (GOST 2.303).
    const QLineF line(a, b);
    const auto length = line.length();
    if (length < 1)
        return;
    const auto direction = (b - a) / length;
    const double target = std::clamp(10 * scale, 24., 68.), gap = 5., dot = 1.2;
    const int count = std::max(1, qRound((length + 2 * gap + dot) / (target + 2 * gap + dot)));
    const auto dash = (length - (count - 1) * (2 * gap + dot)) / count;
    p.save();
    p.setPen(QPen(muted(), .85, Qt::SolidLine, Qt::FlatCap));
    double at = 0;
    for (int i = 0; i < count; ++i)
    {
        p.drawLine(a + at * direction, a + (at + dash) * direction);
        at += dash;
        if (i + 1 < count)
        {
            at += gap;
            p.drawLine(a + at * direction, a + (at + dot) * direction);
            at += dot + gap;
        }
    }
    p.restore();
}
void drawWorkOrigin(QPainter& p, QPointF center)
{
    p.save();
    p.setPen(Qt::NoPen);
    p.setBrush(paper());
    p.drawEllipse(center, 11, 11);
    p.setPen(QPen(ink(), 1));
    p.drawEllipse(center, 8, 8);
    p.drawEllipse(center, 5.5, 5.5);
    QPainterPath quadrant;
    quadrant.moveTo(center);
    quadrant.lineTo(center + QPointF(5.5, 0));
    quadrant.arcTo(QRectF(center - QPointF(5.5, 5.5), QSizeF(11, 11)), 0, -90);
    quadrant.closeSubpath();
    p.fillPath(quadrant, ink());
    p.drawLine(center - QPointF(13, 0), center + QPointF(13, 0));
    p.drawLine(center - QPointF(0, 13), center + QPointF(0, 13));
    p.restore();
}
void drawHatching(QPainter& p, const QPainterPath& path, QPointF origin, double scale, const QColor& color)
{
    // A world-space lattice: panning translates it, zooming scales it with the material.
    double step = 2.5 * scale;
    if (step <= 0)
        return;
    while (step < 6)
        step *= 2;
    const auto bounds = path.boundingRect().intersected(QRectF(p.viewport()));
    const double anchor = origin.x() + origin.y();
    const int first = int(std::floor((bounds.left() + bounds.top() - anchor) / step));
    const int last = int(std::ceil((bounds.right() + bounds.bottom() - anchor) / step));
    p.save();
    p.setClipPath(path, Qt::IntersectClip);
    p.setPen(QPen(color.isValid() ? color : border(), .7));
    for (int i = first; i <= last; ++i)
    {
        const double sum = anchor + i * step;
        const auto pixelAligned = [](double value) { return std::round(value * 64) / 64; };
        p.drawLine(QPointF(pixelAligned(bounds.left()), pixelAligned(sum - bounds.left())),
                   QPointF(pixelAligned(bounds.right()), pixelAligned(sum - bounds.right())));
    }
    p.restore();
}
void drawCoordinateAxes(QPainter& p, QPointF base, double zDirection)
{
    p.save();
    p.setPen(QPen(ink(), 1.2));
    p.setBrush(ink());
    for (const auto& end : {base + QPointF(44 * zDirection, 0), base - QPointF(0, 44)})
    {
        auto u = (end - base) / 44.;
        QPointF n(-u.y(), u.x());
        p.drawLine(base, end);
        p.drawPolygon(QPolygonF{end, end - u * 6 + n * 2.5, end - u * 6 - n * 2.5});
    }
    p.drawText(base + QPointF(zDirection > 0 ? 50 : -58, 4), "Z");
    p.drawText(base + QPointF(-4, -51), "X");
    p.restore();
}
} // namespace pf::ui
