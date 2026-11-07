#pragma once
#include "model.h"

namespace pf
{
QVector<double> passes(double start, double finish, double depth);
Plan firstSetup(const Parameters& parameters, const Calculations& calculations, const QString& id);
Plan secondSetup(const Parameters& parameters, const Calculations& calculations, const QString& id);
} // namespace pf
