#include "model.h"
#include <QFile>
#include <QJsonDocument>
#include <QLocale>
#include <cmath>
#include <limits>
#include <stdexcept>

static void initializeResources()
{
    Q_INIT_RESOURCE(resources);
}

namespace pf
{
const QJsonObject& schema()
{
    static const auto data = []
    {
        initializeResources();
        QFile file(":/paramforge/schema.json");
        if (!file.open(QIODevice::ReadOnly))
            throw std::runtime_error("Parameter schema is missing");
        return QJsonDocument::fromJson(file.readAll()).object();
    }();
    return data;
}
QString number(double value, int decimals)
{
    if (value == 0)
        return "0";
    auto result = QString::number(value, 'f', decimals);
    if (result.contains('.'))
    {
        while (result.endsWith('0'))
            result.chop(1);
        if (result.endsWith('.'))
            result.chop(1);
    }
    return result == "-0" ? "0" : result;
}
QString localized(double value, int decimals)
{
    return number(value, decimals).replace('.', ',');
}
double roundTo(double value, int decimals)
{
    const auto scale = std::pow(10.L, decimals);
    return static_cast<double>(std::round(static_cast<long double>(value) * scale) / scale);
}
qint64 dimensionTicks(double value)
{
    if (!std::isfinite(value) || std::abs(value) > 100000)
        throw std::invalid_argument("Размеры превышают числовой диапазон расчёта.");
    return std::llround(value * 10000);
}
double roundedDimensionSum(std::initializer_list<double> terms)
{
    qint64 ticks = 0;
    for (const auto value : terms)
        ticks += dimensionTicks(value);
    const auto rounded = (std::abs(ticks) + 5) / 10;
    return static_cast<double>(ticks < 0 ? -rounded : rounded) / 1000.;
}
double target(double nominal, double lower, double upper)
{
    // Integer tenths of a micrometre preserve JS half-widths and midpoint ties.
    const auto numerator = 2 * dimensionTicks(nominal) + dimensionTicks(lower) + dimensionTicks(upper);
    const auto magnitude = std::abs(numerator);
    const auto rounded = (magnitude + 10) / 20;
    return static_cast<double>(numerator < 0 ? -rounded : rounded) / 1000.;
}
const QVector<Field>& fields()
{
    static const auto result = []
    {
        QVector<Field> list;
        for (const auto& item : schema()["fields"].toArray())
        {
            const auto f = item.toObject();
            list.append({f["key"].toString(), f["label"].toString(), f["unit"].toString(), f["group"].toString(),
                         f["note"].toString(), f["min"].toDouble(), f["max"].toDouble(), f["step"].toDouble()});
        }
        return list;
    }();
    return result;
}
const QVector<Dimension>& dimensions()
{
    static const auto result = []
    {
        QVector<Dimension> list;
        for (const auto& item : schema()["dimensions"].toArray())
        {
            const auto d = item.toObject();
            QStringList positions;
            for (const auto& p : d["positions"].toArray())
                positions.append(p.toString());
            list.append({d["key"].toString(), d["nominal"].toString(), d["lower"].toString(), d["upper"].toString(),
                         d["designation"].toString(), positions});
        }
        return list;
    }();
    return result;
}
const Field& field(const QString& key)
{
    for (const auto& f : fields())
        if (f.key == key)
            return f;
    throw std::invalid_argument("Unknown numeric parameter");
}
const Dimension& dimension(const QString& key)
{
    for (const auto& d : dimensions())
        if (d.key == key)
            return d;
    throw std::invalid_argument("Unknown dimension");
}
Parameters::Parameters() : values(schema()["defaults"].toObject()) {}
Parameters::Parameters(const QJsonObject& overrides) : Parameters()
{
    for (auto i = overrides.begin(); i != overrides.end(); ++i)
        values.insert(i.key(), i.value());
    for (const auto& d : dimensions())
        if ((d.key == "bodyDiameter" || d.key == "flangeWidth") && s(d.designation) == "m")
        {
            const auto nominal = n(d.nominal);
            if (!std::isfinite(nominal) || nominal < .5 || nominal > 400)
                continue;
            const auto [lower, upper] = deviations(d, nominal, "m");
            set(d.lower, lower);
            set(d.upper, upper);
            set(d.designation, "");
        }
}
double Parameters::n(const QString& key) const
{
    const auto value = values.value(key);
    return value.isDouble() ? value.toDouble() : std::numeric_limits<double>::quiet_NaN();
}
QString Parameters::s(const QString& key) const
{
    return values.value(key).toString();
}
bool Parameters::b(const QString& key) const
{
    return values.value(key).toBool();
}
void Parameters::set(const QString& key, const QJsonValue& value)
{
    values.insert(key, value);
}
QJsonObject Calculations::json() const
{
    return {{"stockLength", stockLength},
            {"cutLength", cutLength},
            {"clearOut", clearOut},
            {"clearIn", clearIn},
            {"totalCutMinutes", totalCutMinutes},
            {"targetBoreDiameter", targetBoreDiameter},
            {"targetCounterboreDiameter", targetCounterboreDiameter},
            {"targetFlangeDiameter", targetFlangeDiameter},
            {"targetLength", targetLength},
            {"targetCounterboreDepth", targetCounterboreDepth},
            {"cutoffWidth", cutoffWidth},
            {"partingFrontZ", partingFrontZ},
            {"partingBackZ", partingBackZ},
            {"jawsFrontZ", jawsFrontZ},
            {"rawStockLength", rawStockLength},
            {"stockOverhang", stockOverhang},
            {"gripLength", gripLength},
            {"chuckClearance", chuckClearance},
            {"secondClampStart", secondClampStart},
            {"secondClampEnd", secondClampEnd},
            {"softJawPocketDiameter", softJawPocketDiameter},
            {"softJawPocketDepth", softJawPocketDepth},
            {"targetBodyDiameter", targetBodyDiameter},
            {"targetFlangeWidth", targetFlangeWidth}};
}
double Transition::actualMaximumDepth() const
{
    double maximum = 0, previous = startDiameter;
    for (auto current : passes)
    {
        maximum = std::max(maximum, std::abs(current - previous) / 2);
        previous = current;
    }
    return roundTo(maximum, 4);
}
} // namespace pf
