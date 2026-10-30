namespace Kontur.Core;

public record GenerationResult(bool Ok, List<ValidationError> Errors, List<string> Warnings, Project? Project);
