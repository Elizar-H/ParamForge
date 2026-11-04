#include "part_view.h"
#include "core/geometry.h"
#include <QApplication>
#include <QButtonGroup>
#include <QCheckBox>
#include <QKeyEvent>
#include <QMouseEvent>
#include <QPainter>
#include <QPainterPath>
#include <QResizeEvent>
#include <QSplitter>
#include <QWheelEvent>
#include <algorithm>
#include <cmath>

namespace pf::ui
{
namespace
{
QString caption(const Parameters& p, const QString& key)
{
    auto text = localized(p.n(key));
    if (key == "flangeDiameter" || key == "bodyDiameter" || key == "boreDiameter" || key == "counterboreDiameter")
        text.prepend("Ø");
    return text;
}
} // namespace
PartCanvas::PartCanvas(QWidget* parent) : QWidget(parent)
{
    setObjectName("partCanvas");
    setMinimumSize(320, 300);
    setFocusPolicy(Qt::StrongFocus);
    setMouseTracking(true);
}
void PartCanvas::setParameters(const Parameters& p)
{
    if (!drawablePart(p))
        return;
    bool changed = false;
    for (const auto& d : dimensions())
        changed |= parameters.n(d.nominal) != p.n(d.nominal);
    parameters = p;
    if (changed && viewReady)
        keepView();
    update();
}
QRectF PartCanvas::dimensionBounds(const QString& key) const
{
    return hits.value(key);
}
QPointF PartCanvas::projectPoint(double z, double radius) const
{
    return {width() / 2. + (parameters.n("length") / 2 + z) * scale, originY - radius * scale};
}
void PartCanvas::setShowTolerances(bool value)
{
    showTolerances = value;
    update();
}
void PartCanvas::paintEvent(QPaintEvent*)
{
    QPainter p(this);
    p.setRenderHint(QPainter::Antialiasing);
    p.fillRect(rect(), paper());
    hits.clear();
    const auto length = parameters.n("length"), diameter = parameters.n("flangeDiameter");
    if (!viewReady)
        resetView();
    else
        keepView();
    const auto map = [&](double z, double radius) { return projectPoint(z, radius); };
    const auto contour = axisContour(parameters);
    for (const int sign : {1, -1})
    {
        QPainterPath path;
        path.moveTo(map(contour[0].x(), contour[0].y() * sign));
        for (qsizetype i = 1; i < contour.size(); ++i)
            path.lineTo(map(contour[i].x(), contour[i].y() * sign));
        path.closeSubpath();
        drawHatching(p, path, projectPoint(0, 0), scale);
        p.setPen(QPen(ink(), 1.7));
        p.drawPath(path);
    }
    // A section also shows the circular edges behind its cutting plane.
    // Their projections join the material halves at the two faces and the bore shoulder.
    p.setPen(QPen(ink(), 1.7));
    for (const auto& edge : sectionProjectionEdges(parameters))
        p.drawLine(map(edge.x1(), edge.y1()), map(edge.x2(), edge.y2()));
    drawCenterLine(p, map(-length, 0) - QPointF(18, 0), map(0, 0) + QPointF(22, 0), scale);
    const auto labelSize = [&](const QString& key)
    {
        const auto metrics = p.fontMetrics();
        double deviationWidth = 0;
        if (showTolerances)
        {
            const auto& d = dimension(key);
            for (const auto& line : toleranceText(parameters.n(d.lower), parameters.n(d.upper)).split('\n'))
                deviationWidth = std::max(deviationWidth, double(metrics.horizontalAdvance(line)) + 6);
        }
        return QSizeF(metrics.horizontalAdvance(caption(parameters, key)) + deviationWidth + 10,
                      showTolerances ? metrics.height() * 1.65 : metrics.height() + 4);
    };
    const auto dimensionLine =
        [&](const QString& key, QPointF a, QPointF b, QPointF fromA, QPointF fromB, bool vertical)
    {
        p.setPen(QPen(muted(), .9));
        for (const auto& line : {QLineF(fromA, a), QLineF(fromB, b)})
            if (line.length() > 3)
            {
                const auto direction = (line.p2() - line.p1()) / line.length();
                p.drawLine(line.p1() + direction * 2, line.p2() + direction * 3);
            }
        drawDimensionArrow(p, a, b, true, 12);
        auto mid = (a + b) / 2;
        const auto text = caption(parameters, key);
        const auto& d = dimension(key);
        const auto lo = parameters.n(d.lower), hi = parameters.n(d.upper);
        p.save();
        p.setPen(ink());
        if (vertical)
        {
            p.translate(mid);
            p.rotate(-90);
            mid = {0, 0};
        }
        const auto size = labelSize(key);
        const auto labelWidth = size.width(), labelHeight = size.height();
        const auto deviationWidth = labelWidth - p.fontMetrics().horizontalAdvance(text) - 10;
        const auto bounds = QRectF(-labelWidth / 2., -labelHeight - 4, labelWidth, labelHeight).translated(mid);
        p.fillRect(bounds, paper());
        p.drawText(bounds.adjusted(5, 0, -deviationWidth - 5, 0), Qt::AlignCenter, text);
        if (showTolerances)
            drawDeviation(p, QRectF(bounds.right() - deviationWidth - 3, bounds.top(), deviationWidth, labelHeight), lo,
                          hi, ink());
        p.restore();
        const auto screenBounds =
            vertical ? QRectF((a + b) / 2 - QPointF(labelHeight + 4, labelWidth / 2.), QSizeF(labelHeight, labelWidth))
                     : bounds;
        hits[key] = screenBounds;
    };
    const auto maxR = diameter / 2, bodyR = parameters.n("bodyDiameter") / 2, boreR = parameters.n("boreDiameter") / 2,
               cbR = parameters.n("counterboreDiameter") / 2;
    const auto back = map(-length, 0).x(), front = map(0, 0).x(), top = map(0, maxR).y(), bottom = map(0, -maxR).y();
    const auto widthX = map(-parameters.n("flangeWidth"), 0).x(),
               depthX = map(-parameters.n("counterboreDepth"), 0).x();
    dimensionLine("length", {back, bottom + 42}, {front, bottom + 42}, map(-length, -bodyR), map(0, -maxR), false);
    const bool flangeWidthIsInner = parameters.n("flangeWidth") <= parameters.n("counterboreDepth");
    const auto widthY = top - (flangeWidthIsInner ? 25 : 61);
    const auto depthY = top - (flangeWidthIsInner ? 61 : 25);
    dimensionLine("flangeWidth", {widthX, widthY}, {front, widthY}, map(-parameters.n("flangeWidth"), maxR),
                  map(0, maxR), false);
    dimensionLine("counterboreDepth", {depthX, depthY}, {front, depthY},
                  map(-parameters.n("counterboreDepth"), flangeWidthIsInner ? bodyR : maxR), map(0, maxR), false);
    const auto insideDiameterFits = [&](const QString& key, double x, double start, double end, double radius)
    {
        const auto size = labelSize(key);
        const QRectF mask(x - size.height() - 4, map(0, 0).y() - size.width() / 2, size.height(), size.width());
        const QRectF opening(QPointF(start, map(0, radius).y()), QPointF(end, map(0, -radius).y()));
        return opening.adjusted(5, 5, -5, -5).contains(mask);
    };
    const auto innerBoreX = back + (depthX - back) * .42, innerCounterboreX = (depthX + front) / 2;
    const bool boreOutside = !insideDiameterFits("boreDiameter", innerBoreX, back, depthX, boreR);
    const bool counterboreOutside = !insideDiameterFits("counterboreDiameter", innerCounterboreX, depthX, front, cbR);
    const auto flangeLineX = front + (counterboreOutside ? 80 : 48);
    const auto bodyLineX = back - (boreOutside ? 78 : 40);
    dimensionLine("flangeDiameter", {flangeLineX, top}, {flangeLineX, bottom}, map(0, maxR), map(0, -maxR), true);
    dimensionLine("bodyDiameter", {bodyLineX, map(-length, bodyR).y()}, {bodyLineX, map(-length, -bodyR).y()},
                  map(-length, bodyR), map(-length, -bodyR), true);
    const auto xBore = boreOutside ? back - 34 : innerBoreX;
    const auto xCb = counterboreOutside ? front + 36 : innerCounterboreX;
    dimensionLine("boreDiameter", {xBore, map(0, boreR).y()}, {xBore, map(0, -boreR).y()},
                  map(boreOutside ? -length : (xBore - front) / scale, boreR),
                  map(boreOutside ? -length : (xBore - front) / scale, -boreR), true);
    dimensionLine("counterboreDiameter", {xCb, map(0, cbR).y()}, {xCb, map(0, -cbR).y()},
                  map(counterboreOutside ? 0 : (xCb - front) / scale, cbR),
                  map(counterboreOutside ? 0 : (xCb - front) / scale, -cbR), true);
    drawWorkOrigin(p, map(0, 0));
    drawCoordinateAxes(p, QPointF(28, height() - 24.));
}
void PartCanvas::resetView()
{
    constexpr double horizontalMargin = 119;
    const double verticalMargin = 61 + QFontMetrics(font()).height() * 1.65 + 12;
    const auto length = parameters.n("length"), diameter = parameters.n("flangeDiameter");
    scale = std::max(.1, std::min((width() - 2 * horizontalMargin) / std::max(length, 1.),
                                  (height() - 2 * verticalMargin) / std::max(diameter, 1.)));
    originY = verticalMargin + diameter * scale / 2;
    viewReady = true;
    update();
}
void PartCanvas::keepView()
{
    const auto length = parameters.n("length"), radius = parameters.n("flangeDiameter") / 2;
    const auto y = originY;
    if (!dragging)
    {
        const double margin = 61 + QFontMetrics(font()).height() * 1.65 + 12;
        const auto availableRadius = std::max(1., std::min(y - margin, height() - margin - y));
        scale = std::max(.1, std::min({scale, (width() - 238.) / length, availableRadius / radius}));
    }
}
void PartCanvas::resizeEvent(QResizeEvent* e)
{
    QWidget::resizeEvent(e);
    if (!dragging)
        viewReady = false;
}
QPointF PartCanvas::edgePoint(const QString& key, int side) const
{
    const auto l = parameters.n("length"), w = parameters.n("flangeWidth"), h = parameters.n("counterboreDepth");
    const auto flange = parameters.n("flangeDiameter") / 2, body = parameters.n("bodyDiameter") / 2;
    const auto bore = parameters.n("boreDiameter") / 2, cb = parameters.n("counterboreDiameter") / 2;
    if (key == "length")
        return projectPoint(-l, side * (body + bore) / 2);
    if (key == "flangeWidth")
        return projectPoint(-w, side * (flange + body) / 2);
    if (key == "counterboreDepth")
        return projectPoint(-h, side * (cb + bore) / 2);
    if (key == "flangeDiameter")
        return projectPoint(-w / 2, side * flange);
    if (key == "bodyDiameter")
        return projectPoint(-(l + w) / 2, side * body);
    if (key == "boreDiameter")
        return projectPoint(-(l + h) / 2, side * bore);
    return projectPoint(-h / 2, side * cb);
}
QVector<std::pair<QString, int>> PartCanvas::edgesAt(QPointF pos) const
{
    QVector<std::pair<QString, int>> result;
    QVector<std::pair<double, std::pair<QString, int>>> nearest;
    const auto contour = axisContour(parameters);
    const QStringList keys{"flangeDiameter", "flangeWidth",      "bodyDiameter",       "length",
                           "boreDiameter",   "counterboreDepth", "counterboreDiameter"};
    for (int side : {1, -1})
        for (int i = 0; i < keys.size(); ++i)
        {
            const auto a = projectPoint(contour[i].x(), side * contour[i].y());
            const auto b = projectPoint(contour[i + 1].x(), side * contour[i + 1].y());
            const auto delta = b - a;
            const auto denominator = QPointF::dotProduct(delta, delta);
            const auto t = denominator > 0 ? std::clamp(QPointF::dotProduct(pos - a, delta) / denominator, 0., 1.) : 0.;
            const auto distance = QLineF(pos, a + t * delta).length();
            if (distance <= 7)
                nearest.append({distance, {keys[i], side}});
        }
    std::stable_sort(nearest.begin(), nearest.end(), [](const auto& a, const auto& b) { return a.first < b.first; });
    for (const auto& edge : nearest)
        result.append(edge.second);
    return result;
}
namespace
{
bool axial(const QString& key)
{
    return key == "length" || key == "flangeWidth" || key == "counterboreDepth";
}
} // namespace
double PartCanvas::constrainedValue(const QString& key, double requested, double step) const
{
    const auto& f = field(key);
    requested = std::clamp(roundTo(requested), f.minimum, f.maximum);
    const auto valid = [&](double value)
    {
        try
        {
            auto candidate = baseline;
            candidate.set(key, value);
            return drawablePart(resolveTolerances(candidate));
        }
        catch (const std::exception&)
        {
            return false;
        }
    };
    if (valid(requested))
        return requested;
    const auto start = baseline.n(key);
    // Stop on the last valid grid step; the gesture can never turn a wall inside out.
    qint64 low = 0, high = static_cast<qint64>(std::ceil(std::abs(requested - start) / step));
    const double direction = requested > start ? 1 : -1;
    while (low < high)
    {
        const auto mid = (low + high + 1) / 2;
        if (valid(roundTo(start + direction * mid * step)))
            low = mid;
        else
            high = mid - 1;
    }
    return roundTo(start + direction * low * step);
}
void PartCanvas::mousePressEvent(QMouseEvent* e)
{
    if (e->button() != Qt::LeftButton)
        return;
    down = e->position();
    moved = false;
    dragKey.clear();
    pressedDimension.clear();
    candidates.clear();
    if (e->button() == Qt::LeftButton)
    {
        for (auto i = hits.begin(); i != hits.end(); ++i)
            if (i.value().contains(down))
            {
                pressedDimension = i.key();
                return;
            }
        if (dragAllowed)
            candidates = edgesAt(down);
    }
    dragging = !candidates.isEmpty();
    if (!dragging)
        return;
    baseline = parameters;
    dragKey = candidates.first().first;
    dragSide = candidates.first().second;
    setFocus();
    grabMouse();
}
void PartCanvas::mouseMoveEvent(QMouseEvent* e)
{
    if (!pressedDimension.isEmpty())
    {
        if ((e->position() - down).manhattanLength() >= QApplication::startDragDistance())
            pressedDimension.clear();
        return;
    }
    if (!dragging)
    {
        const auto edges = dragAllowed ? edgesAt(e->position()) : QVector<std::pair<QString, int>>{};
        bool label = false;
        for (const auto& hit : hits)
            label |= hit.contains(e->position());
        setCursor(label                        ? Qt::IBeamCursor
                  : edges.isEmpty()            ? Qt::ArrowCursor
                  : axial(edges.first().first) ? Qt::SizeHorCursor
                                               : Qt::SizeVerCursor);
        return;
    }
    const auto delta = e->position() - down;
    if (!moved)
    {
        if (std::hypot(delta.x(), delta.y()) < 3)
            return;
        for (const auto& candidate : candidates)
            if (axial(candidate.first) == (std::abs(delta.x()) >= std::abs(delta.y())))
            {
                dragKey = candidate.first;
                dragSide = candidate.second;
                break;
            }
        moved = true;
        Q_EMIT dragStarted();
    }
    setCursor(axial(dragKey) ? Qt::SizeHorCursor : Qt::SizeVerCursor);
    const auto change =
        axial(dragKey) ? -delta.x() / scale * (dragKey == "length" ? 2 : 1) : -2 * dragSide * delta.y() / scale;
    const auto step = e->modifiers().testFlag(Qt::ControlModifier) ? .01
                      : e->modifiers().testFlag(Qt::ShiftModifier) ? .1
                                                                   : 1.;
    const auto value = constrainedValue(dragKey, baseline.n(dragKey) + std::round(change / step) * step, step);
    if (value != parameters.n(dragKey))
        Q_EMIT nominalDragged(dragKey, value);
}
void PartCanvas::mouseReleaseEvent(QMouseEvent* e)
{
    if (e->button() != Qt::LeftButton)
        return;
    const auto dimension = pressedDimension;
    pressedDimension.clear();
    if (!dimension.isEmpty())
    {
        if (hits.value(dimension).contains(e->position()) &&
            (e->position() - down).manhattanLength() < QApplication::startDragDistance())
            Q_EMIT editRequested(dimension);
        return;
    }
    const bool changed = dragging && moved;
    if (!dragging)
        return;
    dragging = false;
    releaseMouse();
    unsetCursor();
    if (changed)
        keepView();
    if (changed)
        Q_EMIT dragFinished();
}
void PartCanvas::keyPressEvent(QKeyEvent* e)
{
    if (e->key() == Qt::Key_Escape && dragging)
    {
        const bool changed = moved;
        dragging = false;
        releaseMouse();
        parameters = baseline;
        keepView();
        if (changed)
            Q_EMIT dragCancelled(baseline);
        update();
        return;
    }
    if (e->key() == Qt::Key_Escape)
        pressedDimension.clear();
    QWidget::keyPressEvent(e);
}
bool PartCanvas::event(QEvent* event)
{
    if (event->type() == QEvent::UngrabMouse || event->type() == QEvent::WindowDeactivate ||
        event->type() == QEvent::Hide)
        pressedDimension.clear();
    if ((event->type() == QEvent::UngrabMouse || event->type() == QEvent::WindowDeactivate ||
         event->type() == QEvent::Hide) &&
        dragging)
    {
        const bool changed = dragging && moved;
        dragging = false;
        releaseMouse();
        unsetCursor();
        if (changed)
            keepView();
        if (changed)
            Q_EMIT dragFinished();
    }
    return QWidget::event(event);
}
void PartCanvas::wheelEvent(QWheelEvent* e)
{
    e->accept();
}
PartView::PartView(QWidget* parent) : QWidget(parent)
{
    auto layout = new QHBoxLayout(this);
    layout->setContentsMargins(0, 0, 0, 0);
    auto sidebar = new QWidget(this);
    sidebar->setFixedWidth(336);
    auto sidebarLayout = new QVBoxLayout(sidebar);
    sidebarLayout->setContentsMargins(12, 10, 0, 12);
    sidebarLayout->setSpacing(6);
    layout->addWidget(sidebar);
    auto headingsWidget = new QWidget(sidebar);
    headingsWidget->setFixedHeight(36);
    sidebarLayout->addWidget(headingsWidget);
    auto panel = new QWidget(sidebar);
    panel->setObjectName("partParameters");
    auto panelLayout = new QVBoxLayout(panel);
    panelLayout->setContentsMargins(0, 0, 0, 0);
    panelLayout->setSpacing(0);
    sidebarLayout->addWidget(panel, 1);
    scroll = new QScrollArea(panel);
    scroll->setWidgetResizable(true);
    auto formWidget = new QWidget(scroll);
    form = new QVBoxLayout(formWidget);
    form->setContentsMargins(0, 0, 0, 0);
    form->setSpacing(2);
    scroll->setWidget(formWidget);
    panelLayout->addWidget(scroll, 1);
    auto collapsedSpace = new QWidget(panel);
    collapsedSpace->hide();
    panelLayout->addWidget(collapsedSpace, 1);
    auto collapse = new QToolButton(sidebar);
    collapse->setObjectName("collapseParameters");
    collapse->setArrowType(Qt::LeftArrow);
    collapse->setToolTip("Свернуть параметры");
    collapse->setFixedSize(28, 24);
    panelLayout->addWidget(collapse, 0, Qt::AlignRight);
    connect(collapse, &QToolButton::clicked, this,
            [this, sidebar, collapse, headingsWidget, collapsedSpace]
            {
                const bool show = scroll->isHidden();
                closeDimension(false);
                scroll->setVisible(show);
                collapsedSpace->setVisible(!show);
                sidebar->setFixedWidth(show ? 336 : 40);
                for (auto label : headingsWidget->findChildren<QLabel*>())
                    label->setVisible(show);
                collapse->setArrowType(show ? Qt::LeftArrow : Qt::RightArrow);
                collapse->setToolTip(show ? "Свернуть параметры" : "Показать параметры");
            });
    auto headings = new QHBoxLayout(headingsWidget);
    headings->setContentsMargins(6, 0, 6, 2);
    headings->addWidget(new QLabel("Параметр", headingsWidget), 1);
    auto valueHeader = new QLabel("Размер", headingsWidget);
    valueHeader->setFixedWidth(128);
    valueHeader->setAlignment(Qt::AlignCenter);
    headings->addWidget(valueHeader);
    for (const auto& d : dimensions())
    {
        auto item = new ParameterRow(field(d.nominal), true, formWidget);
        rows.append(item);
        form->addWidget(item);
        connect(item, &ParameterRow::edited, this, &PartView::numberEdited);
        connect(item, &ParameterRow::parentRequested, this, &PartView::parentRequested);
        connect(item, &ParameterRow::dimensionRequested, this, &PartView::openDimension);
        connect(item, &ParameterRow::replaceRequested, this,
                [this](const QString& key, const QString& text)
                {
                    openDimension(key);
                    if (dimensionPane)
                    {
                        QKeyEvent event(QEvent::KeyPress, 0, Qt::NoModifier, text);
                        QApplication::sendEvent(dimensionPane->nominal, &event);
                    }
                });
        connect(item, &ParameterRow::navigate, this,
                [this, item](int delta)
                {
                    const auto index = (rows.indexOf(item) + delta + rows.size()) % rows.size();
                    rows[index]->setFocus();
                });
    }
    form->addStretch();
    auto preview = new QWidget(this);
    auto previewLayout = new QVBoxLayout(preview);
    previewLayout->setContentsMargins(12, 10, 12, 12);
    previewLayout->setSpacing(6);
    auto toolbarWidget = new QWidget(preview);
    toolbarWidget->setFixedHeight(36);
    auto toolbar = new QHBoxLayout(toolbarWidget);
    toolbar->setContentsMargins(0, 0, 0, 0);
    auto section = new QCheckBox("Разрез", this);
    section->setObjectName("modelSection");
    section->setVisible(false);
    toolbar->addStretch();
    auto tolerances = new QCheckBox("Допуски", this);
    tolerances->setObjectName("drawingTolerances");
    tolerances->setChecked(true);
    auto options = new QStackedWidget(toolbarWidget);
    options->addWidget(tolerances);
    options->addWidget(section);
    options->setSizePolicy(QSizePolicy::Fixed, QSizePolicy::Preferred);
    toolbar->addWidget(options);
    auto group = new QButtonGroup(this);
    for (int i = 0; i < 2; ++i)
    {
        auto b = new QToolButton(this);
        b->setText(i == 0 ? "2D" : "3D");
        b->setCheckable(true);
        b->setChecked(i == 0);
        b->setObjectName(i == 0 ? "view2D" : "view3D");
        group->addButton(b, i);
        toolbar->addWidget(b);
    }
    previewLayout->addWidget(toolbarWidget);
    views = new QStackedWidget(preview);
    canvas = new PartCanvas(views);
    connect(tolerances, &QCheckBox::toggled, canvas, &PartCanvas::setShowTolerances);
    model = new Part3D(views);
    views->addWidget(canvas);
    views->addWidget(model);
    previewLayout->addWidget(views, 1);
    connect(group, &QButtonGroup::idClicked, this,
            [this, section](int i)
            {
                views->setCurrentIndex(i);
                section->setVisible(i == 1);
            });
    connect(views, &QStackedWidget::currentChanged, this,
            [this, group, section, tolerances, options](int i)
            {
                options->setCurrentIndex(i);
                if (group->button(i))
                    group->button(i)->setChecked(true);
                section->setVisible(i == 1);
                tolerances->setVisible(i == 0);
                if (i == 1)
                    model->setParameters(validParameters);
            });
    connect(section, &QCheckBox::toggled, model, &Part3D::setSection);
    connect(canvas, &PartCanvas::nominalDragged, this, &PartView::nominalDragged);
    connect(canvas, &PartCanvas::editRequested, this, &PartView::focusField);
    connect(canvas, &PartCanvas::dragStarted, this, &PartView::dragStarted);
    connect(canvas, &PartCanvas::dragFinished, this, &PartView::dragFinished);
    connect(canvas, &PartCanvas::dragCancelled, this, &PartView::dragCancelled);
    layout->addWidget(preview, 1);
    qApp->installEventFilter(this);
    setParameters(parameters);
}
void PartView::setParameters(const Parameters& p)
{
    parameters = p;
    for (auto row : rows)
        row->setParameters(p);
    if (drawablePart(p))
        validParameters = p;
    canvas->setParameters(validParameters);
    if (views->currentIndex() == 1 && !canvas->isDragging())
        model->setParameters(validParameters);
    if (dimensionPane)
        dimensionPane->setParameters(p);
}
void PartView::setErrors(const QVector<Error>& errors)
{
    for (auto row : rows)
    {
        bool bad = false;
        const auto& d = dimension(row->key);
        for (const auto& e : errors)
            if (e.field == row->key || e.field == d.lower || e.field == d.upper)
                bad = true;
        row->setError(bad);
    }
}
void PartView::focusField(const QString& key)
{
    for (auto row : rows)
    {
        const auto& descriptor = dimension(row->key);
        if (key == descriptor.nominal || key == descriptor.lower || key == descriptor.upper)
        {
            scroll->ensureWidgetVisible(row);
            if (!dimensionPane || openedDimension != row->key)
                openDimension(row->key);
            (key == descriptor.lower   ? dimensionPane->lower
             : key == descriptor.upper ? dimensionPane->upper
                                       : dimensionPane->nominal)
                ->setFocus();
            return;
        }
    }
}
void PartView::openDimension(const QString& key)
{
    if (scroll->isHidden())
    {
        findChild<QToolButton*>("collapseParameters")->click();
        layout()->activate();
    }
    if (dimensionPane && openedDimension == key)
    {
        closeDimension();
        return;
    }
    closeDimension(false);
    const auto iterator = std::find_if(rows.begin(), rows.end(), [&](auto row) { return row->key == key; });
    if (iterator == rows.end())
        return;
    openedDimension = key;
    dimensionPane = new DimensionPane(dimension(key), parameters, this);
    dimensionPane->adjustSize();
    dimensionPane->setFixedSize(dimensionPane->sizeHint());
    auto anchor = (*iterator)->mapTo(this, QPoint((*iterator)->width(), 0));
    anchor.setX(std::min(anchor.x() + 8, width() - dimensionPane->width() - 8));
    anchor.setY(std::clamp(anchor.y(), 8, std::max(8, height() - dimensionPane->height() - 8)));
    dimensionPane->move(anchor);
    connect(dimensionPane, &DimensionPane::designationEdited, this, &PartView::designationEdited);
    connect(dimensionPane, &DimensionPane::numberEdited, this, &PartView::numberEdited);
    connect(dimensionPane, &DimensionPane::closeRequested, this, [this] { closeDimension(); });
    dimensionPane->show();
    dimensionPane->raise();
    Q_EMIT dimensionOpened();
    dimensionPane->nominal->setFocus();
}
void PartView::closeDimension(bool restoreFocus)
{
    if (!dimensionPane)
        return;
    auto old = dimensionPane;
    dimensionPane = nullptr;
    old->hide();
    old->deleteLater();
    if (restoreFocus)
        for (auto row : rows)
            if (row->key == openedDimension)
                row->setFocus();
}
bool PartView::eventFilter(QObject* object, QEvent* event)
{
    if (event->type() == QEvent::MouseButtonPress &&
        static_cast<QMouseEvent*>(event)->button() == Qt::LeftButton && scroll->isVisible() &&
        (object == scroll->widget() || object == scroll->viewport() || object == scroll ||
         object == scroll->parentWidget()))
    {
        closeDimension(false);
        rows.first()->setFocus(Qt::MouseFocusReason);
        return true;
    }
    if (dimensionPane && event->type() == QEvent::MouseButtonPress)
    {
        auto target = qobject_cast<QWidget*>(object);
        bool owned = false;
        for (auto owner = object; owner; owner = owner->parent())
            owned |= owner == dimensionPane;
        if (target && !owned)
        {
            bool sameToggle = false;
            for (auto row : rows)
                if (row->key == openedDimension && (target == row->dimensionValue || target == row))
                    sameToggle = true;
            if (!sameToggle)
                closeDimension(false);
        }
    }
    if (dimensionPane && event->type() == QEvent::ApplicationDeactivate)
        closeDimension(false);
    return QWidget::eventFilter(object, event);
}
void PartView::resizeEvent(QResizeEvent* event)
{
    closeDimension(false);
    QWidget::resizeEvent(event);
}
void PartView::hideEvent(QHideEvent* event)
{
    closeDimension(false);
    QWidget::hideEvent(event);
}
} // namespace pf::ui
