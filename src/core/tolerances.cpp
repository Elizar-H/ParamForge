#include "model.h"
#include <QRegularExpression>
#include <array>
#include <cmath>
#include <stdexcept>

namespace pf
{
double toleranceWidth(double nominal, int grade)
{
    static constexpr std::array bounds = {3., 6., 10., 18., 30., 50., 80., 120., 180., 250.};
    static constexpr int widths[10][11] = {{6, 10, 14, 25, 40, 60, 100, 140, 250, 400, 600},
                                           {8, 12, 18, 30, 48, 75, 120, 180, 300, 480, 750},
                                           {9, 15, 22, 36, 58, 90, 150, 220, 360, 580, 900},
                                           {11, 18, 27, 43, 70, 110, 180, 270, 430, 700, 1100},
                                           {13, 21, 33, 52, 84, 130, 210, 330, 520, 840, 1300},
                                           {16, 25, 39, 62, 100, 160, 250, 390, 620, 1000, 1600},
                                           {19, 30, 46, 74, 120, 190, 300, 460, 740, 1200, 1900},
                                           {22, 35, 54, 87, 140, 220, 350, 540, 870, 1400, 2200},
                                           {25, 40, 63, 100, 160, 250, 400, 630, 1000, 1600, 2500},
                                           {29, 46, 72, 115, 185, 290, 460, 720, 1150, 1850, 2900}};
    if (!std::isfinite(nominal) || nominal <= 0 || nominal > 250 || grade < 6 || grade > 16)
        throw std::invalid_argument("Стандартный допуск: размер 0…250 мм, квалитет IT6…IT16.");
    const auto row = std::lower_bound(bounds.begin(), bounds.end(), nominal) - bounds.begin();
    return widths[row][grade - 6] / 1000.;
}
std::pair<double, double> deviations(const Dimension& d, double nominal, const QString& designation)
{
    if (designation == "m" && (d.key == "bodyDiameter" || d.key == "flangeWidth"))
    {
        const double width = nominal >= .5 && nominal <= 6     ? .1
                             : nominal > 6 && nominal <= 30    ? .2
                             : nominal > 30 && nominal <= 120  ? .3
                             : nominal > 120 && nominal <= 400 ? .5
                                                               : -1;
        if (width < 0)
            throw std::invalid_argument("Для размера нет общего допуска выбранного профиля.");
        return {-width, width};
    }
    static const QRegularExpression pattern("^(H|h|JS|js|IT)([6-9]|1[0-6])$");
    const auto match = pattern.match(designation);
    if (!match.hasMatch() || !d.positions.contains(match.captured(1)))
        throw std::invalid_argument("Недопустимое обозначение допуска для выбранного размера.");
    const double width = toleranceWidth(nominal, match.captured(2).toInt());
    if (match.captured(1) == "H")
        return {0, width};
    if (match.captured(1) == "h")
        return {-width, 0};
    return {-width / 2, width / 2};
}
Parameters resolveTolerances(Parameters p)
{
    for (const auto& d : dimensions())
    {
        if (!p.json().value(d.designation).isString())
            throw std::invalid_argument("Обозначение допуска должно быть строкой.");
        if (p.s(d.designation).isEmpty())
            continue;
        const auto [lower, upper] = deviations(d, p.n(d.nominal), p.s(d.designation));
        p.set(d.lower, lower);
        p.set(d.upper, upper);
        if (p.s(d.designation) == "m")
            p.set(d.designation, "");
    }
    return p;
}
Parameters setTolerance(Parameters p, const QString& key, const QString& designation)
{
    p.set(dimension(key).designation, designation);
    return resolveTolerances(p);
}
double minimumStockDiameter(const Parameters& parameters)
{
    const auto p = resolveTolerances(parameters);
    return roundedDimensionSum({target(p.n("flangeDiameter"), p.n("flangeLowerDeviation"), p.n("flangeUpperDeviation")),
                                p.n("finishAllowance"), p.n("finishAllowance"), .1});
}
std::pair<double, double> boreLimits(const Parameters& parameters)
{
    const auto p = resolveTolerances(parameters);
    const auto minimum = dimensionTicks(boringTool(p).minBore) + 2 * dimensionTicks(p.n("clearanceX"));
    const auto maximum =
        dimensionTicks(target(p.n("boreDiameter"), p.n("boreLowerDeviation"), p.n("boreUpperDeviation"))) -
        2 * dimensionTicks(p.n("finishAllowance")) - dimensionTicks(.1);
    return {static_cast<double>(minimum) / 10000, static_cast<double>(maximum) / 10000};
}
Parameters resolveStock(Parameters p)
{
    p = resolveTolerances(p);
    bool axialValid = true;
    for (const auto& key : {"length", "lengthLowerDeviation", "lengthUpperDeviation", "trimAllowance", "cutoffWidth",
                            "frontFacingAllowance", "gripLength", "chuckClearance", "blankLength", "blankOverhang"})
        axialValid &= std::isfinite(p.n(key)) && std::abs(p.n(key)) <= 10000;
    if (axialValid)
    {
        const auto c = calculate(p);
        p.set("blankLength", c.rawStockLength);
        p.set("blankOverhang", c.stockOverhang);
    }
    if (!p.b("autoStock"))
        return p;
    for (const auto& key : {"boreDiameter", "flangeDiameter", "boreLowerDeviation", "boreUpperDeviation",
                            "flangeLowerDeviation", "flangeUpperDeviation", "finishAllowance", "clearanceX"})
        if (!std::isfinite(p.n(key)) || std::abs(p.n(key)) > 10000)
            return p;
    const auto [minimum, maximum] = boreLimits(p);
    p.set("stockDiameter", std::ceil(std::max(p.n("flangeDiameter") + 4, minimumStockDiameter(p))));
    p.set("stockBore", std::max(minimum, std::floor(std::min(p.n("boreDiameter") - 2, maximum))));
    return p;
}
} // namespace pf
