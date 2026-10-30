using System.Globalization;
namespace Kontur.Core;
/// <summary>Lists the dimensions to inspect and their acceptance limits independently of document layout.</summary>
internal static class InspectionPlan
{
    private static string F(double value) => value.ToString("0.####", CultureInfo.GetCultureInfo("ru-RU"));

    private static string Signed(double value) => (value > 0 ? "+" : "") + F(value);


    internal static IEnumerable<(string Name, double Nominal, double Lower, double Upper, double Target)> GetDimensions(Parameters p, Calculations c)
    {
        yield return ("L — конечная длина", p.Length, p.LengthLowerDeviation, p.LengthUpperDeviation, c.TargetLength);
        yield return ("D — буртик", p.FlangeDiameter, p.FlangeLowerDeviation, p.FlangeUpperDeviation, c.TargetFlangeDiameter);
        yield return ("d — основное отверстие", p.BoreDiameter, p.BoreLowerDeviation, p.BoreUpperDeviation, c.TargetBoreDiameter);
        yield return ("d2 — внутренняя ступень", p.CounterboreDiameter, p.CounterboreLowerDeviation, p.CounterboreUpperDeviation, c.TargetCounterboreDiameter);
        yield return ("h — глубина расточки", p.CounterboreDepth, p.CounterboreDepthLowerDeviation, p.CounterboreDepthUpperDeviation, c.TargetCounterboreDepth);
        yield return ("Db — тело", p.BodyDiameter, p.BodyLowerDeviation, p.BodyUpperDeviation, c.TargetBodyDiameter);
        yield return ("b — ширина бурта", p.FlangeWidth, p.FlangeWidthLowerDeviation, p.FlangeWidthUpperDeviation, c.TargetFlangeWidth);
    }


    internal static string Describe(Parameters p, Calculations c) => "Проверить: " + string.Join("; ", GetDimensions(p, c).Select(d =>
        $"{d.Name}: номинал {F(d.Nominal)}, отклонения {Signed(d.Lower)}/{Signed(d.Upper)}, границы {F(d.Nominal + d.Lower)}…{F(d.Nominal + d.Upper)}, расчётная цель {F(d.Target)} мм")) + ".";

}
