#include "inspection_view.h"
#include "controls.h"
#include <QHeaderView>
#include <QItemSelectionModel>
#include <QKeyEvent>
#include <QMouseEvent>
#include <QPainter>
#include <QPushButton>
#include <QResizeEvent>
#include <QSignalBlocker>
#include <QSplitter>
#include <QTextBlock>
#include <QTextEdit>
#include <QTimer>
#include <QVBoxLayout>
#include <QWheelEvent>
#include <cmath>

namespace pf::ui
{
void TransitionModel::setTransitions(const QVector<Transition>& value)
{
    beginResetModel();
    transitions = value;
    endResetModel();
}
int TransitionModel::rowCount(const QModelIndex& parent) const
{
    return parent.isValid() ? 0 : static_cast<int>(transitions.size());
}
int TransitionModel::columnCount(const QModelIndex& parent) const
{
    return parent.isValid() ? 0 : 7;
}
QVariant TransitionModel::headerData(int section, Qt::Orientation orientation, int role) const
{
    if (orientation == Qt::Horizontal && role == Qt::DisplayRole)
        return QStringList{"Переход", "Содержание", "Инстр.", "Проходы", "f, мм/об", "n, об/мин", "G1, мин"}.value(
            section);
    return {};
}
QVariant TransitionModel::data(const QModelIndex& index, int role) const
{
    if (!index.isValid() || index.row() >= transitions.size())
        return {};
    const auto& t = transitions[index.row()];
    if (role == Qt::TextAlignmentRole)
        return static_cast<int>((index.column() <= 1 ? Qt::AlignLeft : Qt::AlignRight) | Qt::AlignVCenter);
    if (role != Qt::DisplayRole)
        return {};
    switch (index.column())
    {
    case 0:
        return t.id;
    case 1:
        return t.title;
    case 2:
        return t.tool;
    case 3:
        return t.passes.size();
    case 4:
        return localized(t.feed);
    case 5:
        return localized(t.rpm);
    case 6:
        return localized(t.cutMinutes, 3);
    default:
        return {};
    }
}
TrajectoryCanvas::TrajectoryCanvas(QWidget* parent) : QWidget(parent)
{
    setObjectName("trajectoryCanvas");
    setMinimumSize(280, 220);
    setMouseTracking(true);
    setFocusPolicy(Qt::StrongFocus);
    setCursor(Qt::OpenHandCursor);
}
void TrajectoryCanvas::setProgram(const Parameters& p, const Program& value)
{
    parameters = p;
    program = value;
    if (view)
        viewport();
    update();
}
void TrajectoryCanvas::selectBlocks(const QSet<int>& value, int frame)
{
    selected = value;
    activeFrame = frame;
    update();
}
void TrajectoryCanvas::showRapids(bool value)
{
    rapids = value;
    update();
}
void TrajectoryCanvas::showAll(bool value)
{
    all = value;
    update();
}
void TrajectoryCanvas::clear()
{
    program.reset();
    segments.clear();
    update();
}
SetupMapping& TrajectoryCanvas::viewport()
{
    if (!view)
        view = fitSetup(rect().adjusted(10, 5, -10, -5), parameters, program->setup, true, true);
    view->zDirection = program->setup.flipped ? -1 : 1;
    view->zOffset = program->setup.flipped ? -calculate(parameters).targetLength : 0;
    return *view;
}
QPointF TrajectoryCanvas::projectPoint(double z, double radius) const
{
    return program && view ? view->map(z, radius) : QPointF();
}
void TrajectoryCanvas::resetView()
{
    view.reset();
    update();
}
void TrajectoryCanvas::resizeEvent(QResizeEvent* e)
{
    const QPointF offset((e->size().width() - e->oldSize().width()) / 2.,
                         (e->size().height() - e->oldSize().height()) / 2.);
    if (view)
        view->origin += offset;
    QWidget::resizeEvent(e);
}
namespace
{
// Clip before placing a direction arrow, so its tip remains on the visible part of a long move.
bool visibleSegment(QPointF& a, QPointF& b, const QRectF& bounds)
{
    const auto delta = b - a;
    double begin = 0, end = 1;
    const auto clip = [&](double p, double q)
    {
        if (p == 0)
            return q >= 0;
        const auto t = q / p;
        if (p < 0)
            begin = std::max(begin, t);
        else
            end = std::min(end, t);
        return begin <= end;
    };
    if (!clip(-delta.x(), a.x() - bounds.left()) || !clip(delta.x(), bounds.right() - a.x()) ||
        !clip(-delta.y(), a.y() - bounds.top()) || !clip(delta.y(), bounds.bottom() - a.y()))
        return false;
    b = a + end * delta;
    a += begin * delta;
    return true;
}
} // namespace
void TrajectoryCanvas::paintEvent(QPaintEvent*)
{
    QPainter p(this);
    p.fillRect(rect(), paper());
    p.setRenderHint(QPainter::Antialiasing);
    segments.clear();
    if (!program)
        return;
    const auto mapping =
        drawSetup(p, rect().adjusted(10, 5, -10, -5), parameters, program->setup, {}, true, viewport());
    const auto& moves = program->verification.moves;
    struct Arrow
    {
        QPointF tip, direction;
        QColor color;
    };
    QVector<Arrow> arrows;
    for (int layer = 0; layer < 2; ++layer)
        for (qsizetype i = 1; i < moves.size(); ++i)
        {
            const auto& a = moves[i - 1];
            const auto& b = moves[i];
            if (a.tool != b.tool)
                continue;
            const bool chosen = selected.contains(b.line) || b.line == activeFrame;
            if (chosen != (layer == 1))
                continue;
            if ((b.mode == "G0" && !rapids) || (!all && !chosen))
                continue;
            const auto start = mapping.map(a.z, a.x / 2), end = mapping.map(b.z, b.x / 2);
            QColor color = b.mode == "G0" ? rapidColor() : feedColor();
            if (!chosen && (!selected.isEmpty() || activeFrame))
                color = QColor::fromRgbF(color.redF() * .5 + paper().redF() * .5,
                                         color.greenF() * .5 + paper().greenF() * .5,
                                         color.blueF() * .5 + paper().blueF() * .5);
            QPen pen(color, 1.2, b.mode == "G0" ? Qt::DashLine : Qt::SolidLine);
            auto lineStart = start, lineEnd = end;
            if (b.mode == "G0")
            {
                if (lineStart.x() > lineEnd.x() ||
                    (qFuzzyCompare(lineStart.x(), lineEnd.x()) && lineStart.y() > lineEnd.y()))
                    std::swap(lineStart, lineEnd);
                const auto distance = QLineF(lineStart, lineEnd).length();
                if (distance > .1)
                {
                    const auto unit = (lineEnd - lineStart) / distance;
                    pen.setDashOffset((lineStart.x() * unit.x() + lineStart.y() * unit.y()) / pen.widthF());
                }
            }
            p.setPen(pen);
            p.drawLine(lineStart, lineEnd);
            segments.append({start, end, b.line});
            auto visibleStart = start, visibleEnd = end;
            if (!visibleSegment(visibleStart, visibleEnd, rect().adjusted(8, 8, -8, -8)))
                continue;
            const auto delta = visibleEnd - visibleStart;
            const auto distance = std::hypot(delta.x(), delta.y());
            if ((chosen || (selected.isEmpty() && !activeFrame)) && distance > 40)
            {
                const auto tip = visibleStart + delta * .65;
                bool nearby = false;
                for (const auto& arrow : arrows)
                    nearby |= QLineF(arrow.tip, tip).length() < 24;
                if (!nearby)
                    arrows.append({tip, delta / distance, color});
            }
        }
    for (const auto& arrow : arrows)
    {
        const auto u = arrow.direction;
        const QPointF n(-u.y(), u.x());
        p.setPen(QPen(arrow.color, 1.3, Qt::SolidLine, Qt::RoundCap, Qt::RoundJoin));
        p.setBrush(Qt::NoBrush);
        p.drawPolyline(QPolygonF{arrow.tip - u * 8 + n * 3.2, arrow.tip, arrow.tip - u * 8 - n * 3.2});
    }
}
void TrajectoryCanvas::mousePressEvent(QMouseEvent* e)
{
    if (!program || (e->button() != Qt::LeftButton && e->button() != Qt::MiddleButton))
        return;
    setFocus(Qt::MouseFocusReason);
    pressed = e->button();
    pressPoint = lastMouse = e->position();
    panning = false;
    grabMouse();
}
void TrajectoryCanvas::mouseReleaseEvent(QMouseEvent* e)
{
    if (pressed == Qt::NoButton)
        return;
    const bool select = pressed == Qt::LeftButton && !panning;
    pressed = Qt::NoButton;
    releaseMouse();
    setCursor(Qt::OpenHandCursor);
    if (!select)
        return;
    int line = 0;
    double best = 8;
    for (const auto& segment : segments)
    {
        const auto d = segment.b - segment.a;
        const auto denominator = d.x() * d.x() + d.y() * d.y();
        const auto t =
            denominator > 0 ? std::clamp(QPointF::dotProduct(e->position() - segment.a, d) / denominator, 0., 1.) : 0.;
        const auto nearest = segment.a + d * t;
        const auto delta = e->position() - nearest;
        const auto distance = std::hypot(delta.x(), delta.y());
        if (distance < best)
        {
            best = distance;
            line = segment.line;
        }
    }
    if (line)
        Q_EMIT blockClicked(line, e->modifiers());
    else
        Q_EMIT clearSelectionRequested();
}
void TrajectoryCanvas::mouseMoveEvent(QMouseEvent* e)
{
    if (pressed == Qt::NoButton || !program)
        return;
    if (!panning && (e->position() - pressPoint).manhattanLength() < 3)
        return;
    panning = true;
    viewport().origin += e->position() - lastMouse;
    lastMouse = e->position();
    setCursor(Qt::ClosedHandCursor);
    update();
}
void TrajectoryCanvas::mouseDoubleClickEvent(QMouseEvent*)
{
    resetView();
}
bool TrajectoryCanvas::event(QEvent* event)
{
    if ((event->type() == QEvent::UngrabMouse || event->type() == QEvent::WindowDeactivate ||
         event->type() == QEvent::Hide) &&
        pressed != Qt::NoButton)
    {
        pressed = Qt::NoButton;
        panning = false;
        releaseMouse();
        setCursor(Qt::OpenHandCursor);
    }
    return QWidget::event(event);
}
void TrajectoryCanvas::wheelEvent(QWheelEvent* e)
{
    if (!program || pressed != Qt::NoButton)
        return;
    auto& mapping = viewport();
    const auto next = std::clamp(mapping.scale * std::pow(1.15, e->angleDelta().y() / 120.), .1, 100.);
    mapping.origin = e->position() + (mapping.origin - e->position()) * (next / mapping.scale);
    mapping.scale = next;
    update();
    e->accept();
}
InspectionView::InspectionView(QWidget* parent) : QWidget(parent)
{
    auto layout = new QVBoxLayout(this);
    layout->setContentsMargins(14, 12, 14, 10);
    layout->setSpacing(8);
    auto toolbar = new QHBoxLayout;
    setups = new ChoiceList(this);
    setups->setObjectName("ncSetup");
    setups->addItem("010", "010");
    setups->addItem("020", "020");
    setups->setAccessibleName("Установ");
    toolbar->addWidget(setups);
    toolbar->addStretch();
    layout->addLayout(toolbar);
    auto vertical = new Splitter(Qt::Vertical, this);
    split = vertical;
    vertical->setChildrenCollapsible(false);
    layout->addWidget(vertical, 1);
    table = new QTableView(vertical);
    table->setObjectName("transitions");
    table->setMaximumHeight(320);
    transitions = new TransitionModel(this);
    table->setModel(transitions);
    table->setItemDelegate(new ItemDelegate(table));
    table->setSelectionBehavior(QAbstractItemView::SelectRows);
    table->setSelectionMode(QAbstractItemView::ExtendedSelection);
    table->setEditTriggers(QAbstractItemView::NoEditTriggers);
    table->verticalHeader()->hide();
    table->verticalHeader()->setDefaultSectionSize(33);
    table->setShowGrid(false);
    table->setAlternatingRowColors(false);
    table->horizontalHeader()->setStretchLastSection(false);
    table->horizontalHeader()->setSectionResizeMode(1, QHeaderView::Stretch);
    for (int i : {0, 2, 3, 4, 5, 6})
        table->horizontalHeader()->setSectionResizeMode(i, QHeaderView::ResizeToContents);
    auto bottom = new Splitter(Qt::Horizontal, vertical);
    bottom->setChildrenCollapsible(false);
    auto trajectoryPanel = new QWidget(bottom);
    trajectoryPanel->setObjectName("trajectoryPanel");
    auto trajectoryLayout = new QVBoxLayout(trajectoryPanel);
    trajectoryLayout->setContentsMargins(0, 0, 0, 0);
    trajectoryLayout->setSpacing(0);
    auto drawingToolbar = new QWidget(trajectoryPanel);
    drawingToolbar->setObjectName("trajectoryToolbar");
    auto drawingActions = new QHBoxLayout(drawingToolbar);
    drawingActions->setContentsMargins(8, 6, 8, 6);
    auto fit = new QToolButton(drawingToolbar);
    fit->setObjectName("fitTrajectory");
    fit->setText("Вписать");
    drawingActions->addWidget(fit);
    auto rapids = new QCheckBox("Холостые", drawingToolbar);
    rapids->setObjectName("showRapids");
    rapids->setChecked(true);
    drawingActions->addWidget(rapids);
    auto all = new QCheckBox("Все переходы", drawingToolbar);
    all->setObjectName("showAllTransitions");
    all->setChecked(true);
    drawingActions->addWidget(all);
    drawingActions->addStretch();
    trajectoryLayout->addWidget(drawingToolbar);
    trajectory = new TrajectoryCanvas(trajectoryPanel);
    trajectoryLayout->addWidget(trajectory, 1);
    code = new QPlainTextEdit(bottom);
    code->setObjectName("ncCode");
    code->setReadOnly(true);
    code->setLineWrapMode(QPlainTextEdit::NoWrap);
    code->setStyleSheet("QPlainTextEdit { font-family: 'Cascadia Mono', 'Consolas'; font-size: 10.5pt; }");
    bottom->addWidget(trajectoryPanel);
    bottom->addWidget(code);
    bottom->setSizes({700, 470});
    vertical->addWidget(table);
    vertical->addWidget(bottom);
    vertical->setSizes({240, 500});
    vertical->setStretchFactor(0, 0);
    vertical->setStretchFactor(1, 1);
    detail = new QLabel(this);
    detail->setObjectName("ncSelectionSummary");
    layout->addWidget(detail);
    connect(setups, &ChoiceList::currentIndexChanged, this, [this] { loadSetup(); });
    connect(table->selectionModel(), &QItemSelectionModel::selectionChanged, this, [this] { selectRows(); });
    connect(rapids, &QCheckBox::toggled, trajectory, &TrajectoryCanvas::showRapids);
    connect(all, &QCheckBox::toggled, trajectory, &TrajectoryCanvas::showAll);
    connect(fit, &QToolButton::clicked, trajectory, &TrajectoryCanvas::resetView);
    connect(code, &QPlainTextEdit::cursorPositionChanged, this,
            [this]
            {
                if (updating || !project || code->textCursor().hasSelection())
                    return;
                const auto physical = code->textCursor().blockNumber() + 1;
                for (const auto& m : project->programs[setups->currentIndex()].verification.moves)
                    if (m.physicalLine == physical)
                    {
                        selectFrame(m.line, true);
                        return;
                    }
            });
    connect(trajectory, &TrajectoryCanvas::blockClicked, this, [this](int line, Qt::KeyboardModifiers modifiers)
            { selectFrame(line, modifiers.testFlag(Qt::ControlModifier)); });
    connect(trajectory, &TrajectoryCanvas::clearSelectionRequested, this, &InspectionView::clearSelection);
    for (auto surface : {static_cast<QWidget*>(table), table->viewport(), static_cast<QWidget*>(code), code->viewport(),
                         static_cast<QWidget*>(trajectory), static_cast<QWidget*>(setups), static_cast<QWidget*>(fit),
                         static_cast<QWidget*>(rapids), static_cast<QWidget*>(all)})
        surface->installEventFilter(this);
}
bool InspectionView::eventFilter(QObject* object, QEvent* event)
{
    if (event->type() == QEvent::KeyPress && static_cast<QKeyEvent*>(event)->key() == Qt::Key_Left &&
        static_cast<QKeyEvent*>(event)->modifiers() == Qt::NoModifier && !setups->view()->isVisible())
    {
        Q_EMIT parentRequested();
        return true;
    }
    if ((event->type() == QEvent::KeyPress && static_cast<QKeyEvent*>(event)->key() == Qt::Key_Escape) ||
        (event->type() == QEvent::MouseButtonPress && static_cast<QMouseEvent*>(event)->button() == Qt::RightButton))
    {
        clearSelection();
        return true;
    }
    if (event->type() == QEvent::ContextMenu)
        return true;
    return QWidget::eventFilter(object, event);
}
void InspectionView::clearSelection()
{
    updating = true;
    selected.clear();
    activeFrame = 0;
    table->selectionModel()->clear();
    auto cursor = code->textCursor();
    cursor.clearSelection();
    code->setTextCursor(cursor);
    code->setExtraSelections({});
    trajectory->selectBlocks({}, 0);
    if (loadedSetup >= 0)
    {
        selectedBySetup[loadedSetup] = {};
        frameBySetup.remove(loadedSetup);
    }
    updating = false;
    detail->clear();
    if (pending)
        detail->setText("Показана предыдущая УП: параметры изменены.");
}
void InspectionView::showEvent(QShowEvent* event)
{
    QWidget::showEvent(event);
    QTimer::singleShot(0, this, &InspectionView::arrangePanels);
}
void InspectionView::arrangePanels()
{
    if (!arrangePending || !isVisible() || split->height() < 300)
        return;
    const int desired = std::min(table->maximumHeight(), split->height() / 3);
    split->setSizes({desired, split->height() - desired});
    arrangePending = false;
}
void InspectionView::setProject(const Project& value)
{
    if (project)
        for (int i = 0; i < value.programs.size(); ++i)
            if (i >= project->programs.size() || project->programs[i].text != value.programs[i].text)
            {
                frameBySetup.remove(i);
                if (loadedSetup == i)
                    activeFrame = 0;
            }
    project = value;
    pending = false;
    loadSetup();
}
void InspectionView::resetDocument()
{
    project.reset();
    selected.clear();
    selectedBySetup.clear();
    frameBySetup.clear();
    activeFrame = 0;
    loadedSetup = -1;
    trajectory->resetDocument();
    transitions->setTransitions({});
    code->clear();
    setups->setCurrentIndex(0);
    setPending();
}
void InspectionView::setPending()
{
    pending = true;
    trajectory->setEnabled(project.has_value());
    table->setEnabled(project.has_value());
    code->setEnabled(true);
    detail->setText(project ? "Показана предыдущая УП: параметры изменены." : "УП ещё не сформирована");
}
void InspectionView::loadSetup()
{
    if (!project)
        return;
    table->setEnabled(true);
    code->setEnabled(true);
    trajectory->setEnabled(true);
    if (loadedSetup >= 0)
    {
        selectedBySetup[loadedSetup] = selected;
        frameBySetup[loadedSetup] = activeFrame;
    }
    arrangePending |= loadedSetup < 0;
    loadedSetup = setups->currentIndex();
    selected = selectedBySetup.value(loadedSetup);
    const auto rememberedFrame = frameBySetup.value(loadedSetup);
    updating = true;
    activeFrame = 0;
    const auto& p =
        project->programs[std::clamp(setups->currentIndex(), 0, static_cast<int>(project->programs.size() - 1))];
    transitions->setTransitions(p.transitions);
    code->setPlainText(p.text);
    trajectory->setProgram(project->parameters, p);
    bool restored = false;
    for (qsizetype i = 0; i < p.transitions.size(); ++i)
        if (selected.contains(p.transitions[i].id))
        {
            table->selectionModel()->select(transitions->index(static_cast<int>(i), 0),
                                            QItemSelectionModel::Select | QItemSelectionModel::Rows);
            restored = true;
        }
    if (!restored && !selectedBySetup.contains(loadedSetup) && !p.transitions.isEmpty())
        table->selectRow(0);
    updating = false;
    selectRows();
    if (rememberedFrame)
        selectFrame(rememberedFrame, true);
    if (pending)
        detail->setText("Показана предыдущая УП: параметры изменены.");
    QTimer::singleShot(0, this, &InspectionView::arrangePanels);
}
void InspectionView::selectRows()
{
    if (updating || !project)
        return;
    selected.clear();
    activeFrame = 0;
    for (const auto& index : table->selectionModel()->selectedRows())
        if (index.row() < transitions->transitions.size())
            selected.insert(transitions->transitions[index.row()].id);
    highlightSelection();
}
void InspectionView::highlightSelection()
{
    if (!project)
        return;
    const auto& p = project->programs[setups->currentIndex()];
    QSet<int> blocks;
    double minutes = 0;
    int passCount = 0;
    for (const auto& t : p.transitions)
        if (selected.contains(t.id))
        {
            for (auto block : t.blocks)
                blocks.insert(block);
            minutes += t.cutMinutes;
            passCount += static_cast<int>(t.passes.size());
        }
    QList<QTextEdit::ExtraSelection> selections;
    for (const auto& m : p.verification.moves)
        if (blocks.contains(m.line) || m.line == activeFrame)
        {
            QTextEdit::ExtraSelection item;
            item.cursor = QTextCursor(code->document()->findBlockByNumber(m.physicalLine - 1));
            item.format.setBackground(selection());
            item.format.setProperty(QTextFormat::FullWidthSelection, true);
            selections.append(item);
        }
    code->setExtraSelections(selections);
    trajectory->selectBlocks(blocks, activeFrame);
    if (selected.size() == 1)
    {
        for (const auto& t : p.transitions)
            if (selected.contains(t.id))
                detail->setText(t.id + "  " + t.title + "     " + t.tool + "     f " + localized(t.feed) +
                                " мм/об     n " + localized(t.rpm) + " об/мин     G1 " + localized(minutes, 3) +
                                " мин");
    }
    else if (!selected.isEmpty())
        detail->setText("Переходов: " + QString::number(selected.size()) +
                        "     Проходов: " + QString::number(passCount) + "     G1 " + localized(minutes, 3) + " мин");
    else
        detail->clear();
    if (pending)
        detail->setText("Показана предыдущая УП: параметры изменены.");
}
void InspectionView::selectFrame(int line, bool keepSelection)
{
    if (!project)
        return;
    const auto& p = project->programs[setups->currentIndex()];
    QString id;
    int row = -1;
    for (int i = 0; i < p.transitions.size(); ++i)
        if (p.transitions[i].blocks.contains(line))
        {
            id = p.transitions[i].id;
            row = i;
            break;
        }
    if (row < 0)
        return;
    const bool retain = keepSelection && selected.contains(id);
    updating = true;
    if (!retain)
    {
        if (!keepSelection)
            table->clearSelection();
        table->selectionModel()->select(transitions->index(row, 0),
                                        QItemSelectionModel::Select | QItemSelectionModel::Rows);
    }
    selected.clear();
    for (const auto& index : table->selectionModel()->selectedRows())
        selected.insert(p.transitions[index.row()].id);
    activeFrame = line;
    for (const auto& m : p.verification.moves)
        if (m.line == line)
        {
            QTextCursor cursor(code->document()->findBlockByNumber(m.physicalLine - 1));
            code->setTextCursor(cursor);
            code->ensureCursorVisible();
            break;
        }
    updating = false;
    highlightSelection();
}
} // namespace pf::ui
