using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Kontur.Core;

/// <summary>Coordinates validation, machining planning, postprocessing and document generation.</summary>
public static class Engine
{
    public const string Version = "2.13.1";
    public static Parameters Defaults => new();

    internal static double Round(double value) => Math.Round(value, 3, MidpointRounding.AwayFromZero);
    internal static string Fmt(double value) => value.ToString("0.####", CultureInfo.InvariantCulture);
    // Inputs are validated to three decimal places. Decimal keeps an exact half-micron
    // midpoint from falling below its tie through binary floating-point addition.
    public static double Target(double nominal, double lower, double upper) => (double)Math.Round((decimal)nominal + ((decimal)lower + (decimal)upper) / 2m, 3, MidpointRounding.AwayFromZero);
    public static double MinimumStockDiameter(Parameters p)
    {
        p = Tolerances.Resolve(p);
        return Round(Target(p.FlangeDiameter, p.FlangeLowerDeviation, p.FlangeUpperDeviation) + 2 * (p.FinishAllowance + .05));
    }

    internal static Calculations Calculate(Parameters p, double time = 0)
    {
        var length = Target(p.Length, p.LengthLowerDeviation, p.LengthUpperDeviation);
        var cut = Round(length + p.TrimAllowance);
        var stock = Round(cut + ToolingProfile.CutoffWidth + p.ChuckClearance + p.GripLength);
        var widthTolerance = p.FlangeWidthUpperDeviation;
        var secondClampStart = Round(p.FlangeWidth + widthTolerance + ToolingProfile.SoftJawBeltStartClearance);
        return new(
            stock,
            cut,
            Round(p.StockDiameter + 2 * p.ClearanceX),
            Round(p.StockBore - 2 * p.ClearanceX),
            time,
            Target(p.BoreDiameter, p.BoreLowerDeviation, p.BoreUpperDeviation),
            Target(p.CounterboreDiameter, p.CounterboreLowerDeviation, p.CounterboreUpperDeviation),
            Target(p.FlangeDiameter, p.FlangeLowerDeviation, p.FlangeUpperDeviation),
            length,
            Target(p.CounterboreDepth, p.CounterboreDepthLowerDeviation, p.CounterboreDepthUpperDeviation),
            ToolingProfile.CutoffWidth,
            -cut,
            -Round(cut + ToolingProfile.CutoffWidth),
            -Round(cut + ToolingProfile.CutoffWidth + p.ChuckClearance),
            Round(stock + p.FrontFacingAllowance),
            secondClampStart,
            Round(secondClampStart + ToolingProfile.SecondSetupGripLength),
            Round(p.FlangeDiameter + p.FlangeUpperDeviation + ToolingProfile.SoftJawPocketDiametralClearance),
            Round(p.FlangeWidth + widthTolerance + ToolingProfile.SoftJawPocketAxialClearance),
            Target(p.BodyDiameter, p.BodyLowerDeviation, p.BodyUpperDeviation),
            Target(p.FlangeWidth, p.FlangeWidthLowerDeviation, p.FlangeWidthUpperDeviation)
        );
    }

    public static List<double> PassDiameters(double start, double target, double depth)
    {
        if (!double.IsFinite(start) || !double.IsFinite(target) || !double.IsFinite(depth) || depth <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(depth), "Параметры проходов должны быть конечными, глубина — положительной.");
        }

        decimal first;
        decimal last;
        decimal step;
        try
        {
            first = (decimal)start;
            last = (decimal)target;
            step = (decimal)depth;
        }
        catch (OverflowException)
        {
            throw new ArgumentOutOfRangeException(nameof(depth), "Параметры проходов превышают числовой диапазон.");
        }

        if (step <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(depth), "Глубина прохода слишком мала.");
        }

        var delta = last - first;
        var count = Math.Ceiling(Math.Abs(delta) / (2m * step));
        if (count > 400)
        {
            throw new ArgumentOutOfRangeException(nameof(depth), "Число проходов превышает предел 400.");
        }

        var values = new List<double>();
        // Равные доли полного съёма вместо полного шага с микроскопическим остатком.
        for (var i = 1; i <= count; i++)
        {
            values.Add((double)Math.Round(i == count ? last : first + delta * i / count, 3, MidpointRounding.AwayFromZero));
        }

        return values;
    }

    public static IReadOnlyList<FieldDefinition> Fields => ParameterSchema.Fields;

    internal static double Number(Parameters p, string key) => ParameterSchema.Number(p, key);
    public static List<ValidationError> Validate(Parameters p) => ParameterValidator.Validate(p);
    public static GenerationResult Generate(Parameters? parameters = null)
    {
        var p = parameters ?? Defaults;
        var errors = Validate(p);
        if (errors.Count == 0)
        {
            p = StockSelection.Resolve(Tolerances.Resolve(p));
        }

        if (errors.Count > 0)
        {
            return new(false, errors, [], null);
        }

        var c = Calculate(p);
        var id = "V-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Version + "|" + JsonSerializer.Serialize(p, ProjectFiles.JsonOptions))))[..16];
        var warnings = new List<string>();
        var operations = OperationCatalog.Build(p, c);
        var plan = MachiningPlanBuilder.Build(p, id, c);
        c = plan.Calculations;
        var output = CncProfiles.Postprocessor(p.Controller).Process(plan);
        var nc = output.Text;
        var moves = output.Moves;
        var transitions = output.Transitions;
        var verification = NcVerifier.Verify(nc, p);
        if (!verification.Ok)
        {
            errors.AddRange(verification.Errors.Select(e => new ValidationError("nc", e)));
        }

        if (verification.Moves.Count != moves.Count)
        {
            errors.Add(new("nc", "Независимая проверка восстановила другое число движений."));
        }
        else
        {
            for (var i = 0; i < moves.Count; i++)
            {
                var a = moves[i];
                var b = verification.Moves[i];
                if (a.X != b.X || a.Z != b.Z || a.Mode != b.Mode || a.Line != b.Line || (a.Mode == "G1" && (a.Feed != b.Feed || a.Rpm != b.Rpm)))
                {
                    errors.Add(new("nc", $"Независимая проверка движения N{a.Line} не совпала с расчётной моделью."));
                    break;
                }
            }
        }

        if (Math.Abs(c.TotalCutMinutes - verification.CutMinutes) > 1e-9)
        {
            errors.Add(new("nc", "Время G1 независимой проверки не совпало с расчётной моделью."));
        }

        if (errors.Count > 0)
        {
            return new(false, errors, warnings, null);
        }

        var project = new Project
        {
            Id = id,
            Params = p,
            Calculations = c,
            Operations = operations,
            Transitions = transitions,
            Moves = moves,
            Nc = nc,
            NcFileName = output.FileName,
            ProgramName = output.ProgramName,
            Warnings = warnings,
            Verification = verification
        };
        project = project with
        {
            RouteHtml = HtmlCards.Route(project),
            TechnologyHtml = HtmlCards.Technology(project)
        };
        return new(true, [], warnings, project);
    }
}
