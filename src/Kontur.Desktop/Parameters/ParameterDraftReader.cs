using System.Reflection;
using Kontur.Core;

namespace Kontur.Desktop;

/// <summary>Reads numeric text and resolves tolerances before the UI displays validation errors.</summary>
internal static class ParameterDraftReader
{
    internal static PropertyInfo Property(string key) =>
        typeof(Parameters).GetProperty(char.ToUpperInvariant(key[0]) + key[1..])
        ?? throw new InvalidOperationException(key);

    internal static ParameterDraft Read(
        Parameters baseline,
        IEnumerable<KeyValuePair<string, string>> inputs,
        bool validate)
    {
        var result = baseline with
        {
        };
        var invalid = new List<ValidationError>();

        foreach (var (key, text) in inputs)
        {
            if (NumericText.TryParse(text, out var number))
            {
                Property(key).SetValue(result, number);
            }
            else
            {
                invalid.Add(new(key, "Введите число в выделенном поле."));
            }
        }

        if (invalid.Count == 0)
        {
            try
            {
                result = StockSelection.Resolve(Tolerances.Resolve(result));
            }
            catch (ArgumentException ex)
            {
                invalid.Add(new("tolerances", ex.Message));
            }
        }

        if (invalid.Count == 0 && validate)
        {
            invalid.AddRange(Engine.Validate(result));
        }

        return new(invalid.Count == 0 ? result : null, invalid);
    }
}
