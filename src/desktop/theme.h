#pragma once
#include <QColor>
#include <QString>

namespace pf::ui
{
bool dark();
QColor ink();
QColor muted();
QColor border();
QColor paper();
QColor selection();
void applyTheme(const QString& preference);
QColor rapidColor();
QColor feedColor();
QColor contourColor();
} // namespace pf::ui
