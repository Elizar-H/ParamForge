using System.Globalization;
using System.Text;

namespace Kontur.Core;

internal static class HtmlCards
{
    private const string Style = "body{font-family:Arial,sans-serif;max-width:1120px;margin:28px auto;padding:0 20px;color:#182a35;line-height:1.45}h1{font-size:25px}h2{font-size:18px;margin-top:24px}table{border-collapse:collapse;width:100%;font-size:12px;margin:14px 0}th,td{border:1px solid #bcc9cf;padding:7px;text-align:left;vertical-align:top}th{background:#eaf1f3}.warning{border-left:4px solid #b86d1c;background:#fff5e5;padding:12px}@media print{body{margin:0;padding:0;font-size:11px}h1{font-size:20px}thead{display:table-header-group}tr{break-inside:avoid}h2{break-after:avoid}@page{size:A4 landscape;margin:14mm}}";
    private static string Esc(string value) => value.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;").Replace("'", "&#39;");
    private static string F(double value) => value.ToString("0.####", CultureInfo.GetCultureInfo("ru-RU"));
    private static string Signed(double value) => (value > 0 ? "+" : "") + F(value);
    private static string Document(string title, string body) => "<!doctype html><html lang=\"ru\"><head><meta charset=\"utf-8\"><title>" + Esc(title) + "</title><style>" + Style + "</style></head><body>" + body + "</body></html>";
    private static string Header(Project p, string title) => $"<h1>{Esc(title)}</h1><p><strong>{Esc(p.Params.PartName)}</strong>; {Esc(p.Id)}; версия генератора {Esc(p.Version)}</p><p>Материал: {Esc(p.Params.Material)}. X — диаметр; мм; Z0 — правый торец. ЧПУ: {Esc(CncProfiles.Name(p.Params.Controller))}; УП {Esc(p.NcFileName)}.</p>";

    private static IEnumerable<(string Name, double Nominal, double Lower, double Upper, double Target)> Dimensions(Parameters p, Calculations c)
    {
        yield return ("L — конечная длина", p.Length, p.LengthLowerDeviation, p.LengthUpperDeviation, c.TargetLength);
        yield return ("D — буртик", p.FlangeDiameter, p.FlangeLowerDeviation, p.FlangeUpperDeviation, c.TargetFlangeDiameter);
        yield return ("d — основное отверстие", p.BoreDiameter, p.BoreLowerDeviation, p.BoreUpperDeviation, c.TargetBoreDiameter);
        yield return ("d2 — внутренняя ступень", p.CounterboreDiameter, p.CounterboreLowerDeviation, p.CounterboreUpperDeviation, c.TargetCounterboreDiameter);
        yield return ("h — глубина расточки", p.CounterboreDepth, p.CounterboreDepthLowerDeviation, p.CounterboreDepthUpperDeviation, c.TargetCounterboreDepth);
        var db = ToolingProfile.GeneralLinearTolerance(p.BodyDiameter);
        var b = ToolingProfile.GeneralLinearTolerance(p.FlangeWidth);
        yield return ("Db — тело, общий допуск m", p.BodyDiameter, -db, db, p.BodyDiameter);
        yield return ("b — ширина бурта, общий допуск m", p.FlangeWidth, -b, b, p.FlangeWidth);
    }

    internal static string ControlDescription(Parameters p, Calculations c) => "Проверить: " + string.Join("; ", Dimensions(p, c).Select(d =>
        $"{d.Name}: номинал {F(d.Nominal)}, отклонения {Signed(d.Lower)}/{Signed(d.Upper)}, границы {F(d.Nominal + d.Lower)}…{F(d.Nominal + d.Upper)}, расчётная цель {F(d.Target)} мм")) + ".";

    private static string Tolerances(Project project)
    {
        var rows = string.Concat(Dimensions(project.Params, project.Calculations).Select(d =>
            $"<tr><td>{Esc(d.Name)}</td><td>{F(d.Nominal)}</td><td>{Signed(d.Lower)} / {Signed(d.Upper)}</td><td>{F(d.Nominal + d.Lower)} … {F(d.Nominal + d.Upper)}</td><td>{F(d.Target)}</td></tr>"));
        return "<p>" + Esc(Kontur.Core.Tolerances.Description(project.Params)) + "</p><h2>Номиналы, допуски и расчётные цели</h2><p>Все размеры в мм. Цель — середина поля допуска с округлением до 0,001 мм.</p><table><thead><tr><th>Размер</th><th>Номинал</th><th>Нижнее / верхнее отклонения</th><th>Границы</th><th>Расчётная цель</th></tr></thead><tbody>" + rows + "</tbody></table>";
    }
    private static string ParametersTable(Parameters p) => "<h2>Исходные параметры</h2><table><thead><tr><th>Параметр</th><th>Значение</th><th>Назначение</th></tr></thead><tbody>" +
        string.Concat(Engine.Fields.Select(f => $"<tr><td>{Esc(f.Label)}</td><td>{F(Engine.Number(p, f.Key))} {Esc(f.Unit)}</td><td>{Esc(f.Note)}</td></tr>")) + "</tbody></table>";

    internal static string Route(Project project)
    {
        var table = string.Concat(project.Operations.Select(o => $"<tr><td>{Esc(o.Code)}</td><td>{Esc(o.Title)}</td><td>{Esc(o.Description)}</td><td>{(o.Kind == "nc" ? "УП " + Esc(project.ProgramName) : "Вне УП")}</td></tr>"));
        return Document("Маршрутная карта " + project.Id, Header(project, "Маршрутная карта") +
            "<table><thead><tr><th>Операция</th><th>Наименование</th><th>Содержание и оснащение</th><th>Связь с УП</th></tr></thead><tbody>" + table + "</tbody></table>" + Tolerances(project) + ParametersTable(project.Params));
    }
    internal static string Technology(Project project)
    {
        var p = project.Params; var c = project.Calculations;
        var body = new StringBuilder(Header(project, "Технологическая карта операции 010"));
        body.Append($"<p>Зажим за хвостовик {F(p.GripLength)} мм. Заготовка Ø{F(p.StockDiameter)}/Ø{F(p.StockBore)} × {F(c.StockLength)} мм после005; до005 {F(c.RawStockLength)} мм, рез {F(c.CutoffWidth)} мм, кулачки Z{F(c.JawsFrontZ)}. T1: {Esc(ToolingProfile.ExternalTool)}; T2: {Esc(ToolingProfile.BoringTool)}; пластина {Esc(ToolingProfile.Insert)}. Сопряжения R≤{F(ToolingProfile.MaximumCornerRadius)} мм.</p>");
        body.Append(Tolerances(project));
        body.Append("<table><thead><tr><th>Переход</th><th>Содержание / Z мм</th><th>Инструмент</th><th>X проходов, диаметры мм</th><th>Число</th><th>Фактический max ap мм</th><th>n об/мин</th><th>f мм/об</th><th>t G1 мин</th><th>Кадры движений</th></tr></thead><tbody>");
        foreach (var t in project.Transitions)
            body.Append($"<tr><td>{Esc(t.Id)}</td><td>{Esc(t.Title)}<br>Z {F(t.ZStart)} → {F(t.ZEnd)}</td><td>{Esc(t.Tool)}</td><td>{string.Join("; ", t.Passes.Select(F))}</td><td>{t.Passes.Count}</td><td>{t.ActualMaximumDepth.ToString("0.####", CultureInfo.GetCultureInfo("ru-RU"))}</td><td>{F(t.Rpm)}</td><td>{F(t.Feed)}</td><td>{t.CutMinutes.ToString("F3", CultureInfo.InvariantCulture)}</td><td>N{t.Blocks.First()}–N{t.Blocks.Last()}</td></tr>");
        body.Append($"</tbody></table><p>Сумма времени G1: <strong>{c.TotalCutMinutes.ToString("F3", CultureInfo.InvariantCulture)} мин</strong>. Это не штучное время и не норматив производительности.</p><h2>Операции вне УП</h2>");
        foreach (var o in project.Operations.Where(o => o.Kind == "manual")) body.Append($"<p><strong>{Esc(o.Code)} {Esc(o.Title)}</strong>: {Esc(o.Description)}</p>");
        body.Append(ParametersTable(p));
        return Document("Технологическая карта " + project.Id, body.ToString());
    }
}
