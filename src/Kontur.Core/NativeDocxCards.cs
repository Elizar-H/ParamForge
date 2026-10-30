using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace Kontur.Core;

/// <summary>
/// Редактируемые формы: МК 1 по ГОСТ 3.1118-82; ОК 3 и 2а по ГОСТ 3.1404-86.
/// Сетка следует образцам Дубна с. 89 и ТПУ с. 108–111, 118–120.
/// Полное соответствие ГОСТ без измерительной проверки не заявляется.
/// </summary>
internal static class NativeDocxCards
{
    private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    private static readonly XNamespace R = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private static readonly XNamespace Rel = "http://schemas.openxmlformats.org/package/2006/relationships";
    private static readonly XNamespace Ct = "http://schemas.openxmlformats.org/package/2006/content-types";
    private static readonly DateTimeOffset ArchiveDate = new(2000, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private const int WidthMm = 272, WidthTwips = 15420;
    // Одинаковый шаг всех линейных строк тела на каждом листе, в целых twips.
    private const double RouteBodyMm = 326 * 25.4 / 1440;
    private const double FirstOperationBodyMm = 327 * 25.4 / 1440;
    private const double ContinuedOperationBodyMm = 346 * 25.4 / 1440;
    private static string F(double n) => n == 0 ? "0" : n.ToString("0.####", CultureInfo.GetCultureInfo("ru-RU"));
    private static string Depth(double n) => n.ToString("0.####", CultureInfo.GetCultureInfo("ru-RU"));
    private static string Sign(double n) => (n > 0 ? "+" : "") + F(n);
    private static string Mark(ref int n, string prefix = "") => prefix + (n++).ToString("D2", CultureInfo.InvariantCulture);
    private static int Mm(double n) => (int)Math.Round(n * 1440 / 25.4);
    private static XElement Val(string n, string v) => new(W + n, new XAttribute(W + "val", v));
    private sealed record Cell(int Width, string Text = "", bool Center = false, bool Bold = false,
        int Font = 18, string? Merge = null, XElement? Nested = null, bool NoBorders = false);

    private static XElement P(string text, int font = 18, bool bold = false, string align = "left", bool pageBreak = false)
    {
        var pr = new XElement(W + "pPr", pageBreak ? new XElement(W + "pageBreakBefore") : null,
            new XElement(W + "spacing", new XAttribute(W + "before", 0), new XAttribute(W + "after", 0), new XAttribute(W + "line", 240), new XAttribute(W + "lineRule", "auto")), Val("jc", align),
            new XElement(W + "rPr", Val("sz", font.ToString(CultureInfo.InvariantCulture)), Val("szCs", font.ToString(CultureInfo.InvariantCulture))));
        var run = new XElement(W + "r", new XElement(W + "rPr", bold ? new XElement(W + "b") : null,
            Val("color", "000000"), Val("sz", font.ToString(CultureInfo.InvariantCulture)), Val("szCs", font.ToString(CultureInfo.InvariantCulture))));
        var pieces = text.Split('\n');
        for (var i = 0; i < pieces.Length; i++)
        {
            if (i > 0) run.Add(new XElement(W + "br"));
            run.Add(new XElement(W + "t", new XAttribute(XNamespace.Xml + "space", "preserve"), pieces[i]));
        }
        return new(W + "p", pr, run);
    }
    private static XElement Table(int mm = WidthMm, bool visible = true)
    {
        var width = mm == WidthMm ? WidthTwips : Mm(mm);
        return new XElement(W + "tbl", new XElement(W + "tblPr",
            new XElement(W + "tblW", new XAttribute(W + "w", width), new XAttribute(W + "type", "dxa")),
            new XElement(W + "tblInd", new XAttribute(W + "w", mm == WidthMm && visible ? 45 : 0), new XAttribute(W + "type", "dxa")),
            new XElement(W + "tblBorders", new[] { "top", "left", "bottom", "right", "insideH", "insideV" }.Select(side =>
                new XElement(W + side, new XAttribute(W + "val", visible ? "single" : "nil"), new XAttribute(W + "sz", side.StartsWith("inside", StringComparison.Ordinal) ? 4 : 8), new XAttribute(W + "color", "000000")))),
            new XElement(W + "tblLayout", new XAttribute(W + "type", "fixed")),
            new XElement(W + "tblCellMar", new[] { ("top", 12), ("left", 45), ("bottom", 12), ("right", 45) }.Select(v =>
                new XElement(W + v.Item1, new XAttribute(W + "w", v.Item2), new XAttribute(W + "type", "dxa"))))),
            new XElement(W + "tblGrid", Enumerable.Range(0, mm).Select(i => new XElement(W + "gridCol", new XAttribute(W + "w", (i + 1) * width / mm - i * width / mm)))));
    }
    // Одна миллиметровая сетка с объединениями воспроизводит разные графы А/Б/О/Т/Р.
    private static void Row(XElement table, double height, IEnumerable<Cell> cells, bool header = false, int mm = WidthMm)
    {
        var values = cells.ToArray();
        if (values.Sum(c => c.Width) != mm) throw new InvalidOperationException("Некорректная ширина граф бланка.");
        var tr = new XElement(W + "tr", new XElement(W + "trPr", new XElement(W + "cantSplit"),
            new XElement(W + "trHeight", new XAttribute(W + "val", Mm(height)), new XAttribute(W + "hRule", "atLeast")), header ? new XElement(W + "tblHeader") : null));
        var twips = mm == WidthMm ? WidthTwips : Mm(mm); var x = 0;
        foreach (var c in values)
        {
            var pr = new XElement(W + "tcPr", new XElement(W + "tcW", new XAttribute(W + "w", (x + c.Width) * twips / mm - x * twips / mm), new XAttribute(W + "type", "dxa")), Val("gridSpan", c.Width.ToString(CultureInfo.InvariantCulture)));
            if (c.Merge is not null) pr.Add(Val("vMerge", c.Merge));
            if (c.NoBorders) pr.Add(new XElement(W + "tcBorders", new[] { "top", "left", "bottom", "right" }.Select(side => new XElement(W + side, new XAttribute(W + "val", "nil")))));
            pr.Add(Val("vAlign", "center"));
            var tc = new XElement(W + "tc", pr);
            if (c.Nested is not null) { tc.Add(c.Nested); tc.Add(P("", 2)); }
            else tc.Add(P(c.Text, c.Font, c.Bold, c.Center ? "center" : "left"));
            tr.Add(tc); x += c.Width;
        }
        table.Add(tr);
    }
    private static IEnumerable<XElement> Top(string standard, string form, bool nextPage = false)
    {
        yield return P($"{standard}     Форма {form}", 14, align: "right", pageBreak: nextPage);
        var left = Table(75);
        foreach (var label in new[] { "Дубл.", "Взам.", "Подл." }) Row(left, 4, [new(17, label, Font: 14), new(19), new(19), new(20)], mm: 75);
        var right = Table(128);
        for (var i = 0; i < 3; i++) Row(right, 4, [new(10), new(12), new(18), new(18), new(25), new(45)], mm: 128);
        var center = Table(69);
        for (var i = 0; i < 2; i++) Row(center, 4, [new(15), new(15), new(20), new(19)], mm: 69);
        var zone = Table(visible: false);
        Row(zone, 13, [new(75, Nested: left, NoBorders: true), new(69, Nested: center, NoBorders: true), new(128, Nested: right, NoBorders: true)]);
        yield return zone; yield return P("", 2);
    }
    private static void Ribbon(XElement t, int page, int count) => Row(t, 6,
        [new(212), new(30), new(15, "Листов " + count, true, Font: 14), new(15, "Лист " + page, true, Font: 14)], true);
    private static void Address(XElement t, Project p, string operation = "")
    {
        var labels = new[] { "Разраб.", "Проверил", "Нормировал", "Н.контр." };
        for (var i = 0; i < 4; i++)
        {
            var cells = new List<Cell> { new(22, labels[i], Font: 14), new(31), new(18), new(14) };
            if (i < 3)
            {
                var merge = i == 0 ? "restart" : "continue";
                cells.AddRange([new(40, Merge: merge), new(77, i == 0 ? p.Id : "", true, Merge: merge), new(70, Merge: merge)]);
            }
            else cells.AddRange([new(172, p.Params.PartName, true, Font: 20), new(15, operation, true)]);
            Row(t, 4, cells, true);
        }
    }
    private static void ContinuedAddress(XElement t, Project p)
    {
        for (var i = 0; i < 3; i++)
        {
            var merge = i == 0 ? "restart" : "continue";
            Row(t, 4, [new(15), new(15), new(20), new(20), new(20), new(20), new(20), new(32),
                new(75, i == 0 ? p.Id : "", true, Merge: merge), new(35, i == 0 ? "010" : "", true, Merge: merge)], true);
        }
    }
    private static void Bottom(XElement t, string code, string title, int page) => Row(t, 7,
        [new(22, code, true, true, 24), new(230, title, Font: 22), new(20, page.ToString(CultureInfo.InvariantCulture), true, Font: 20)]);
    private static void TextRow(XElement t, string mark, string text, double height = RouteBodyMm) => Row(t, height, [new(12, mark, true), new(260, text)]);
    private static string Designation(Parameters p, string key) {
        var d = Tolerances.Dimensions.Single(d => d.Key == key);
        var text = Tolerances.Designation(p, d);
        return text.Length == 0 ? "" : " " + text;
    }
    private static string Nominals(Parameters p) => $"Номиналы и отклонения (ниж./верх.), мм: d {F(p.BoreDiameter)}{Designation(p, "boreDiameter")} ({Sign(p.BoreLowerDeviation)}/{Sign(p.BoreUpperDeviation)}); d2 {F(p.CounterboreDiameter)}{Designation(p, "counterboreDiameter")} ({Sign(p.CounterboreLowerDeviation)}/{Sign(p.CounterboreUpperDeviation)}); D {F(p.FlangeDiameter)}{Designation(p, "flangeDiameter")} ({Sign(p.FlangeLowerDeviation)}/{Sign(p.FlangeUpperDeviation)}); L {F(p.Length)}{Designation(p, "length")} ({Sign(p.LengthLowerDeviation)}/{Sign(p.LengthUpperDeviation)}); h {F(p.CounterboreDepth)}{Designation(p, "counterboreDepth")} ({Sign(p.CounterboreDepthLowerDeviation)}/{Sign(p.CounterboreDepthUpperDeviation)}).";
    private static string Targets(Calculations c) => $"Расчётные цели, мм: d {F(c.TargetBoreDiameter)}; d2 {F(c.TargetCounterboreDiameter)}; D {F(c.TargetFlangeDiameter)}; L {F(c.TargetLength)}; h {F(c.TargetCounterboreDepth)}. Цели — середины полей допуска.";

    private static string GeneralAcceptance(Parameters p)
    {
        var db = ToolingProfile.GeneralLinearTolerance(p.BodyDiameter);
        var width = ToolingProfile.GeneralLinearTolerance(p.FlangeWidth);
        return $"Db {F(p.BodyDiameter)} ±{F(db)}; b {F(p.FlangeWidth)} ±{F(width)} мм ({ToolingProfile.GeneralToleranceDesignation}); галтели R≤{F(ToolingProfile.MaximumCornerRadius)} мм.";
    }

    private static string InspectionTools(Parameters p)
    {
        var low = p.FlangeDiameter + p.FlangeLowerDeviation;
        var high = p.FlangeDiameter + p.FlangeUpperDeviation;
        var first = (int)Math.Floor(low / 25) * 25;
        var last = Math.Max(first, ((int)Math.Ceiling(high / 25) - 1) * 25);
        var micrometers = string.Join(" / ", Enumerable.Range(0, (last - first) / 25 + 1).Select(i => $"{first + i * 25}–{first + (i + 1) * 25}"));
        var boreRanges = new List<string>();
        foreach (var (minimum, maximum) in new[] { (18D, 50D), (50D, 100D), (100D, 160D), (160D, 250D) })
            if (new[] { (p.BoreDiameter + p.BoreLowerDeviation, p.BoreDiameter + p.BoreUpperDeviation),
                (p.CounterboreDiameter + p.CounterboreLowerDeviation, p.CounterboreDiameter + p.CounterboreUpperDeviation) }
                .Any(range => range.Item1 < maximum && range.Item2 > minimum || range.Item1 == range.Item2 && range.Item1 >= minimum && range.Item1 <= maximum))
                boreRanges.Add($"{F(minimum)}–{F(maximum)}");
        var caliperMaximum = Math.Max(p.Length + p.LengthUpperDeviation, p.BodyDiameter + ToolingProfile.GeneralLinearTolerance(p.BodyDiameter));
        var caliper = caliperMaximum <= 150 ? 150 : caliperMaximum <= 200 ? 200 : 300;
        return $"Микрометр {micrometers}; нутромеры {string.Join(" / ", boreRanges)}; штангенциркуль 0–{caliper}; глубиномер 0–150.";
    }

    internal static byte[] Route(Project project)
    {
        var p = project.Params; var c = project.Calculations;
        var body = Top("ГОСТ 3.1118-82", "1").ToList(); var card = Table(); Ribbon(card, 1, 1); Address(card, project);
        TextRow(card, "М01", "Полый обработанный полуфабрикат / " + p.Material + "; ОТ, HB≤197", 6);
        int[] widths = [12, 30, 12, 12, 12, 20, 15, 30, 80, 18, 31];
        string[] labels = ["М02", "Код", "ЕВ", "МД", "ЕН", "Н. расх.", "КИМ", "Код загот.", "Профиль и размеры", "КД", "МЗ"];
        Row(card, 4, widths.Select((w, i) => new Cell(w, labels[i], true, Font: 14)), true);
        Row(card, 6, widths.Select((w, i) => new Cell(w, i == 8 ? $"Ø{F(p.StockDiameter)} / Ø{F(p.StockBore)} × {F(c.RawStockLength)} мм" : "", true)), true);
        int[] aw = [12, 8, 8, 9, 12, 97, 126]; string[] ah = ["А", "Цех", "Уч.", "РМ", "Опер.", "Код, наименование операции", "Обозначение документа"];
        Row(card, 4, aw.Select((w, i) => new Cell(w, ah[i], true, Font: 14)), true);
        int[] bw = [12, 108, 30, 14, 8, 8, 8, 10, 8, 8, 15, 21, 22]; string[] bh = ["Б", "Код, наименование оборудования", "СМ", "Проф.", "Р", "УТ", "КР", "КОИД", "ЕН", "ОП", "Кшт.", "Тпз", "Тшт."];
        Row(card, 4, bw.Select((w, i) => new Cell(w, bh[i], true, Font: 14)), true);
        var texts = new Dictionary<string, string>
        {
            ["005"] = $"Принять полый полуфабрикат Ø{F(p.StockDiameter)}/Ø{F(p.StockBore)} × {F(c.RawStockLength)}; подрезать первый торец на {F(p.FrontFacingAllowance)} до {F(c.StockLength)} мм.",
            ["010"] = $"Зажим {F(p.GripLength)} мм; кулачки Z{F(c.JawsFrontZ)}; обработка до Z{F(c.PartingFrontZ)}. Точить буртик, тело и отверстия; непрерывная СОЖ. УП {project.NcFileName}.",
            ["020"] = $"Отрезать в полосе Z{F(c.PartingFrontZ)}…{F(c.PartingBackZ)} мм (рез {F(c.CutoffWidth)} мм); коридор до кулачков {F(p.ChuckClearance)} мм. Вне УП.",
            ["030"] = "Контролировать размеры по указанным отклонениям; h — от первого торца на плоской части уступа вне галтели."
        };
        var n = 3;
        foreach (var op in project.Operations)
        {
            var name = op.Code switch { "005" => "Подготовка заготовки", "010" => "Токарная с ЧПУ", "020" => "Отделение и доводка", "030" => "Контроль", _ => op.Title };
            var av = new[] { Mark(ref n, "А"), "", "", "", op.Code, name, op.Code == "010" ? "ОК 010; УП " + project.ProgramName : "" };
            Row(card, RouteBodyMm, aw.Select((w, i) => new Cell(w, av[i], i is 0 or 4)));
            var bv = new string[bw.Length]; Array.Fill(bv, ""); bv[0] = Mark(ref n, "Б");
            bv[1] = op.Code switch { "010" => "Токарный с ЧПУ " + CncProfiles.Name(p.Controller), "005" or "020" => "Токарный, наладочный режим", _ => "Контрольное место" };
            Row(card, RouteBodyMm, bw.Select((w, i) => new Cell(w, bv[i], i == 0)));
            TextRow(card, Mark(ref n, "О"), texts.GetValueOrDefault(op.Code, op.Description));
            if (op.Code == "010") TextRow(card, Mark(ref n, "Т"), $"T1/D1 {ToolingProfile.ExternalTool}; T2/D1 {ToolingProfile.BoringTool}; {ToolingProfile.Insert}, R{F(ToolingProfile.NoseRadius)}.");
            if (op.Code == "020")
            {
                TextRow(card, Mark(ref n, "О"), $"ПФ-01: Ø{F(p.BodyDiameter)}, пояс {F(c.SecondClampStart)}…{F(c.SecondClampEnd)} от первого торца; упор первым торцом. Подрезать до L{F(c.TargetLength)} мм; снять заусенцы.");
                TextRow(card, Mark(ref n, "Т"), $"{ToolingProfile.CutoffTool} / {ToolingProfile.CutoffInsert}; ПФ-02; карман ПФ-01 Ø{F(c.SoftJawPocketDiameter)} × {F(c.SoftJawPocketDepth)} мм.");
            }
            if (op.Code == "030") TextRow(card, Mark(ref n, "Т"), InspectionTools(p));
        }
        TextRow(card, Mark(ref n, "О"), Nominals(p));
        TextRow(card, Mark(ref n, "О"), GeneralAcceptance(p));
        TextRow(card, Mark(ref n, "О"), Targets(c));
        Bottom(card, "МК", "Маршрутная карта", 1); body.Add(card);
        return Package(project, "Маршрутная карта", body);
    }

    private static readonly int[] RegimeWidths = [12, 108, 18, 30, 20, 14, 10, 20, 20, 20];
    private static void OperationHeader(XElement t, Project project, bool continued)
    {
        if (continued) { Ribbon(t, 2, 2); ContinuedAddress(t, project); }
        else
        {
            Ribbon(t, 1, 2); Address(t, project, "010");
            int[] widths = [75, 60, 30, 14, 18, 47, 14, 14]; string[] labels = ["Наименование операции", "Материал", "Твердость", "ЕВ", "МД", "Профиль и размеры", "МЗ", "КОИД"];
            Row(t, 4, widths.Select((w, i) => new Cell(w, labels[i], true, Font: 14)), true);
            var p = project.Params; var c = project.Calculations;
            var values = new[] { "Токарная с ЧПУ", p.Material, "≤197 HB", "", "", $"Ø{F(p.StockDiameter)}/Ø{F(p.StockBore)} × {F(c.StockLength)}", "", "" };
            Row(t, 8, widths.Select((w, i) => new Cell(w, values[i], true)), true);
            widths = [75, 60, 18, 18, 18, 18, 65]; labels = ["Оборудование, устройство ЧПУ", "Обозначение программы", "То", "Тв", "Тпз", "Тшт.", "СОЖ"];
            Row(t, 4, widths.Select((w, i) => new Cell(w, labels[i], true, Font: 14)), true);
            values = ["ЧПУ " + CncProfiles.Name(p.Controller), project.Params.Controller == CncProfiles.Sinumerik ? project.NcFileName : project.ProgramName, "", "", "", "", "Эмульсия, непрерывно (M8)"];
            Row(t, 10, widths.Select((w, i) => new Cell(w, values[i], true)), true);
        }
        string[] rh = ["Р", "", "ПИ", "D или B", "L", "t", "i", "S", "n", "V"];
        Row(t, 4, RegimeWidths.Select((w, i) => new Cell(w, rh[i], true, Font: 14)), true);
    }
    private static void TransitionRows(XElement card, Transition t, ref int n, double height)
    {
        TextRow(card, Mark(ref n, "О"), $"{t.Id} {t.Title}. Ø{F(t.StartDiameter)} → Ø{F(t.Diameter)}; Z {F(t.ZStart)} → {F(t.ZEnd)} мм.", height);
        TextRow(card, Mark(ref n, "Т"), t.Tool + " D1 — " + (t.Tool == "T1" ? ToolingProfile.ExternalTool : ToolingProfile.BoringTool) + "; " + ToolingProfile.Insert + ", R" + F(ToolingProfile.NoseRadius) + ".", height);
        var values = new[] { Mark(ref n, "Р"), t.Id, t.Tool.TrimStart('T'),
            F(t.ReferenceDiameter), F(Math.Abs(t.ZEnd - t.ZStart)), Depth(t.ActualMaximumDepth), t.Passes.Count.ToString(CultureInfo.InvariantCulture),
            F(t.Feed), F(t.Rpm), F(Math.PI * t.ReferenceDiameter * t.Rpm / 1000) };
        Row(card, height, RegimeWidths.Select((w, i) => new Cell(w, values[i], i != 1)));
    }
    internal static byte[] Technology(Project project)
    {
        var p = project.Params; var c = project.Calculations;
        var body = Top("ГОСТ 3.1404-86", "3").ToList(); var first = Table(); OperationHeader(first, project, false); var n = 1;
        TextRow(first, Mark(ref n, "О"), $"Заготовка {F(c.StockLength)} мм; зажим {F(p.GripLength)} мм, кулачки Z{F(c.JawsFrontZ)}; длина обработки {F(c.CutLength)} мм.", FirstOperationBodyMm);
        TextRow(first, Mark(ref n, "О"), "Z0 — первый подготовленный торец; Z− — к зажиму; X — диаметр. T/D привязаны к виртуальной вершине касательных X/Z.", FirstOperationBodyMm);
        foreach (var t in project.Transitions.Take(4)) TransitionRows(first, t, ref n, FirstOperationBodyMm);
        TextRow(first, Mark(ref n, "О"), $"T2: BD{F(ToolingProfile.BoringBarDiameter)}, DMIN{F(ToolingProfile.BoringMinimumDiameter)}, вылет {F(ToolingProfile.BoringOverhang)} мм; зажим оправки {F(ToolingProfile.BoringClampLength)} мм.", FirstOperationBodyMm);
        TextRow(first, Mark(ref n, "О"), $"Перед держателем T2 при полном ходе остаётся {F(ToolingProfile.BoringOverhang - c.CutLength)} мм. Отвод X{F(c.ClearIn)}; галтели R≤{F(ToolingProfile.MaximumCornerRadius)} мм.", FirstOperationBodyMm);
        TextRow(first, Mark(ref n, "О"), $"Рез {F(c.CutoffWidth)} мм и коридор {F(p.ChuckClearance)} мм: задний габарит блока ≤{F(ToolingProfile.CutoffHolderAxialProjection)}, остаточный запас ≥{F(p.ChuckClearance - ToolingProfile.CutoffHolderAxialProjection)} мм.", FirstOperationBodyMm);
        TextRow(first, Mark(ref n), "", FirstOperationBodyMm);
        Bottom(first, "ОК", "Операционная карта", 1); body.Add(first);
        // Продолжение формы 3 — форма 2а, а не 3а: ТПУ, с. 119–120.
        body.AddRange(Top("ГОСТ 3.1404-86", "2а", true)); var second = Table(); OperationHeader(second, project, true);
        foreach (var t in project.Transitions.Skip(4)) TransitionRows(second, t, ref n, ContinuedOperationBodyMm);
        TextRow(second, Mark(ref n, "О"), Nominals(p), ContinuedOperationBodyMm);
        TextRow(second, Mark(ref n, "О"), GeneralAcceptance(p), ContinuedOperationBodyMm);
        TextRow(second, Mark(ref n, "О"), Targets(c), ContinuedOperationBodyMm);
        TextRow(second, Mark(ref n, "О"), "В строках Р: D — опорный диаметр; t — фактический максимальный радиальный съём; S — мм/об, n — об/мин, V — м/мин.", ContinuedOperationBodyMm);
        TextRow(second, Mark(ref n, "О"), "005, 020 и 030 — вне этой УП. Получение готовой детали — по маршрутной карте; контроль h от первого торца вне галтели.", ContinuedOperationBodyMm);
        for (var i = 0; i < 5; i++) TextRow(second, Mark(ref n), "", ContinuedOperationBodyMm);
        Bottom(second, "ОК", "Операционная карта", 2); body.Add(second);
        return Package(project, "Операционная карта операции 010", body);
    }

    private static XDocument Styles()
    {
        return new XDocument(new XElement(W + "styles", new XAttribute(XNamespace.Xmlns + "w", W),
            new XElement(W + "docDefaults", new XElement(W + "rPrDefault", new XElement(W + "rPr",
                new XElement(W + "rFonts", new XAttribute(W + "ascii", "Times New Roman"), new XAttribute(W + "hAnsi", "Times New Roman"), new XAttribute(W + "cs", "Times New Roman")),
                Val("color", "000000"), Val("sz", "18"), new XElement(W + "lang", new XAttribute(W + "val", "ru-RU"))))),
            new XElement(W + "style", new XAttribute(W + "type", "paragraph"), new XAttribute(W + "styleId", "Normal"), new XAttribute(W + "default", "1"),
                new XElement(W + "name", new XAttribute(W + "val", "Normal")), new XElement(W + "pPr",
                    new XElement(W + "spacing", new XAttribute(W + "after", 0), new XAttribute(W + "line", 240), new XAttribute(W + "lineRule", "auto"))))));
    }
    private static void CompactGrid(XElement table)
    {
        // В документ попадают только реальные границы граф: Word ограничивает
        // таблицу 63 столбцами, поэтому рабочая миллиметровая сетка уплотняется.
        var grid = table.Element(W + "tblGrid")!;
        var original = grid.Elements(W + "gridCol").Select(c => (int)c.Attribute(W + "w")!).ToArray();
        var prefix = new int[original.Length + 1];
        for (var i = 0; i < original.Length; i++) prefix[i + 1] = prefix[i] + original[i];
        var boundaries = new SortedSet<int> { 0, original.Length };
        foreach (var row in table.Elements(W + "tr"))
        {
            var position = 0;
            foreach (var cell in row.Elements(W + "tc"))
            { position += (int)cell.Element(W + "tcPr")!.Element(W + "gridSpan")!.Attribute(W + "val")!; boundaries.Add(position); }
        }
        var points = boundaries.ToArray();
        if (points.Length - 1 > 63) throw new InvalidOperationException("Сетка формы превышает ограничение Word на число столбцов.");
        var indices = points.Select((point, index) => (point, index)).ToDictionary(v => v.point, v => v.index);
        grid.ReplaceNodes(Enumerable.Range(0, points.Length - 1).Select(i => new XElement(W + "gridCol", new XAttribute(W + "w", prefix[points[i + 1]] - prefix[points[i]]))));
        foreach (var row in table.Elements(W + "tr"))
        {
            var position = 0;
            foreach (var cell in row.Elements(W + "tc"))
            {
                var span = cell.Element(W + "tcPr")!.Element(W + "gridSpan")!;
                var end = position + (int)span.Attribute(W + "val")!;
                span.SetAttributeValue(W + "val", indices[end] - indices[position]); position = end;
            }
        }
    }
    private static byte[] Package(Project project, string title, IEnumerable<XElement> body)
    {
        var section = new XElement(W + "sectPr", new XElement(W + "pgSz", new XAttribute(W + "w", 16838), new XAttribute(W + "h", 11906), new XAttribute(W + "orient", "landscape")),
            new XElement(W + "pgMar", new XAttribute(W + "top", 283), new XAttribute(W + "right", 283), new XAttribute(W + "bottom", 283), new XAttribute(W + "left", 1134), new XAttribute(W + "header", 0), new XAttribute(W + "footer", 0), new XAttribute(W + "gutter", 0)));
        var document = new XDocument(new XDeclaration("1.0", "UTF-8", "yes"), new XElement(W + "document", new XAttribute(XNamespace.Xmlns + "w", W), new XElement(W + "body", body, P("", 2), section)));
        foreach (var table in document.Descendants(W + "tbl").ToList()) CompactGrid(table);
        var types = new XDocument(new XElement(Ct + "Types",
            new XElement(Ct + "Default", new XAttribute("Extension", "rels"), new XAttribute("ContentType", "application/vnd.openxmlformats-package.relationships+xml")),
            new XElement(Ct + "Default", new XAttribute("Extension", "xml"), new XAttribute("ContentType", "application/xml")),
            new[] { ("/word/document.xml", "application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"), ("/word/styles.xml", "application/vnd.openxmlformats-officedocument.wordprocessingml.styles+xml"), ("/docProps/core.xml", "application/vnd.openxmlformats-package.core-properties+xml") }.Select(v => new XElement(Ct + "Override", new XAttribute("PartName", v.Item1), new XAttribute("ContentType", v.Item2)))));
        XElement Link(string id, string type, string target) => new(Rel + "Relationship", new XAttribute("Id", id), new XAttribute("Type", type), new XAttribute("Target", target));
        var rootRels = new XDocument(new XElement(Rel + "Relationships", Link("rId1", R.NamespaceName + "/officeDocument", "word/document.xml"), Link("rId2", "http://schemas.openxmlformats.org/package/2006/relationships/metadata/core-properties", "docProps/core.xml")));
        var docRels = new XDocument(new XElement(Rel + "Relationships", Link("rId1", R.NamespaceName + "/styles", "styles.xml")));
        XNamespace cp = "http://schemas.openxmlformats.org/package/2006/metadata/core-properties", dc = "http://purl.org/dc/elements/1.1/";
        var props = new XDocument(new XElement(cp + "coreProperties", new XAttribute(XNamespace.Xmlns + "dc", dc), new XElement(dc + "title", title), new XElement(dc + "creator", "ParamForge"), new XElement(dc + "description", "Технологическая документация. Ревизия " + project.Id)));
        using var memory = new MemoryStream();
        using (var zip = new ZipArchive(memory, ZipArchiveMode.Create, true))
        {
            void Write(string path, XDocument xml)
            {
                var entry = zip.CreateEntry(path, CompressionLevel.Optimal); entry.LastWriteTime = ArchiveDate;
                using var stream = entry.Open(); using var writer = XmlWriter.Create(stream, new XmlWriterSettings { Encoding = new UTF8Encoding(false), Indent = false }); xml.Save(writer);
            }
            Write("[Content_Types].xml", types); Write("_rels/.rels", rootRels); Write("word/document.xml", document); Write("word/styles.xml", Styles()); Write("word/_rels/document.xml.rels", docRels); Write("docProps/core.xml", props);
        }
        return memory.ToArray();
    }
}
