namespace Kontur.Core;

public static class CardFields
{
    public static IReadOnlyList<(string Key, string Label, int Maximum)> Fields
    {
        get;
    } = Array.AsReadOnly(new[]
    {
        ("organization", "Организация", 45), ("drawingNumber", "Обозначение чертежа", 40),
        ("developer", "Разработал", 24), ("checker", "Проверил", 24),
        ("rateSetter", "Нормировал", 24), ("normController", "Нормоконтроль", 24)
    });
    public static string Value(Parameters p, string key) => (string)typeof(Parameters)
        .GetProperty(char.ToUpperInvariant(key[0]) + key[1..])!.GetValue(p)!;
}
