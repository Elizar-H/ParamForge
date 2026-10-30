using Kontur.Core;

namespace Kontur.Desktop;

internal sealed record ParsedNcSummary(string Tools, string Feeds, string Speeds, int Passes, double CutMinutes)
{
    internal static ParsedNcSummary From(IReadOnlyList<ParsedMove> moves, HashSet<int>? blocks = null)
    {
        var selected = moves.Where(m => blocks is null || blocks.Contains(m.Line)).ToArray();
        var cutting = selected.Where(m => m.Mode == "G1").ToArray();
        static string Range(IEnumerable<double?> values)
        {
            var distinct = values.Where(v => v.HasValue).Select(v => v!.Value).Distinct().Order().ToArray();
            return distinct.Length switch
            {
                0 => "—",
                1 => NumericText.Format(distinct[0]),
                _ => NumericText.Format(distinct[0]) + "…" + NumericText.Format(distinct[^1])
            };
        }
        double minutes = 0;
        var passes = 0;
        for (var i = 1; i < moves.Count; i++)
        {
            var previous = moves[i - 1];
            var current = moves[i];
            if (current.Mode != "G1" || current.Tool != previous.Tool || (blocks is not null && !blocks.Contains(current.Line)))
            {
                continue;
            }

            if (current.Z < previous.Z - .0005)
            {
                passes++;
            }

            if (current.Feed is > 0 && current.Rpm is > 0)
            {
                minutes += Math.Sqrt(Math.Pow((current.X - previous.X) / 2, 2) + Math.Pow(current.Z - previous.Z, 2)) / (current.Feed.Value * current.Rpm.Value);
            }
        }
        var tools = string.Join(", ", selected.Where(m => m.Tool.HasValue).Select(m => m.Tool!.Value).Distinct().Order().Select(t => "T" + t));
        return new(
            tools.Length == 0 ? "—" : tools,
            Range(cutting.Select(m => m.Feed)),
            Range(cutting.Select(m => m.Rpm)),
            passes,
            minutes
        );
    }
    internal string Display => $"Инструмент {Tools}    Подача {Feeds} мм/об    Частота {Speeds} об/мин    G1 {CutMinutes:0.000} мин";
}
