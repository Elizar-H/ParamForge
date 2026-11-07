#pragma once
#include "model.h"

namespace pf
{
Calculations calculate(const Parameters& parameters);
QVector<Error> validate(const Parameters& parameters);
Generation generate(const Parameters& parameters);
} // namespace pf
