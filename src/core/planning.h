#pragma once
#include "model.h"

namespace pf
{
enum class PassAxis { Diameter, Z };
QVector<double> passes(double start, double finish, double depth, PassAxis axis = PassAxis::Diameter);
Setup firstSetupFixture(const Parameters& parameters, const Calculations& calculations);
Setup secondSetupFixture(const Parameters& parameters, const Calculations& calculations);
Plan firstSetup(const Parameters& parameters, const Calculations& calculations, const QString& id);
Plan secondSetup(const Parameters& parameters, const Calculations& calculations, const QString& id);
} // namespace pf
