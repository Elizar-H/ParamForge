#pragma once
#include "core/model.h"
#include <QLineF>
#include <QPointF>
#include <QRectF>
#include <QVector>
#include <optional>

class QPainter;

namespace pf::ui
{
inline constexpr double SetupTopMargin = 44, SetupBottomMargin = 60;

struct SetupMapping
{
    double scale = 1;
    double zDirection = 1, zOffset = 0;
    QPointF origin;
    QPointF axesOrigin;
    QLineF parameterIndicator;
    QRectF parameterLabel;
    QVector<QLineF> extensionLines;
    QVector<QLineF> cuttingPasses;
    QPointF map(double z, double radius) const
    {
        return origin + QPointF((zOffset + zDirection * z) * scale, -radius * scale);
    }
};
SetupMapping drawSetup(QPainter&, const QRectF&, const Parameters&, const Setup&, const QString& highlight = {},
                       bool positiveOnly = false, std::optional<SetupMapping> viewport = {});
SetupMapping fitSetup(const QRectF&, const Parameters&, const Setup&, bool positiveOnly = false,
                      bool keepPartOrientation = false);
} // namespace pf::ui
