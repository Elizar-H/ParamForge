using Kontur.Core;

namespace Kontur.Desktop;

internal sealed partial class MainForm
{
    // The selected bar/setup can reject a length without making the nominal
    // part impossible to draw. Generation still validates every process rule.
    private static bool IsSetupLengthError(ValidationError error) => error.Field == "length" && (error.Message.StartsWith("Длина обработки превышает", StringComparison.Ordinal) || error.Message.StartsWith("Недостаточно длины для второго установа", StringComparison.Ordinal));
    private void ShowErrors(IReadOnlyList<ValidationError> invalid)
    {
        stale = true;
        foreach (var error in invalid)
        {
            if (inputs.TryGetValue(error.Field, out var box))
            {
                errors.SetError(box, error.Message);
                box.BackColor = Color.FromArgb(255, 239, 239);
            }
        }

        if (invalid.Count > 0 && inputs.TryGetValue(invalid[0].Field, out var first))
        {
            first.Focus();
            first.SelectAll();
        }

        status.ForeColor = Theme.Error;
        status.Text = InteractionError(invalid, ReadParameters(false, false) ?? working);
        tips.SetToolTip(status, string.Join(Environment.NewLine, invalid.Select(e => e.Message)));
        UpdatePreview();
    }

    private string ShortError(IReadOnlyList<ValidationError> invalid)
    {
        if (ParameterMessages.ProfileError(invalid) is { } profileError)
        {
            return profileError;
        }

        if (invalid.Any(e => e.Message.Contains("3 мм", StringComparison.Ordinal)))
        {
            return "Минимальная стенка — 3 мм.";
        }

        if (invalid.Any(e => e.Message.Contains("трёх знаков", StringComparison.Ordinal)))
        {
            return "Не более 3 знаков после запятой.";
        }

        if (invalid.Any(e => e.Message.StartsWith("Верхнее отклонение", StringComparison.Ordinal)))
        {
            return "Верхнее отклонение ниже нижнего.";
        }

        foreach (var error in invalid)
        {
            if (inputs.TryGetValue(error.Field, out var box))
            {
                if (!NumericText.TryParse(box.Text, out var number))
                {
                    return "Введите число.";
                }

                var field = Engine.Fields.FirstOrDefault(f => f.Key == error.Field);
                if (field is not null && (number < field.Min || number > field.Max))
                {
                    var name = field.Key switch
                    {
                        "length" => "Длина L",
                        "flangeDiameter" => "D",
                        "bodyDiameter" => "Db",
                        "flangeWidth" => "b",
                        "boreDiameter" => "d",
                        "counterboreDiameter" => "d2",
                        "counterboreDepth" => "h",
                        _ => "Отклонение"
                    };
                    return $"{name}: {NumericText.Format(field.Min)}…{NumericText.Format(field.Max)} мм.";
                }

                if (error.Message.Length <= 38)
                {
                    return error.Message;
                }
            }
        }

        return invalid.Any(e => inputs.ContainsKey(e.Field)) ? "Проверьте связь размеров." : "Проверьте параметры обработки.";
    }
}
