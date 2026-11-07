#pragma once
#include "model.h"

namespace pf
{
struct BoringTool
{
    QString id, name;
    double diameter, minBore, offset, length, minOverhang, maxOverhang;
};

const QVector<BoringTool>& boringTools();
QString turningToolName(const Parameters& parameters);
QString bodyToolName(const Parameters& parameters);
QString partingToolName(const Parameters& parameters);
BoringTool boringTool(const Parameters& parameters);
QVector<Error> validateToolSetup(const Parameters& parameters);
} // namespace pf
