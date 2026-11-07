#include "process_view.h"
#include "core/engine.h"
#include "core/geometry.h"
#include "core/tooling.h"
#include "drawing.h"
#include "numeric_edit.h"
#include "theme.h"
#include <QFormLayout>
#include <QKeyEvent>
#include <QLabel>
#include <QMouseEvent>
#include <QPainterPath>
#include <QScrollArea>
#include <QSignalBlocker>
#include <QSplitter>
#include <QVBoxLayout>
#include <cmath>
#include <numbers>

namespace pf::ui
{
ProcessDiagram::ProcessDiagram(QWidget* parent) : QWidget(parent)
{
    setObjectName("processDiagram");
    setMinimumSize(300, 300);
    setSizePolicy(QSizePolicy::Expanding, QSizePolicy::Expanding);
}
void ProcessDiagram::setParameters(const Parameters& p)
{
    if (!drawableSetup(p))
        return;
    parameters = p;
    update();
}
void ProcessDiagram::setSetup(int index)
{
    if (setupIndex != index)
        viewport.reset();
    setupIndex = index;
    update();
}
void ProcessDiagram::highlight(const QString& key)
{
    focusedKey = key;
    update();
}
void ProcessDiagram::paintEvent(QPaintEvent*)
{
    QPainter painter(this);
    painter.fillRect(rect(), paper());
    const auto c = calculate(parameters);
    const Setup setup = setupIndex == 0 ? Setup{"010",
                                                "Первый установ",
                                                false,
                                                0,
                                                c.jawsFrontZ,
                                                c.gripLength,
                                                parameters.n("stockDiameter"),
                                                -c.stockLength}
                                        : Setup{"020",
                                                "Второй установ",
                                                true,
                                                parameters.n("trimAllowance"),
                                                -c.targetLength + c.secondClampEnd,
                                                25,
                                                c.targetBodyDiameter,
                                                -c.targetLength};
    const auto bounds = QRectF(rect().adjusted(8, 8, -8, -8));
    if (!viewport)
        viewport = fitSetup(bounds, parameters, setup, false, true);
    else
    {
        const auto radius = std::max(parameters.n("stockDiameter"), parameters.n("flangeDiameter")) / 2;
        const auto left = std::min(-c.stockLength - 14, -c.targetLength - parameters.n("trimAllowance") - 12);
        const auto right = std::max(20., parameters.n("frontFacingAllowance") + 12);
        viewport->zDirection = setup.flipped ? -1 : 1;
        viewport->zOffset = setup.flipped ? -c.targetLength : 0;
        const auto room = std::max(1., std::min(viewport->origin.y() - bounds.top() - SetupTopMargin,
                                                bounds.bottom() - SetupBottomMargin - viewport->origin.y()));
        const auto nextScale =
            std::max(.1, std::min({viewport->scale, (bounds.width() - 170) / (right - left), room / (radius + 15)}));
        if (nextScale < viewport->scale - 1e-8)
            viewport->scale = nextScale;
        viewport->origin.setX(bounds.center().x() - 25 - (left + right) / 2 * viewport->scale);
    }
    drawSetup(painter, rect().adjusted(8, 8, -8, -8), parameters, setup, focusedKey, false, viewport);
}
void ProcessDiagram::resizeEvent(QResizeEvent* e)
{
    viewport.reset();
    QWidget::resizeEvent(e);
}
ProcessView::ProcessView(QWidget* parent) : QWidget(parent)
{
    auto layout = new QHBoxLayout(this);
    layout->setContentsMargins(0, 0, 0, 0);
    layout->setSpacing(6);
    sections = new QListWidget(this);
    sections->setObjectName("processSections");
    sections->addItems({"Подготовка", "Инструмент", "Режимы", "Реквизиты"});
    sections->setFixedWidth(128);
    sections->setItemDelegate(new ItemDelegate(sections));
    sections->installEventFilter(this);
    layout->addWidget(sections);
    pages = new QStackedWidget(this);
    pages->setObjectName("processParameters");
    pages->installEventFilter(this);
    pages->setFixedWidth(320);
    layout->addWidget(pages);
    const QVector<QStringList> keys = {
        {"stockDiameter", "stockBore", "blankLength", "blankOverhang", "trimAllowance", "frontFacingAllowance"},
        {"outerTool",
         "customOuterName",
         "outerToolNumber",
         "outerCorrector",
         "bodyTool",
         "customBodyName",
         "bodyToolNumber",
         "bodyCorrector",
         "boringToolNumber",
         "boringCorrector",
         "boringTool",
         "boringOverhang",
         "boringClamp",
         "customBoringName",
         "customBoringDiameter",
         "customBoringMinBore",
         "customBoringOffset",
         "customBoringLength",
         "customBoringMaxOverhang",
         "cutoffWidth"},
        {"depthOutside", "depthInside", "finishAllowance", "cuttingSpeed", "feedRough", "feedFinish", "rpmMax",
         "clearanceX", "clearanceZ"},
        {"partName", "material", "organization", "drawingNumber", "developer", "checker", "rateSetter",
         "normController"}};
    for (int index = 0; index < keys.size(); ++index)
    {
        auto scroll = new QScrollArea(pages);
        scroll->installEventFilter(this);
        scroll->viewport()->installEventFilter(this);
        scroll->setWidgetResizable(true);
        scroll->setHorizontalScrollBarPolicy(Qt::ScrollBarAlwaysOff);
        auto content = new QWidget(scroll);
        content->installEventFilter(this);
        auto form = new QVBoxLayout(content);
        form->setContentsMargins(14, 12, 14, 12);
        form->setSpacing(3);
        QVector<ParameterRow*> group;
        QVBoxLayout* rowForm = form;
        QWidget* rowHost = content;
        const auto parameterGroup = [this, content, form, &rowForm, &rowHost](const QString& title, const QString& name)
        {
            if (form->count())
                form->addSpacing(8);
            rowHost = new QWidget(content);
            rowHost->setObjectName(name);
            rowHost->installEventFilter(this);
            rowForm = new QVBoxLayout(rowHost);
            rowForm->setContentsMargins(0, 0, 0, 8);
            rowForm->setSpacing(3);
            auto heading = new QLabel(title, rowHost);
            auto font = heading->font();
            font.setBold(true);
            heading->setFont(font);
            rowForm->addWidget(heading);
            form->addWidget(rowHost);
            return heading;
        };
        if (index == Preparation)
        {
            auto heading = parameterGroup("Заготовка", "stockParameters");
            secondStock = new QLabel(rowHost);
            secondStock->setObjectName("secondStock");
            secondStock->setWordWrap(true);
            secondStock->setMargin(6);
            rowForm->addWidget(secondStock);
            resetStock = new QToolButton(rowHost);
            resetStock->setObjectName("resetStock");
            resetStock->setText("Сбросить");
            resetStock->setToolTip("Вернуть исходные торцевые припуски и автоматический расчёт заготовки");
            rowForm->removeWidget(heading);
            auto header = new QHBoxLayout;
            header->addWidget(heading);
            header->addStretch();
            header->addWidget(resetStock);
            rowForm->insertLayout(0, header);
            connect(resetStock, &QToolButton::clicked, this, &ProcessView::stockResetRequested);
        }
        if (index == Tooling)
        {
            const auto toolCard = [content, form](const QString& title, const QStringList& facts)
            {
                auto card = new QFrame(content);
                card->setObjectName("toolCard");
                auto group = new QVBoxLayout(card);
                group->setContentsMargins(10, 10, 10, 10);
                group->setSpacing(5);
                auto heading = new QLabel(title, card);
                auto font = heading->font();
                font.setBold(true);
                heading->setFont(font);
                heading->setWordWrap(true);
                group->addWidget(heading);
                for (const auto& text : facts)
                {
                    auto label = new QLabel(text, card);
                    label->setWordWrap(true);
                    group->addWidget(label);
                }
                form->addWidget(card);
                return card;
            };
            auto outerTools = toolCard("Наружное точение", {"Радиус вершины 0,4 мм"});
            outerTools->setObjectName("outerToolCard");
            outerTools->installEventFilter(this);
            const auto toolGroup = [this, outerTools](const QString& title, const QString& explanation)
            {
                auto group = new QWidget(outerTools);
                group->installEventFilter(this);
                auto layout = new QVBoxLayout(group);
                layout->setContentsMargins(0, 4, 0, 0);
                layout->setSpacing(3);
                auto heading = new QLabel(title, group);
                heading->setToolTip(explanation);
                auto font = heading->font();
                font.setBold(true);
                heading->setFont(font);
                layout->addWidget(heading);
                outerTools->layout()->addWidget(group);
                return group;
            };
            rowHost = toolGroup("Прямой резец", "Обработка буртика в 010 и заднего торца в 020.");
            rowForm = qobject_cast<QVBoxLayout*>(rowHost->layout());
            bodyToolCard =
                toolGroup("Обратный резец", "Продольное точение к буртику с подачей +Z. Другое направление резания.");
            bodyToolCard->setObjectName("reverseToolParameters");
            boringToolCard = toolCard("Растачивание", {});
            boringToolCard->installEventFilter(this);
        }
        if (index == Regimes)
            parameterGroup("Обработка", "cuttingParameters");
        for (const auto& key : keys[index])
        {
            if (key == "bodyTool")
            {
                rowHost = bodyToolCard;
                rowForm = qobject_cast<QVBoxLayout*>(rowHost->layout());
            }
            if (key == "boringToolNumber")
            {
                rowHost = boringToolCard;
                rowForm = qobject_cast<QVBoxLayout*>(rowHost->layout());
            }
            if (key == "clearanceX")
                parameterGroup("Подходы", "approachParameters");
            if (key == "cutoffWidth")
            {
                parameterGroup("Ручная отрезка", "partingParameters");
                partingParameters = rowHost;
            }
            Field f;
            if (schema()["defaults"].toObject()[key].isString())
            {
                f.key = key;
                f.label = key == "partName"           ? "Наименование"
                          : key == "material"         ? "Материал"
                          : key == "boringTool"       ? "Оправка"
                          : key == "outerTool"        ? "Резец"
                          : key == "bodyTool"         ? "Резец"
                          : key == "customOuterName"  ? "Название"
                          : key == "customBodyName"   ? "Название"
                          : key == "customBoringName" ? "Название"
                                                      : QString();
                for (const auto& entry : schema()["cardFields"].toArray())
                    if (entry.toObject()["key"].toString() == key)
                        f.label = entry.toObject()["label"].toString();
            }
            else
                f = field(key);
            if (key == "cutoffWidth")
            {
                f.label = "Ширина реза";
                f.note = "Задаётся по отрезному инструменту. Учитывается в длине заготовки и картах. "
                         "Отрезка выполняется вручную между 010 и 020 и не входит в УП.";
            }
            if (index == Tooling && key.endsWith("ToolNumber"))
            {
                f.label = "Номер T";
                f.note = "Номер этого инструмента в таблице ЧПУ. Геометрия и привязка задаются на станке.";
            }
            if (index == Tooling && key.endsWith("Corrector"))
            {
                f.label = "Корректор D";
                f.note = "Номер режущей кромки с сохранёнными размерами и привязкой этого инструмента в ЧПУ.";
            }
            auto row = new ParameterRow(f, false, rowHost);
            group.append(row);
            rows.append(row);
            fieldSections[key] = index;
            rowForm->addWidget(row);
            connect(row, &ParameterRow::edited, this, &ProcessView::valueEdited);
            connect(row, &ParameterRow::parentRequested, this, [this] { sections->setFocus(); });
            connect(row, &ParameterRow::navigate, this,
                    [this, row, index](int delta)
                    {
                        if (index == Preparation && setupChoice->currentIndex() == 1)
                        {
                            secondFixture->setCurrentRow(delta > 0 ? 0 : secondFixture->count() - 1);
                            secondFixture->setFocus();
                            return;
                        }
                        if (simulationBlank->isVisible() && ((row->key == "frontFacingAllowance" && delta > 0) ||
                                                             (row->key == "stockDiameter" && delta < 0)))
                        {
                            simulationBlank->setFocus();
                            return;
                        }
                        const auto& group = pageRows[index];
                        int next = static_cast<int>(group.indexOf(row));
                        for (int visited = 0; visited < group.size(); ++visited)
                        {
                            next = (next + delta + group.size()) % group.size();
                            if (group[next]->input->isEnabled() && !group[next]->isHidden())
                            {
                                group[next]->setFocus();
                                break;
                            }
                        }
                    });
        }
        if (index == Preparation)
        {
            simulationBlank = new QCheckBox("Заготовка для моделирования", rowHost);
            simulationBlank->setObjectName("simulationBlank");
            simulationBlank->setToolTip("WORKPIECE задаёт только изображение заготовки в симуляторе SINUMERIK. "
                                        "Не задаёт движения и не заменяет установку заготовки на станке. "
                                        "Снимите флажок, чтобы выпустить УП без WORKPIECE.");
            simulationBlank->installEventFilter(this);
            rowForm->addWidget(simulationBlank);
            connect(simulationBlank, &QCheckBox::toggled, this,
                    [this](bool on) { Q_EMIT valueEdited("simulationBlank", on ? "true" : "false"); });
            parameterGroup("Закрепление", "fixtureParameters");
            fixtureParameters = rowHost;
            secondFixture = new QListWidget(rowHost);
            secondFixture->setWordWrap(true);
            secondFixture->setHorizontalScrollBarPolicy(Qt::ScrollBarAlwaysOff);
            secondFixture->setObjectName("secondFixtureFacts");
            secondFixture->setItemDelegate(new ItemDelegate(secondFixture));
            secondFixture->installEventFilter(this);
            rowForm->addWidget(secondFixture);
        }
        if (index == Tooling)
        {
            boringToolFacts = new QLabel(boringToolCard);
            boringToolFacts->setWordWrap(true);
            boringToolFacts->setMargin(6);
            boringToolCard->layout()->addWidget(boringToolFacts);
        }
        form->addStretch();
        pageRows.append(group);
        scroll->setWidget(content);
        pages->addWidget(scroll);
    }
    auto preview = new QWidget(this);
    layout->addWidget(preview, 1);
    auto previewLayout = new QVBoxLayout(preview);
    previewLayout->setContentsMargins(10, 10, 10, 10);
    setupChoice = new ChoiceList(preview);
    setupChoice->setObjectName("fixtureSetup");
    setupChoice->addItem("010", "010");
    setupChoice->addItem("020", "020");
    setupChoice->setAccessibleName("Установ");
    previewLayout->addWidget(setupChoice, 0, Qt::AlignLeft);
    auto sequence = new QLabel("010 → отрезка и ручная переустановка → 020", preview);
    sequence->setObjectName("setupSequence");
    sequence->setWordWrap(true);
    previewLayout->addWidget(sequence);
    diagram = new ProcessDiagram(preview);
    previewLayout->addWidget(diagram, 1);
    connect(setupChoice, &ChoiceList::currentIndexChanged, this, [this] { updateContext(); });
    for (auto row : rows)
        connect(row, &ParameterRow::activated, this,
                [this, row](const QString& key)
                {
                    focusedField = key;
                    diagram->highlight(key == "cutoffWidth" ? key
                                       : fieldSections.value(key) == Tooling
                                           ? (key.startsWith("outer") || key.startsWith("customOuter") ? "outerTooling"
                                              : key.startsWith("body") || key.startsWith("customBody")
                                                  ? "bodyTooling"
                                                  : "boringTooling")
                                           : key);
                    if (auto scroll = qobject_cast<QScrollArea*>(pages->currentWidget()))
                        scroll->ensureWidgetVisible(row, 0, 12);
                });
    connect(sections, &QListWidget::currentRowChanged, this,
            [this](int i)
            {
                pages->setCurrentIndex(i);
                updateContext();
            });
    connect(sections, &QListWidget::itemClicked, this, [this] { focusFirst(); });
    connect(secondFixture, &QListWidget::currentRowChanged, this,
            [this](int i) { diagram->highlight(i == 0 ? "gripLength" : ""); });
    sections->setCurrentRow(0);
    setParameters(Parameters());
}
void ProcessView::setParameters(const Parameters& p)
{
    parameters = p;
    const QSignalBlocker blankBlocker(simulationBlank);
    simulationBlank->setChecked(p.b("simulationBlank"));
    auto display = p;
    const auto sizes = calculate(p);
    display.set("blankLength", sizes.rawStockLength);
    display.set("blankOverhang", sizes.stockOverhang);
    for (auto row : rows)
    {
        row->setParameters(display);
    }
    diagram->setParameters(p);
    try
    {
        const auto tool = boringTool(p);
        const auto allowedOverhang =
            std::max(0., std::min({p.n("boringOverhang"), tool.maxOverhang,
                                   tool.length - std::max(p.n("boringClamp"), 4 * tool.diameter)}));
        const auto reach = std::max(0., allowedOverhang - 5);
        boringToolFacts->setText("Диаметр оправки " + localized(tool.diameter) + " мм\nИсходное отверстие от " +
                                 localized(tool.minBore + 2 * p.n("clearanceX")) +
                                 " мм с учётом отвода\nГлубина обработки до " + localized(reach) + " мм");
    }
    catch (const std::exception&)
    {
        boringToolFacts->clear();
    }
    const auto c = calculate(parameters);
    secondStock->setText("После отделения и переворота\nДлина " + localized(c.cutLength) +
                         " мм\nЗадний торец — до длины " + localized(c.targetLength) + " мм");
    const QSignalBlocker factsBlocker(secondFixture);
    const int selected = secondFixture->currentRow();
    secondFixture->clear();
    secondFixture->addItems(
        {"Захват за Ø" + localized(c.targetBodyDiameter) + ", пояс " + localized(c.secondClampStart) + "…" +
             localized(c.secondClampEnd) + " мм от первого торца",
         "Карман: Ø" + localized(c.softJawPocketDiameter) + " × " + localized(c.softJawPocketDepth) + " мм",
         "Упор по первому торцу"});
    secondFixture->setCurrentRow(std::max(0, selected));
    updateContext();
}
void ProcessView::updateContext()
{
    const int section = sections->currentRow();
    if (section < 0)
        return;
    const bool second = setupChoice->currentIndex() == 1;
    resetStock->setVisible(!second);
    secondStock->setVisible(second);
    boringToolCard->setVisible(!second);
    bodyToolCard->setVisible(!second);
    partingParameters->setVisible(!second);
    simulationBlank->setVisible(!second && parameters.s("controller") == "sinumerik840dsl");
    diagram->setSetup(second ? 1 : 0);
    secondFixture->setVisible(second);
    fixtureParameters->setVisible(second);
    for (auto row : rows)
    {
        if (fieldSections.value(row->key) == Tooling)
            row->setVisible((!second || row->key.startsWith("outer") || row->key.startsWith("customOuter")) &&
                            (!row->key.startsWith("customBoring") || parameters.s("boringTool") == "custom") &&
                            (row->key != "customOuterName" || parameters.s("outerTool") == "custom") &&
                            (row->key != "customBodyName" || parameters.s("bodyTool") == "custom"));
        if (row->key == "blankLength" || row->key == "blankOverhang" || row->key == "stockDiameter" ||
            row->key == "stockBore" || row->key == "frontFacingAllowance" || row->key == "cutoffWidth")
            row->setVisible(!second);
        if (row->key == "depthInside")
            row->setVisible(!second);
        if (row->key == "depthOutside")
        {
            row->setCaption(second ? "Глубина торцевого прохода ap, мм" : field(row->key).label + ", мм");
            row->setToolTip(second ? "Осевой съём за один черновой проход заднего торца." : field(row->key).note);
        }
    }
    if (section == Tooling)
    {
        const bool outer = second || focusedField.startsWith("outer") || focusedField.startsWith("customOuter");
        const bool body = focusedField.startsWith("body") || focusedField.startsWith("customBody");
        diagram->highlight(focusedField == "cutoffWidth" && !second           ? "cutoffWidth"
                           : outer                                            ? "outerTooling"
                           : body                                             ? "bodyTooling"
                           : fieldSections.value(focusedField, -1) == Tooling ? "boringTooling"
                                                                              : "tooling");
    }
    else
    {
        QString key;
        for (auto row : pageRows[section])
            if (!row->isHidden() && row->input->isEnabled())
            {
                if (key.isEmpty())
                    key = row->key;
                if (row->key == focusedField)
                {
                    key = focusedField;
                    break;
                }
            }
        diagram->highlight(key);
    }
}
void ProcessView::setErrors(const QVector<Error>& errors)
{
    for (auto row : rows)
    {
        bool bad = false;
        for (const auto& e : errors)
            if (e.field == row->key)
                bad = true;
        row->setError(bad);
    }
}
void ProcessView::focusField(const QString& key)
{
    if (!fieldSections.contains(key))
        return;
    if ((fieldSections.value(key) == Tooling && !key.startsWith("outer") && !key.startsWith("customOuter")) ||
        QStringList{"depthInside", "blankLength", "blankOverhang", "stockDiameter", "stockBore", "frontFacingAllowance"}
            .contains(key))
        setupChoice->setCurrentIndex(0);
    sections->setCurrentRow(fieldSections[key]);
    for (auto row : rows)
        if (row->key == key)
        {
            (row->choices && !row->choices->isEditable() ? static_cast<QWidget*>(row) : row->input)->setFocus();
            return;
        }
}
void ProcessView::focusFirst()
{
    for (auto row : pageRows[sections->currentRow()])
        if (row->input->isEnabled() && !row->isHidden())
        {
            row->setFocus();
            return;
        }
    sections->setFocus();
}
bool ProcessView::eventFilter(QObject* object, QEvent* e)
{
    if (object == simulationBlank && e->type() == QEvent::FocusIn)
        if (auto scroll = qobject_cast<QScrollArea*>(pages->currentWidget()))
            scroll->ensureWidgetVisible(simulationBlank, 0, 12);
    if (object == simulationBlank && e->type() == QEvent::KeyPress)
    {
        const auto key = static_cast<QKeyEvent*>(e)->key();
        if (key == Qt::Key_Up || key == Qt::Key_Down)
        {
            focusField(key == Qt::Key_Up ? "frontFacingAllowance" : "stockDiameter");
            return true;
        }
        if (key == Qt::Key_Left)
        {
            sections->setFocus();
            return true;
        }
    }
    if (object == secondFixture && e->type() == QEvent::FocusIn)
    {
        diagram->highlight(secondFixture->currentRow() == 0 ? "gripLength" : "");
        if (auto scroll = qobject_cast<QScrollArea*>(pages->currentWidget()))
            scroll->ensureWidgetVisible(secondFixture, 0, 12);
    }
    if (e->type() == QEvent::MouseButtonPress && object != sections && object != secondFixture &&
        object != simulationBlank && static_cast<QMouseEvent*>(e)->button() == Qt::LeftButton)
    {
        if (const auto background = qobject_cast<QWidget*>(object))
        {
            for (auto row : background->findChildren<ParameterRow*>())
                if (row->isVisible() && row->input->isEnabled())
                {
                    row->setFocus(Qt::MouseFocusReason);
                    return true;
                }
            if (background->isAncestorOf(secondFixture) && secondFixture->isVisible())
            {
                secondFixture->setFocus(Qt::MouseFocusReason);
                return true;
            }
        }
        focusFirst();
        return true;
    }
    if (object == sections && e->type() == QEvent::KeyPress)
    {
        const auto key = static_cast<QKeyEvent*>(e)->key();
        if (key == Qt::Key_Up || key == Qt::Key_Down)
        {
            sections->setCurrentRow((sections->currentRow() + (key == Qt::Key_Down ? 1 : -1) + sections->count()) %
                                    sections->count());
            return true;
        }
        if (key == Qt::Key_Right || key == Qt::Key_Return)
        {
            focusFirst();
            return true;
        }
        if (key == Qt::Key_Left)
        {
            Q_EMIT parentRequested();
            return true;
        }
    }
    if (object == secondFixture && e->type() == QEvent::KeyPress && static_cast<QKeyEvent*>(e)->key() == Qt::Key_Left)
    {
        sections->setFocus();
        return true;
    }
    if (object == secondFixture && e->type() == QEvent::KeyPress)
    {
        const auto key = static_cast<QKeyEvent*>(e)->key();
        if ((key == Qt::Key_Up && secondFixture->currentRow() <= 0) ||
            (key == Qt::Key_Down && secondFixture->currentRow() == secondFixture->count() - 1))
        {
            focusFirst();
            return true;
        }
    }
    return QWidget::eventFilter(object, e);
}
} // namespace pf::ui
