using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Kontur.Core;

/// <summary>Независимая статическая проверка ограниченного подмножества LinuxCNC.</summary>
public static class NcVerifier
{
    public const string Version = "2.1.1";
    public const int MaximumCharacters = 1_000_000;
    public const int MaximumLines = 25_000;
    public const int MaximumErrors = 100;
    private const double Epsilon = .000501;
    private static readonly int[] Preparation = [21, 18, 7, 90, 95, 97, 40, 49, 80, 54];
    private static readonly HashSet<double> AllowedG = [21, 18, 7, 90, 95, 97, 40, 49, 80, 54, 0, 1];
    private static readonly HashSet<double> AllowedM = [2, 3, 5, 6];
    private static readonly HashSet<char> AllowedWords = ['N', 'G', 'M', 'X', 'Z', 'F', 'S', 'T'];
    private static readonly Regex TokenPattern = new(@"([A-Z])([+-]?(?:\d+(?:\.\d*)?|\.\d+))", RegexOptions.CultureInvariant);
    private static bool Integer(double value) => double.IsFinite(value) && value == Math.Floor(value) && Math.Abs(value) <= 9_007_199_254_740_991d;
    private static string Text(double value) => value.ToString("0.###############", CultureInfo.InvariantCulture);

    public static VerificationResult Verify(string? nc, Parameters? parameters)
    {
        var errors = new List<string>();
        var warnings = new List<string>
        {
            "Это статическая проверка ограниченного учебного подмножества LinuxCNC, а не запуск интерпретатора или станочная симуляция.",
            "Проверяется только диапазон координат идеальной точки инструмента. Не проверяются столкновения, съём материала, крепление, форма резца, коррекции, жёсткость и допустимость режимов.",
            "Время учитывает только линейные рабочие перемещения G1 при заданных F и S. Холостые ходы, разгон, смена инструмента, ручные операции и фактическое машинное время не учтены.",
            "Начальное положение станка неизвестно: первый G0 задаёт расчётную исходную точку, но безопасность физического подхода не доказана."
        };
        var moves = new List<ParsedMove>();
        double cutMinutes = 0;
        var clipped = false;
        void Fail(string message) { if (errors.Count < MaximumErrors) errors.Add(message); else clipped = true; }
        VerificationResult Finish()
        {
            if (clipped) warnings.Add($"Показаны только первые {MaximumErrors} ошибок.");
            return new(errors.Count == 0, errors, warnings, moves, cutMinutes);
        }
        if (string.IsNullOrWhiteSpace(nc)) { Fail("Управляющая программа должна быть непустой строкой."); return Finish(); }
        if (nc.Length > MaximumCharacters) { Fail($"Программа превышает предел учебной проверки: {MaximumCharacters} символов."); return Finish(); }
        if (parameters is null) { Fail("Для проверки границ нужны конечные числовые параметры."); return Finish(); }
        var p = parameters;
        foreach (var (key, value) in new[]
        {
            ("length", p.Length), ("trimAllowance", p.TrimAllowance), ("stockDiameter", p.StockDiameter),
            ("clearanceX", p.ClearanceX), ("clearanceZ", p.ClearanceZ),
            ("lengthLowerDeviation", p.LengthLowerDeviation), ("lengthUpperDeviation", p.LengthUpperDeviation)
        })
            if (!double.IsFinite(value)) Fail("Для проверки границ нужен конечный числовой параметр " + key + ".");
        if (errors.Count != 0) return Finish();
        if (p.Length <= 0 || p.TrimAllowance < 0 || p.StockDiameter <= 0 || p.ClearanceX <= 0 || p.ClearanceZ <= 0 ||
            p.Length + p.LengthLowerDeviation <= 0 || p.LengthLowerDeviation > p.LengthUpperDeviation)
        { Fail("Длина, диаметр заготовки и зазоры должны быть положительными, припуск торца — неотрицательным; поле допуска длины должно быть упорядочено."); return Finish(); }
        if (!double.IsFinite(p.RpmMax) || p.RpmMax <= 0) { Fail("Ограничение rpmMax должно быть положительным конечным числом."); return Finish(); }
        // Граница рассчитана заново из параметров: модели переходов здесь нет.
        double targetLength;
        try
        {
            targetLength = (double)Math.Round((decimal)p.Length + ((decimal)p.LengthLowerDeviation + (decimal)p.LengthUpperDeviation) / 2m,
                3, MidpointRounding.AwayFromZero);
        }
        catch (OverflowException)
        {
            Fail("Размеры превышают числовой диапазон проверки.");
            return Finish();
        }
        var maxX = p.StockDiameter + 2 * p.ClearanceX;
        var minZ = -Math.Round(targetLength + p.TrimAllowance, 3, MidpointRounding.AwayFromZero);
        var maxZ = p.ClearanceZ;
        if (!double.IsFinite(maxX) || !double.IsFinite(minZ) || !double.IsFinite(maxZ)) { Fail("Переполнение при расчёте границ координат."); return Finish(); }
        var lines = nc.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        if (lines.Length > MaximumLines) { Fail($"Программа превышает предел {MaximumLines} строк."); return Finish(); }
        var preparation = new HashSet<int>();
        var lastN = -1;
        double? selectedTool = null, tool = null, rpm = null, feed = null, xState = null, zState = null;
        var spindle = false; var ended = false; var stopSinceStart = false; var afterToolChange = false; var percentCount = 0;
        for (var index = 0; index < lines.Length; index++)
        {
            var original = lines[index];
            if (string.IsNullOrWhiteSpace(original)) continue;
            var physicalLine = index + 1;
            var label = $"Строка {physicalLine}: ";
            if (original.Any(c => c != '\t' && (c < 32 || c > 126))) { Fail(label + "в учебной УП допустимы только ASCII-символы."); continue; }
            // Только невложенные комментарии (...) в пределах одной строки.
            var depth = 0; var commentError = false; var builder = new StringBuilder();
            foreach (var ch in original)
            {
                if (ch == '(') { if (depth != 0) commentError = true; depth++; }
                else if (ch == ')') { if (depth == 0) commentError = true; else depth--; }
                else if (depth == 0) builder.Append(ch);
            }
            if (depth != 0 || commentError) { Fail(label + "незакрытый или вложенный комментарий."); continue; }
            var code = builder.ToString().Trim().ToUpperInvariant();
            if (code.Length == 0) continue;
            if (code == "%")
            {
                if (percentCount == 0 && lastN == -1) percentCount = 1;
                else if (percentCount == 1 && ended) percentCount = 2;
                else Fail(label + "символ % допустим только парой вокруг всей программы.");
                continue;
            }
            if (ended) { Fail(label + "исполняемые команды после M2 недопустимы."); continue; }
            var tokens = new List<(char Word, double Value)>();
            var consumed = 0; var badLexeme = false;
            foreach (Match match in TokenPattern.Matches(code))
            {
                if (!string.IsNullOrWhiteSpace(code[consumed..match.Index])) badLexeme = true;
                if (!double.TryParse(match.Groups[2].Value, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
                    CultureInfo.InvariantCulture, out var value)) value = double.NaN;
                tokens.Add((match.Groups[1].Value[0], value)); consumed = match.Index + match.Length;
            }
            if (!string.IsNullOrWhiteSpace(code[consumed..])) badLexeme = true;
            if (tokens.Count == 0 || badLexeme) { Fail(label + "неподдерживаемый синтаксис или нечисловое значение."); continue; }
            var words = new Dictionary<char, double>();
            var gs = new List<double>(); var ms = new List<double>(); var invalid = false;
            if (tokens[0].Word != 'N') { Fail(label + "номер N должен стоять первым словом кадра."); invalid = true; }
            foreach (var token in tokens)
            {
                if (!AllowedWords.Contains(token.Word)) { Fail(label + "слово " + token.Word + " не входит в учебное подмножество."); invalid = true; }
                if (!double.IsFinite(token.Value)) { Fail(label + "неконечное числовое значение."); invalid = true; }
                if (token.Word == 'G') gs.Add(token.Value);
                else if (token.Word == 'M') ms.Add(token.Value);
                else
                {
                    if (!words.TryAdd(token.Word, token.Value)) { Fail(label + "слово " + token.Word + " повторено в кадре."); invalid = true; }
                }
            }
            if (!words.TryGetValue('N', out var n) || !Integer(n) || n < 0 || n > int.MaxValue)
            { Fail(label + "нужен целый неотрицательный номер N в диапазоне Int32."); invalid = true; }
            else
            {
                label = $"Кадр N{Text(n)} (строка {physicalLine}): ";
                if (n <= lastN) { Fail(label + "номера N должны строго возрастать и не повторяться."); invalid = true; }
                lastN = (int)n;
            }
            foreach (var g in gs) if (!AllowedG.Contains(g)) { Fail(label + "G" + Text(g) + " не поддерживается."); invalid = true; }
            foreach (var m in ms) if (!AllowedM.Contains(m)) { Fail(label + "M" + Text(m) + " не поддерживается."); invalid = true; }
            if (gs.Distinct().Count() != gs.Count || ms.Distinct().Count() != ms.Count)
            { Fail(label + "одинаковая G- или M-команда повторена."); invalid = true; }
            var motions = gs.Where(g => g == 0 || g == 1).ToList();
            if (motions.Count > 1 || (gs.Contains(80) && motions.Count != 0)) { Fail(label + "конфликт команд группы движения."); invalid = true; }
            if (ms.Contains(3) && ms.Contains(5)) { Fail(label + "M3 и M5 конфликтуют в одном кадре."); invalid = true; }
            if (motions.Count != 0 && (ms.Contains(6) || ms.Contains(2)))
            { Fail(label + "движение нельзя совмещать со сменой инструмента или завершением программы."); invalid = true; }
            if (invalid) continue;
            foreach (var g in gs) if (Preparation.Contains((int)g)) preparation.Add((int)g);
            if (words.TryGetValue('F', out var f)) { if (f <= 0) Fail(label + "подача F должна быть больше нуля."); feed = f; }
            if (words.TryGetValue('S', out var s))
            {
                if (!Integer(s) || s <= 0) Fail(label + "S должен быть положительным целым числом оборотов в минуту.");
                if (s > p.RpmMax) Fail(label + "S превышает установленное ограничение rpmMax."); rpm = s;
            }
            if (words.TryGetValue('T', out var t))
            { if (t != 1 && t != 2) Fail(label + "поддерживаются только учебные инструменты T1 и T2."); selectedTool = t; }
            if (ms.Contains(5)) { spindle = false; stopSinceStart = true; }
            if (ms.Contains(6))
            {
                if (spindle) Fail(label + "перед M6 требуется M5.");
                if (selectedTool != 1 && selectedTool != 2) Fail(label + "M6 требует выбранного T1 или T2.");
                tool = selectedTool; afterToolChange = true; xState = null; zState = null;
            }
            if (ms.Contains(3))
            { if (!(rpm > 0)) Fail(label + "перед M3 нужны положительные обороты S."); spindle = true; stopSinceStart = false; }
            if (motions.Count == 0 && (words.ContainsKey('X') || words.ContainsKey('Z'))) Fail(label + "координаты требуют явного G0 или G1.");
            if (motions.Count != 0)
            {
                var mode = motions[0] == 0 ? "G0" : "G1";
                var missing = Preparation.Where(g => !preparation.Contains(g)).ToList();
                if (missing.Count != 0) Fail(label + "до движения требуются " + string.Join(' ', missing.Select(g => "G" + g)) + ".");
                if (!words.TryGetValue('X', out var x) || !words.TryGetValue('Z', out var z))
                { Fail(label + "каждое движение должно явно задавать X и Z."); continue; }
                if (x < -Epsilon || x > maxX + Epsilon) Fail(label + "X вне учебного диапазона от 0 до " + Text(maxX) + " мм по диаметру.");
                if (z < minZ - Epsilon || z > maxZ + Epsilon) Fail(label + "Z вне учебного диапазона от " + Text(minZ) + " до " + Text(maxZ) + " мм.");
                if (tool != 1 && tool != 2) Fail(label + "до движения требуется загрузка T1 или T2 командой M6.");
                if (moves.Count == 0)
                {
                    if (mode != "G0") Fail(label + "первое движение должно быть G0.");
                    if (Math.Abs(x - maxX) > Epsilon || Math.Abs(z - maxZ) > Epsilon)
                        Fail(label + "первая расчётная точка должна быть X=" + Text(maxX) + ", Z=" + Text(maxZ) + ".");
                }
                if (afterToolChange && mode != "G0") Fail(label + "после M6 требуется G0 с явно заданной исходной точкой.");
                if (mode == "G1")
                {
                    if (!spindle) Fail(label + "рабочее перемещение G1 требует M3.");
                    if (!(rpm > 0) || !Integer(rpm.Value)) Fail(label + "G1 требует положительных целых оборотов S.");
                    if (!(feed > 0)) Fail(label + "G1 требует положительной подачи F.");
                    if (!words.ContainsKey('F')) Fail(label + "в учебном профиле каждый G1 явно задаёт F.");
                    if (xState is { } oldX && zState is { } oldZ && rpm > 0 && feed > 0)
                    {
                        var distance = Math.Sqrt(Math.Pow((x - oldX) / 2, 2) + Math.Pow(z - oldZ, 2));
                        var minutes = distance / (feed.Value * rpm.Value);
                        if (!double.IsFinite(minutes)) Fail(label + "переполнение расчёта времени."); else cutMinutes += minutes;
                    }
                }
                var toolNumber = tool is { } toolValue && Integer(toolValue) && toolValue >= int.MinValue && toolValue <= int.MaxValue ? (int?)toolValue : null;
                moves.Add(new(x, z, mode, (int)n, feed, rpm, physicalLine, toolNumber));
                xState = x; zState = z; afterToolChange = false;
            }
            if (ms.Contains(2))
            { if (spindle || !stopSinceStart) Fail(label + "перед M2 требуется явный M5 после последнего M3."); ended = true; }
        }
        if (moves.Count == 0) Fail("Программа не содержит поддерживаемых перемещений.");
        if (percentCount == 1) Fail("Открывающий % требует закрывающего % после M2.");
        if (!ended) Fail("Программа должна завершаться M2.");
        if (!double.IsFinite(cutMinutes)) Fail("Итоговое время не является конечным числом.");
        return Finish();
    }
}
