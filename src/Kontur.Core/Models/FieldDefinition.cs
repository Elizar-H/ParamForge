namespace Kontur.Core;

public record FieldDefinition(string Key, string Label, string Unit, string Group, double Min, double Max, double Step, string Note);
