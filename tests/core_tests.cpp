#include "core/model.h"
#include "files/projects.h"
#include <QFile>
#include <QJsonDocument>
#include <QTemporaryDir>
#include <QXmlStreamReader>
#include <QtTest>
#include <algorithm>
#include <cmath>
#include <miniz.h>
#include <random>

class CoreTests final : public QObject
{
    Q_OBJECT
  private Q_SLOTS:
    void defaults()
    {
        const auto g = pf::generate(pf::Parameters());
        QVERIFY2(g.ok(), g.errors.isEmpty() ? "No project" : qPrintable(g.errors.first().message));
        QCOMPARE(g.project->programs.size(), 2);
        QCOMPARE(g.project->calculations.targetLength, 115.5);
        QCOMPARE(g.project->calculations.targetFlangeDiameter, 119.565);
        QCOMPARE(g.project->programs[0].moves.size(), 130);
        QCOMPARE(g.project->programs[0].transitions.size(), 8);
        QVERIFY(std::abs(g.project->programs[0].verification.cutMinutes - 37.61840605536863) < 1e-9);
        for (const auto& program : g.project->programs)
            QVERIFY(program.verification.ok);
    }
    void toolSelectionAndReach()
    {
        for (const auto& controller : {"axioma", "sinumerik840dsl"})
        {
            pf::Parameters p;
            p.set("controller", controller);
            p.set("length", 200);
            QVERIFY(!pf::generate(p).ok());
            p.set("boringTool", "custom");
            p.set("customBoringName", "Длинная оправка");
            p.set("boringOverhang", 210);
            const auto g = pf::generate(p);
            QVERIFY2(g.ok(), g.errors.isEmpty() ? "No project" : qPrintable(g.errors.first().message));
            QVERIFY(
                g.project->programs[0].text.contains("CUSTOM BORING BAR BD32.000 DMIN40.000 OH210.000 CLAMP128.000"));
            QVERIFY(g.project->operations[1].description.contains("Длинная оправка"));
            QTemporaryDir saved;
            const auto path = saved.filePath("custom.json");
            pf::saveParameters(*g.project, path);
            QCOMPARE(pf::importParameters(path).json(), g.project->parameters.json());
            for (const auto& program : g.project->programs)
                QVERIFY(pf::verify(program.text, g.project->parameters, program.setup).ok);
            auto invalid = p;
            invalid.set("boringClamp", 20);
            QVERIFY(!pf::generate(invalid).ok());
            QVERIFY(!pf::verify(g.project->programs[0].text, invalid, g.project->programs[0].setup).ok);
            invalid = p;
            invalid.set("customBoringOffset", 10);
            QVERIFY(!pf::generate(invalid).ok());
            invalid = p;
            invalid.set("customBoringName", "bad\nname");
            QVERIFY(!pf::generate(invalid).ok());
            p.set("boringTool", "a40t");
            p.set("boringOverhang", 140);
            p.set("boringClamp", 160);
            p.set("length", 130);
            QVERIFY(!pf::generate(p).ok()); // This bar does not fit the original hole.
            p.set("boreDiameter", 60);
            p.set("autoStock", true);
            const auto larger = pf::generate(p);
            QVERIFY2(larger.ok(), larger.errors.isEmpty() ? "No project" : qPrintable(larger.errors.first().message));
            QCOMPARE(pf::boreLimits(p).first, 54.);
            QVERIFY(larger.project->programs[0].text.contains("A40T-SCLCR 12"));
            for (const auto& program : larger.project->programs)
                QVERIFY(pf::verify(program.text, larger.project->parameters, program.setup).ok);
            auto tooNarrow = larger.project->parameters;
            tooNarrow.set("stockBore", 53);
            QVERIFY(!pf::verify(larger.project->programs[0].text, tooNarrow, larger.project->programs[0].setup).ok);
        }
    }
    void adjustableCutoff()
    {
        for (const auto& controller : {"axioma", "sinumerik840dsl"})
        {
            pf::Parameters p;
            p.set("controller", controller);
            const auto original = pf::generate(p);
            QVERIFY(original.ok());
            p.set("cutoffWidth", 4.5);
            const auto changed = pf::generate(p);
            QVERIFY(changed.ok());
            const auto& c = changed.project->calculations;
            QCOMPARE(c.cutoffWidth, 4.5);
            QVERIFY(!changed.project->operations[2].description.contains("QD-NG-0300"));
            QVERIFY(changed.project->operations[2].description.contains("4,5"));
            QCOMPARE(c.cutLength, original.project->calculations.cutLength);
            QCOMPARE(c.partingFrontZ - c.partingBackZ, 4.5);
            QCOMPARE(c.stockLength - original.project->calculations.stockLength, 1.5);
            QCOMPARE(c.partingBackZ - c.jawsFrontZ, p.n("chuckClearance"));
            for (const auto& program : changed.project->programs)
                QVERIFY(pf::verify(program.text, changed.project->parameters, program.setup).ok);
            QTemporaryDir saved;
            pf::saveParameters(*changed.project, saved.filePath("cutoff.json"));
            QCOMPARE(pf::importParameters(saved.filePath("cutoff.json")).n("cutoffWidth"), 4.5);
            if (qEnvironmentVariableIsSet("PARAMFORGE_QA_DIR") && QString(controller) == "axioma")
                pf::exportProject(*changed.project,
                                  QDir(qEnvironmentVariable("PARAMFORGE_QA_DIR")).filePath("word-cutoff"));
            p.set("cutoffWidth", 0.1);
            QVERIFY(!pf::generate(p).ok());
        }
    }
    void stockLengthAndOverhang()
    {
        for (const auto& controller : {"axioma", "sinumerik840dsl"})
        {
            pf::Parameters p;
            p.set("controller", controller);
            auto g = pf::generate(p);
            QVERIFY(g.ok());
            QCOMPARE(g.project->calculations.rawStockLength, 157.5);
            QCOMPARE(g.project->calculations.stockOverhang, 132.5);
            p.set("frontFacingAllowance", 4);
            g = pf::generate(p);
            QVERIFY(g.ok());
            QCOMPARE(g.project->calculations.rawStockLength, 159.5);
            QCOMPARE(g.project->calculations.stockLength, 155.5);
            QCOMPARE(g.project->calculations.jawsFrontZ, -130.5);
            p.set("autoBlankLength", false);
            p.set("blankLength", 180);
            p.set("autoBlankOverhang", false);
            p.set("blankOverhang", 140);
            g = pf::generate(p);
            QVERIFY(g.ok());
            const auto& c = g.project->calculations;
            QCOMPARE(c.rawStockLength, 180.);
            QCOMPARE(c.stockLength, 176.);
            QCOMPARE(c.stockOverhang, 140.);
            QCOMPARE(c.gripLength, 40.);
            QCOMPARE(c.chuckClearance, 15.5);
            QCOMPARE(g.project->programs[0].setup.rearZ, -176.);
            QCOMPARE(g.project->programs[0].setup.jawsFrontZ, -136.);
            QCOMPARE(g.project->programs[0].setup.gripLength, 40.);
            QVERIFY(g.project->operations[1].description.contains("40 мм"));
            QTemporaryDir folder;
            pf::saveParameters(*g.project, folder.filePath("stock.json"));
            const auto saved = pf::importParameters(folder.filePath("stock.json"));
            QCOMPARE(saved, g.project->parameters);
            p = saved;
            p.set("length", 117);
            g = pf::generate(p);
            QVERIFY(g.ok());
            QCOMPARE(g.project->calculations.rawStockLength, 180.);
            QCOMPARE(g.project->calculations.stockOverhang, 140.);
            QCOMPARE(g.project->calculations.chuckClearance, 14.5);
            p.set("blankLength", 160);
            const auto shortGrip = pf::generate(p);
            QVERIFY(!shortGrip.ok());
            QVERIFY(std::any_of(shortGrip.errors.begin(), shortGrip.errors.end(),
                                [](const auto& e) { return e.field == "blankLength"; }));
            p.set("blankLength", 180);
            p.set("blankOverhang", 125);
            const auto shortOverhang = pf::generate(p);
            QVERIFY(!shortOverhang.ok());
            QVERIFY(std::any_of(shortOverhang.errors.begin(), shortOverhang.errors.end(),
                                [](const auto& e) { return e.field == "blankOverhang"; }));
        }
        auto legacy = pf::Parameters().json();
        for (const auto& key : {"autoBlankLength", "autoBlankOverhang", "blankLength", "blankOverhang"})
            legacy.remove(key);
        legacy["gripLength"] = 30;
        const auto migrated = pf::generate(pf::Parameters(legacy));
        QVERIFY(migrated.ok());
        QCOMPARE(migrated.project->calculations.rawStockLength, 162.5);
        QCOMPARE(migrated.project->calculations.gripLength, 30.);
    }
    void sinumerikWorkpiece()
    {
        pf::Parameters p;
        p.set("controller", "sinumerik840dsl");
        p.set("autoBlankLength", false);
        p.set("blankLength", 180);
        p.set("autoBlankOverhang", false);
        p.set("blankOverhang", 140);
        const auto g = pf::generate(p);
        QVERIFY(g.ok());
        const auto& first = g.project->programs[0];
        for (const auto& program : g.project->programs)
        {
            QVERIFY(!program.text.contains("%_N_"));
            QVERIFY(!program.text.contains(";$PATH"));
            QVERIFY(program.text.contains("N10 G710 G18 G90 G97 G40 G54\r\n"));
            QVERIFY(program.text.contains("N30 G94\r\n"));
            auto badGroup = program.text;
            badGroup.replace("N10 G710 G18 G90 G97", "N10 G710 G18 G90 G94 G97");
            QVERIFY(!pf::verify(badGroup, g.project->parameters, program.setup).ok);
            badGroup = program.text;
            badGroup.replace("N30 G94", "N30 G94 G95");
            QVERIFY(!pf::verify(badGroup, g.project->parameters, program.setup).ok);
            auto noFeedMode = program.text;
            noFeedMode.remove("N30 G94\r\n");
            QVERIFY(!pf::verify(noFeedMode, g.project->parameters, program.setup).ok);
        }
        const QString blank = "WORKPIECE(,,,\"PIPE\",448,0,-178,-138,124,45)";
        QVERIFY(first.text.contains(blank));
        QVERIFY(first.text.indexOf("G54") < first.text.indexOf(blank));
        QVERIFY(first.text.indexOf(blank) < first.text.indexOf("T1 D1"));
        QVERIFY(!g.project->programs[1].text.contains("WORKPIECE"));
        for (const auto& replacement :
             {QString(), QString("WORKPIECE(,,,\"PIPE\",448,0,-180,-140,124,45)"),
              QString("WORKPIECE(,,,\"PIPE\",192,0,-178,-138,124,45)"),
              QString("WORKPIECE(,,,\"PIPE\",448,0,-178,-138,124,44)"), blank + "\r\n" + blank})
        {
            auto changed = first.text;
            changed.replace(blank, replacement);
            QVERIFY(!pf::verify(changed, g.project->parameters, first.setup).ok);
        }
        auto early = first.text;
        early.replace(blank + "\r\n", "");
        early.replace("N10 G710", blank + "\r\nN10 G710");
        QVERIFY(!pf::verify(early, g.project->parameters, first.setup).ok);
        p.set("controller", "axioma");
        const auto a = pf::generate(p);
        QVERIFY(a.ok());
        QVERIFY(!a.project->programs[0].text.contains("WORKPIECE"));
        QCOMPARE(a.project->calculations.json(), g.project->calculations.json());
        for (int setup = 0; setup < 2; ++setup)
        {
            const auto& am = a.project->programs[setup].verification.moves;
            const auto& sm = g.project->programs[setup].verification.moves;
            QCOMPARE(am.size(), sm.size());
            for (int index = 0; index < am.size(); ++index)
            {
                QCOMPARE(am[index].x, sm[index].x);
                QCOMPARE(am[index].z, sm[index].z);
                QCOMPARE(am[index].mode, sm[index].mode);
            }
        }
    }
    void standards()
    {
        QCOMPARE(pf::toleranceWidth(130, 7), .040);
        QCOMPARE(pf::toleranceWidth(200, 7), .046);
        for (const auto bound : {3., 6., 10., 18., 30., 50., 80., 120., 180.})
            for (int grade = 6; grade <= 16; ++grade)
                QVERIFY(pf::toleranceWidth(bound, grade) <= pf::toleranceWidth(bound + .001, grade));
        auto p = pf::setTolerance(pf::Parameters(), "flangeDiameter", "h7");
        p.set("flangeDiameter", 130);
        p = pf::resolveTolerances(p);
        QCOMPARE(p.n("flangeLowerDeviation"), -.040);
        p.set("flangeDiameter", 200);
        p = pf::resolveTolerances(p);
        QCOMPARE(p.n("flangeLowerDeviation"), -.046);
        p = pf::setTolerance(p, "flangeDiameter", "");
        p.set("flangeDiameter", 150);
        p = pf::resolveTolerances(p);
        QCOMPARE(p.n("flangeLowerDeviation"), -.046);
        const auto js = pf::setTolerance(pf::Parameters(), "boreDiameter", "JS7");
        QCOMPARE(js.n("boreLowerDeviation"), -.0125);
        QCOMPARE(js.n("boreUpperDeviation"), .0125);
        QCOMPARE(pf::target(100, -.001, 0), 100.);
        QVERIFY_THROWS_EXCEPTION(std::exception, pf::toleranceWidth(251, 7));
    }
    void legacyGeneralTolerancesBecomeNumeric()
    {
        QTemporaryDir folder;
        QVERIFY(folder.isValid());
        auto legacy = pf::Parameters().json();
        legacy["bodyDiameter"] = 130;
        legacy["flangeDiameter"] = 150;
        legacy["stockDiameter"] = 154;
        legacy["bodyTolerance"] = "m";
        legacy["bodyLowerDeviation"] = -.1;
        legacy["flangeWidthTolerance"] = "m";
        QFile file(folder.filePath("legacy.json"));
        QVERIFY(file.open(QIODevice::WriteOnly));
        file.write(QJsonDocument(legacy).toJson());
        file.close();
        auto p = pf::importParameters(file.fileName());
        QVERIFY(p.s("bodyTolerance").isEmpty());
        QVERIFY(p.s("flangeWidthTolerance").isEmpty());
        QCOMPARE(p.n("bodyLowerDeviation"), -.5);
        QCOMPARE(p.n("bodyUpperDeviation"), .5);
        QCOMPARE(p.n("flangeWidthLowerDeviation"), -.2);
        p.set("bodyDiameter", 100);
        p.set("flangeWidth", 6);
        p = pf::resolveTolerances(p);
        QCOMPARE(p.n("bodyLowerDeviation"), -.5);
        QCOMPARE(p.n("flangeWidthLowerDeviation"), -.2);
        for (const auto& controller : {"axioma", "sinumerik840dsl"})
        {
            p.set("controller", controller);
            const auto g = pf::generate(p);
            QVERIFY(g.ok());
            const auto saved = folder.filePath(QString(controller) + ".json");
            pf::saveParameters(*g.project, saved);
            QCOMPARE(pf::importParameters(saved), g.project->parameters);
        }
    }
    void invalidParameters()
    {
        for (const auto& f : pf::fields())
        {
            auto p = pf::Parameters();
            for (const auto& d : pf::dimensions())
                if (d.lower == f.key || d.upper == f.key)
                    p.set(d.designation, "");
            p.set(f.key, QJsonValue(QJsonValue::Null));
            QVERIFY2(!pf::generate(p).ok(), qPrintable(f.key));
        }
        auto p = pf::Parameters();
        p.set("rpmMax", 1200.5);
        QVERIFY(!pf::generate(p).ok());
        p = pf::Parameters();
        p.set("partName", QString(1, QChar(1)));
        QVERIFY(!pf::generate(p).ok());
    }
    void independentParser()
    {
        for (const auto& controller : {"axioma", "sinumerik840dsl"})
        {
            auto p = pf::Parameters();
            p.set("controller", controller);
            const auto g = pf::generate(p);
            QVERIFY(g.ok());
            for (const auto& program : g.project->programs)
            {
                for (const auto& mutation : {QString("M8"), QString("M30"), QString("G95"), QString("M3")})
                {
                    auto text = program.text;
                    text.replace(mutation, " ");
                    QVERIFY2(!pf::verify(text, g.project->parameters, program.setup).ok, qPrintable(mutation));
                }
                auto text = program.text;
                text.replace("G1 ", "G2 ");
                QVERIFY(!pf::verify(text, g.project->parameters, program.setup).ok);
                text = program.text + "N999999 G0 X0 Z0\n";
                QVERIFY(!pf::verify(text, g.project->parameters, program.setup).ok);
            }
        }
    }
    void flangeContinuity()
    {
        auto p = pf::Parameters();
        p.set("flangeWidthTolerance", "");
        p.set("flangeWidthLowerDeviation", 0);
        p.set("flangeWidthUpperDeviation", 0);
        p.set("counterboreDepthLowerDeviation", 0);
        p.set("counterboreDepthUpperDeviation", 0);
        p.set("bodyTolerance", "");
        p.set("bodyLowerDeviation", 0);
        p.set("bodyUpperDeviation", 0);
        p.set("counterboreLowerDeviation", 0);
        p.set("counterboreUpperDeviation", 0);
        p.set("counterboreDepth", 12);
        for (const auto diameter : {110., 100., 99., 94.001})
        {
            p.set("counterboreDiameter", diameter);
            QVERIFY2(!pf::generate(p).ok(), qPrintable(QString::number(diameter)));
        }
        p.set("counterboreDiameter", 94);
        QVERIFY(pf::generate(p).ok());
        p.set("counterboreDiameter", 99);
        p.set("counterboreDepth", 11.999);
        QVERIFY(pf::generate(p).ok());
        p.set("counterboreDepth", 12.001);
        QVERIFY(!pf::generate(p).ok());
    }
    void decimalBoundaries()
    {
        auto p = pf::Parameters();
        p.set("flangeWidth", 2.002);
        p.set("flangeWidthTolerance", "");
        p.set("flangeWidthLowerDeviation", 0);
        p.set("flangeWidthUpperDeviation", .0005);
        auto g = pf::generate(p);
        QVERIFY(g.ok());
        QCOMPARE(g.project->calculations.secondClampStart, 4.003);
        QCOMPARE(g.project->calculations.secondClampEnd, 29.003);
        QCOMPARE(g.project->calculations.softJawPocketDepth, 3.003);
        p = pf::Parameters();
        p.set("autoStock", true);
        p.set("boreDiameter", 65);
        p.set("boreLowerDeviation", -.998);
        p.set("boreUpperDeviation", -.998);
        p.set("finishAllowance", .451);
        g = pf::generate(p);
        QVERIFY(g.ok());
        QCOMPARE(g.project->parameters.n("stockBore"), 63.);
        p = pf::Parameters();
        p.set("trimAllowance", 2.3);
        p.set("depthOutside", .7);
        g = pf::generate(p);
        QVERIFY(g.ok());
        const auto passZ = g.project->programs[1].transitions[0].passes;
        QCOMPARE(passZ, QVector<double>({1.6, .9, .2}));
    }
    void secondSetup()
    {
        const auto g = pf::generate(pf::Parameters());
        QVERIFY(g.ok());
        const auto& p = g.project->programs[1];
        QCOMPARE(p.setup.stockFrontZ, 2.);
        QCOMPARE(p.setup.gripLength, 25.);
        QVERIFY(p.setup.jawsFrontZ < -5);
        const auto& finish = p.transitions.last();
        QCOMPARE(finish.zEnd, 0.);
        bool finished = false;
        for (const auto& move : p.verification.moves)
        {
            QVERIFY(move.z >= -.000001);
            if (move.mode == "G1" && move.z == 0)
                finished = true;
        }
        QVERIFY(finished);
    }
    void files()
    {
        QTemporaryDir temporary;
        QVERIFY(temporary.isValid());
        const auto g = pf::generate(pf::Parameters());
        QVERIFY(g.ok());
        const auto path = temporary.filePath("параметры.json");
        pf::saveParameters(*g.project, path);
        QCOMPARE(pf::importParameters(path), g.project->parameters);
        for (const auto& invalid : {QByteArray("{\"length\":100,\"length\":101}"), QByteArray("{\"unknown\":1}"),
                                    QByteArray("{\"autoStock\":1}"), QByteArray("{\"params\":null}"),
                                    QByteArray("{\"material\":\"\\u0001\"}")})
        {
            QFile file(path);
            QVERIFY(file.open(QIODevice::WriteOnly));
            file.write(invalid);
            file.close();
            QVERIFY_THROWS_EXCEPTION(std::exception, pf::importParameters(path));
        }
        const auto files = pf::exportProject(*g.project, temporary.filePath("cards"));
        QCOMPARE(files.size(), 4);
        for (const auto& name : files)
            QVERIFY(QFileInfo(name).size() > 100);
        QFile unrelated(temporary.filePath("cards/заметки.txt"));
        QVERIFY(unrelated.open(QIODevice::WriteOnly));
        unrelated.write("keep");
        unrelated.close();
        const auto repeated = pf::exportProject(*g.project, temporary.filePath("cards"));
        QCOMPARE(repeated.size(), 4);
        QVERIFY(QFileInfo(repeated.first()).absolutePath() != QFileInfo(files.first()).absolutePath());
        for (int i = 0; i < files.size(); ++i)
        {
            QFile first(files[i]), second(repeated[i]);
            QVERIFY(first.open(QIODevice::ReadOnly));
            QVERIFY(second.open(QIODevice::ReadOnly));
            QCOMPARE(first.readAll(), second.readAll());
        }
        QVERIFY(unrelated.open(QIODevice::ReadOnly));
        QCOMPARE(unrelated.readAll(), QByteArray("keep"));
        auto changed = *g.project;
        changed.programs[0].text.replace("M8", "M9");
        QVERIFY_THROWS_EXCEPTION(std::exception, pf::exportProject(changed, temporary.filePath("changed")));
        const auto qa = qEnvironmentVariable("PARAMFORGE_QA_DIR");
        if (!qa.isEmpty())
        {
            const auto exportFolder = QDir(qa).filePath("cards");
            if (!QDir(exportFolder).exists())
                pf::exportProject(*g.project, exportFolder);
        }
    }
    void legacyOracle()
    {
        const auto requested = qEnvironmentVariable("PARAMFORGE_REFERENCE_FILE");
        const auto path = requested.isEmpty() ? QFINDTESTDATA("data/default-reference.json") : requested;
        QFile file(path);
        QVERIFY(file.open(QIODevice::ReadOnly));
        const auto cases = QJsonDocument::fromJson(file.readAll()).array();
        QVERIFY(!cases.isEmpty());
        int valid = 0;
        for (const auto& item : cases)
        {
            const auto data = item.toObject();
            const auto name = data["name"].toString();
            const auto g = pf::generate(pf::Parameters(data["input"].toObject()));
            QStringList errors;
            for (const auto& e : g.errors)
                errors.append(e.field + ": " + e.message);
            QVERIFY2(g.ok() == data["ok"].toBool(), qPrintable(name + "\n" + errors.join('\n')));
            if (!g.ok())
            {
                QStringList actualFields, expectedFields;
                for (const auto& e : g.errors)
                    actualFields.append(e.field);
                actualFields.removeDuplicates();
                actualFields.sort();
                for (const auto& e : data["errors"].toArray())
                    expectedFields.append(e.toString());
                QCOMPARE(actualFields, expectedFields);
                continue;
            }
            ++valid;
            const auto expected = data["result"].toObject();
            QVERIFY2(g.project->parameters.json() ==
                         pf::resolveStock(pf::Parameters(expected["params"].toObject())).json(),
                     qPrintable(name + " parameters"));
            const auto calculations = g.project->calculations.json(),
                       oldCalculations = expected["calculations"].toObject();
            for (auto i = oldCalculations.begin(); i != oldCalculations.end(); ++i)
                if (i.key() != "totalCutMinutes")
                    QVERIFY2(std::abs(i->toDouble() - calculations[i.key()].toDouble()) < 1e-9,
                             qPrintable(name + " calculation " + i.key()));
            const auto& first = g.project->programs[0];
            const auto oldMoves = expected["moves"].toArray();
            QCOMPARE(first.moves.size(), oldMoves.size());
            for (qsizetype i = 0; i < oldMoves.size(); ++i)
            {
                const auto m = oldMoves[i].toObject();
                const auto& actual = first.moves[i];
                QVERIFY2(std::abs(actual.x - m["x"].toDouble()) < 1e-9 &&
                             std::abs(actual.z - m["z"].toDouble()) < 1e-9 && actual.mode == m["mode"].toString() &&
                             actual.line == m["line"].toInt() + 10,
                         qPrintable(name + " move " + QString::number(i)));
            }
            QVERIFY2(std::abs(first.verification.cutMinutes - oldCalculations["totalCutMinutes"].toDouble()) < 1e-9,
                     qPrintable(name + " G1 time"));
        }
        qInfo() << "Migration comparison:" << cases.size() << "cases," << valid << "valid projects";
    }
    void sweepBothControllers()
    {
        std::mt19937 random(6120);
        for (int i = 0; i < 400; ++i)
        {
            pf::Parameters p;
            p.set("autoStock", true);
            p.set("length", 70 + int(random() % 45));
            p.set("flangeDiameter", 119 + int(random() % 41));
            p.set("bodyDiameter", 95 + int(random() % 16));
            p.set("boreDiameter", 46 + int(random() % 9));
            p.set("counterboreDiameter", 65 + int(random() % 20));
            p.set("flangeWidth", 8 + int(random() % 9));
            p.set("counterboreDepth", 18 + int(random() % 18));
            const auto a = pf::generate(p);
            QVERIFY2(a.ok(), a.errors.isEmpty() ? "No result" : qPrintable(a.errors.first().message));
            p.set("controller", "sinumerik840dsl");
            const auto b = pf::generate(p);
            QVERIFY(b.ok());
            QCOMPARE(a.project->calculations.json(), b.project->calculations.json());
            QCOMPARE(a.project->operations.size(), b.project->operations.size());
            for (qsizetype operation = 0; operation < a.project->operations.size(); ++operation)
            {
                QCOMPARE(a.project->operations[operation].code, b.project->operations[operation].code);
                QCOMPARE(a.project->operations[operation].description, b.project->operations[operation].description);
            }
            for (int setup = 0; setup < 2; ++setup)
            {
                const auto& am = a.project->programs[setup].verification.moves;
                const auto& bm = b.project->programs[setup].verification.moves;
                QCOMPARE(am.size(), bm.size());
                for (qsizetype m = 0; m < am.size(); ++m)
                {
                    QCOMPARE(am[m].x, bm[m].x);
                    QCOMPARE(am[m].z, bm[m].z);
                    QCOMPARE(am[m].mode, bm[m].mode);
                }
            }
        }
    }
    void documentForms()
    {
        QVector<std::pair<QString, pf::Parameters>> variants;
        variants.append({"default", pf::Parameters()});
        auto p = pf::Parameters();
        p.set("controller", "sinumerik840dsl");
        variants.append({"sinumerik", p});
        p.set("autoBlankLength", false);
        p.set("autoBlankOverhang", false);
        p.set("blankLength", 180);
        p.set("blankOverhang", 140);
        variants.append({"manual-stock", p});
        p = pf::Parameters();
        p.set("length", 60);
        p.set("counterboreDepth", 20);
        p.set("flangeWidth", 8);
        variants.append({"short", p});
        p = pf::setTolerance(pf::Parameters(), "boreDiameter", "H7");
        p = pf::setTolerance(p, "flangeDiameter", "js8");
        variants.append({"standards", p});
        p = pf::Parameters();
        p.set("partName", QString(45, QChar(0x0428)));
        p.set("material", QString(35, QChar(0x0429)));
        for (const auto& entry : pf::schema()["cardFields"].toArray())
        {
            const auto f = entry.toObject();
            p.set(f["key"].toString(), QString(f["maximum"].toInt(), QChar(0x0428)));
        }
        variants.append({"wide-metadata", p});
        p = pf::Parameters();
        p.set("material", QString(35, 'i'));
        for (const auto& entry : pf::schema()["cardFields"].toArray())
        {
            const auto f = entry.toObject();
            p.set(f["key"].toString(), QString(f["maximum"].toInt(), 'i'));
        }
        variants.append({"narrow-metadata", p});
        p = pf::Parameters();
        p.set("autoStock", true);
        p.set("flangeDiameter", 200);
        p.set("bodyDiameter", 180);
        p.set("boreDiameter", 135);
        p.set("counterboreDiameter", 160);
        variants.append({"wide-part", p});
        p = pf::Parameters();
        p.set("length", 200);
        p.set("boringTool", "custom");
        p.set("boringOverhang", 210);
        p.set("customBoringName", QString(35, QChar(0x0428)));
        variants.append({"custom-tool", p});
        for (const auto& variant : variants)
        {
            const auto g = pf::generate(variant.second);
            QVERIFY2(g.ok(), qPrintable(variant.first));
            const auto route = pf::routeCard(*g.project), technology = pf::technologyCard(*g.project);
            QCOMPARE(route, pf::routeCard(*g.project));
            QCOMPARE(technology, pf::technologyCard(*g.project));
            for (const auto& bytes : {route, technology})
            {
                mz_zip_archive zip{};
                QVERIFY(mz_zip_reader_init_mem(&zip, bytes.constData(), static_cast<size_t>(bytes.size()), 0));
                QCOMPARE(mz_zip_reader_get_num_files(&zip), 7u);
                size_t length = 0;
                auto memory = mz_zip_reader_extract_file_to_heap(&zip, "word/document.xml", &length, 0);
                QVERIFY(memory);
                const QByteArray xml(static_cast<const char*>(memory), static_cast<qsizetype>(length));
                mz_free(memory);
                mz_zip_reader_end(&zip);
                QXmlStreamReader reader(xml);
                int columns = 0;
                bool landscape = false;
                bool fittedRun = false;
                int fullMaterialRuns = 0;
                while (!reader.atEnd())
                {
                    reader.readNext();
                    if (!reader.isStartElement())
                        continue;
                    if (reader.name() == u"r")
                        fittedRun = false;
                    QVERIFY(reader.name() != u"fitText");
                    if (reader.name() == u"w")
                    {
                        const int scale = reader.attributes().value("w:val").toInt();
                        QVERIFY(scale > 0 && scale < 100);
                        QVERIFY(variant.first != "default" && variant.first != "narrow-metadata");
                        fittedRun = true;
                    }
                    if (reader.name() == u"t" && variant.first == "wide-metadata" && bytes == technology)
                    {
                        if (reader.readElementText() == variant.second.s("material"))
                        {
                            QVERIFY(fittedRun);
                            ++fullMaterialRuns;
                        }
                        continue;
                    }
                    if (reader.name() == u"tblGrid")
                        columns = 0;
                    if (reader.name() == u"gridCol")
                    {
                        ++columns;
                        QVERIFY(columns <= 63);
                    }
                    if (reader.name() == u"pgSz")
                        landscape = reader.attributes().value("w:orient") == u"landscape";
                }
                QVERIFY(!reader.hasError());
                QVERIFY(landscape);
                QVERIFY(xml.contains(g.project->id.toUtf8()));
                QVERIFY(xml.contains("020"));
                if (variant.first == "wide-metadata" && bytes == technology)
                    QCOMPARE(fullMaterialRuns, 2);
            }
            const auto qa = qEnvironmentVariable("PARAMFORGE_QA_DIR");
            if (!qa.isEmpty())
            {
                const auto folder = QDir(qa).filePath("word-" + variant.first);
                if (!QDir(folder).exists())
                    pf::exportProject(*g.project, folder);
            }
        }
    }
};
QTEST_GUILESS_MAIN(CoreTests)
#include "core_tests.moc"
