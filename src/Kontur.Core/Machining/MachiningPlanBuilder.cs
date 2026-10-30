using static Kontur.Core.Engine;

namespace Kontur.Core;

/// <summary>Builds controller-independent transitions and explicit tool movements for operation 010.</summary>
internal static class MachiningPlanBuilder
{
    private record Definition(
        string Id,
        string Title,
        string Tag,
        int Tool,
        string Kind,
        double Start,
        double Target,
        double ZStart,
        double ZEnd,
        double Depth,
        bool Finish,
        double ReferenceDiameter
    );
    internal static MachiningPlan Build(Parameters p, string id, Calculations c)
    {
        var definitions = new[]
        {
            new Definition(
                "010.01",
                "Буртик — черновое точение",
                "FLANGE ROUGH",
                1,
                "outer",
                p.StockDiameter,
                c.TargetFlangeDiameter + 2 * p.FinishAllowance,
                0,
                c.TargetFlangeWidth,
                p.DepthOutside,
                false,
                p.StockDiameter
            ),
            new(
                "010.02",
                "Буртик — чистовое точение",
                "FLANGE FINISH",
                1,
                "outer",
                c.TargetFlangeDiameter + 2 * p.FinishAllowance,
                c.TargetFlangeDiameter,
                0,
                c.TargetFlangeWidth,
                p.FinishAllowance,
                true,
                c.TargetFlangeDiameter
            ),
            new(
                "010.03",
                "Тело — черновое точение",
                "BODY ROUGH",
                1,
                "body",
                p.StockDiameter,
                c.TargetBodyDiameter + 2 * p.FinishAllowance,
                c.TargetFlangeWidth,
                c.CutLength,
                p.DepthOutside,
                false,
                p.StockDiameter
            ),
            new(
                "010.04",
                "Тело — чистовое точение",
                "BODY FINISH",
                1,
                "body",
                c.TargetBodyDiameter + 2 * p.FinishAllowance,
                c.TargetBodyDiameter,
                c.TargetFlangeWidth,
                c.CutLength,
                p.FinishAllowance,
                true,
                c.TargetBodyDiameter
            ),
            new(
                "010.05",
                "Основное отверстие — черновое растачивание",
                "BORE ROUGH",
                2,
                "inner",
                p.StockBore,
                c.TargetBoreDiameter - 2 * p.FinishAllowance,
                0,
                c.CutLength,
                p.DepthInside,
                false,
                c.TargetBoreDiameter
            ),
            new(
                "010.06",
                "Основное отверстие — чистовое растачивание",
                "BORE FINISH",
                2,
                "inner",
                c.TargetBoreDiameter - 2 * p.FinishAllowance,
                c.TargetBoreDiameter,
                0,
                c.CutLength,
                p.FinishAllowance,
                true,
                c.TargetBoreDiameter
            ),
            new(
                "010.07",
                "Внутренняя ступень — черновое растачивание",
                "STEP ROUGH",
                2,
                "inner",
                c.TargetBoreDiameter,
                c.TargetCounterboreDiameter - 2 * p.FinishAllowance,
                0,
                c.TargetCounterboreDepth,
                p.DepthInside,
                false,
                c.TargetCounterboreDiameter
            ),
            new(
                "010.08",
                "Внутренняя ступень — чистовое растачивание",
                "STEP FINISH",
                2,
                "inner",
                c.TargetCounterboreDiameter - 2 * p.FinishAllowance,
                c.TargetCounterboreDiameter,
                0,
                c.TargetCounterboreDepth,
                p.FinishAllowance,
                true,
                c.TargetCounterboreDiameter
            )
        };
        var transitions = new List<Transition>();
        var instructions = new List<NcInstruction>();
        void Emit(NcInstructionKind kind, int tool = 0, double value = 0, string text = "") => instructions.Add(new(kind, Tool: tool, Value: value, Text: text));
        (double X, double Z)? position = null;
        var activeTool = 0;
        void Move(string mode, double x, double z, Transition? t)
        {
            x = Round(x);
            z = Round(z);
            var distance = position is { } a ? Math.Sqrt(Math.Pow((x - a.X) / 2, 2) + Math.Pow(z - a.Z, 2)) : 0;
            if (mode == "G1")
            {
                t!.CutMinutes += distance / (t.Feed * t.Rpm);
            }

            var move = new Move(x, z, mode, t?.Id ?? "", 0, mode == "G1" ? t!.Feed : null, t?.Rpm ?? 0);
            instructions.Add(new(NcInstructionKind.Motion, Motion: move));
            position = (x, z);
        }

        Emit(NcInstructionKind.Prepare);
        Emit(NcInstructionKind.Diameter);
        Emit(NcInstructionKind.CoolantOff);
        Emit(NcInstructionKind.Stop);
        foreach (var d in definitions)
        {
            var t = new Transition
            {
                Id = d.Id,
                Title = d.Title,
                Tool = "T" + d.Tool,
                Kind = d.Kind,
                Diameter = Round(d.Target),
                StartDiameter = Round(d.Start),
                Rpm = Math.Min(p.RpmMax, Math.Floor(1000 * p.CuttingSpeed / (Math.PI * d.ReferenceDiameter))),
                Feed = d.Finish ? p.FeedFinish : p.FeedRough,
                Depth = d.Depth,
                ReferenceDiameter = d.ReferenceDiameter,
                Passes = PassDiameters(d.Start, d.Target, d.Depth),
                ZStart = -d.ZStart,
                ZEnd = -d.ZEnd
            };
            if (d.Tool != activeTool)
            {
                if (activeTool != 0)
                {
                    Move("G0", c.ClearOut, p.ClearanceZ, null);
                    Emit(NcInstructionKind.FeedPerMinute);
                    Emit(NcInstructionKind.CoolantOff);
                    Emit(NcInstructionKind.Stop);
                }

                Emit(NcInstructionKind.Tool, tool: d.Tool);
                Emit(NcInstructionKind.GeometryOffset);
                Emit(NcInstructionKind.CoolantOn);
                activeTool = d.Tool;
            }

            Emit(NcInstructionKind.Comment, text: $"TR {d.Id} {d.Tag}");
            Emit(NcInstructionKind.Speed, value: t.Rpm);
            Emit(NcInstructionKind.FeedPerRevolution);
            // После смены инструмента исходная точка задаётся явным G0.
            Move("G0", d.Kind == "inner" ? c.ClearIn : c.ClearOut, p.ClearanceZ, t);
            foreach (var diameter in t.Passes)
            {
                if (d.Kind == "body")
                {
                    Move("G0", c.ClearOut, -d.ZStart, t);
                    Move("G1", diameter, -d.ZStart, t);
                    Move("G1", diameter, -d.ZEnd, t);
                    Move("G0", c.ClearOut, -d.ZEnd, t);
                    Move("G0", c.ClearOut, p.ClearanceZ, t);
                }
                else if (d.Kind == "outer")
                {
                    Move("G0", diameter, p.ClearanceZ, t);
                    Move("G1", diameter, -d.ZEnd, t);
                    Move("G0", c.ClearOut, -d.ZEnd, t);
                    Move("G0", c.ClearOut, p.ClearanceZ, t);
                }
                else
                {
                    Move("G0", diameter, p.ClearanceZ, t);
                    Move("G1", diameter, -d.ZEnd, t);
                    Move("G1", c.ClearIn, -d.ZEnd, t);
                    Move("G0", c.ClearIn, p.ClearanceZ, t);
                }
            }

            transitions.Add(t);
        }

        Move("G0", c.ClearIn, p.ClearanceZ, null);
        Move("G0", c.ClearOut, p.ClearanceZ, null);
        Emit(NcInstructionKind.FeedPerMinute);
        Emit(NcInstructionKind.CoolantOff);
        Emit(NcInstructionKind.Stop);
        Emit(NcInstructionKind.End);
        c = c with
        {
            TotalCutMinutes = transitions.Sum(t => t.CutMinutes)
        };
        return new(id, c, transitions, instructions);
    }
}
