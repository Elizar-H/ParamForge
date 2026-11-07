#include "postprocessor.h"
#include <stdexcept>

namespace pf
{
Program postprocess(const Plan& plan, const Calculations& c, const QString& controller)
{
    const bool siemens = controller == "sinumerik840dsl";
    Program p;
    p.setup = plan.setup;
    p.setupId = plan.setup.id;
    p.name = "PF_" + plan.id.mid(2) + "_" + plan.setup.id;
    p.fileName = siemens ? p.name + ".MPF" : "УП_" + plan.setup.id + ".nc";
    QStringList lines = siemens ? QStringList{} : QStringList{"% " + p.name};
    auto comment = [&](const QString& text) { lines.append(siemens ? "; " + text : "(" + text + ")"); };
    comment("PARAMFORGE " + QString(Version) + " - " + plan.id);
    comment("NOT VERIFIED ON A MACHINE");
    comment((siemens ? QString("SINUMERIK 840D SL") : QString("AXIOMA CONTROL")) + " - MM - X DIAMETER - OPERATION " +
            plan.setup.id);
    comment("TOOL OFFSETS: VIRTUAL SHARP TIP - NOSE R0.4");
    if (plan.setup.flipped)
    {
        comment("MANUAL PARTING AND RECLAMP BEFORE THIS PROGRAM");
        comment("G54 Z0 = FINISHED BACK FACE; RAW FACE Z+" + number(plan.setup.stockFrontZ));
    }
    else
        comment("TARGETS D " + number(c.targetFlangeDiameter) + " d " + number(c.targetBoreDiameter) + " d2 " +
                number(c.targetCounterboreDiameter) + " L " + number(c.targetLength) + " h " +
                number(c.targetCounterboreDepth));
    p.transitions = plan.transitions;
    for (auto& t : p.transitions)
        t.blocks.clear();
    int n = 0;
    for (const auto& i : plan.instructions)
    {
        QString code;
        switch (i.kind)
        {
        case InstructionKind::Comment:
            comment(i.text);
            continue;
        case InstructionKind::Prepare:
            code = siemens ? "G710 G18 G90 G97 G40 G54" : "G21 G18 G90 G97 G40 G49 G80 G54";
            break;
        case InstructionKind::Diameter:
            code = siemens ? "DIAMON" : "G10 X1";
            break;
        case InstructionKind::CoolantOff:
            code = "M9";
            break;
        case InstructionKind::Stop:
            code = "M5";
            break;
        case InstructionKind::Tool:
            code = "T" + QString::number(i.tool) + " D" + QString::number(i.corrector) + (siemens ? "" : " M66");
            break;
        case InstructionKind::GeometryOffset:
            if (siemens)
                continue;
            code = "G43";
            break;
        case InstructionKind::CoolantOn:
            code = "M8";
            break;
        case InstructionKind::ApproachFront:
            code = "G0 Z" + number(i.value);
            break;
        case InstructionKind::Speed:
            code = "S" + number(i.value) + " M3";
            break;
        case InstructionKind::FeedPerRevolution:
            code = "G95";
            break;
        case InstructionKind::FeedPerMinute:
            code = "G94";
            break;
        case InstructionKind::End:
            code = "M30";
            break;
        case InstructionKind::Motion:
            code = i.motion.mode + " X" + number(i.motion.x) + " Z" + number(i.motion.z) +
                   (i.motion.feed ? " F" + number(*i.motion.feed) : "");
            break;
        }
        n += 10;
        lines.append("N" + QString::number(n) + " " + code);
        if (siemens && i.kind == InstructionKind::Prepare && plan.blank)
        {
            const auto& blank = *plan.blank;
            // Bits 6, 7 and 8: rear Z, machining Z and inner diameter are absolute.
            lines.append("WORKPIECE(,,,\"PIPE\",448," + number(blank.frontZ) + "," + number(blank.rearZ) + "," +
                         number(blank.jawsZ) + "," + number(blank.outerDiameter) + "," + number(blank.innerDiameter) +
                         ")");
        }
        if (i.kind == InstructionKind::Motion)
        {
            auto m = i.motion;
            m.line = n;
            p.moves.append(m);
            for (auto& t : p.transitions)
                if (t.id == m.transition)
                {
                    t.blocks.append(n);
                    break;
                }
        }
    }
    p.text = lines.join("\r\n") + "\r\n";
    return p;
}
} // namespace pf
