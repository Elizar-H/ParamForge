using System.Globalization;
using System.Reflection;
using System.Text.RegularExpressions;

namespace Kontur.Core;

public record DimensionTolerance(string Key, string NominalProperty, string LowerProperty, string UpperProperty,
    string DesignationProperty, string[] Positions);

/// <summary>ГОСТ 25346-2013, таблица 1: IT6–IT16, 0 < размер <= 250 мм.
/// H/h: нулевое основное отклонение; JS/js: симметричное расположение.
/// Для осевых размеров ITn — выбор ширины симметричного числового допуска.</summary>
public static class Tolerances
{
    public static IReadOnlyList<DimensionTolerance> Dimensions { get; } = Array.AsReadOnly(new[]
    {
        new DimensionTolerance("length", "Length", "LengthLowerDeviation", "LengthUpperDeviation", "LengthTolerance", ["IT"]),
        new("flangeDiameter", "FlangeDiameter", "FlangeLowerDeviation", "FlangeUpperDeviation", "FlangeTolerance", ["h", "js"]),
        new("boreDiameter", "BoreDiameter", "BoreLowerDeviation", "BoreUpperDeviation", "BoreTolerance", ["H", "JS"]),
        new("counterboreDiameter", "CounterboreDiameter", "CounterboreLowerDeviation", "CounterboreUpperDeviation", "CounterboreTolerance", ["H", "JS"]),
        new("counterboreDepth", "CounterboreDepth", "CounterboreDepthLowerDeviation", "CounterboreDepthUpperDeviation", "CounterboreDepthTolerance", ["IT"])
    });
    private static readonly double[] UpperBounds = [3, 6, 10, 18, 30, 50, 80, 120, 180, 250];
    // All entries in micrometres, including IT12–IT16 (printed in mm in the standard).
    private static readonly int[][] Micrometres =
    [
        [6,10,14,25,40,60,100,140,250,400,600],
        [8,12,18,30,48,75,120,180,300,480,750],
        [9,15,22,36,58,90,150,220,360,580,900],
        [11,18,27,43,70,110,180,270,430,700,1100],
        [13,21,33,52,84,130,210,330,520,840,1300],
        [16,25,39,62,100,160,250,390,620,1000,1600],
        [19,30,46,74,120,190,300,460,740,1200,1900],
        [22,35,54,87,140,220,350,540,870,1400,2200],
        [25,40,63,100,160,250,400,630,1000,1600,2500],
        [29,46,72,115,185,290,460,720,1150,1850,2900]
    ];
    private static PropertyInfo Property(string name) => typeof(Parameters).GetProperty(name)!;
    public static double Number(Parameters p, string property) => (double)Property(property).GetValue(p)!;
    public static string Designation(Parameters p, DimensionTolerance dimension) => (string?)Property(dimension.DesignationProperty).GetValue(p) ?? "";
    public static double Width(double nominal, int grade)
    {
        if (!double.IsFinite(nominal) || nominal <= 0 || nominal > 250 || grade < 6 || grade > 16)
            throw new ArgumentException("Для стандартного допуска нужны размер 0…250 мм и квалитет IT6…IT16.");
        var row = Array.FindIndex(UpperBounds, upper => nominal <= upper);
        return Micrometres[row][grade - 6] / 1000d;
    }
    public static (double Lower, double Upper) Deviations(DimensionTolerance dimension, double nominal, string designation)
    {
        var match = Regex.Match(designation, @"^(H|h|JS|js|IT)([6-9]|1[0-6])$", RegexOptions.CultureInvariant);
        if (!match.Success || !dimension.Positions.Contains(match.Groups[1].Value))
            throw new ArgumentException("Недопустимое обозначение допуска для размера " + dimension.Key + ".");
        var width = (decimal)Width(nominal, int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture));
        return match.Groups[1].Value switch
        {
            "H" => (0, (double)width), "h" => (-(double)width, 0),
            _ => (-(double)(width / 2m), (double)(width / 2m))
        };
    }
    public static Parameters SetDesignation(Parameters parameters, string key, string designation)
    {
        var result = parameters with { };
        var dimension = Dimensions.Single(d => d.Key == key);
        Property(dimension.DesignationProperty).SetValue(result, designation);
        return Resolve(result);
    }
    public static Parameters Resolve(Parameters parameters)
    {
        var result = parameters with { };
        foreach (var dimension in Dimensions)
        {
            if (Property(dimension.DesignationProperty).GetValue(parameters) is not string designation)
                throw new ArgumentException("Обозначение допуска должно быть строкой.");
            if (designation.Length == 0) continue;
            var (lower, upper) = Deviations(dimension, Number(result, dimension.NominalProperty), designation);
            Property(dimension.LowerProperty).SetValue(result, lower);
            Property(dimension.UpperProperty).SetValue(result, upper);
        }
        return result;
    }
    public static string Format(double value) => value.ToString("0.####", CultureInfo.GetCultureInfo("ru-RU"));
    public static string Signed(double value) => (value > 0 ? "+" : "") + Format(value);
    public static string Numeric(double lower, double upper) => lower == -upper
        ? "±" + Format(upper) : (upper == 0 ? "" : Signed(upper)) + " / " + (lower == 0 ? "" : Signed(lower));
    public static string Description(Parameters p) => string.Join("; ", Dimensions.Select(d =>
    {
        var designation = Designation(p, d);
        return d.Key + ": " + (designation.Length == 0 ? "вручную" : designation) + " (" +
            Signed(Number(p, d.LowerProperty)) + "/" + Signed(Number(p, d.UpperProperty)) + ")";
    }));
}
