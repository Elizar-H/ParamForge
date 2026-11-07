#pragma once
#include "model.h"

namespace pf
{
QVector<Operation> operations(const Parameters& parameters, const Calculations& calculations);
QString inspectionDescription(const Parameters& parameters, const Calculations& calculations);
QJsonObject projectJson(const Project& project);
} // namespace pf
