#include "model.h"
#include <QMap>
#include <QRegularExpression>
#include <QSet>
#include <cmath>
#include <limits>

namespace pf
{
Verification verify(const QString& text, const Parameters& p, const Setup& setup)
{
    Verification result;
    auto fail = [&](const QString& message)
    {
        if (result.errors.size() < 100)
            result.errors.append(message);
    };
    const auto finish = [&]
    {
        result.ok = result.errors.isEmpty();
        return result;
    };
    const auto toolErrors = validateToolSetup(p);
    if (!toolErrors.isEmpty())
    {
        fail(toolErrors.first().message);
        return finish();
    }
    if (!setup.flipped && calculate(p).cutLength > p.n("boringOverhang") - 5 + 1e-8)
    {
        fail("Глубина обработки превышает доступный вылет расточной оправки.");
        return finish();
    }
    if (!setup.flipped && (p.n("stockBore") < boringTool(p).minBore || calculate(p).clearIn < boringTool(p).minBore))
    {
        fail("Отверстие с учётом отвода меньше допустимого для выбранной оправки.");
        return finish();
    }
    if (text.trimmed().isEmpty() || text.size() > 1000000)
    {
        fail("Пустая или слишком большая УП.");
        return finish();
    }
    for (const auto& f : fields())
        if (!std::isfinite(p.n(f.key)) || std::abs(p.n(f.key)) > 10000)
        {
            fail("Для проверки нужны конечные параметры в рабочем диапазоне.");
            return finish();
        }
    const bool siemens = p.s("controller") == "sinumerik840dsl";
    const double maxX = setup.flipped ? calculate(p).targetBodyDiameter + 2 * p.n("clearanceX")
                                      : p.n("stockDiameter") + 2 * p.n("clearanceX");
    const double minZ = setup.flipped ? 0 : -calculate(p).cutLength;
    const double maxZ = p.n("clearanceZ") + (setup.flipped ? p.n("trimAllowance") : 0);
    const auto lines = QString(text).replace("\r\n", "\n").replace('\r', '\n').split('\n');
    if (lines.size() > 25000)
    {
        fail("УП содержит более 25000 строк.");
        return finish();
    }
    const QSet<int> required = siemens ? QSet<int>{710, 18, 90, 97, 40, 54} : QSet<int>{21, 18, 90, 97, 40, 49, 80, 54};
    const QSet<int> allowedG = siemens ? QSet<int>{710, 18, 90, 94, 95, 97, 40, 54, 0, 1}
                                       : QSet<int>{21, 18, 10, 90, 94, 95, 97, 40, 43, 49, 80, 54, 0, 1};
    const QSet<int> allowedM = siemens ? QSet<int>{3, 5, 8, 9, 30} : QSet<int>{3, 5, 6, 8, 9, 30, 66};
    QSet<int> preparation;
    int lastN = -1, tool = 0, corrector = 0, feedMode = 0;
    std::optional<double> rpm, feed, xState, zState;
    bool spindle = false, coolant = false, coolantOff = false, stopped = false, ended = false, header = false,
         diameter = false, compensation = false, afterTool = false, blankDefined = false;
    static const QRegularExpression tokenPattern("([A-Z])([+-]?(?:\\d+(?:\\.\\d*)?|\\.\\d+))");
    static const QRegularExpression siemensHeader("^%_N_PF_[0-9A-F]{16}(?:_(?:010|020))?_MPF$");
    const auto integer = [](double value)
    { return std::isfinite(value) && value == std::floor(value) && std::abs(value) <= 2147483647.; };
    for (qsizetype index = 0; index < lines.size(); ++index)
    {
        const auto original = lines[index];
        if (original.trimmed().isEmpty())
            continue;
        const auto label = "Строка " + QString::number(index + 1) + ": ";
        bool ascii = true;
        for (const auto ch : original)
            if (ch != '\t' && (ch.unicode() < 32 || ch.unicode() > 126))
            {
                ascii = false;
                break;
            }
        if (!ascii)
        {
            fail(label + "В УП допустимы только ASCII-символы.");
            continue;
        }
        const auto trimmed = original.trimmed();
        if (trimmed.startsWith('%'))
        {
            if ((siemens && !siemensHeader.match(trimmed).hasMatch()) || header || lastN != -1 || ended ||
                trimmed.mid(1).trimmed().isEmpty())
                fail(label + "Некорректный заголовок программы.");
            else
                header = true;
            continue;
        }
        if (trimmed.startsWith("WORKPIECE", Qt::CaseInsensitive))
        {
            static const QRegularExpression blankPattern("^WORKPIECE\\((.*)\\)$",
                                                         QRegularExpression::CaseInsensitiveOption);
            const auto match = blankPattern.match(trimmed);
            const auto args = match.captured(1).split(',');
            bool valid = siemens && !setup.flipped && !ended && !blankDefined && tool == 0 && result.moves.isEmpty() &&
                         preparation.contains(54) && match.hasMatch() && args.size() == 10;
            if (valid)
            {
                valid = args[0].trimmed().isEmpty() && args[1].trimmed().isEmpty() && args[2].trimmed().isEmpty() &&
                        args[3].trimmed().toUpper() == "\"PIPE\"" && args[4].trimmed() == "448";
                const auto c = calculate(p);
                const QVector<double> expected = {0, -c.stockLength, c.jawsFrontZ, p.n("stockDiameter"),
                                                  p.n("stockBore")};
                for (int arg = 0; arg < expected.size(); ++arg)
                {
                    bool converted = false;
                    const auto value = args[arg + 5].trimmed().toDouble(&converted);
                    valid &= converted && std::isfinite(value) && std::abs(value - expected[arg]) < 1e-8;
                }
            }
            if (!valid)
                fail(label +
                     "WORKPIECE должен задавать полую заготовку 010 после G54, с расчётными размерами и вылетом.");
            blankDefined = true;
            continue;
        }
        QString code;
        int depth = 0;
        bool commentError = false;
        for (qsizetype i = 0; i < original.size(); ++i)
        {
            const auto ch = original[i];
            if (depth == 0 && (ch == ';' || (ch == '/' && i + 1 < original.size() && original[i + 1] == '/')))
                break;
            if (ch == '(')
            {
                if (depth != 0)
                    commentError = true;
                ++depth;
            }
            else if (ch == ')')
            {
                if (depth == 0)
                    commentError = true;
                else
                    --depth;
            }
            else if (depth == 0)
                code.append(ch);
        }
        if (depth || commentError)
        {
            fail(label + "Незакрытый или вложенный комментарий.");
            continue;
        }
        code = code.trimmed().toUpper();
        if (code.isEmpty())
            continue;
        if (!header && !siemens)
            fail(label + "Нет заголовка программы.");
        if (ended)
        {
            fail(label + "Команды после M30 недопустимы.");
            continue;
        }
        const bool diameterCommand = siemens && code.endsWith(" DIAMON");
        if (diameterCommand)
        {
            code.chop(7);
            code = code.trimmed();
        }
        QMap<QChar, double> words;
        QVector<int> gs, ms;
        bool invalid = false;
        int consumed = 0, tokenCount = 0;
        auto matches = tokenPattern.globalMatch(code);
        while (matches.hasNext())
        {
            const auto match = matches.next();
            if (!code.mid(consumed, match.capturedStart() - consumed).trimmed().isEmpty())
                invalid = true;
            consumed = static_cast<int>(match.capturedEnd());
            const auto key = match.captured(1)[0];
            bool converted = false;
            const auto value = match.captured(2).toDouble(&converted);
            if (tokenCount++ == 0 && key != 'N')
                invalid = true;
            if (!converted || !std::isfinite(value) || !QString("NGMXZFSTD").contains(key))
            {
                invalid = true;
                continue;
            }
            if (key == 'G' || key == 'M')
            {
                if (!integer(value))
                {
                    invalid = true;
                    continue;
                }
                const auto command = static_cast<int>(value);
                auto& list = key == 'G' ? gs : ms;
                if (list.contains(command) || !(key == 'G' ? allowedG : allowedM).contains(command))
                    invalid = true;
                list.append(command);
            }
            else
            {
                if (words.contains(key))
                    invalid = true;
                words.insert(key, value);
            }
        }
        if (!tokenCount || !code.mid(consumed).trimmed().isEmpty())
            invalid = true;
        const auto n = words.value('N', -1);
        if (!integer(n) || n < 0 || n <= lastN)
            invalid = true;
        else
            lastN = static_cast<int>(n);
        const bool motion = gs.contains(0) || gs.contains(1);
        const bool toolChange =
            siemens ? (words.contains('T') || words.contains('D')) : (ms.contains(6) || ms.contains(66));
        const int spindleFeedGroup = int(gs.contains(94)) + int(gs.contains(95)) + int(gs.contains(97));
        if ((siemens && spindleFeedGroup > 1) || (gs.contains(0) && gs.contains(1)) || (gs.contains(80) && motion) ||
            (gs.contains(94) && gs.contains(95)) || (gs.contains(43) && gs.contains(49)) || ms.size() > 1)
            invalid = true;
        if ((ms.contains(8) || ms.contains(9)) && (!gs.isEmpty() || words.size() != 1))
            invalid = true;
        if (motion && (toolChange || ms.contains(30)))
            invalid = true;
        if (invalid)
        {
            fail(label + "Неподдерживаемые, повторённые или конфликтующие команды.");
            continue;
        }
        if (diameterCommand)
        {
            if (!gs.isEmpty() || !ms.isEmpty() || words.size() != 1)
                fail(label + "DIAMON должен быть отдельным кадром.");
            else
                diameter = true;
            continue;
        }
        if (gs.contains(10))
        {
            if (gs.size() != 1 || !ms.isEmpty() || words.size() != 2 || words.value('X', 0) != 1)
                fail(label + "Требуется отдельный G10 X1.");
            else
                diameter = true;
            continue;
        }
        for (const auto g : gs)
            if (required.contains(g))
                preparation.insert(g);
        if (gs.contains(94) || gs.contains(95))
        {
            const int next = gs.contains(95) ? 95 : 94;
            if (feedMode != next)
                feed.reset();
            feedMode = next;
            if (next == 95 && (!rpm || *rpm <= 0 || !spindle))
                fail(label + "Перед G95 нужны S > 0 и M3.");
        }
        if (words.contains('F'))
        {
            feed = words['F'];
            if (*feed <= 0)
                fail(label + "Подача F должна быть положительной.");
        }
        if (words.contains('S'))
        {
            rpm = words['S'];
            if (!integer(*rpm) || *rpm <= 0 || *rpm > p.n("rpmMax"))
                fail(label + "Некорректные обороты S.");
        }
        if ((words.contains('T') || words.contains('D')) && !toolChange)
            fail(label + "T/D задаются при смене инструмента.");
        if (ms.contains(8))
        {
            coolant = true;
            coolantOff = false;
        }
        if (ms.contains(9))
        {
            coolant = false;
            coolantOff = true;
        }
        if (ms.contains(5))
        {
            if (feedMode != 94)
                fail(label + "Перед M5 требуется G94.");
            spindle = false;
            stopped = true;
        }
        if (toolChange)
        {
            if (spindle || !stopped || feedMode != 94 || coolant || !coolantOff)
                fail(label + "Смена инструмента требует G94, M9 и M5.");
            if ((words.value('T', 0) != 1 && words.value('T', 0) != 2) || words.value('D', 0) != 1)
                fail(label + "Требуется T1/T2 и D1.");
            tool = static_cast<int>(words.value('T', 0));
            corrector = static_cast<int>(words.value('D', 0));
            compensation = siemens;
            afterTool = true;
            xState.reset();
            zState.reset();
        }
        if (gs.contains(49))
            compensation = false;
        if (gs.contains(43))
        {
            if ((tool != 1 && tool != 2) || corrector != 1)
                fail(label + "G43 требует выбранного инструмента и D1.");
            compensation = true;
        }
        if (ms.contains(3))
        {
            if (!rpm || *rpm <= 0)
                fail(label + "M3 требует S > 0.");
            spindle = true;
            stopped = false;
        }
        if (!motion && (words.contains('X') || words.contains('Z')))
            fail(label + "Координаты требуют явного G0/G1.");
        if (motion)
        {
            if (!preparation.contains(required) || !diameter)
                fail(label + "Не заданы подготовительные команды и диаметральный режим.");
            if (!words.contains('X') || !words.contains('Z'))
            {
                fail(label + "Каждое движение задаёт X и Z.");
                continue;
            }
            const auto x = words['X'], z = words['Z'];
            const QString mode = gs.contains(0) ? "G0" : "G1";
            if (x < -.000501 || x > maxX + .000501 || z < minZ - .000501 || z > maxZ + .000501)
                fail(label + "Координаты выходят за границы установа.");
            if ((tool != 1 && tool != 2) || !compensation)
                fail(label + "До движения нужен инструмент с корректором.");
            if (result.moves.isEmpty() &&
                (mode != "G0" || std::abs(x - maxX) > .000501 || std::abs(z - maxZ) > .000501))
                fail(label + "Неверная первая расчётная точка.");
            if (afterTool && mode != "G0")
                fail(label + "После смены инструмента требуется G0.");
            if (mode == "G1")
            {
                if (!spindle || !coolant || feedMode != 95 || !rpm || !feed || *rpm <= 0 || *feed <= 0 ||
                    !integer(*rpm) || !words.contains('F'))
                    fail(label + "G1 требует M3, M8, G95, целые S > 0 и явную F > 0.");
                if (xState && zState && rpm && feed && *rpm > 0 && *feed > 0)
                    result.cutMinutes += std::hypot((x - *xState) / 2, z - *zState) / (*feed * *rpm);
            }
            result.moves.append({x, z, mode, static_cast<int>(n), static_cast<int>(index + 1), tool, feed, rpm});
            xState = x;
            zState = z;
            afterTool = false;
        }
        if (ms.contains(30))
        {
            if (spindle || !stopped || feedMode != 94 || coolant || !coolantOff)
                fail(label + "Перед M30 требуются G94, M9 и M5.");
            ended = true;
        }
    }
    if (!header && !siemens)
        fail("Нет заголовка программы.");
    if (siemens && !setup.flipped && !blankDefined)
        fail("Нет определения полой заготовки WORKPIECE для 010.");
    if (result.moves.isEmpty())
        fail("Нет поддерживаемых перемещений.");
    if (!ended)
        fail("Нет завершающей команды M30.");
    if (!std::isfinite(result.cutMinutes))
        fail("Неконечное время G1.");
    return finish();
}
} // namespace pf
