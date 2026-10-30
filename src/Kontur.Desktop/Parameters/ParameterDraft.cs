using Kontur.Core;

namespace Kontur.Desktop;

internal sealed record ParameterDraft(Parameters? Value, IReadOnlyList<ValidationError> Errors);
