namespace Kontur.Core;

public record MachiningPlan(string Id, Calculations Calculations, List<Transition> Transitions, List<NcInstruction> Instructions);
