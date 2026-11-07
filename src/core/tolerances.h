#pragma once
#include "model.h"

namespace pf
{
double toleranceWidth(double nominal, int grade);
std::pair<double, double> deviations(const Dimension& dimension, double nominal, const QString& designation);
Parameters resolveTolerances(Parameters parameters);
Parameters setTolerance(Parameters parameters, const QString& key, const QString& designation);
Parameters resolveStock(Parameters parameters);
std::pair<double, double> boreLimits(const Parameters& parameters);
double minimumStockDiameter(const Parameters& parameters);
} // namespace pf
