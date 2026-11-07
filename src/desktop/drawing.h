#pragma once
#include <QColor>
#include <QPointF>
#include <QRectF>
#include <QString>

class QPainter;
class QPainterPath;

namespace pf::ui
{
QString toleranceText(double lower, double upper);
void drawWorkOrigin(QPainter&, QPointF);
void drawCoordinateAxes(QPainter&, QPointF, double zDirection = 1);
void drawHatching(QPainter&, const QPainterPath&, QPointF origin, double scale, const QColor& color = {});
void drawCenterLine(QPainter&, QPointF, QPointF, double scale);
void drawDeviation(QPainter&, const QRectF&, double lower, double upper, const QColor&);
void drawDimensionArrow(QPainter&, QPointF from, QPointF to, bool bothEnds = true, double headLength = 10);
} // namespace pf::ui
