namespace Kontur.Core;

public record Project
{
    public string NcFileName { get; init; } = "Управляющая программа.nc";
    public string ProgramName { get; init; } = "";
    public string Id { get; init; } = "";
    public string Version { get; init; } = Engine.Version;
    public Parameters Params { get; init; } = new();
    public Calculations Calculations { get; init; } = new(0, 0, 0, 0, 0);
    public List<Operation> Operations { get; init; } = [];
    public List<Transition> Transitions { get; init; } = [];
    public List<Move> Moves { get; init; } = [];
    public string Nc { get; init; } = "";
    public string RouteHtml { get; init; } = "";
    public string TechnologyHtml { get; init; } = "";
    public List<string> Warnings { get; init; } = [];
    public VerificationResult Verification { get; init; } = new(false, [], [], [], 0);
}
