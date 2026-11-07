#include "window.h"
#include "core/engine.h"
#include "core/geometry.h"
#include "core/tolerances.h"
#include "dimension_pane.h"
#include "files/projects.h"
#include "numeric_edit.h"
#include "theme.h"
#include <QActionGroup>
#include <QApplication>
#include <QButtonGroup>
#include <QClipboard>
#include <QCloseEvent>
#include <QDesktopServices>
#include <QDialogButtonBox>
#include <QFileDialog>
#include <QHBoxLayout>
#include <QHeaderView>
#include <QKeyEvent>
#include <QMenu>
#include <QMessageBox>
#include <QMouseEvent>
#include <QPainterPath>
#include <QPixmap>
#include <QRegularExpression>
#include <QScopedValueRollback>
#include <QScreen>
#include <QSettings>
#include <QShortcut>
#include <QSignalBlocker>
#include <QStandardPaths>
#include <QStatusBar>
#include <QStyle>
#include <QStyleHints>
#include <QStylePainter>
#include <QUrl>
#include <QVBoxLayout>
#include <QWindow>
#include <QtConcurrent>
#include <algorithm>
#include <cmath>

namespace pf::ui
{
namespace
{
QIcon errorIcon()
{
    const auto makeIcon = [](const QColor& color)
    {
        QIcon icon;
        for (const double scale : {1., 1.25, 1.5, 2., 3., 4.})
        {
            QPixmap pixmap(qRound(18 * scale), qRound(18 * scale));
            pixmap.setDevicePixelRatio(scale);
            pixmap.fill(Qt::transparent);
            QPainter painter(&pixmap);
            painter.setRenderHint(QPainter::Antialiasing);
            painter.setPen(QPen(color, 1.4, Qt::SolidLine, Qt::RoundCap, Qt::RoundJoin));
            QPainterPath outline;
            outline.moveTo(9, 2);
            outline.lineTo(16.3, 15.4);
            outline.lineTo(1.7, 15.4);
            outline.closeSubpath();
            painter.drawPath(outline);
            painter.drawLine(QPointF(9, 6.5), QPointF(9, 10.5));
            painter.setPen(Qt::NoPen);
            painter.setBrush(color);
            painter.drawEllipse(QPointF(9, 12.8), .8, .8);
            painter.end();
            icon.addPixmap(pixmap);
        }
        return icon;
    };
    static const QIcon light = makeIcon(QColor("#d32f2f"));
    static const QIcon night = makeIcon(QColor("#ef4444"));
    return dark() ? night : light;
}
class StatusButton final : public QPushButton
{
  public:
    using QPushButton::QPushButton;

  protected:
    void paintEvent(QPaintEvent*) override
    {
        QStyleOptionButton option;
        initStyleOption(&option);
        const auto width = style()->subElementRect(QStyle::SE_PushButtonContents, &option, this).width();
        option.text = fontMetrics().elidedText(text(), Qt::ElideRight, width);
        QStylePainter painter(this);
        painter.drawControl(QStyle::CE_PushButton, option);
    }
};
} // namespace
Window::Window(QWidget* parent) : QMainWindow(parent)
{
    working.set("autoStock", true);
    setObjectName("ParamForgeMainWindow");
    setWindowTitle("ParamForge");
    setWindowIcon(QIcon(":/paramforge/paramforge.ico"));

    auto root = new QWidget(this);
    auto layout = new QVBoxLayout(root);
    layout->setContentsMargins(0, 0, 0, 0);
    layout->setSpacing(0);
    setCentralWidget(root);
    auto header = new QWidget(root);
    header->setObjectName("mainToolbar");
    header->installEventFilter(this);
    header->setSizePolicy(QSizePolicy::Expanding, QSizePolicy::Fixed);
    auto toolbar = new QHBoxLayout(header);
    toolbar->setContentsMargins(10, 6, 10, 6);
    toolbar->setSpacing(5);
    auto fileButton = new QToolButton(header);
    fileButton->setObjectName("fileMenuButton");
    fileButton->setFocusPolicy(Qt::StrongFocus);
    fileButton->installEventFilter(this);
    fileButton->setText("Файл");
    fileButton->setPopupMode(QToolButton::InstantPopup);
    auto fileMenu = new QMenu(fileButton);
    fileMenu->setObjectName("fileMenu");
    fileMenu->installEventFilter(this);
    fileButton->setMenu(fileMenu);
    connect(fileMenu->addAction("Новая деталь"), &QAction::triggered, this,
            [this]
            {
                if (confirmSave())
                {
                    currentPath.clear();
                    Parameters fresh;
                    fresh.set("autoStock", true);
                    setParameters(fresh, false);
                }
            });
    connect(fileMenu->addAction("Открыть…"), &QAction::triggered, this, &Window::openDialog);
    connect(fileMenu->addAction("Сохранить"), &QAction::triggered, this, [this] { saveDialog(); });
    connect(fileMenu->addAction("Сохранить как…"), &QAction::triggered, this, [this] { saveDialog(true); });
    fileMenu->addSeparator();
    auto exportFolder = fileMenu->addAction("Папка комплектов…");
    exportFolder->setObjectName("exportDirectory");
    connect(exportFolder, &QAction::triggered, this, [this] { chooseExportDirectory(); });
    fileMenu->addSeparator();

    toolbar->addWidget(fileButton);
    auto group = new QButtonGroup(this);
    const QStringList names = {"Деталь", "Технология", "УП"};
    for (int i = 0; i < names.size(); ++i)
    {
        auto button = new QToolButton(header);
        button->setText(names[i]);
        button->setCheckable(true);
        button->setFocusPolicy(Qt::StrongFocus);
        button->setProperty("pageButton", true);
        button->setObjectName("page_" + QString::number(i));
        button->installEventFilter(this);
        group->addButton(button, i);
        toolbar->addWidget(button);
        pageButtons.append(button);
    }
    toolbar->addStretch();
    controller = new ChoiceList(this);
    controller->setObjectName("controller");
    controller->setProperty("popupAbove", true);
    controller->addItem("AxiOMA", "axioma");
    controller->addItem("SINUMERIK", "sinumerik840dsl");
    controller->setAccessibleName("Система ЧПУ");
    controller->setToolTip("Система ЧПУ");
    controller->setSizePolicy(QSizePolicy::Fixed, QSizePolicy::Fixed);

    exportButton = new QPushButton("Выпустить документы", this);
    exportButton->setObjectName("export");

    auto theme = fileMenu->addMenu("Тема");
    theme->setObjectName("themeMenu");
    theme->installEventFilter(this);
    fileMenu->addSeparator();
    connect(fileMenu->addAction("Выход"), &QAction::triggered, this, &QWidget::close);
    themePreference = QSettings().value("appearance/theme", "system").toString();
    auto themes = new QActionGroup(this);
    for (const auto& option :
         QVector<std::pair<QString, QString>>{{"Как в системе", "system"}, {"Светлая", "light"}, {"Тёмная", "dark"}})
    {
        auto action = theme->addAction(option.first);
        action->setCheckable(true);
        action->setChecked(themePreference == option.second);
        themes->addAction(action);
        connect(action, &QAction::triggered, this,
                [this, value = option.second]
                {
                    themePreference = value;
                    applyTheme(value);
                    for (auto widget : findChildren<QWidget*>())
                        widget->update();
                });
    }
    layout->addWidget(header);
    auto separator = new QFrame(root);
    separator->setFrameShape(QFrame::HLine);
    layout->addWidget(separator);
    pages = new QStackedWidget(root);
    part = new PartView(pages);
    process = new ProcessView(pages);
    inspection = new InspectionView(pages);
    connect(process->setupChoice, &ChoiceList::currentIndexChanged, inspection->setups, &ChoiceList::setCurrentIndex);
    connect(inspection->setups, &ChoiceList::currentIndexChanged, process->setupChoice, &ChoiceList::setCurrentIndex);
    pages->addWidget(part);
    pages->addWidget(process);
    pages->addWidget(inspection);
    layout->addWidget(pages, 1);
    auto messageBar = new QWidget(this);
    messageBar->setSizePolicy(QSizePolicy::Ignored, QSizePolicy::Fixed);
    messageBar->setMinimumWidth(0);
    auto messageLayout = new QHBoxLayout(messageBar);
    messageLayout->setContentsMargins(0, 0, 0, 0);
    messageLayout->setSpacing(0);
    statusToggle = new QToolButton(messageBar);
    statusToggle->setObjectName("statusDetailsToggle");
    statusToggle->setCheckable(true);
    statusToggle->setFixedHeight(28);
    statusToggle->setMinimumWidth(24);
    statusToggle->setToolButtonStyle(Qt::ToolButtonTextBesideIcon);
    statusToggle->setIconSize(QSize(18, 18));
    messageLayout->addWidget(statusToggle);
    status = new StatusButton(messageBar);
    status->setFlat(true);
    status->setSizePolicy(QSizePolicy::Ignored, QSizePolicy::Fixed);
    status->setMinimumWidth(0);
    status->setObjectName("validationStatus");
    messageLayout->addWidget(status, 1);
    messageLayout->addStretch(0);
    statusBar()->addWidget(messageBar, 1);
    statusDetails = new QFrame(root);
    statusDetails->setObjectName("statusDetails");
    auto detailsLayout = new QVBoxLayout(statusDetails);
    detailsLayout->setContentsMargins(8, 8, 8, 8);
    detailsLayout->setSpacing(6);
    statusDetailsTitle = new QLabel(statusDetails);
    auto titleFont = statusDetailsTitle->font();
    titleFont.setBold(true);
    statusDetailsTitle->setFont(titleFont);
    detailsLayout->addWidget(statusDetailsTitle);
    statusDetailsTable = new QTableWidget(0, 2, statusDetails);
    statusDetailsTable->setObjectName("statusDetailsTable");
    statusDetailsTable->setAccessibleName("Сообщения");
    statusDetailsTable->setFrameShape(QFrame::NoFrame);
    statusDetailsTable->setEditTriggers(QAbstractItemView::NoEditTriggers);
    statusDetailsTable->setSelectionBehavior(QAbstractItemView::SelectRows);
    statusDetailsTable->setSelectionMode(QAbstractItemView::ExtendedSelection);
    statusDetailsTable->setWordWrap(true);
    statusDetailsTable->setTextElideMode(Qt::ElideNone);
    statusDetailsTable->setShowGrid(false);
    statusDetailsTable->setAlternatingRowColors(true);
    statusDetailsTable->setHorizontalScrollBarPolicy(Qt::ScrollBarAlwaysOff);
    statusDetailsTable->verticalHeader()->hide();
    statusDetailsTable->horizontalHeader()->hide();
    statusDetailsTable->horizontalHeader()->setSectionResizeMode(0, QHeaderView::Fixed);
    statusDetailsTable->horizontalHeader()->setSectionResizeMode(1, QHeaderView::Stretch);
    statusDetailsTable->setColumnWidth(0, 36);
    detailsLayout->addWidget(statusDetailsTable);
    statusDetails->hide();
    root->installEventFilter(this);
    status->installEventFilter(this);
    statusToggle->installEventFilter(this);
    statusDetailsTable->installEventFilter(this);
    connect(statusToggle, &QToolButton::toggled, this, &Window::updateStatusDetails);
    const auto activateStatusRow = [this](int row, int)
    {
        if (!statusDetails->isVisible() || row < 0 || row >= statusEntries.size())
            return;
        const auto key = statusEntries[row].field;
        if (!key.isEmpty())
            focusErrorField(key);
    };
    connect(statusDetailsTable, &QTableWidget::cellClicked, this, activateStatusRow);
    connect(statusDetailsTable, &QTableWidget::cellActivated, this, activateStatusRow);
    connect(statusDetailsTable->horizontalHeader(), &QHeaderView::sectionResized, this, &Window::updateStatusDetails,
            Qt::QueuedConnection);
    statusBar()->setSizeGripEnabled(false);
    statusBar()->addPermanentWidget(controller);
    statusBar()->addPermanentWidget(exportButton);
    windowGrip = new ResizeGrip(this);
    windowGrip->setObjectName("windowResizeGrip");
    statusBar()->setContentsMargins(0, 0, windowGrip->width(), 0);
    connect(group, &QButtonGroup::idClicked, this,
            [this](int index)
            {
                selectPage(index);
                pageButtons[index]->setFocus(Qt::MouseFocusReason);
            });
    connect(controller, &ChoiceList::currentIndexChanged, this,
            [this]
            {
                if (!loading)
                    setController(controller->currentData().toString());
            });
    connect(exportButton, &QPushButton::clicked, this, &Window::exportDialog);
    connect(part, &PartView::numberEdited, this, &Window::editValue);
    connect(process, &ProcessView::valueEdited, this, &Window::editValue);
    connect(part, &PartView::designationEdited, this, &Window::editTolerance);
    connect(part, &PartView::dimensionOpened, this, &Window::refresh);
    connect(part, &PartView::dragStarted, this,
            [this]
            {
                interactiveDrag = true;
                modifiedBeforeDrag = modified;
                generationTimer.stop();
            });
    connect(part, &PartView::nominalDragged, this,
            [this](const QString& key, double value) { editValue(key, number(value)); });
    connect(part, &PartView::dragFinished, this,
            [this]
            {
                interactiveDrag = false;
                generationTimer.stop();
                refresh();
                startGeneration();
            });
    connect(part, &PartView::dragCancelled, this,
            [this](const Parameters& p)
            {
                interactiveDrag = false;
                working = p;
                modified = modifiedBeforeDrag;
                refresh();
                schedule();
            });
    connect(process, &ProcessView::stockResetRequested, this,
            [this]
            {
                if (loading)
                    return;
                working.set("autoStock", true);
                working.set("autoBlankLength", true);
                working.set("autoBlankOverhang", true);
                for (const auto& key : {"stockDiameter", "stockBore", "blankLength", "blankOverhang", "trimAllowance",
                                        "frontFacingAllowance", "cutoffWidth"})
                {
                    invalidDraft.remove(key);
                    if (QString(key).endsWith("Allowance"))
                        working.set(key, Parameters().n(key));
                }
                modified = true;
                try
                {
                    working = resolveStock(working);
                }
                catch (const std::exception&)
                {
                }
                refresh();
                schedule();
            });
    connect(part, &PartView::parentRequested, this, [this] { pageButtons[0]->setFocus(); });
    connect(process, &ProcessView::parentRequested, this, [this] { pageButtons[1]->setFocus(); });
    connect(inspection, &InspectionView::parentRequested, this, [this] { pageButtons[2]->setFocus(); });
    generationTimer.setSingleShot(true);
    generationTimer.setInterval(120);
    connect(&generationTimer, &QTimer::timeout, this, &Window::startGeneration);
    connect(&watcher, &QFutureWatcher<Generation>::finished, this, &Window::generationFinished);
    connect(qApp->styleHints(), &QStyleHints::colorSchemeChanged, this,
            [this]
            {
                if (themePreference == "system")
                    QTimer::singleShot(0, this,
                                       [this]
                                       {
                                           applyTheme(themePreference);
                                           update();
                                       });
            });
    auto exportShortcut = new QShortcut(QKeySequence(Qt::CTRL | Qt::Key_E), this);
    connect(exportShortcut, &QShortcut::activated, this, &Window::exportDialog);
    auto openShortcut = new QShortcut(QKeySequence::Open, this);
    connect(openShortcut, &QShortcut::activated, this, &Window::openDialog);
    auto saveShortcut = new QShortcut(QKeySequence::Save, this);
    connect(saveShortcut, &QShortcut::activated, this, [this] { saveDialog(); });
    const auto available = screen()->availableGeometry();
    setMinimumSize(std::min(940, available.width() - 30), std::min(600, available.height() - 40));
    resize(std::min(1280, available.width() - 40), std::min(820, available.height() - 60));
    selectPage(0);
    setParameters(working, false);
}
Window::~Window()
{
    // QWidget hides descendants after this class's members have been destroyed.
    // A lost drag grab must not call generation code during that later teardown.
    QObject::disconnect(part, nullptr, this, nullptr);
    generationTimer.stop();
    watcher.waitForFinished();
}
void Window::selectPage(int index)
{
    if (index < 0 || index >= 3)
        return;
    pages->setCurrentIndex(index);
    pageButtons[index]->setChecked(true);
}
void Window::refresh()
{
    loading = true;
    part->setParameters(working);
    if (!interactiveDrag)
        process->setParameters(working);
    controller->setCurrentIndex(controller->findData(working.s("controller")));
    for (auto row : process->rows)
        if (invalidDraft.contains(row->key) && row->input->text() != invalidDraft[row->key].text)
            row->input->setText(invalidDraft[row->key].text);
    for (auto row : part->rows)
        if (invalidDraft.contains(row->key))
            row->dimensionValue->setNominalText(invalidDraft[row->key].text);
    if (part->dimensionPane)
        for (auto i = invalidDraft.cbegin(); i != invalidDraft.cend(); ++i)
            part->dimensionPane->setDraftText(i.key(), i.value().text);
    part->canvas->setDragAllowed(invalidDraft.isEmpty() && drawablePart(working));
    loading = false;
}
void Window::setParameters(const Parameters& p, bool value)
{
    part->closeDimension(false);
    if (auto focused = QApplication::focusWidget(); focused && isAncestorOf(focused))
        focused->clearFocus();
    working = resolveStock(resolveTolerances(p));
    invalidDraft.clear();
    inspection->resetDocument();
    process->setupChoice->setCurrentIndex(0);
    process->diagram->resetView();
    part->model->resetView();
    modified = value;
    refresh();
    part->canvas->resetView();
    schedule();
}
void Window::editValue(const QString& key, const QString& text)
{
    if (loading)
        return;
    modified = true;
    if (key == "stockDiameter" || key == "stockBore")
        working.set("autoStock", false);
    if (key == "blankLength")
        working.set("autoBlankLength", false);
    if (key == "blankOverhang")
        working.set("autoBlankOverhang", false);
    if (key == "gripLength")
    {
        working.set("autoBlankLength", true);
        invalidDraft.remove("blankLength");
    }
    if (key == "chuckClearance")
    {
        working.set("autoBlankOverhang", true);
        invalidDraft.remove("blankOverhang");
    }
    for (const auto& dimension : dimensions())
        if (key == dimension.lower || key == dimension.upper)
            working.set(dimension.designation, "");
    const auto expected = schema()["defaults"].toObject().value(key);
    if (expected.isBool())
    {
        working.set(key, text == "true");
        invalidDraft.remove(key);
    }
    else if (expected.isString())
    {
        working.set(key, text);
        invalidDraft.remove(key);
        if (key == "boringTool" && text != "custom")
        {
            working.set("boringOverhang", text == "a40t" ? 140 : 125);
            working.set("boringClamp", text == "a40t" ? 160 : 128);
            invalidDraft.remove("boringOverhang");
            invalidDraft.remove("boringClamp");
            for (auto draft = invalidDraft.begin(); draft != invalidDraft.end();)
                if (draft.key().startsWith("customBoring"))
                    draft = invalidDraft.erase(draft);
                else
                    ++draft;
        }
    }
    else
    {
        const auto trimmed = text.trimmed();
        const auto normalized = QString(trimmed).replace(',', '.');
        bool parsed = false;
        const auto value = normalized.toDouble(&parsed);
        static const QRegularExpression pattern("^[+-]?(?:[0-9]+(?:[.,][0-9]+)?|[.,][0-9]+)$");
        const auto& f = field(key);
        const int precision = key.endsWith("Deviation") ? 4 : 3;
        QString problem;
        if (!pattern.match(trimmed).hasMatch() || !parsed || !std::isfinite(value))
            problem = f.label + ": введите число.";
        else if (value < f.minimum || value > f.maximum)
            problem = f.label + ": диапазон " + localized(f.minimum) + "…" + localized(f.maximum) + " " + f.unit + ".";
        else if (std::abs(value - roundTo(value, precision)) > 1e-10)
            problem = f.label + (precision == 4 ? ": не более четырёх знаков после запятой."
                                                : ": не более трёх знаков после запятой.");
        if (!problem.isEmpty())
        {
            invalidDraft[key] = {text, problem};
            refresh();
            schedule();
            return;
        }
        invalidDraft.remove(key);
        working.set(key, value);
        for (const auto& d : dimensions())
            if (key == d.lower || key == d.upper)
                working.set(d.designation, "");
    }
    try
    {
        working = resolveStock(resolveTolerances(working));
    }
    catch (const std::exception&)
    {
    }
    refresh();
    schedule();
}
void Window::editTolerance(const QString& key, const QString& designation)
{
    const auto& descriptor = dimension(key);
    if (working.s(descriptor.designation) == designation && !invalidDraft.contains(descriptor.lower) &&
        !invalidDraft.contains(descriptor.upper))
        return;
    try
    {
        working = setTolerance(working, key, designation);
        const auto& d = dimension(key);
        invalidDraft.remove(d.lower);
        invalidDraft.remove(d.upper);
        modified = true;
        refresh();
        schedule();
    }
    catch (const std::exception& e)
    {
        showErrors({{key, QString::fromUtf8(e.what())}});
    }
}
void Window::setController(const QString& value)
{
    if (working.s("controller") == value)
        return;
    working.set("controller", value);
    modified = true;
    refresh();
    schedule();
}
void Window::showErrors(const QVector<Error>& value)
{
    errors = value;
    part->setErrors(errors);
    process->setErrors(errors);
    setStatusMessage(errors.isEmpty() ? QString() : errors.first().message, !errors.isEmpty(), errors);
}
void Window::setStatusMessage(const QString& message, bool error, const QVector<Error>& entries)
{
    statusError = error;
    statusEntries = entries;
    if (statusEntries.isEmpty() && !message.isEmpty())
        statusEntries.append(Error{{}, message});
    status->setText(message);
    status->setToolTip(message);
    status->setProperty("error", error);
    status->style()->unpolish(status);
    status->style()->polish(status);
    if (message.isEmpty())
    {
        const QSignalBlocker blocker(statusToggle);
        statusToggle->setChecked(false);
    }
    updateStatusDetails();
}
void Window::updateStatusDetails()
{
    if (!statusDetails || updatingStatusDetails)
        return;
    QScopedValueRollback updating(updatingStatusDetails, true);
    const bool expanded = statusToggle->isChecked() && !status->text().isEmpty();
    status->setVisible(!statusError);
    auto layout = qobject_cast<QHBoxLayout*>(status->parentWidget()->layout());
    layout->setStretch(1, statusError ? 0 : 1);
    layout->setStretch(2, statusError ? 1 : 0);
    statusToggle->setVisible(
        !status->text().isEmpty() &&
        (statusError || expanded || status->fontMetrics().horizontalAdvance(status->text()) > status->width() - 16));
    statusToggle->setProperty("errorCount", statusError ? statusEntries.size() : 0);
    statusToggle->setText(statusError ? QString::number(statusEntries.size()) : QString());
    statusToggle->setArrowType(statusError ? Qt::NoArrow : expanded ? Qt::DownArrow : Qt::UpArrow);
    statusToggle->setIcon(statusError ? errorIcon() : QIcon());
    const auto action =
        statusError ? QString("Ошибки: %1. %2 список").arg(statusEntries.size()).arg(expanded ? "Свернуть" : "Открыть")
        : expanded  ? QString("Свернуть сообщение")
                    : QString("Развернуть сообщение");
    statusToggle->setAccessibleName(action);
    statusToggle->setToolTip(action);
    statusDetailsTitle->setText(statusError ? QString("Ошибки: %1").arg(statusEntries.size()) : QString("Сообщение"));
    statusDetailsTable->setColumnHidden(0, !statusError);
    statusDetailsTable->setRowCount(statusEntries.size());
    for (int row = 0; row < statusEntries.size(); ++row)
    {
        const auto& entry = statusEntries[row];
        auto message = entry.message;
        QString label;
        for (const auto& f : fields())
            if (f.key == entry.field)
                label = f.label;
        for (const auto& item : schema()["cardFields"].toArray())
            if (item.toObject()["key"].toString() == entry.field)
                label = item.toObject()["label"].toString();
        if (!label.isEmpty() && !entry.message.startsWith(label))
            message = label + ": " + message;
        for (int column = 0; column < 2; ++column)
        {
            auto item = statusDetailsTable->item(row, column);
            if (!item)
            {
                item = new QTableWidgetItem;
                item->setTextAlignment((column == 0 ? Qt::AlignHCenter : Qt::AlignLeft) | Qt::AlignTop);
                statusDetailsTable->setItem(row, column, item);
            }
            const auto text = column == 0 ? QString::number(row + 1) : message;
            if (item->text() != text)
                item->setText(text);
            item->setToolTip(message);
        }
    }
    statusDetails->setVisible(expanded);
    if (!expanded)
        return;
    const auto area = centralWidget()->rect().adjusted(8, 8, -8, -8);
    const int width = std::min(800, area.width());
    statusDetails->resize(width, statusDetails->height());
    statusDetails->layout()->activate();
    statusDetailsTable->resizeRowsToContents();
    const int contentHeight =
        statusDetailsTitle->sizeHint().height() + statusDetailsTable->verticalHeader()->length() + 26;
    const int height = std::min(contentHeight, std::max(1, area.height() / 2));
    statusDetails->setGeometry(area.left(), area.bottom() - height + 1, width, height);
    statusDetails->raise();
}
QVector<Error> Window::currentErrors() const
{
    QVector<Error> value;
    for (auto i = invalidDraft.cbegin(); i != invalidDraft.cend(); ++i)
        value.append({i.key(), i.value().message});
    for (const auto& error : validate(working))
        if (!invalidDraft.contains(error.field))
            value.append(error);
    return value;
}
void Window::focusErrorField(const QString& key)
{
    if (key == "nc")
    {
        selectPage(2);
        inspection->table->setFocus();
        return;
    }
    if (key == "controller")
    {
        controller->setFocus();
        return;
    }
    for (const auto& d : dimensions())
        if (key == d.nominal || key == d.lower || key == d.upper)
        {
            selectPage(0);
            part->focusField(key);
            return;
        }
    selectPage(1);
    process->focusField(key);
}
void Window::schedule()
{
    ++revision;
    project.reset();
    inspection->setPending();
    const auto invalid = currentErrors();
    showErrors(invalid);
    if (!invalid.isEmpty())
    {
        generationTimer.stop();
        return;
    }
    if (!interactiveDrag)
        generationTimer.start();
}
void Window::startGeneration()
{
    if (interactiveDrag || watcher.isRunning() || !invalidDraft.isEmpty() || !validate(working).isEmpty())
        return;
    workerRevision = revision;
    const auto snapshot = working;
    watcher.setFuture(QtConcurrent::run([snapshot] { return generate(snapshot); }));
}
void Window::generationFinished()
{
    if (workerRevision != revision)
    {
        if (!generationTimer.isActive())
            startGeneration();
        return;
    }
    const auto result = watcher.result();
    if (!result.ok())
    {
        showErrors(result.errors);
        return;
    }
    project = *result.project;
    working = project->parameters;
    showErrors({});
    inspection->setProject(*project);
}
bool Window::saveProject(const QString& path)
{
    if (!ensureReady())
        return false;
    try
    {
        saveParameters(*project, path);
        currentPath = path;
        modified = false;
        return true;
    }
    catch (const std::exception& e)
    {
        setStatusMessage(QString::fromUtf8(e.what()), true);
        return false;
    }
}
bool Window::openProject(const QString& path)
{
    try
    {
        setParameters(importParameters(path), false);
        currentPath = path;
        return true;
    }
    catch (const std::exception& e)
    {
        setStatusMessage(QString::fromUtf8(e.what()), true);
        return false;
    }
}
bool Window::exportTo(const QString& folder)
{
    if (!ensureReady())
        return false;
    try
    {
        exportedFiles = exportProject(*project, folder);
        QSettings().setValue("files/exportDirectory", QFileInfo(folder).absoluteFilePath());
        setStatusMessage("Комплект сохранён");
        return true;
    }
    catch (const std::exception& e)
    {
        setStatusMessage(QString::fromUtf8(e.what()), true);
        return false;
    }
}
void Window::openDialog()
{
    if (!confirmSave())
        return;
    const auto path = QFileDialog::getOpenFileName(this, "Открыть деталь",
                                                   QStandardPaths::writableLocation(QStandardPaths::DocumentsLocation),
                                                   "Параметры детали (*.json)");
    if (!path.isEmpty())
        openProject(path);
}
void Window::saveDialog(bool saveAs)
{
    if (!ensureReady())
        return;
    auto path = currentPath;
    if (saveAs || path.isEmpty())
        path = QFileDialog::getSaveFileName(
            this, "Сохранить деталь",
            QDir(QStandardPaths::writableLocation(QStandardPaths::DocumentsLocation)).filePath("Втулка.json"),
            "Параметры детали (*.json)");
    if (!path.isEmpty())
        saveProject(path);
}
bool Window::ensureReady()
{
    if (isReady())
        return true;
    if (!invalidDraft.isEmpty())
    {
        showErrors(currentErrors());
        return false;
    }
    if (interactiveDrag)
    {
        setStatusMessage("Завершите ввод параметров.", true);
        return false;
    }
    generationTimer.stop();
    const auto result = generate(working);
    if (!result.ok())
    {
        showErrors(result.errors);
        return false;
    }
    project = *result.project;
    working = project->parameters;
    showErrors({});
    inspection->setProject(*project);
    return true;
}
void Window::exportDialog()
{
    const auto explainFailure = [this] { QMessageBox::information(this, "Документы не выпущены", status->text()); };
    if (!ensureReady())
    {
        explainFailure();
        return;
    }
    auto folder = QSettings().value("files/exportDirectory").toString();
    if (folder.isEmpty() || !QDir(folder).exists())
    {
        if (!chooseExportDirectory())
            return;
        folder = QSettings().value("files/exportDirectory").toString();
    }
    if (exportTo(folder))
        showExportResult();
    else
        explainFailure();
}
bool Window::chooseExportDirectory()
{
    const auto folder = QFileDialog::getExistingDirectory(
        this, "Папка для комплектов — каждый выпуск в отдельной подпапке",
        QSettings()
            .value("files/exportDirectory", QStandardPaths::writableLocation(QStandardPaths::DocumentsLocation))
            .toString());
    if (folder.isEmpty())
        return false;
    QSettings().setValue("files/exportDirectory", folder);
    return true;
}
void Window::showExportResult()
{
    if (exportedFiles.isEmpty())
        return;
    QDialog dialog(this);
    dialog.setObjectName("exportResult");
    dialog.setWindowTitle("Документы готовы");
    auto layout = new QVBoxLayout(&dialog);
    auto files = new QListWidget(&dialog);
    files->setObjectName("exportedFiles");
    for (const auto& path : exportedFiles)
    {
        auto item = new QListWidgetItem(QFileInfo(path).fileName(), files);
        item->setData(Qt::UserRole, path);
        item->setToolTip(path);
    }
    files->setCurrentRow(0);
    layout->addWidget(files);
    auto location = new QLabel(QDir::toNativeSeparators(QFileInfo(exportedFiles.first()).absolutePath()), &dialog);
    location->setTextInteractionFlags(Qt::TextSelectableByMouse);
    location->setWordWrap(true);
    layout->addWidget(location);
    auto buttons = new QDialogButtonBox(&dialog);
    auto open = buttons->addButton("Открыть файл", QDialogButtonBox::ActionRole);
    auto folder = buttons->addButton("Открыть папку", QDialogButtonBox::ActionRole);
    auto close = buttons->addButton("Закрыть", QDialogButtonBox::RejectRole);
    const auto openSelected = [files]
    {
        if (files->currentItem())
            QDesktopServices::openUrl(QUrl::fromLocalFile(files->currentItem()->data(Qt::UserRole).toString()));
    };
    connect(open, &QPushButton::clicked, &dialog, openSelected);
    connect(files, &QListWidget::itemDoubleClicked, &dialog, openSelected);
    connect(folder, &QPushButton::clicked, &dialog, [this]
            { QDesktopServices::openUrl(QUrl::fromLocalFile(QFileInfo(exportedFiles.first()).absolutePath())); });
    connect(close, &QPushButton::clicked, &dialog, &QDialog::reject);
    layout->addWidget(buttons);
    dialog.resize(520, 300);
    dialog.exec();
}
bool Window::confirmSave()
{
    if (!modified)
        return true;
    QMessageBox dialog(QMessageBox::Question, "Сохранить изменения?", "Параметры детали изменены.",
                       QMessageBox::NoButton, this);
    auto save = dialog.addButton("Сохранить", QMessageBox::AcceptRole);
    auto discard = dialog.addButton("Не сохранять", QMessageBox::DestructiveRole);
    dialog.addButton("Отмена", QMessageBox::RejectRole);
    dialog.setDefaultButton(save);
    dialog.exec();
    if (dialog.clickedButton() == save)
    {
        saveDialog();
        return !modified;
    }
    return dialog.clickedButton() == discard;
}
void Window::resizeEvent(QResizeEvent* e)
{
    QMainWindow::resizeEvent(e);
    if (windowGrip)
    {
        windowGrip->move(rect().bottomRight() - windowGrip->rect().bottomRight());
        windowGrip->raise();
        statusBar()->setContentsMargins(0, 0, isMaximized() || isFullScreen() ? 0 : windowGrip->width(), 0);
    }
    updateStatusDetails();
}
void Window::closeEvent(QCloseEvent* e)
{
    if (confirmSave())
    {
        QSettings().setValue("window/size", size());
        e->accept();
    }
    else
        e->ignore();
}
bool Window::eventFilter(QObject* object, QEvent* e)
{
    if ((object == status || object == centralWidget()) &&
        (e->type() == QEvent::Resize || e->type() == QEvent::FontChange || e->type() == QEvent::PaletteChange))
        updateStatusDetails();
    if (object == statusDetailsTable && e->type() == QEvent::KeyPress &&
        static_cast<QKeyEvent*>(e)->matches(QKeySequence::Copy))
    {
        QStringList messages;
        for (int row = 0; row < statusDetailsTable->rowCount(); ++row)
            if (statusDetailsTable->selectionModel()->isRowSelected(row))
                messages.append((statusError ? QString::number(row + 1) + "\t" : QString()) +
                                statusDetailsTable->item(row, 1)->text());
        if (!messages.isEmpty())
            QApplication::clipboard()->setText(messages.join('\n'));
        return true;
    }
    if ((object == statusToggle || object == statusDetailsTable) && e->type() == QEvent::KeyPress &&
        static_cast<QKeyEvent*>(e)->key() == Qt::Key_Escape && statusToggle->isChecked())
    {
        statusToggle->setChecked(false);
        statusToggle->setFocus();
        return true;
    }
    if (e->type() == QEvent::MouseButtonPress && object->objectName() == "mainToolbar" &&
        static_cast<QMouseEvent*>(e)->button() == Qt::LeftButton)
    {
        pageButtons[pages->currentIndex()]->setFocus(Qt::MouseFocusReason);
        return true;
    }
    if (e->type() == QEvent::KeyPress)
    {
        const auto key = static_cast<QKeyEvent*>(e)->key();
        const auto fileButton = findChild<QToolButton*>("fileMenuButton");
        const auto fileMenu = fileButton->menu();
        if (const auto menu = qobject_cast<QMenu*>(object))
        {
            if (key == Qt::Key_Left || key == Qt::Key_Escape)
            {
                menu->close();
                if (menu == fileMenu)
                    fileButton->setFocus(Qt::OtherFocusReason);
                else if (const auto parent = qobject_cast<QMenu*>(menu->parentWidget()))
                {
                    parent->setFocus(Qt::OtherFocusReason);
                }
                return true;
            }
            const auto action = menu->activeAction();
            if ((key == Qt::Key_Right || key == Qt::Key_Return || key == Qt::Key_Enter) && action &&
                action->isEnabled() && !action->isSeparator())
            {
                if (const auto submenu = action->menu())
                {
                    submenu->popup(menu->mapToGlobal(menu->actionGeometry(action).topRight()));
                    auto actions = submenu->actions();
                    actions.removeIf([](QAction* item)
                                     { return item->isSeparator() || !item->isEnabled() || !item->isVisible(); });
                    auto selected =
                        std::find_if(actions.begin(), actions.end(), [](QAction* item) { return item->isChecked(); });
                    if (selected != actions.end())
                        submenu->setActiveAction(*selected);
                    else if (!actions.isEmpty())
                        submenu->setActiveAction(actions.first());
                    submenu->setFocus(Qt::OtherFocusReason);
                }
                else
                {
                    menu->close();
                    fileMenu->close();
                    fileButton->setFocus(Qt::OtherFocusReason);
                    action->trigger();
                }
                return true;
            }
        }
        if (object == fileButton && key == Qt::Key_Left)
            return true;
        if (object == fileButton && key == Qt::Key_Right)
        {
            pageButtons[0]->setFocus();
            return true;
        }
        if (object == fileButton &&
            (key == Qt::Key_Up || key == Qt::Key_Down || key == Qt::Key_Return || key == Qt::Key_Enter))
        {
            auto actions = fileMenu->actions();
            actions.removeIf([](QAction* item)
                             { return item->isSeparator() || !item->isEnabled() || !item->isVisible(); });
            const auto selected = actions.isEmpty() ? nullptr : key == Qt::Key_Up ? actions.last() : actions.first();
            fileMenu->popup(fileButton->mapToGlobal(QPoint(0, fileButton->height())));
            fileMenu->setActiveAction(selected);
            fileMenu->setFocus(Qt::OtherFocusReason);
            return true;
        }
        const auto index = pageButtons.indexOf(qobject_cast<QToolButton*>(object));
        if (index >= 0)
        {
            if (key == Qt::Key_Left || key == Qt::Key_Right)
            {
                if (index == 0 && key == Qt::Key_Left)
                {
                    fileButton->setFocus();
                    return true;
                }
                const auto next = std::clamp(static_cast<int>(index) + (key == Qt::Key_Right ? 1 : -1), 0, 2);
                selectPage(next);
                pageButtons[next]->setFocus();
                return true;
            }
            if (key == Qt::Key_Down || key == Qt::Key_Return)
            {
                selectPage(static_cast<int>(index));
                if (index == 0)
                    part->rows.first()->setFocus();
                else if (index == 1)
                    process->sections->setFocus();
                else
                    inspection->table->setFocus();
                return true;
            }
        }
    }
    return QMainWindow::eventFilter(object, e);
}
} // namespace pf::ui
