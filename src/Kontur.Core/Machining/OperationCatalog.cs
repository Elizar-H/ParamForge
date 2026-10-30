using static Kontur.Core.Engine;

namespace Kontur.Core;

internal static class OperationCatalog
{
    internal static List<Operation> Build(Parameters p, Calculations c)
    {
        var controls = InspectionPlan.Describe(p, c);
        var generalToleranceNote = p.BodyTolerance == "m" && p.FlangeWidthTolerance == "m" ? " (" + ToolingProfile.GeneralToleranceDesignation + ")" : "";
        var operations = new List<Operation>
        {
            new(
                "005",
                "Подготовка заготовки",
                "manual",
                $"Принять предварительно обработанный полый полуфабрикат Ø{Fmt(p.StockDiameter)} / Ø{Fmt(p.StockBore)} × {Fmt(c.RawStockLength)} мм. Снять с переднего торца {Fmt(p.FrontFacingAllowance)} мм до длины {Fmt(c.StockLength)} мм; этот торец принять исходным. Отверстие сквозное, способ его получения находится вне данного процесса."
            ),
            new(
                "010",
                "Токарная с ЧПУ",
                "nc",
                $"Один установ: длина зажима {Fmt(p.GripLength)} мм; отрезная полоса {Fmt(c.CutoffWidth)} мм и отдельный осевой зазор до кулачков {Fmt(p.ChuckClearance)} мм. Передняя плоскость кулачков Z={Fmt(c.JawsFrontZ)} мм. Z0 — подготовленный передний торец, Z− — к зажиму, X — диаметр. Обработать буртик, тело, отверстие и расточку на длине {Fmt(c.CutLength)} мм с задним припуском {Fmt(p.TrimAllowance)} мм. Цели D={Fmt(c.TargetFlangeDiameter)}, d={Fmt(c.TargetBoreDiameter)}, d2={Fmt(c.TargetCounterboreDiameter)}, h={Fmt(c.TargetCounterboreDepth)} мм. Галтели R≤{Fmt(ToolingProfile.MaximumCornerRadius)} при пластине R{Fmt(ToolingProfile.NoseRadius)}; непрерывная СОЖ."
            ),
            new(
                "020",
                "Отделение и доводка",
                "manual",
                $"Отрезать лезвием {ToolingProfile.CutoffTool} с пластиной {ToolingProfile.CutoffInsert}: полоса Z={Fmt(c.PartingFrontZ)}…{Fmt(c.PartingBackZ)} мм, ширина {Fmt(c.CutoffWidth)} мм. Сохранить задний припуск {Fmt(p.TrimAllowance)} мм. Переустановить в мягкие кулачки по Db={Fmt(p.BodyDiameter)} мм с карманом Ø{Fmt(c.SoftJawPocketDiameter)} глубиной {Fmt(c.SoftJawPocketDepth)} мм и осевым упором по первому торцу. Контактный пояс от {Fmt(c.SecondClampStart)} до {Fmt(c.SecondClampEnd)} мм от первого торца; перед обработкой заднего торца требуется свободный участок не меньше {Fmt(ToolingProfile.SecondSetupFaceClearance)} мм. Снять задний припуск до длины {Fmt(c.TargetLength)} мм (границы {Fmt(p.Length + p.LengthLowerDeviation)}…{Fmt(p.Length + p.LengthUpperDeviation)}). Сохранить h={Fmt(c.TargetCounterboreDepth)} и b={Fmt(p.FlangeWidth)} мм от первого торца; удалить заусенцы."
            ),
            new(
                "030",
                "Контроль",
                "manual",
                controls + $" Проверить Db={Fmt(p.BodyDiameter)} ({Tolerances.Signed(p.BodyLowerDeviation)}/{Tolerances.Signed(p.BodyUpperDeviation)}) и b={Fmt(p.FlangeWidth)} ({Tolerances.Signed(p.FlangeWidthLowerDeviation)}/{Tolerances.Signed(p.FlangeWidthUpperDeviation)}) мм{generalToleranceNote}. Глубину h измерять от первого торца на плоской части уступа вне галтели R≤{Fmt(ToolingProfile.MaximumCornerRadius)}. Средства: микрометр/штангенциркуль, нутромер и глубиномер."
            )
        };
        return operations;
    }
}
