#pragma once
#include "core/model.h"

namespace pf
{
Parameters importParameters(const QString& path);
void saveParameters(const Project&, const QString& path);
QStringList exportProject(const Project&, const QString& folder);
QByteArray routeCard(const Project&);
QByteArray technologyCard(const Project&);
} // namespace pf
