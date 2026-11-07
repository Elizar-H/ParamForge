#include "core/tooling.h"
#include "projects.h"
#include <QBuffer>
#include <QMap>
#include <QSet>
#include <QXmlStreamWriter>
#include <algorithm>
#include <cmath>
#include <ctime>
#include <miniz.h>
#include <numbers>
#include <stdexcept>
#ifdef Q_OS_WIN
#include <windows.h>
#endif

namespace pf
{
namespace
{
struct Cell
{
    double width;
    QString text;
    bool center = false;
    int font = 18;
    QString merge;
    bool frame = false, noBorders = false, fit = false;
};
struct Row
{
    double height;
    QVector<Cell> cells;
    bool header = false;
};
struct Table
{
    QVector<Row> rows;
    bool visible = true;
};
struct Page
{
    QString standard, form;
    Table top, card;
};
int mm(double value)
{
    return static_cast<int>(std::round(value * 1440 / 25.4));
}
void row(Table& table, double height, const QVector<Cell>& cells, bool header = false)
{
    double total = 0;
    for (const auto& c : cells)
        total += c.width;
    if (std::abs(total - 286) > .001)
        throw std::runtime_error("Неверная ширина граф карты.");
    table.rows.append({height, cells, header});
}
void textRow(Table& table, int& n, const QString& prefix, const QString& text)
{
    row(table, 8.5, {{13, prefix + QString::number(n++).rightJustified(2, '0'), true}, {273, text}});
}
void columns(Table& table, const QVector<double>& widths, const QStringList& texts, double height = 4.25,
             bool header = true, int font = 14, int fittedColumn = -1)
{
    QVector<Cell> cells;
    for (qsizetype i = 0; i < widths.size(); ++i)
        cells.append({widths[i], texts.value(i), true, font, {}, false, false, i == fittedColumn});
    row(table, height, cells, header);
}
void address(Table& t, const Project& p, const QString& operation = {})
{
    const QStringList labels = {"Разраб.", "Проверил", "Нормировал", "", "Н. контр."};
    const QStringList names = {p.parameters.s("developer"), p.parameters.s("checker"), p.parameters.s("rateSetter"), "",
                               p.parameters.s("normController")};
    for (int i = 0; i < 5; ++i)
    {
        QVector<Cell> cells = {
            {23.4, labels[i], false, 14}, {36.4, names[i], false, 14, {}, false, false, true}, {20.8}, {15.6}};
        const QString merge = (i == 0 || i == 3) ? "restart" : "continue";
        if (i < 3)
            cells.append(QVector<Cell>{{41.6, i == 0 ? p.parameters.s("organization") : QString(), true, 14, merge},
                                       {59.8, i == 0 ? p.parameters.s("drawingNumber") : QString(), true, 16, merge},
                                       {41.6, {}, false, 18, merge},
                                       {46.8, i == 0 ? p.id : QString(), true, 16, merge}});
        else
            cells.append(QVector<Cell>{{145.6, i == 3 ? p.parameters.s("partName") : QString(), true, 20, merge},
                                       {10.4, {}, false, 18, merge},
                                       {10.4, {}, false, 18, merge},
                                       {10.4, {}, false, 18, merge},
                                       {13, i == 3 ? operation : QString(), true, 16, merge}});
        row(t, 4.25, cells, true);
    }
}
Page page(const QString& standard, const QString& form, int index, int count, const Project& p, bool continued = false,
          const QString& operation = {})
{
    Page result;
    result.standard = standard;
    result.form = form;
    result.top.visible = false;
    for (int r = 0; r < 3; ++r)
    {
        QVector<Cell> cells;
        if (r == 1)
            cells = {{18.2, "Взам.", false, 14, {}, true}, {57.2, {}, false, 18, {}, true}};
        else
            cells = {{18.2, r == 0 ? "Дубл." : "Подл.", false, 14, {}, true},
                     {20.8, {}, false, 18, {}, true},
                     {20.8, {}, false, 18, {}, true},
                     {15.6, {}, false, 18, {}, true}};
        cells.append({49.4, {}, false, 18, {}, false, true});
        if (r == 0)
            cells.append({80.6, {}, false, 18, {}, false, true});
        else
            for (auto w : {10.4, 10.4, 23.4, 20.8, 15.6})
                cells.append({w, {}, false, 18, {}, true});
        for (auto w : {10.4, 10.4, 23.4, 20.8, 15.6})
            cells.append({w, {}, false, 18, {}, true});
        row(result.top, 4.25, cells);
    }
    row(result.card, 4.25,
        {{13}, {119.6}, {122.2}, {15.6, QString::number(count), true, 14}, {15.6, QString::number(index), true, 14}},
        true);
    if (continued)
        row(result.card, 12.75,
            {{166.4},
             {59.8, p.parameters.s("drawingNumber"), true, 16},
             {46.8, p.id, true, 16},
             {13, operation, true, 16}},
            true);
    else
        address(result.card, p, operation);
    return result;
}
void bottom(Table& t, const QString& code, const QString& title, int page)
{
    row(t, 8.5, {{23.4, code, true, 22}, {247, title, false, 20}, {15.6, QString::number(page), true, 18}});
}
void val(QXmlStreamWriter& w, const QString& tag, const QString& value)
{
    w.writeEmptyElement("w:" + tag);
    w.writeAttribute("w:val", value);
}
int textScale(const QString& text, int font, int width)
{
    if (text.isEmpty())
        return 100;
#ifdef Q_OS_WIN
    // Measure at 1440 units/inch: half-points and cell widths share twip units.
    const auto dc = CreateCompatibleDC(nullptr);
    const auto face = CreateFontW(-font * 10, 0, 0, 0, FW_NORMAL, FALSE, FALSE, FALSE, RUSSIAN_CHARSET, OUT_TT_PRECIS,
                                  CLIP_DEFAULT_PRECIS, DEFAULT_QUALITY, DEFAULT_PITCH | FF_ROMAN, L"Times New Roman");
    SIZE size{};
    bool measured = false;
    if (dc && face)
    {
        const auto previous = SelectObject(dc, face);
        measured = GetTextExtentPoint32W(dc, reinterpret_cast<LPCWSTR>(text.utf16()), int(text.size()), &size);
        SelectObject(dc, previous);
    }
    if (face)
        DeleteObject(face);
    if (dc)
        DeleteDC(dc);
    if (!measured)
        throw std::runtime_error("Не удалось измерить шрифт карты.");
    return size.cx <= width ? 100 : std::max(1, int(100LL * width / size.cx));
#else
    Q_UNUSED(font);
    Q_UNUSED(width);
    throw std::runtime_error("Подгонка текста карт поддерживается в Windows.");
#endif
}
void paragraph(QXmlStreamWriter& w, const QString& text, int font = 18, bool center = false, bool pageBreak = false,
               int scale = 100, bool right = false, int lineHeight = 0)
{
    w.writeStartElement("w:p");
    w.writeStartElement("w:pPr");
    if (pageBreak)
        w.writeEmptyElement("w:pageBreakBefore");
    w.writeEmptyElement("w:spacing");
    w.writeAttribute("w:before", "0");
    w.writeAttribute("w:after", "0");
    w.writeAttribute("w:line", QString::number(lineHeight > 0 ? lineHeight : font * 12));
    w.writeAttribute("w:lineRule", "exact");
    val(w, "jc", right ? "right" : center ? "center" : "left");
    w.writeStartElement("w:rPr");
    val(w, "sz", QString::number(font));
    val(w, "szCs", QString::number(font));
    w.writeEndElement();
    w.writeEndElement();
    w.writeStartElement("w:r");
    w.writeStartElement("w:rPr");
    val(w, "color", "000000");
    val(w, "sz", QString::number(font));
    val(w, "szCs", QString::number(font));
    if (scale < 100)
        val(w, "w", QString::number(scale));
    w.writeEndElement();
    const auto lines = text.split('\n');
    for (qsizetype i = 0; i < lines.size(); ++i)
    {
        if (i)
            w.writeEmptyElement("w:br");
        w.writeStartElement("w:t");
        w.writeAttribute("xml:space", "preserve");
        w.writeCharacters(lines[i]);
        w.writeEndElement();
    }
    w.writeEndElement();
    w.writeEndElement();
}
void table(QXmlStreamWriter& w, const Table& t)
{
    QSet<int> boundarySet{0, 2860};
    for (const auto& r : t.rows)
    {
        int x = 0;
        for (const auto& c : r.cells)
        {
            x += qRound(c.width * 10);
            boundarySet.insert(x);
        }
    }
    QList<int> points = boundarySet.values();
    std::sort(points.begin(), points.end());
    if (points.size() - 1 > 63)
        throw std::runtime_error("Сетка карты превышает 63 столбца Word.");
    QMap<int, int> indices;
    for (int i = 0; i < points.size(); ++i)
        indices[points[i]] = i;
    w.writeStartElement("w:tbl");
    w.writeStartElement("w:tblPr");
    w.writeEmptyElement("w:tblW");
    w.writeAttribute("w:w", "16214");
    w.writeAttribute("w:type", "dxa");
    w.writeEmptyElement("w:tblInd");
    w.writeAttribute("w:w", "0");
    w.writeAttribute("w:type", "dxa");
    w.writeStartElement("w:tblBorders");
    for (const auto& side : {"top", "left", "bottom", "right", "insideH", "insideV"})
    {
        w.writeEmptyElement(QString("w:") + side);
        w.writeAttribute("w:val", t.visible ? "single" : "nil");
        w.writeAttribute("w:sz", QString(side).startsWith("inside") ? "4" : "8");
        w.writeAttribute("w:color", "000000");
    }
    w.writeEndElement();
    w.writeEmptyElement("w:tblLayout");
    w.writeAttribute("w:type", "fixed");
    w.writeStartElement("w:tblCellMar");
    for (const auto& side : {"top", "left", "bottom", "right"})
    {
        w.writeEmptyElement(QString("w:") + side);
        w.writeAttribute("w:w", QString(side) == "left" || QString(side) == "right" ? "45" : "0");
        w.writeAttribute("w:type", "dxa");
    }
    w.writeEndElement();
    w.writeEndElement();
    w.writeStartElement("w:tblGrid");
    for (int i = 1; i < points.size(); ++i)
    {
        w.writeEmptyElement("w:gridCol");
        w.writeAttribute("w:w",
                         QString::number(qRound(points[i] * 16214. / 2860) - qRound(points[i - 1] * 16214. / 2860)));
    }
    w.writeEndElement();
    for (const auto& r : t.rows)
    {
        w.writeStartElement("w:tr");
        w.writeStartElement("w:trPr");
        w.writeEmptyElement("w:cantSplit");
        w.writeEmptyElement("w:trHeight");
        w.writeAttribute("w:val", QString::number(mm(r.height)));
        w.writeAttribute("w:hRule", "exact");
        if (r.header)
            w.writeEmptyElement("w:tblHeader");
        w.writeEndElement();
        int x = 0;
        for (const auto& c : r.cells)
        {
            const int end = x + qRound(c.width * 10);
            w.writeStartElement("w:tc");
            w.writeStartElement("w:tcPr");
            w.writeEmptyElement("w:tcW");
            w.writeAttribute("w:w", QString::number(qRound(end * 16214. / 2860) - qRound(x * 16214. / 2860)));
            w.writeAttribute("w:type", "dxa");
            val(w, "gridSpan", QString::number(indices[end] - indices[x]));
            if (!c.merge.isEmpty())
                val(w, "vMerge", c.merge);
            if (c.frame || c.noBorders)
            {
                w.writeStartElement("w:tcBorders");
                for (const auto& side : {"top", "left", "bottom", "right"})
                {
                    w.writeEmptyElement(QString("w:") + side);
                    w.writeAttribute("w:val", c.frame ? "single" : "nil");
                    w.writeAttribute("w:sz", "4");
                    w.writeAttribute("w:color", "000000");
                }
                w.writeEndElement();
            }
            val(w, "vAlign", "center");
            w.writeEndElement();
            const int fitWidth = mm(c.width) - 110;
            paragraph(w, c.text, c.font, c.center, false, c.fit ? textScale(c.text, c.font, fitWidth) : 100);
            w.writeEndElement();
            x = end;
        }
        w.writeEndElement();
    }
    w.writeEndElement();
}
QByteArray package(const Project& project, const QString& title, const QVector<Page>& pages)
{
    QByteArray document;
    QXmlStreamWriter w(&document);
    w.writeStartDocument();
    w.writeStartElement("w:document");
    w.writeNamespace("http://schemas.openxmlformats.org/wordprocessingml/2006/main", "w");
    w.writeStartElement("w:body");
    for (qsizetype i = 0; i < pages.size(); ++i)
    {
        const auto& p = pages[i];
        paragraph(w, p.standard + "     Форма " + p.form + " САПР", 14, false, i > 0, 100, true, mm(4.25));
        table(w, p.top);
        table(w, p.card);
    }
    paragraph(w, "", 2);
    w.writeStartElement("w:sectPr");
    w.writeEmptyElement("w:pgSz");
    w.writeAttribute("w:w", "16838");
    w.writeAttribute("w:h", "11906");
    w.writeAttribute("w:orient", "landscape");
    w.writeEmptyElement("w:pgMar");
    for (const auto& side : {"top", "right", "bottom", "left"})
        w.writeAttribute(QString("w:") + side, "312");
    for (const auto& side : {"header", "footer", "gutter"})
        w.writeAttribute(QString("w:") + side, "0");
    w.writeEndElement();
    w.writeEndElement();
    w.writeEndElement();
    w.writeEndDocument();
    const QByteArray types =
        R"(<?xml version="1.0" encoding="UTF-8"?><Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types"><Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/><Default Extension="xml" ContentType="application/xml"/><Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/><Override PartName="/word/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.styles+xml"/><Override PartName="/word/settings.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.settings+xml"/><Override PartName="/docProps/core.xml" ContentType="application/vnd.openxmlformats-package.core-properties+xml"/></Types>)";
    const QByteArray rootRels =
        R"(<?xml version="1.0"?><Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="word/document.xml"/><Relationship Id="rId2" Type="http://schemas.openxmlformats.org/package/2006/relationships/metadata/core-properties" Target="docProps/core.xml"/></Relationships>)";
    const QByteArray docRels =
        R"(<?xml version="1.0"?><Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles" Target="styles.xml"/><Relationship Id="rId2" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/settings" Target="settings.xml"/></Relationships>)";
    const QByteArray styles =
        R"(<?xml version="1.0"?><w:styles xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main"><w:docDefaults><w:rPrDefault><w:rPr><w:rFonts w:ascii="Times New Roman" w:hAnsi="Times New Roman" w:cs="Times New Roman"/><w:color w:val="000000"/><w:sz w:val="18"/><w:lang w:val="ru-RU"/></w:rPr></w:rPrDefault></w:docDefaults><w:style w:type="paragraph" w:styleId="Normal" w:default="1"><w:name w:val="Normal"/><w:pPr><w:spacing w:after="0" w:line="216" w:lineRule="exact"/></w:pPr></w:style></w:styles>)";
    const QByteArray settings =
        R"(<?xml version="1.0"?><w:settings xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main"><w:compat><w:compatSetting w:name="compatibilityMode" w:uri="http://schemas.microsoft.com/office/word" w:val="15"/></w:compat></w:settings>)";
    QByteArray properties;
    QXmlStreamWriter prop(&properties);
    prop.writeStartDocument();
    prop.writeStartElement("cp:coreProperties");
    prop.writeNamespace("http://schemas.openxmlformats.org/package/2006/metadata/core-properties", "cp");
    prop.writeNamespace("http://purl.org/dc/elements/1.1/", "dc");
    prop.writeTextElement("dc:title", title);
    prop.writeTextElement("dc:creator", "ParamForge");
    prop.writeTextElement("dc:description", "Технологическая документация. Ревизия " + project.id);
    prop.writeEndElement();
    prop.writeEndDocument();
    mz_zip_archive zip{};
    if (!mz_zip_writer_init_heap(&zip, 0, 0))
        throw std::runtime_error("Не удалось создать Word-карту.");
    const QVector<std::pair<QByteArray, QByteArray>> entries = {
        {"[Content_Types].xml", types},   {"_rels/.rels", rootRels},       {"word/document.xml", document},
        {"word/styles.xml", styles},      {"word/settings.xml", settings}, {"word/_rels/document.xml.rels", docRels},
        {"docProps/core.xml", properties}};
    std::tm archiveCalendar{};
    archiveCalendar.tm_year = 100;
    archiveCalendar.tm_mday = 1;
    archiveCalendar.tm_isdst = -1;
    MZ_TIME_T archiveDate = std::mktime(&archiveCalendar);
    for (const auto& e : entries)
        if (!mz_zip_writer_add_mem_ex_v2(&zip, e.first.constData(), e.second.constData(),
                                         static_cast<size_t>(e.second.size()), nullptr, 0, MZ_BEST_COMPRESSION, 0, 0,
                                         &archiveDate, nullptr, 0, nullptr, 0))
        {
            mz_zip_writer_end(&zip);
            throw std::runtime_error("Ошибка упаковки Word-карты.");
        }
    void* memory = nullptr;
    size_t size = 0;
    if (!mz_zip_writer_finalize_heap_archive(&zip, &memory, &size))
    {
        mz_zip_writer_end(&zip);
        throw std::runtime_error("Ошибка завершения Word-карты.");
    }
    QByteArray bytes(static_cast<const char*>(memory), static_cast<qsizetype>(size));
    mz_free(memory);
    mz_zip_writer_end(&zip);
    return bytes;
}
QString deviationText(const Parameters& p, const Dimension& d)
{
    const auto lo = p.n(d.lower), hi = p.n(d.upper);
    const auto sign = [](double v) { return (v > 0 ? QString("+") : QString()) + localized(v); };
    const auto designation = p.s(d.designation);
    return field(d.nominal).label + " " + localized(p.n(d.nominal)) +
           (designation.isEmpty() ? QString() : " " + designation) + " (" + sign(lo) + "/" + sign(hi) + ")";
}
QString inspectionTools(const Parameters& p)
{
    const auto low = p.n("flangeDiameter") + p.n("flangeLowerDeviation"),
               high = p.n("flangeDiameter") + p.n("flangeUpperDeviation");
    const int first = static_cast<int>(std::floor(low / 25)) * 25,
              last = std::max(first, (static_cast<int>(std::ceil(high / 25)) - 1) * 25);
    QStringList micrometers, bores;
    for (int start = first; start <= last; start += 25)
        micrometers.append(QString::number(start) + "–" + QString::number(start + 25));
    for (const auto& range : QVector<std::pair<double, double>>{{18, 50}, {50, 100}, {100, 160}, {160, 250}})
    {
        bool required = false;
        for (const auto& d : dimensions())
            if (d.key == "boreDiameter" || d.key == "counterboreDiameter")
            {
                const auto lo = p.n(d.nominal) + p.n(d.lower), hi = p.n(d.nominal) + p.n(d.upper);
                if ((lo < range.second && hi > range.first) || (lo == hi && lo >= range.first && lo <= range.second))
                    required = true;
            }
        if (required)
            bores.append(localized(range.first) + "–" + localized(range.second));
    }
    const auto caliperMaximum =
        std::max(p.n("length") + p.n("lengthUpperDeviation"), p.n("bodyDiameter") + p.n("bodyUpperDeviation"));
    const int caliper = caliperMaximum <= 150 ? 150 : caliperMaximum <= 200 ? 200 : 300;
    return "Микрометр " + micrometers.join(" / ") + "; нутромеры " + bores.join(" / ") + "; штангенциркуль 0–" +
           QString::number(caliper) + "; глубиномер 0–150.";
}
const QVector<double> regimeWidths = {13, 109.2, 18.2, 28.6, 23.4, 13, 15.6, 26, 18.2, 20.8};
} // namespace
QByteArray routeCard(const Project& project)
{
    QVector<Page> pages;
    const auto& p = project.parameters;
    const auto& c = project.calculations;
    const QVector<double> aw = {13, 10.4, 10.4, 10.4, 13, 75.4, 153.4},
                          bw = {13, 119.6, 10.4, 18.2, 10.4, 13, 10.4, 13, 13, 13, 13, 18.2, 20.8};
    QVector<Table> bodies;
    Table body;
    for (const auto& op : project.operations)
    {
        Table card;
        int n = 1;
        const auto program = op.code == "010"   ? &project.programs[0]
                             : op.code == "020" ? &project.programs[1]
                                                : nullptr;
        columns(card, aw,
                {"А" + QString::number(n++).rightJustified(2, '0'), "", "", "", op.code, op.title,
                 program ? "ОК " + op.code + "; УП " + program->name : QString()},
                8.5, false, 18);
        columns(card, bw,
                {"Б" + QString::number(n++).rightJustified(2, '0'),
                 program ? "Токарный с ЧПУ " +
                               QString(p.s("controller") == "axioma" ? "AxiOMA Control" : "SINUMERIK 840D sl")
                 : op.code == "005" ? "Токарный, наладочный режим"
                                    : "Контрольное место"},
                8.5, false, 18);
        if (op.code == "005")
            textRow(card, n, "О",
                    "Принять полый полуфабрикат Ø" + localized(p.n("stockDiameter")) + "/Ø" +
                        localized(p.n("stockBore")) + " × " + localized(c.rawStockLength) +
                        "; подрезать первый торец на " + localized(p.n("frontFacingAllowance")) + " до " +
                        localized(c.stockLength) + " мм.");
        if (op.code == "010")
        {
            textRow(card, n, "О",
                    "Вылет " + localized(c.stockOverhang) + " мм; кулачки Z" + localized(c.jawsFrontZ) +
                        ". Точить буртик, тело и отверстия до Z" + localized(c.partingFrontZ) + ". УП " +
                        program->fileName + ".");
            textRow(card, n, "Т",
                    "T" + number(p.n("outerToolNumber")) + "/D" + number(p.n("outerCorrector")) + " " +
                        turningToolName(p) + "; тело к буртику: T" + number(p.n("bodyToolNumber")) + "/D" +
                        number(p.n("bodyCorrector")) + " " + bodyToolName(p) + "; T" + number(p.n("boringToolNumber")) +
                        "/D" + number(p.n("boringCorrector")) + " " + boringTool(p).name + "; R0,4. Непрерывная СОЖ.");
        }
        if (op.code == "020")
        {
            textRow(card, n, "О",
                    "Отдельно от УП: отрезать в полосе Z" + localized(c.partingFrontZ) + "…" +
                        localized(c.partingBackZ) + " мм; рез " + localized(c.cutoffWidth) +
                        " мм, коридор до кулачков " + localized(c.chuckClearance) + " мм.");
            textRow(card, n, "О",
                    "Перевернуть деталь. Мягкие кулачки: пояс " + localized(c.secondClampStart) + "…" +
                        localized(c.secondClampEnd) + " мм от первого торца; упор по первому торцу; карман Ø" +
                        localized(c.softJawPocketDiameter) + " × " + localized(c.softJawPocketDepth) + " мм.");
            textRow(card, n, "О",
                    "G54 Z0 — готовый задний торец; припуск Z+" + localized(p.n("trimAllowance")) + " мм. По УП " +
                        program->fileName + " подрезать до L=" + localized(c.targetLength) + " мм; снять заусенцы.");
            textRow(card, n, "Т",
                    "Отрезка: " + partingToolName(p) + ". Торцевание: T" + number(p.n("outerToolNumber")) + "/D" +
                        number(p.n("outerCorrector")) + " " + turningToolName(p) + ", R0,4.");
        }
        if (op.code == "030")
        {
            textRow(card, n, "О",
                    "Контролировать размеры по отклонениям. h и b — от первого торца вне галтелей R≤0,5 мм.");
            textRow(card, n, "Т", inspectionTools(p));
        }
        if (op.code == "030")
        {
            QStringList values;
            for (const auto& d : dimensions())
                values.append(deviationText(p, d));
            textRow(card, n, "О", values.mid(0, 3).join("; ") + " мм.");
            textRow(card, n, "О", values.mid(3).join("; ") + " мм.");
            textRow(card, n, "О",
                    "Цели, мм: D " + localized(c.targetFlangeDiameter) + "; Db " + localized(c.targetBodyDiameter) +
                        "; d " + localized(c.targetBoreDiameter) + "; d2 " + localized(c.targetCounterboreDiameter) +
                        "; L " + localized(c.targetLength) + "; h " + localized(c.targetCounterboreDepth) + "; b " +
                        localized(c.targetFlangeWidth) + ".");
        }
        const auto capacity = bodies.isEmpty() ? 14 : 17;
        if (!body.rows.isEmpty() && body.rows.size() + card.rows.size() > capacity)
        {
            bodies.append(std::move(body));
            body = {};
        }
        body.rows.append(card.rows);
    }
    if (!body.rows.isEmpty())
        bodies.append(std::move(body));
    for (int index = 1; index <= bodies.size(); ++index)
    {
        auto pageData = page("ГОСТ 3.1118-82", index == 1 ? "1" : "1а", index, bodies.size(), project, index > 1);
        auto& card = pageData.card;
        if (index == 1)
        {
            row(card, 8.5,
                {{13, "М01", true},
                 {231.4, "Полый обработанный полуфабрикат / " + p.s("material") + "; ОТ, HB≤197"},
                 {41.6}});
            const QVector<double> widths = {13, 33.8, 10.4, 18.2, 15.6, 18.2, 13, 33.8, 54.6, 15.6, 18.2, 41.6};
            columns(
                card, widths,
                {"", "Код", "ЕВ", "МД", "ЕН", "Н. расх.", "КИМ", "Код загот.", "Профиль и размеры", "КД", "МЗ", ""});
            columns(card, widths,
                    {"М02", "", "", "", "", "", "", "",
                     "Ø" + localized(p.n("stockDiameter")) + "/Ø" + localized(p.n("stockBore")) + " × " +
                         localized(c.rawStockLength)},
                    4.25, true, 18);
        }
        columns(card, aw, {"А", "Цех", "Уч.", "РМ", "Опер.", "Код, наименование операции", "Обозначение документа"});
        columns(card, bw,
                {"Б", "Код, наименование оборудования", "СМ", "Проф.", "Р", "УТ", "КР", "КОИД", "ЕН", "ОП", "Кшт.",
                 "Тпз", "Тшт."});
        int n = index == 1 ? 3 : 1;
        const auto start = n;
        for (auto line : bodies[index - 1].rows)
        {
            line.cells[0].text = line.cells[0].text.left(1) + QString::number(n++).rightJustified(2, '0');
            card.rows.append(std::move(line));
        }
        while (n - start < (index == 1 ? 14 : 17))
            textRow(card, n, "", "");
        bottom(card, "МК", "Маршрутная карта", index);
        pages.append(std::move(pageData));
    }
    return package(project, "Маршрутная карта", pages);
}
QByteArray technologyCard(const Project& project)
{
    QVector<Page> pages;
    const auto& p = project.parameters;
    const auto& c = project.calculations;
    for (int pageIndex = 0; pageIndex < 3; ++pageIndex)
    {
        const auto& program = project.programs[pageIndex == 2 ? 1 : 0];
        const bool continued = pageIndex == 1;
        auto pageData =
            page("ГОСТ 3.1404-86", continued ? "2а" : "3", pageIndex + 1, 3, project, continued, program.setupId);
        auto& card = pageData.card;
        if (!continued)
        {
            const QVector<double> a = {78, 65, 28.6, 10.4, 18.2, 54.6, 18.2, 13},
                                  b = {78, 65, 18.2, 20.8, 18.2, 20.8, 65};
            columns(card, a,
                    {"Наименование операции", "Материал", "Твердость", "ЕВ", "МД", "Профиль и размеры", "МЗ", "КОИД"});
            columns(card, a,
                    {"Токарная с ЧПУ, установ " + QString::number(pageIndex == 2 ? 2 : 1), p.s("material"), "≤197 HB",
                     "", "",
                     "Ø" + localized(pageIndex == 2 ? c.targetBodyDiameter : p.n("stockDiameter")) + " × " +
                         localized(pageIndex == 2 ? c.cutLength : c.stockLength)},
                    4.25, true, 18, 1);
            columns(card, b,
                    {"Оборудование, устройство ЧПУ", "Обозначение программы", "То", "Тв", "Тпз", "Тшт.", "СОЖ"});
            columns(card, b,
                    {p.s("controller") == "axioma" ? "AxiOMA Control" : "SINUMERIK 840D sl", program.fileName, "", "",
                     "", "", "Эмульсия, непрерывно (M8)"},
                    8.5, true, 18);
        }
        columns(card, regimeWidths,
                {"Р", "", "ПИ", "D или B, мм", "L, мм", "t, мм", "i", "S, мм/об", "n, об/мин", "V, м/мин"});
        int n = 1;
        if (pageIndex == 0)
            textRow(card, n, "О",
                    "Закрепить заготовку с вылетом " + localized(c.stockOverhang) + " мм; кулачки Z" +
                        localized(c.jawsFrontZ) + ". G54 Z0 — подготовленный передний торец; X — диаметр.");
        if (pageIndex == 2)
        {
            textRow(card, n, "О",
                    "До запуска УП: отделить деталь с задним припуском " + localized(p.n("trimAllowance")) +
                        " мм, перевернуть и закрепить в мягких кулачках на поясе " + localized(c.secondClampStart) +
                        "…" + localized(c.secondClampEnd) + " мм от первого торца.");
            textRow(card, n, "О",
                    "G54 Z0 — окончательный задний торец; исходный торец Z+" + localized(p.n("trimAllowance")) +
                        " мм. Упор по первому торцу. Осевой припуск снимать до Z0; длина после обработки " +
                        localized(c.targetLength) + " мм.");
        }
        const int begin = pageIndex == 1 ? 4 : 0,
                  end = pageIndex == 0 ? 4 : static_cast<int>(program.transitions.size());
        for (int i = begin; i < end; ++i)
        {
            const auto& t = program.transitions[i];
            const auto description = t.kind == "face" ? t.id + " " + t.title + ". Z " + localized(t.zStart) + " → " +
                                                            localized(t.zEnd) + " мм; поперечная подача к отверстию."
                                                      : t.id + " " + t.title + ". Ø" + localized(t.startDiameter) +
                                                            " → Ø" + localized(t.diameter) + "; Z " +
                                                            localized(t.zStart) + " → " + localized(t.zEnd) + " мм.";
            row(card, 8.5,
                {{13, "О" + QString::number(n++).rightJustified(2, '0'), true}, {234, description}, {18.2}, {20.8}});
            textRow(card, n, "Т",
                    t.tool + " D" +
                        number(p.n(t.kind == "inner"  ? "boringCorrector"
                                   : t.kind == "body" ? "bodyCorrector"
                                                      : "outerCorrector")) +
                        " — " +
                        (t.kind == "inner"  ? boringTool(p).name
                         : t.kind == "body" ? bodyToolName(p)
                                            : turningToolName(p)) +
                        "; R0,4.");
            double stroke = 0;
            for (qsizetype m = 1; m < program.verification.moves.size(); ++m)
            {
                const auto& a = program.verification.moves[m - 1];
                const auto& b = program.verification.moves[m];
                if (b.mode == "G1" && t.blocks.contains(b.line) && a.tool == b.tool)
                    stroke = std::max(stroke, t.kind == "face" ? std::abs(b.x - a.x) / 2 : std::abs(b.z - a.z));
            }
            double depth = t.actualMaximumDepth();
            if (t.kind == "face")
            {
                depth = 0;
                double previous = t.zStart;
                for (auto z : t.passes)
                {
                    depth = std::max(depth, previous - z);
                    previous = z;
                }
            }
            columns(card, regimeWidths,
                    {"Р" + QString::number(n++).rightJustified(2, '0'), t.id, t.tool.mid(1),
                     localized(t.referenceDiameter), localized(stroke), localized(depth),
                     QString::number(t.passes.size()), localized(t.feed), localized(t.rpm),
                     localized(std::numbers::pi * t.referenceDiameter * t.rpm / 1000)},
                    8.5, false, 18);
        }
        if (pageIndex == 1)
        {
            textRow(card, n, "Т",
                    "T" + number(p.n("boringToolNumber")) + ": BD" + localized(boringTool(p).diameter) + ", DMIN" +
                        localized(boringTool(p).minBore) + ", вылет " + localized(p.n("boringOverhang")) +
                        " мм, зажим " + localized(p.n("boringClamp")) + " мм; отвод X" + localized(c.clearIn) +
                        ". T/D отнесены к виртуальной вершине.");
            textRow(card, n, "О",
                    "Галтели R≤0,5 мм. h — от первого торца на плоской части уступа. Контролировать размеры по полям "
                    "допуска, указанным в МК.");
        }
        if (pageIndex == 2)
            textRow(card, n, "О",
                    "После торцевания удалить заусенцы без изменения размеров. Окончательный контроль — операция 030. "
                    "Отрезка и управление зажимом не входят в УП.");
        while (n <= (continued ? 17 : 13))
            textRow(card, n, "", "");
        bottom(card, "ОК", "Операционная карта " + program.setupId, pageIndex + 1);
        pages.append(std::move(pageData));
    }
    return package(project, "Операционные карты 010 и 020", pages);
}
} // namespace pf
