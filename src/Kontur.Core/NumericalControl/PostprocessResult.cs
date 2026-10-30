namespace Kontur.Core;

public record PostprocessResult(string Text, string FileName, string ProgramName, List<Move> Moves, List<Transition> Transitions);
