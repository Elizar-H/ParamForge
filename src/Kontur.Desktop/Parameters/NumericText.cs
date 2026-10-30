using System.Globalization;

namespace Kontur.Desktop;

/// <summary>Converts editable millimetre values without depending on a particular form.</summary>
internal static class NumericText
{
    internal static string Format(double value) =>
        value.ToString("0.####", CultureInfo.GetCultureInfo("ru-RU"));

    internal static bool TryParse(string text, out double value) =>
        double.TryParse(
            text.Trim().Replace(',', '.'),
            NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
            CultureInfo.InvariantCulture,
            out value)
        && double.IsFinite(value);
}
