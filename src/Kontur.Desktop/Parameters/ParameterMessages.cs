using Kontur.Core;

namespace Kontur.Desktop;

internal static class ParameterMessages
{

    internal static string? ProfileError(IReadOnlyList<ValidationError> errors)
    {
        if (errors.Any(e => e.Field == "length" && e.Message.StartsWith("Недостаточно длины для второго установа", StringComparison.Ordinal)))
        {
            return "Мало длины для второго установа.";
        }

        if (errors.Any(e => e.Field == "stockBore" && e.Message.Contains("оправки", StringComparison.Ordinal)))
        {
            return $"Для оправки нужно отверстие от {NumericText.Format(ToolingProfile.BoringMinimumDiameter)} мм.";
        }

        if (errors.Any(e => e.Field == "clearanceX" && e.Message.Contains("оправки", StringComparison.Ordinal)))
        {
            return $"Отвод оправки: диаметр от {NumericText.Format(ToolingProfile.BoringMinimumDiameter)} мм.";
        }

        if (errors.Any(e => e.Field == "length" && e.Message.StartsWith("Длина обработки превышает", StringComparison.Ordinal)))
        {
            return $"Вылет оправки: обработка до {NumericText.Format(ToolingProfile.MaximumBoringDepth)} мм.";
        }

        if (errors.Any(e => e.Field == "gripLength" && e.Message.StartsWith("Длина зажима", StringComparison.Ordinal)))
        {
            return $"Зажим должен быть не короче {NumericText.Format(ToolingProfile.ChuckMinimumGrip)} мм.";
        }

        if (errors.Any(e => e.Field == "chuckClearance" && e.Message.Contains("диапазон", StringComparison.Ordinal)))
        {
            var field = Engine.Fields.Single(f => f.Key == "chuckClearance");
            return $"Зазор до кулачков: {NumericText.Format(field.Min)}…{NumericText.Format(field.Max)} мм.";
        }

        if (errors.Any(e => e.Field == "chuckClearance" && e.Message.StartsWith("Осевой зазор", StringComparison.Ordinal)))
        {
            return $"Зазор до кулачков — от {NumericText.Format(ToolingProfile.ChuckMinimumClearance)} мм.";
        }

        if (errors.Any(e => e.Message.Contains("галтел", StringComparison.OrdinalIgnoreCase)))
        {
            return "Недостаточно места для сопряжения.";
        }

        return null;
    }

}
