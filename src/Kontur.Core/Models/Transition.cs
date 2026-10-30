namespace Kontur.Core;

public record Transition
{
    public string Id { get; init; } = "";
    public string Title { get; init; } = "";
    public string Tool { get; init; } = "";
    public string Kind { get; init; } = "";
    public double Diameter
    {
        get; init;
    }
    public double StartDiameter
    {
        get; init;
    }
    public double Rpm
    {
        get; init;
    }
    public double Feed
    {
        get; init;
    }
    public double Depth
    {
        get; init;
    }
    public double ReferenceDiameter
    {
        get; init;
    }
    public List<double> Passes { get; init; } = [];

    /// <summary>Фактический наибольший радиальный съём по округлённым диаметрам проходов.</summary>
    public double ActualMaximumDepth
    {
        get
        {
            var previous = (decimal)StartDiameter;
            decimal maximum = 0;
            foreach (var diameter in Passes)
            {
                var current = (decimal)diameter;
                maximum = Math.Max(maximum, Math.Abs(current - previous) / 2m);
                previous = current;
            }

            return (double)Math.Round(maximum, 4, MidpointRounding.AwayFromZero);
        }
    }

    public double ZStart
    {
        get; init;
    }
    public double ZEnd
    {
        get; init;
    }
    public double CutMinutes
    {
        get; set;
    }
    public List<int> Blocks { get; init; } = [];
}
