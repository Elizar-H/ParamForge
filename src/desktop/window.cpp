#include "window.h"
#include "core/geometry.h"
#include "files/projects.h"
#include <QActionGroup>
#include <QApplication>
#include <QButtonGroup>
#include <QCloseEvent>
#include <QDesktopServices>
#include <QDialogButtonBox>
#include <QFileDialog>
#include <QHBoxLayout>
#include <QKeyEvent>
#include <QMenu>
#include <QMessageBox>
#include <QRegularExpression>
#include <QScreen>
#include <QSettings>
#include <QShortcut>
#include <QSignalBlocker>
#include <QStandardPaths>
#include <QStatusBar>
#include <QStyle>
#include <QStyleHints>
#include <QUrl>
#include <QVBoxLayout>
#include <QWindow>
#include <QtConcurrent>
#include <cmath>

namespace pf::ui
{
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
    header->setSizePolicy(QSizePolicy::Expanding, QSizePolicy::Fixed);
    auto toolbar = new QHBoxLayout(header);
    toolbar->setContentsMargins(10, 6, 10, 6);
    toolbar->setSpacing(5);
    auto fileButton = new QToolButton(header);
    fileButton->setObjectName("fileMenuButton");
    fileButton->setText("Файл");
    fileButton->setPopupMode(QToolButton::InstantPopup);
    auto fileMenu = new QMenu(fileButton);
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
    status = new QPushButton(this);
    status->setFlat(true);
    status->setSizePolicy(QSizePolicy::Ignored, QSizePolicy::Fixed);
    status->setMinimumWidth(0);
    status->setObjectName("validationStatus");
    status->setStyleSheet("QPushButton { border:none; background:transparent; text-align:left; padding:2px 8px; }");
    statusBar()->addWidget(status, 1);
    statusBar()->setSizeGripEnabled(false);
    statusBar()->addPermanentWidget(controller);
    statusBar()->addPermanentWidget(exportButton);
    windowGrip = new ResizeGrip(this);
    windowGrip->setObjectName("windowResizeGrip");
    statusBar()->setContentsMargins(0, 0, windowGrip->width(), 0);
    connect(status, &QPushButton::clicked, this,
            [this]
            {
                if (errors.isEmpty())
                    return;
                const auto key = errors.first().field;
                for (const auto& d : dimensions())
                    if (key == d.nominal || key == d.lower || key == d.upper)
                    {
                        selectPage(0);
                        part->focusField(key);
                        return;
                    }
                selectPage(1);
                process->focusField(key);
            });
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
    if (expected.isString())
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
    status->setText(errors.isEmpty() ? QString() : errors.first().message);
    status->setToolTip(errors.isEmpty() ? QString() : errors.first().message);
    status->setStyleSheet(
        QString("QPushButton { border:none; background:transparent; text-align:left; padding:2px 8px; color:%1; }")
            .arg(dark() ? "#e69e9e" : "#a34343"));
}
void Window::schedule()
{
    ++revision;
    project.reset();
    inspection->setPending();
    if (!invalidDraft.isEmpty())
    {
        QVector<Error> invalid;
        for (auto i = invalidDraft.begin(); i != invalidDraft.end(); ++i)
            invalid.append({i.key(), i.value().message});
        showErrors(invalid);
        generationTimer.stop();
        return;
    }
    const auto invalid = validate(working);
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
        status->setText(QString::fromUtf8(e.what()));
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
        status->setText(QString::fromUtf8(e.what()));
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
        status->setText("Комплект сохранён");
        return true;
    }
    catch (const std::exception& e)
    {
        status->setText(QString::fromUtf8(e.what()));
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
    if (interactiveDrag || !invalidDraft.isEmpty())
    {
        status->setText("Завершите ввод параметров.");
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
    const auto folder = QFileDialog::getExistingDirectory(
        this, "Сохранить комплект документов",
        QSettings()
            .value("files/exportDirectory", QStandardPaths::writableLocation(QStandardPaths::DocumentsLocation))
            .toString());
    if (folder.isEmpty())
        return;
    if (exportTo(folder))
    {
        QSettings().setValue("files/exportDirectory", folder);
        showExportResult();
    }
    else
        explainFailure();
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
    if (e->type() == QEvent::KeyPress)
    {
        const auto key = static_cast<QKeyEvent*>(e)->key();
        const auto index = pageButtons.indexOf(qobject_cast<QToolButton*>(object));
        if (index >= 0)
        {
            if (key == Qt::Key_Left || key == Qt::Key_Right)
            {
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
