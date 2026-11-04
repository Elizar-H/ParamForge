#include "controls.h"
#include <QAbstractItemView>
#include <QApplication>
#include <QClipboard>
#include <QContextMenuEvent>
#include <QEvent>
#include <QFocusEvent>
#include <QGridLayout>
#include <QHeaderView>
#include <QInputMethodEvent>
#include <QKeyEvent>
#include <QLabel>
#include <QMenu>
#include <QMouseEvent>
#include <QPainter>
#include <QPainterPath>
#include <QScreen>
#include <QSettings>
#include <QSignalBlocker>
#include <QStyleFactory>
#include <QStyleHints>
#include <QStylePainter>
#include <QStyledItemDelegate>
#include <QTextLayout>
#include <QWheelEvent>
#include <cmath>

namespace pf::ui
{
bool dark()
{
    return qApp->palette().color(QPalette::Base).lightness() < 128;
}
QColor ink()
{
    return qApp->palette().color(QPalette::Text);
}
QColor muted()
{
    return dark() ? QColor("#aaaaaa") : QColor("#626262");
}
QColor border()
{
    return dark() ? QColor("#494949") : QColor("#cccccc");
}
QColor accent()
{
    return dark() ? QColor("#58a8dd") : QColor("#17669a");
}
QColor rapidColor()
{
    return dark() ? QColor("#ef7770") : QColor("#b4342e");
}
QColor feedColor()
{
    return dark() ? QColor("#64c586") : QColor("#1c7d42");
}
QColor contourColor()
{
    return dark() ? QColor("#6fa9d4") : QColor("#376d99");
}
QColor paper()
{
    return qApp->palette().color(QPalette::Base);
}
QColor selection()
{
    return dark() ? QColor("#414141") : QColor("#dedede");
}
namespace
{
class SplitterHandle final : public QSplitterHandle
{
  public:
    SplitterHandle(Qt::Orientation orientation, QSplitter* parent) : QSplitterHandle(orientation, parent) {}

  protected:
    void paintEvent(QPaintEvent*) override
    {
        QPainter painter(this);
        painter.fillRect(rect(), underMouse() ? selection() : palette().color(QPalette::Window));
        painter.setRenderHint(QPainter::Antialiasing);
        painter.setPen(Qt::NoPen);
        painter.setBrush(underMouse() ? ink() : muted());
        const auto center = QRectF(rect()).center();
        const auto grip = orientation() == Qt::Horizontal ? QRectF(center.x() - 1, center.y() - 14, 2, 28)
                                                          : QRectF(center.x() - 14, center.y() - 1, 28, 2);
        painter.drawRoundedRect(grip, 1, 1);
    }
    bool event(QEvent* event) override
    {
        if (event->type() == QEvent::Enter || event->type() == QEvent::Leave)
            update();
        return QSplitterHandle::event(event);
    }
};
} // namespace
Splitter::Splitter(QWidget* parent) : Splitter(Qt::Horizontal, parent) {}
Splitter::Splitter(Qt::Orientation orientation, QWidget* parent) : QSplitter(orientation, parent)
{
    setHandleWidth(8);
}
QSplitterHandle* Splitter::createHandle()
{
    return new SplitterHandle(orientation(), this);
}
ResizeGrip::ResizeGrip(QWidget* parent) : QSizeGrip(parent)
{
    setFixedSize(16, 16);
    setAccessibleName("Изменить размер окна");
}
void ResizeGrip::paintEvent(QPaintEvent*)
{
    QPainter painter(this);
    painter.setRenderHint(QPainter::Antialiasing);
    painter.setPen(QPen(underMouse() ? ink() : muted(), 1.5, Qt::SolidLine, Qt::RoundCap));
    if (layoutDirection() == Qt::RightToLeft)
    {
        painter.translate(width(), 0);
        painter.scale(-1, 1);
    }
    painter.drawLine(QPointF(4, 13), QPointF(13, 4));
    painter.drawLine(QPointF(9, 13), QPointF(13, 9));
}
bool ResizeGrip::event(QEvent* event)
{
    if (event->type() == QEvent::Enter || event->type() == QEvent::Leave)
        update();
    return QSizeGrip::event(event);
}
QString toleranceText(double lower, double upper)
{
    const auto sign = [](double v)
    { return (v > 0 ? QString("+") : QString()) + localized(v).replace('-', QChar(0x2212)); };
    if (lower == -upper)
        return upper == 0 ? QString("0") : "±" + localized(upper);
    return sign(upper) + "\n" + sign(lower);
}
void drawDeviation(QPainter& p, const QRectF& bounds, double lower, double upper, const QColor& color)
{
    p.save();
    p.setPen(color);
    const auto text = toleranceText(lower, upper);
    if (text.contains('\n'))
    {
        auto font = p.font();
        font.setPointSizeF(font.pointSizeF() * .95);
        p.setFont(font);
        const auto lines = text.split('\n');
        const auto height = p.fontMetrics().height();
        const auto top = bounds.center().y() - height;
        p.drawText(QRectF(bounds.left(), top, bounds.width(), height), Qt::AlignLeft | Qt::AlignVCenter, lines[0]);
        p.drawText(QRectF(bounds.left(), top + height, bounds.width(), height), Qt::AlignLeft | Qt::AlignVCenter,
                   lines[1]);
    }
    else
        p.drawText(bounds, Qt::AlignLeft | Qt::AlignVCenter, text);
    p.restore();
}
void DimensionButton::setValue(const QString& text, double lower, double upper)
{
    lo = lower;
    hi = upper;
    setNominalText(text);
}
void DimensionButton::setNominalText(const QString& text)
{
    nominal = text;
    setText(nominal + " " + toleranceText(lo, hi));
    update();
}
void DimensionButton::paintEvent(QPaintEvent*)
{
    QStylePainter p(this);
    QStyleOptionToolButton option;
    initStyleOption(&option);
    option.text.clear();
    p.drawComplexControl(QStyle::CC_ToolButton, option);
    const auto metrics = p.fontMetrics();
    const auto nominalWidth = metrics.horizontalAdvance(nominal);
    double deviationWidth = 0;
    for (const auto& line : toleranceText(lo, hi).split('\n'))
        deviationWidth = std::max(deviationWidth, double(metrics.horizontalAdvance(line)));
    constexpr double left = 12;
    p.setPen(palette().color(QPalette::ButtonText));
    p.drawText(QRectF(left, 0, nominalWidth, height()), Qt::AlignVCenter | Qt::AlignLeft, nominal);
    drawDeviation(p, QRectF(left + nominalWidth + 5, 0, deviationWidth, height()), lo, hi,
                  palette().color(QPalette::ButtonText));
}
void ItemDelegate::paint(QPainter* p, const QStyleOptionViewItem& option, const QModelIndex& index) const
{
    auto clean = option;
    clean.state &= ~QStyle::State_HasFocus;
    QStyledItemDelegate::paint(p, clean, index);
}
namespace
{
class ChoiceDelegate final : public QStyledItemDelegate
{
  public:
    using QStyledItemDelegate::QStyledItemDelegate;
    QSize sizeHint(const QStyleOptionViewItem& option, const QModelIndex& index) const override
    {
        return {QFontMetrics(option.font).horizontalAdvance(index.data().toString()) + 20, 30};
    }
    void paint(QPainter* p, const QStyleOptionViewItem& option, const QModelIndex& index) const override
    {
        p->save();
        const bool selected = option.state.testFlag(QStyle::State_Selected);
        p->fillRect(option.rect, option.palette.color(selected ? QPalette::Highlight : QPalette::Base));
        p->setFont(option.font);
        p->setPen(option.palette.color(selected ? QPalette::HighlightedText : QPalette::Text));
        p->drawText(option.rect.adjusted(10, 0, -10, 0), Qt::AlignVCenter | Qt::AlignLeft, index.data().toString());
        p->restore();
    }
};
} // namespace
ChoiceList::ChoiceList(QWidget* parent) : QComboBox(parent)
{
    setSizePolicy(QSizePolicy::Fixed, QSizePolicy::Fixed);
    setSizeAdjustPolicy(QComboBox::AdjustToContents);
    setMaxVisibleItems(12);
    view()->installEventFilter(this);
    view()->viewport()->installEventFilter(this);
    view()->setVerticalScrollMode(QAbstractItemView::ScrollPerItem);
    view()->setItemDelegate(new ChoiceDelegate(view()));
    view()->setStyleSheet("QAbstractItemView { padding: 0; outline: none; }");
}
void ChoiceList::showPopup()
{
    QComboBox::showPopup();
    auto popup = view()->window();
    const auto below = mapToGlobal(QPoint(0, height() + 2));
    const auto above = mapToGlobal(QPoint(0, -2));
    const auto screenBounds = screen()->availableGeometry();
    auto bounds = popup->geometry();
    int contentHeight = 0;
    for (int row = 0; row < count(); ++row)
        contentHeight += view()->sizeHintForRow(row);
    const int fullHeight = contentHeight + std::max(2, bounds.height() - view()->viewport()->height());
    const int roomBelow = std::max(0, screenBounds.bottom() - below.y() + 1);
    const int roomAbove = std::max(0, above.y() - screenBounds.top());
    const bool openAbove = (property("popupAbove").toBool() && roomAbove >= fullHeight) ||
                           (roomBelow < fullHeight && roomAbove > roomBelow);
    bounds.setWidth(std::max(width(), bounds.width()));
    bounds.setHeight(std::min(fullHeight, std::max(1, openAbove ? roomAbove : roomBelow)));
    bounds.moveTopLeft(QPoint(std::clamp(below.x(), screenBounds.left(),
                                         std::max(screenBounds.left(), screenBounds.right() - bounds.width() + 1)),
                              openAbove ? above.y() - bounds.height() : below.y()));
    popup->setGeometry(bounds);
}
bool ChoiceList::eventFilter(QObject* object, QEvent* event)
{
    if (view()->isVisible() && event->type() == QEvent::KeyPress)
    {
        const auto key = static_cast<QKeyEvent*>(event);
        if (key->modifiers() == Qt::NoModifier)
        {
            const bool confirm =
                key->key() == Qt::Key_Right || key->key() == Qt::Key_Return || key->key() == Qt::Key_Enter;
            if (confirm || key->key() == Qt::Key_Left || key->key() == Qt::Key_Escape)
            {
                const int row = view()->currentIndex().row();
                hidePopup();
                if (confirm && row >= 0)
                {
                    setCurrentIndex(row);
                    Q_EMIT activated(row);
                    Q_EMIT textActivated(itemText(row));
                }
                (isEditable() ? static_cast<QWidget*>(lineEdit()) : this)->setFocus(Qt::OtherFocusReason);
                return true;
            }
        }
    }
    return QComboBox::eventFilter(object, event);
}
void ChoiceList::keyPressEvent(QKeyEvent* event)
{
    if (!view()->isVisible() && event->key() == Qt::Key_Right && event->modifiers() == Qt::NoModifier)
    {
        showPopup();
        return;
    }
    QComboBox::keyPressEvent(event);
}
void ChoiceList::paintEvent(QPaintEvent* event)
{
    QComboBox::paintEvent(event);
    QPainter p(this);
    p.setRenderHint(QPainter::Antialiasing);
    const auto x = width() - 14, y = height() / 2;
    p.fillRect(QRect(width() - 22, 2, 20, height() - 4), palette().color(QPalette::Button));
    p.setPen(QPen(ink(), 1.5));
    p.drawPolyline(
        QPolygonF{{double(x - 4), double(y - 2)}, {double(x), double(y + 2)}, {double(x + 4), double(y - 2)}});
}
void ChoiceList::wheelEvent(QWheelEvent* event)
{
    if (view()->isVisible())
        QComboBox::wheelEvent(event);
    else
        event->ignore();
}
void drawDimensionArrow(QPainter& p, QPointF a, QPointF b, bool bothEnds, double headLength)
{
    const QLineF line(a, b);
    const auto length = line.length();
    if (length < .1)
        return;
    const auto u = (b - a) / length;
    const QPointF normal(-u.y(), u.x());
    const bool outside = bothEnds && length < 24;
    const double direction = outside ? -1 : 1;
    p.save();
    auto pen = p.pen();
    pen.setCapStyle(Qt::FlatCap);
    p.setPen(pen);
    p.drawLine(a - (outside ? u * 17 : QPointF()), b + (outside ? u * 17 : QPointF()));
    p.setBrush(pen.color());
    p.setPen(Qt::NoPen);
    const double head = headLength, halfWidth = headLength * .18;
    if (bothEnds)
        p.drawPolygon(
            QPolygonF{a, a + direction * u * head + normal * halfWidth, a + direction * u * head - normal * halfWidth});
    p.drawPolygon(
        QPolygonF{b, b - direction * u * head + normal * halfWidth, b - direction * u * head - normal * halfWidth});
    p.restore();
}
void drawCenterLine(QPainter& p, QPointF a, QPointF b, double scale)
{
    // Long dash / dot, with equal gaps and full dashes at the ends (GOST 2.303).
    const QLineF line(a, b);
    const auto length = line.length();
    if (length < 1)
        return;
    const auto direction = (b - a) / length;
    const double target = std::clamp(10 * scale, 24., 68.), gap = 5., dot = 1.2;
    const int count = std::max(1, qRound((length + 2 * gap + dot) / (target + 2 * gap + dot)));
    const auto dash = (length - (count - 1) * (2 * gap + dot)) / count;
    p.save();
    p.setPen(QPen(muted(), .85, Qt::SolidLine, Qt::FlatCap));
    double at = 0;
    for (int i = 0; i < count; ++i)
    {
        p.drawLine(a + at * direction, a + (at + dash) * direction);
        at += dash;
        if (i + 1 < count)
        {
            at += gap;
            p.drawLine(a + at * direction, a + (at + dot) * direction);
            at += dot + gap;
        }
    }
    p.restore();
}
void applyTheme(const QString& preference)
{
    QApplication::setEffectEnabled(Qt::UI_AnimateCombo, false);
    auto hints = qApp->styleHints();
    hints->setColorScheme(preference == "dark"    ? Qt::ColorScheme::Dark
                          : preference == "light" ? Qt::ColorScheme::Light
                                                  : Qt::ColorScheme::Unknown);
    const bool useDark = hints->colorScheme() == Qt::ColorScheme::Dark;
    QPalette p;
    const QColor base = useDark ? QColor("#242424") : QColor("#ffffff"),
                 surface = useDark ? QColor("#1b1b1b") : QColor("#f2f2f2");
    const QColor text = useDark ? QColor("#eeeeee") : QColor("#222222"),
                 line = useDark ? QColor("#494949") : QColor("#cccccc");
    p.setColor(QPalette::Window, surface);
    p.setColor(QPalette::Base, base);
    p.setColor(QPalette::AlternateBase, surface);
    p.setColor(QPalette::WindowText, text);
    p.setColor(QPalette::Text, text);
    p.setColor(QPalette::Button, base);
    p.setColor(QPalette::ButtonText, text);
    p.setColor(QPalette::Highlight, useDark ? QColor("#555555") : QColor("#d5d5d5"));
    p.setColor(QPalette::HighlightedText, text);
    p.setColor(QPalette::ToolTipBase, base);
    p.setColor(QPalette::ToolTipText, text);
    p.setColor(QPalette::Disabled, QPalette::Text, useDark ? QColor("#777777") : QColor("#999999"));
    p.setColor(QPalette::Disabled, QPalette::ButtonText, p.color(QPalette::Disabled, QPalette::Text));
    qApp->setPalette(p);
    qApp->setStyleSheet(QString(R"(
        QWidget { font-family: "Segoe UI"; font-size: 10pt; }
        QLineEdit, QComboBox { padding: 3px 5px; border: 1px solid %1; border-radius: 2px; background: %2; color: %3; }
        QLineEdit:focus, QComboBox:focus { border-color: %4; }
        QLineEdit[preset="true"] { color: %6; }
        QLineEdit[preset="true"]:focus { color: %3; }
        QToolButton:focus { border-color: %4; }
        QComboBox { combobox-popup: 0; padding-right: 25px; }
        QComboBox::drop-down { border-left: 1px solid %1; width: 22px; }
        QPushButton, QToolButton { padding: 5px 10px; border: 1px solid %1; border-radius: 2px; background: %2; color: %3; }
        QPushButton:hover, QToolButton:hover { background: %5; }
        QToolButton:checked { background: %5; border-color: %4; }
        QToolButton[choice="true"]:checked { background: %7; color: %8; }
        QPushButton:disabled { color: %6; }
        QToolButton[pageButton="true"] { padding: 6px 12px; border: 1px solid transparent; background: transparent; }
        QToolButton[pageButton="true"]:checked { background: %5; color: %3; }
        QToolButton[pageButton="true"]:focus { border-color: transparent; background: %5; }
        QPushButton#export { background: %7; color: %8; border: none; padding: 6px 12px; }
        QPushButton#export:disabled { background: %1; color: %6; }
        QScrollArea, QStackedWidget { border: none; background: transparent; }
        QListWidget { border: none; padding: 6px; background: transparent; outline: none; }
        QListWidget::item { padding: 7px 10px; border-radius: 2px; }
        QListWidget::item:selected { background: %5; color: %3; }
        QHeaderView::section { background: %9; padding: 8px; border: none; border-bottom: 1px solid %1; color: %6; font-size: 10pt; }
        QTableView { border: none; gridline-color: %1; background: %2; }
        QTableView::item { padding: 5px; }
        QPlainTextEdit { border: none; background: %2; color: %3; }
        QMenu { padding: 5px; border: 1px solid %1; background: %2; }
        QMenu::item { padding: 7px 24px; }
        QMenu::item:selected { background: %5; }
        QToolTip { border: 1px solid %1; padding: 5px; }
        QFrame#dimensionPane { border: 1px solid %1; border-radius: 2px; background: %9; }
        QFrame#toolCard { border: 1px solid %1; border-radius: 2px; background: %2; }
        QListWidget#processSections { border: none; border-right: 1px solid %1; padding: 4px; }
        QStackedWidget#processParameters { border: none; border-right: 1px solid %1; }
        QWidget#partParameters { background: %9; }
        QScrollBar:vertical { background: %9; width: 12px; margin: 0; }
        QScrollBar:horizontal { background: %9; height: 12px; margin: 0; }
        QScrollBar::handle { background: %10; border: 2px solid %9; border-radius: 4px; }
        QScrollBar::handle:vertical { min-height: 28px; }
        QScrollBar::handle:horizontal { min-width: 28px; }
        QScrollBar::handle:hover { background: %6; }
        QScrollBar::add-line, QScrollBar::sub-line { width: 0; height: 0; }
        QScrollBar::add-page, QScrollBar::sub-page { background: transparent; }
        QWidget#trajectoryPanel { background: %2; }
        QStatusBar { border-top: 1px solid %1; }
        QWidget#trajectoryToolbar { background: %2; border-top: 1px solid %1; }
    )")
                            .arg(line.name(), base.name(), text.name(), useDark ? "#eeeeee" : "#272727",
                                 useDark ? "#3c3c3c" : "#dedede", useDark ? "#aaaaaa" : "#626262",
                                 useDark ? "#dddddd" : "#353535", useDark ? "#1b1b1b" : "#ffffff", surface.name(),
                                 useDark ? "#858585" : "#929292"));
    QSettings().setValue("appearance/theme", preference);
}
void drawWorkOrigin(QPainter& p, QPointF center)
{
    p.save();
    p.setPen(Qt::NoPen);
    p.setBrush(paper());
    p.drawEllipse(center, 11, 11);
    p.setPen(QPen(ink(), 1));
    p.drawEllipse(center, 8, 8);
    p.drawEllipse(center, 5.5, 5.5);
    QPainterPath quadrant;
    quadrant.moveTo(center);
    quadrant.lineTo(center + QPointF(5.5, 0));
    quadrant.arcTo(QRectF(center - QPointF(5.5, 5.5), QSizeF(11, 11)), 0, -90);
    quadrant.closeSubpath();
    p.fillPath(quadrant, ink());
    p.drawLine(center - QPointF(13, 0), center + QPointF(13, 0));
    p.drawLine(center - QPointF(0, 13), center + QPointF(0, 13));
    p.restore();
}
void drawHatching(QPainter& p, const QPainterPath& path, QPointF origin, double scale, const QColor& color)
{
    // A world-space lattice: panning translates it, zooming scales it with the material.
    double step = 2.5 * scale;
    if (step <= 0)
        return;
    while (step < 6)
        step *= 2;
    const auto bounds = path.boundingRect().intersected(QRectF(p.viewport()));
    const double anchor = origin.x() + origin.y();
    const int first = int(std::floor((bounds.left() + bounds.top() - anchor) / step));
    const int last = int(std::ceil((bounds.right() + bounds.bottom() - anchor) / step));
    p.save();
    p.setClipPath(path, Qt::IntersectClip);
    p.setPen(QPen(color.isValid() ? color : border(), .7));
    for (int i = first; i <= last; ++i)
    {
        const double sum = anchor + i * step;
        const auto pixelAligned = [](double value) { return std::round(value * 64) / 64; };
        p.drawLine(QPointF(pixelAligned(bounds.left()), pixelAligned(sum - bounds.left())),
                   QPointF(pixelAligned(bounds.right()), pixelAligned(sum - bounds.right())));
    }
    p.restore();
}
void drawCoordinateAxes(QPainter& p, QPointF base, double zDirection)
{
    p.save();
    p.setPen(QPen(ink(), 1.2));
    p.setBrush(ink());
    for (const auto& end : {base + QPointF(44 * zDirection, 0), base - QPointF(0, 44)})
    {
        auto u = (end - base) / 44.;
        QPointF n(-u.y(), u.x());
        p.drawLine(base, end);
        p.drawPolygon(QPolygonF{end, end - u * 6 + n * 2.5, end - u * 6 - n * 2.5});
    }
    p.drawText(base + QPointF(zDirection > 0 ? 50 : -58, 4), "Z");
    p.drawText(base + QPointF(-4, -51), "X");
    p.restore();
}
NumericEdit::NumericEdit(QWidget* parent) : QLineEdit(parent)
{
    setCursor(Qt::ArrowCursor);
    setAlignment(Qt::AlignLeft);
    setFocusPolicy(Qt::ClickFocus);
    setMinimumWidth(80);
}
void NumericEdit::focusInEvent(QFocusEvent* e)
{
    if (e->reason() == Qt::PopupFocusReason)
    {
        QLineEdit::focusInEvent(e);
        return;
    }
    original = text();
    freshFocus = true;
    prepareReplacement();
    // QLineEdit selects all on Tab/shortcut focus. This field has a separate replacement state.
    QFocusEvent focus(e->type(), Qt::OtherFocusReason);
    QLineEdit::focusInEvent(&focus);
}
void NumericEdit::focusOutEvent(QFocusEvent* e)
{
    QLineEdit::focusOutEvent(e);
    if (e->reason() != Qt::PopupFocusReason)
    {
        freshFocus = true;
        pressed = firstPress = false;
        setCursor(Qt::ArrowCursor);
        deselect();
        setCursorPosition(0);
    }
    update();
}
QRect NumericEdit::textBounds() const
{
    QStyleOptionFrame option;
    initStyleOption(&option);
    return style()
        ->subElementRect(QStyle::SE_LineEditContents, &option, this)
        .marginsRemoved(textMargins())
        .adjusted(2, 0, -2, 0);
}
void NumericEdit::paintEvent(QPaintEvent* event)
{
    if (hasFocus() && !replaceOnType)
    {
        QLineEdit::paintEvent(event);
        return;
    }
    QStylePainter painter(this);
    QStyleOptionFrame option;
    initStyleOption(&option);
    painter.drawPrimitive(QStyle::PE_PanelLineEdit, option);
    const auto bounds = textBounds();
    painter.setClipRect(bounds);
    painter.setPen(palette().color(text().isEmpty() ? QPalette::PlaceholderText : QPalette::Text));
    const auto value = text().isEmpty() ? placeholderText() : displayText();
    painter.drawText(bounds, alignment() | Qt::AlignVCenter,
                     fontMetrics().elidedText(value, Qt::ElideRight, bounds.width()));
}
int NumericEdit::positionInReadout(QPoint point) const
{
    const auto bounds = textBounds();
    QTextLayout layout(displayText(), font());
    layout.beginLayout();
    auto line = layout.createLine();
    if (!line.isValid())
    {
        layout.endLayout();
        return 0;
    }
    line.setLineWidth(std::max(bounds.width(), fontMetrics().horizontalAdvance(displayText())));
    layout.endLayout();
    double offset = 0;
    if (line.naturalTextWidth() < bounds.width())
        offset = alignment().testFlag(Qt::AlignRight)     ? bounds.width() - line.naturalTextWidth()
                 : alignment().testFlag(Qt::AlignHCenter) ? (bounds.width() - line.naturalTextWidth()) / 2
                                                          : 0;
    return line.xToCursor(point.x() - bounds.left() - offset);
}
void NumericEdit::beginEditing()
{
    freshFocus = replaceOnType = false;
    setCursor(Qt::IBeamCursor);
    update();
}
void NumericEdit::replaceValue(const QString& value)
{
    beginEditing();
    deselect();
    QInputMethodEvent replacement;
    replacement.setCommitString(value, -cursorPosition(), text().size());
    QLineEdit::inputMethodEvent(&replacement);
}
void NumericEdit::prepareReplacement()
{
    keyboardNavigation = true;
    replaceOnType = true;
    setCursor(Qt::ArrowCursor);
    deselect();
    setCursorPosition(0);
    update();
}
void NumericEdit::mousePressEvent(QMouseEvent* e)
{
    if (e->button() != Qt::LeftButton)
    {
        QLineEdit::mousePressEvent(e);
        return;
    }
    pressed = true;
    firstPress = freshFocus;
    mouseDown = e->pos();
    const auto position = replaceOnType ? positionInReadout(e->pos()) : cursorPositionAt(e->pos());
    dragAnchor = position;
    if (firstPress)
        prepareReplacement();
    else
    {
        keyboardNavigation = false;
        beginEditing();
        if (e->modifiers().testFlag(Qt::ShiftModifier))
        {
            dragAnchor = hasSelectedText() ? (cursorPosition() == selectionStart() ? selectionEnd() : selectionStart())
                                           : cursorPosition();
            setSelection(dragAnchor, position - dragAnchor);
        }
        else
            setCursorPosition(position);
    }
    freshFocus = false;
    e->accept();
}
void NumericEdit::mouseReleaseEvent(QMouseEvent* e)
{
    if (e->button() != Qt::LeftButton)
    {
        QLineEdit::mouseReleaseEvent(e);
        return;
    }
    pressed = firstPress = false;
    e->accept();
}
void NumericEdit::mouseMoveEvent(QMouseEvent* e)
{
    if (!pressed || !e->buttons().testFlag(Qt::LeftButton))
        return;
    if (firstPress && (e->pos() - mouseDown).manhattanLength() <= QApplication::startDragDistance())
        return;
    const auto position = replaceOnType ? positionInReadout(e->pos()) : cursorPositionAt(e->pos());
    keyboardNavigation = false;
    beginEditing();
    firstPress = false;
    setSelection(dragAnchor, position - dragAnchor);
    e->accept();
}
void NumericEdit::mouseDoubleClickEvent(QMouseEvent* e)
{
    if (e->button() != Qt::LeftButton)
    {
        QLineEdit::mouseDoubleClickEvent(e);
        return;
    }
    if (freshFocus)
    {
        // A quick return from another control may arrive as a native double-click.
        // It is still the first click in this field's new focus session.
        mousePressEvent(e);
        return;
    }
    const auto position = replaceOnType ? positionInReadout(e->pos()) : cursorPositionAt(e->pos());
    keyboardNavigation = false;
    beginEditing();
    pressed = true;
    firstPress = false;
    mouseDown = e->pos();
    setCursorPosition(position);
    dragAnchor = position;
    e->accept();
}
void NumericEdit::keyPressEvent(QKeyEvent* e)
{
    freshFocus = false;
    if (e->key() == Qt::Key_Escape)
    {
        if (revertOnEscape && !isReadOnly() && text() != original)
        {
            setText(original);
            Q_EMIT textEdited(original);
        }
        Q_EMIT leaveEditor();
        e->accept();
        return;
    }
    if (e->key() == Qt::Key_Enter || e->key() == Qt::Key_Return)
    {
        Q_EMIT editingFinished();
        Q_EMIT leaveEditor();
        e->accept();
        return;
    }
    if ((e->key() == Qt::Key_Up || e->key() == Qt::Key_Down) && e->modifiers() == Qt::NoModifier)
    {
        Q_EMIT editingFinished();
        Q_EMIT navigate(e->key() == Qt::Key_Up ? -1 : 1);
        e->accept();
        return;
    }
    if (keyboardNavigation && e->modifiers() == Qt::NoModifier &&
        (e->key() == Qt::Key_Left || e->key() == Qt::Key_Right))
    {
        Q_EMIT horizontalNavigate(e->key() == Qt::Key_Left ? -1 : 1);
        e->accept();
        return;
    }
    const bool printable = !e->text().isEmpty() && e->text()[0].isPrint() &&
                           (!e->modifiers().testFlag(Qt::ControlModifier) || e->modifiers().testFlag(Qt::AltModifier));
    if (replaceOnType && !isReadOnly() &&
        (printable || e->matches(QKeySequence::Paste) || e->key() == Qt::Key_Backspace || e->key() == Qt::Key_Delete))
    {
        if (!e->matches(QKeySequence::Paste) || !QApplication::clipboard()->text().isEmpty())
            replaceValue(e->matches(QKeySequence::Paste) ? QApplication::clipboard()->text()
                         : printable                     ? e->text()
                                                         : QString());
        e->accept();
        return;
    }
    if (printable || e->matches(QKeySequence::Paste) || e->matches(QKeySequence::SelectAll) ||
        e->key() == Qt::Key_Left || e->key() == Qt::Key_Right || e->key() == Qt::Key_Home || e->key() == Qt::Key_End ||
        e->key() == Qt::Key_Backspace || e->key() == Qt::Key_Delete)
    {
        if (replaceOnType && (e->key() == Qt::Key_Left || e->key() == Qt::Key_Right || e->key() == Qt::Key_Home ||
                              e->key() == Qt::Key_End))
            setCursorPosition(text().size());
        beginEditing();
    }
    QLineEdit::keyPressEvent(e);
}
void NumericEdit::inputMethodEvent(QInputMethodEvent* event)
{
    if (replaceOnType && !isReadOnly() && (!event->commitString().isEmpty() || !event->preeditString().isEmpty()))
    {
        beginEditing();
        deselect();
        QInputMethodEvent replacement(event->preeditString(), event->attributes());
        replacement.setCommitString(event->commitString(), -cursorPosition(), text().size());
        QLineEdit::inputMethodEvent(&replacement);
        event->accept();
        return;
    }
    QLineEdit::inputMethodEvent(event);
}
void NumericEdit::contextMenuEvent(QContextMenuEvent* event)
{
    QMenu menu(this);
    menu.addAction("Отменить", this,
                   [this]
                   {
                       beginEditing();
                       undo();
                   })
        ->setEnabled(isUndoAvailable());
    menu.addAction("Повторить", this,
                   [this]
                   {
                       beginEditing();
                       redo();
                   })
        ->setEnabled(isRedoAvailable());
    menu.addSeparator();
    menu.addAction("Вырезать", this,
                   [this]
                   {
                       beginEditing();
                       cut();
                   })
        ->setEnabled(hasSelectedText() && !isReadOnly());
    menu.addAction("Копировать", this, &QLineEdit::copy)->setEnabled(hasSelectedText());
    menu.addAction("Вставить", this,
                   [this]
                   {
                       if (replaceOnType)
                           replaceValue(QApplication::clipboard()->text());
                       else
                           paste();
                   })
        ->setEnabled(!isReadOnly() && !QApplication::clipboard()->text().isEmpty());
    menu.addSeparator();
    menu.addAction("Выделить всё", this,
                   [this]
                   {
                       beginEditing();
                       selectAll();
                   })
        ->setEnabled(!text().isEmpty());
    menu.exec(event->globalPos());
}
ParameterRow::ParameterRow(const Field& f, bool hasTolerance, QWidget* parent) : QWidget(parent), key(f.key)
{
    setObjectName("row_" + key);
    setSizePolicy(QSizePolicy::Preferred, QSizePolicy::Fixed);
    setFocusPolicy(Qt::StrongFocus);
    setToolTip(f.note);
    auto layout = new QHBoxLayout(this);
    layout->setContentsMargins(6, 4, 6, 4);
    layout->setSpacing(8);
    QString name = f.label;
    if (hasTolerance)
    {
        const auto split = name.lastIndexOf(' ');
        symbol = new QLabel(name.mid(split + 1), this);
        symbol->setFixedWidth(26);
        symbol->setAlignment(Qt::AlignCenter);
        symbol->setAttribute(Qt::WA_TransparentForMouseEvents);
        layout->addWidget(symbol);
        name = name.left(split);
    }
    label = new QLabel(name + (!hasTolerance && !f.unit.isEmpty() ? ", " + f.unit : QString()), this);
    label->setAttribute(Qt::WA_TransparentForMouseEvents);
    label->setWordWrap(true);
    layout->addWidget(label, 1);
    if (hasTolerance)
    {
        dimensionValue = new DimensionButton(this);
        dimensionValue->setObjectName("dimension_" + key);
        dimensionValue->setFixedSize(128, 40);
        dimensionValue->setFocusPolicy(Qt::StrongFocus);
        dimensionValue->setAccessibleName("Размер: " + f.label);
        layout->addWidget(dimensionValue);
        connect(dimensionValue, &QToolButton::clicked, this, [this] { Q_EMIT dimensionRequested(key); });
        dimensionValue->installEventFilter(this);
        setFocusProxy(dimensionValue);
        return;
    }
    input = new NumericEdit(this);
    input->setObjectName("parameter_" + key);
    input->setAccessibleName(f.label);
    input->setFixedSize(88, 30);
    textValue = schema()["defaults"].toObject().value(key).isString();
    const bool stacked = textValue && key != "boringTool" && key != "customBoringName";
    if (stacked)
    {
        layout->setDirection(QBoxLayout::TopToBottom);
        layout->setSpacing(3);
    }
    if (textValue)
    {
        input->setFixedWidth(210);
        input->setAlignment(Qt::AlignLeft);
        if (key == "customBoringName")
        {
            input->setMinimumWidth(140);
            input->setSizePolicy(QSizePolicy::Preferred, QSizePolicy::Fixed);
        }
    }
    if (key == "boringTool")
    {
        choices = new ChoiceList(this);
        choices->setObjectName("boringTools");
        choices->setAccessibleName(f.label);
        for (const auto& tool : boringTools())
            choices->addItem(tool.name, tool.id);
        choices->addItem("Своя оправка", "custom");
        choices->setFixedSize(210, 30);
        choices->setMinimumWidth(140);
        choices->setSizePolicy(QSizePolicy::Preferred, QSizePolicy::Fixed);
        input->hide();
        input->setFocusPolicy(Qt::NoFocus);
        layout->addWidget(choices);
        choices->installEventFilter(this);
        connect(choices, &QComboBox::activated, this,
                [this](int) { Q_EMIT edited(key, choices->currentData().toString()); });
    }
    else if (key == "material")
    {
        choices = new ChoiceList(this);
        choices->setObjectName("materialChoices");
        choices->setAccessibleName("Материал");
        choices->setEditable(true);
        choices->installEventFilter(this);
        choices->setInsertPolicy(QComboBox::NoInsert);
        choices->setLineEdit(input);
        input->setMinimumWidth(0);
        input->setMaximumWidth(QWIDGETSIZE_MAX);
        input->setMinimumHeight(0);
        input->setMaximumHeight(QWIDGETSIZE_MAX);
        input->setStyleSheet("QLineEdit { border: none; padding: 0; background: transparent; }");
        input->revertOnEscape = false;
        choices->addItems({"Сталь 20", "Сталь 45", "Сталь 40Х", "Сталь Ст3сп", "Сталь 09Г2С", "Сталь 12Х18Н10Т"});
        choices->setFixedSize(210, 30);
        layout->addWidget(choices);
        connect(choices, &ChoiceList::textActivated, this, [this](const QString& value) { Q_EMIT edited(key, value); });
    }
    else
        layout->addWidget(input);
    connect(input, &QLineEdit::textEdited, this, [this](const QString& text) { Q_EMIT edited(key, text); });
    if (stacked)
    {
        (choices ? static_cast<QWidget*>(choices) : input)->setMinimumWidth(0);
        (choices ? static_cast<QWidget*>(choices) : input)->setMaximumWidth(QWIDGETSIZE_MAX);
        (choices ? static_cast<QWidget*>(choices) : input)->setSizePolicy(QSizePolicy::Expanding, QSizePolicy::Fixed);
    }
    setFocusProxy(choices && !choices->isEditable() ? static_cast<QWidget*>(choices) : input);
    connect(input, &NumericEdit::horizontalNavigate, this,
            [this](int direction)
            {
                if (direction < 0)
                    Q_EMIT parentRequested();
                else if (choices)
                    choices->showPopup();
            });
    connect(input, &NumericEdit::leaveEditor, this, [this] { input->prepareReplacement(); });
    connect(input, &NumericEdit::navigate, this,
            [this](int d)
            {
                setFocus();
                Q_EMIT navigate(d);
            });
    input->installEventFilter(this);
}
void ParameterRow::setParameters(const Parameters& p)
{
    if (input && textValue)
    {
        const auto initial = schema()["defaults"].toObject().value(key).toString();
        const bool preset = !initial.isEmpty() && p.s(key) == initial;
        if (input->property("preset").toBool() != preset)
        {
            input->setProperty("preset", preset);
            input->style()->unpolish(input);
            input->style()->polish(input);
            input->update();
        }
    }
    if (choices && !input->hasFocus())
    {
        const QSignalBlocker blocker(choices);
        choices->setCurrentIndex(choices->isEditable() ? choices->findText(p.s(key)) : choices->findData(p.s(key)));
        if (choices->isEditable())
            choices->setEditText(p.s(key));
    }
    else if (input && !input->hasFocus())
        input->setText(textValue ? p.s(key) : localized(p.n(key)));
    if (dimensionValue)
    {
        const auto& d = dimension(key);
        const auto lo = p.n(d.lower), hi = p.n(d.upper);
        dimensionValue->setValue(localized(p.n(key)), lo, hi);
    }
}
void ParameterRow::setError(bool value)
{
    if (erroneous == value)
        return;
    erroneous = value;
    auto editor = choices ? static_cast<QWidget*>(choices) : input ? static_cast<QWidget*>(input) : dimensionValue;
    editor->setStyleSheet(value ? "border-color: #bd6161;" : "");
    update();
}
void ParameterRow::setCaption(const QString& text)
{
    label->setText(text);
}
void ParameterRow::keyPressEvent(QKeyEvent* e)
{
    if (e->key() == Qt::Key_Up || e->key() == Qt::Key_Down)
    {
        Q_EMIT navigate(e->key() == Qt::Key_Up ? -1 : 1);
        return;
    }
    if (e->key() == Qt::Key_Left)
    {
        Q_EMIT parentRequested();
        return;
    }
    if (dimensionValue && (e->key() == Qt::Key_Right || e->key() == Qt::Key_Return || e->key() == Qt::Key_Enter))
    {
        Q_EMIT dimensionRequested(key);
        return;
    }
    if (e->key() == Qt::Key_Return || e->key() == Qt::Key_Enter || e->key() == Qt::Key_Right)
    {
        if (choices && (e->key() == Qt::Key_Right || !choices->isEditable()))
        {
            choices->showPopup();
            return;
        }
        input->setFocus();
        return;
    }
    if (!e->text().isEmpty() && (e->modifiers() == Qt::NoModifier || e->modifiers() == Qt::ShiftModifier) &&
        (textValue || e->text()[0].isDigit() || QString("-+.,").contains(e->text()[0])))
    {
        if (dimensionValue)
        {
            Q_EMIT replaceRequested(key, e->text());
            return;
        }
        if (choices && !choices->isEditable())
        {
            choices->showPopup();
            return;
        }
        input->setFocus();
        input->prepareReplacement();
        QApplication::sendEvent(input, e);
        return;
    }
    QWidget::keyPressEvent(e);
}
void ParameterRow::mousePressEvent(QMouseEvent* e)
{
    if (e->button() == Qt::LeftButton && choices && !choices->isEditable())
    {
        choices->showPopup();
        return;
    }
    if (e->button() == Qt::LeftButton && dimensionValue)
    {
        dimensionPressed = true;
        dimensionDown = e->position();
        dimensionValue->setFocus(Qt::MouseFocusReason);
        e->accept();
        return;
    }
    if (e->button() == Qt::LeftButton && input && input->isEnabled())
    {
        input->setFocus(Qt::MouseFocusReason);
        input->prepareReplacement();
    }
    QWidget::mousePressEvent(e);
}
void ParameterRow::mouseReleaseEvent(QMouseEvent* e)
{
    if (e->button() == Qt::LeftButton && dimensionPressed)
    {
        dimensionPressed = false;
        if (rect().contains(e->position().toPoint()) &&
            (e->position() - dimensionDown).manhattanLength() < QApplication::startDragDistance())
            Q_EMIT dimensionRequested(key);
        e->accept();
        return;
    }
    QWidget::mouseReleaseEvent(e);
}
void ParameterRow::focusInEvent(QFocusEvent* e)
{
    QWidget::focusInEvent(e);
    Q_EMIT activated(key);
    update();
}
bool ParameterRow::eventFilter(QObject* object, QEvent* e)
{
    if (choices && (object == choices || object == input) && e->type() == QEvent::KeyPress &&
        !choices->view()->isVisible() && (object != input || input->navigationMode()))
    {
        const auto keyEvent = static_cast<QKeyEvent*>(e);
        if (keyEvent->key() == Qt::Key_Left)
        {
            Q_EMIT parentRequested();
            return true;
        }
        if (keyEvent->key() == Qt::Key_Up || keyEvent->key() == Qt::Key_Down)
        {
            Q_EMIT navigate(keyEvent->key() == Qt::Key_Up ? -1 : 1);
            return true;
        }
        if (keyEvent->key() == Qt::Key_Right ||
            (!choices->isEditable() && (keyEvent->key() == Qt::Key_Return || keyEvent->key() == Qt::Key_Enter)))
        {
            choices->showPopup();
            return true;
        }
    }
    if (object == input && choices && e->type() == QEvent::KeyPress &&
        static_cast<QKeyEvent*>(e)->key() == Qt::Key_Right && input->navigationMode())
    {
        choices->showPopup();
        return true;
    }
    if (e->type() == QEvent::FocusIn)
    {
        Q_EMIT activated(key);
        update();
    }
    if (object == dimensionValue && e->type() == QEvent::KeyPress)
    {
        const auto keyEvent = static_cast<QKeyEvent*>(e);
        if (keyEvent->key() == Qt::Key_Right)
        {
            Q_EMIT dimensionRequested(key);
            return true;
        }
        if (keyEvent->key() == Qt::Key_Left)
        {
            Q_EMIT parentRequested();
            return true;
        }
        keyPressEvent(static_cast<QKeyEvent*>(e));
        return e->isAccepted();
    }
    return QWidget::eventFilter(object, e);
}
namespace
{
class DeviationDelegate final : public QStyledItemDelegate
{
  public:
    using QStyledItemDelegate::QStyledItemDelegate;
    void paint(QPainter* painter, const QStyleOptionViewItem& option, const QModelIndex& index) const override
    {
        if (!index.data(Qt::UserRole + 1).isValid())
        {
            QStyledItemDelegate::paint(painter, option, index);
            return;
        }
        const auto cell = option.rect.adjusted(2, 2, -7, -2);
        painter->fillRect(cell, option.state.testFlag(QStyle::State_Selected)
                                    ? selection()
                                    : (dark() ? QColor("#292929") : QColor("#eaeaea")));
        const auto color = option.palette.color(
            option.state.testFlag(QStyle::State_Selected) ? QPalette::HighlightedText : QPalette::Text);
        painter->save();
        painter->setPen(color);
        auto font = option.font;
        font.setBold(true);
        painter->setFont(font);
        painter->drawText(QRect(cell.left() + 8, cell.top(), 52, cell.height()), Qt::AlignVCenter | Qt::AlignLeft,
                          index.data().toString());
        painter->restore();
        drawDeviation(*painter, QRectF(cell).adjusted(72, 0, -3, 0), index.data(Qt::UserRole + 1).toDouble(),
                      index.data(Qt::UserRole + 2).toDouble(), color);
    }
    QSize sizeHint(const QStyleOptionViewItem&, const QModelIndex&) const override
    {
        return {164, 40};
    }
};
} // namespace
DimensionPane::DimensionPane(const Dimension& d, const Parameters& p, QWidget* parent) : QFrame(parent), descriptor(d)
{
    setObjectName("dimensionPane");
    auto layout = new QGridLayout(this);
    layout->setContentsMargins(10, 10, 10, 10);
    layout->setHorizontalSpacing(8);
    layout->setVerticalSpacing(4);
    auto fields = new QWidget(this);
    auto fieldLayout = new QGridLayout(fields);
    fieldLayout->setContentsMargins(0, 0, 0, 0);
    fieldLayout->setHorizontalSpacing(8);
    fieldLayout->setVerticalSpacing(4);
    fields->setFixedWidth(192);
    nominal = new NumericEdit(this);
    nominal->setObjectName("parameter_" + d.nominal);
    nominal->setAccessibleName("Номинал");
    nominal->setAlignment(Qt::AlignLeft);
    nominal->setMinimumWidth(86);
    nominal->setFixedHeight(48);
    nominal->revertOnEscape = false;
    fieldLayout->addWidget(new QLabel("Номинал", this), 0, 0);
    fieldLayout->addWidget(new QLabel("Отклонения", this), 0, 1);
    fieldLayout->addWidget(nominal, 1, 0, 2, 1);
    upper = new NumericEdit(this);
    lower = new NumericEdit(this);
    upper->setObjectName("upperDeviation");
    lower->setObjectName("lowerDeviation");
    upper->setAccessibleName("Верхнее отклонение");
    lower->setAccessibleName("Нижнее отклонение");
    upper->setAlignment(Qt::AlignLeft);
    lower->setAlignment(Qt::AlignLeft);
    upper->revertOnEscape = lower->revertOnEscape = false;
    upper->setFixedHeight(24);
    lower->setFixedHeight(24);
    upper->setToolTip("Верхнее отклонение");
    lower->setToolTip("Нижнее отклонение");
    fieldLayout->addWidget(upper, 1, 1);
    fieldLayout->addWidget(lower, 2, 1);
    layout->addWidget(fields, 0, 0, Qt::AlignLeft);
    for (auto input : {nominal, upper, lower})
        input->setFocusPolicy(Qt::StrongFocus);
    for (const auto& position : d.positions)
        for (int grade = 6; grade <= 16; ++grade)
            designations.append(position + QString::number(grade));
    standards = new QListWidget(this);
    standards->setObjectName("standardTolerance");
    standards->setAccessibleName("Стандартные отклонения");
    standards->setStyleSheet("QListWidget { padding: 0; } QListWidget::item { padding: 0; }");
    standards->setTabKeyNavigation(false);
    standards->setHorizontalScrollBarPolicy(Qt::ScrollBarAlwaysOff);
    standards->setVerticalScrollBarPolicy(Qt::ScrollBarAlwaysOff);
    standards->setFlow(QListView::TopToBottom);
    standards->setWrapping(true);
    standards->setResizeMode(QListView::Adjust);
    standards->setMovement(QListView::Static);
    standards->setGridSize({164, 40});
    const int maxRows = parent ? std::max(1, (parent->height() - 150) / 40) : 12;
    const int rowCount = std::min(maxRows, 11);
    const int columns = (designations.size() + rowCount - 1) / rowCount;
    standards->setFixedSize(columns * 164 + 2 * standards->frameWidth(), rowCount * 40 + 2 * standards->frameWidth());
    standards->setItemDelegate(new DeviationDelegate(standards));
    for (const auto& name : designations)
    {
        auto item = new QListWidgetItem(name, standards);
        item->setData(Qt::UserRole, name);
    }
    layout->addWidget(new QLabel("Квалитет", this), 1, 0);
    layout->addWidget(standards, 2, 0);
    for (auto child : {static_cast<QWidget*>(standards), static_cast<QWidget*>(nominal), static_cast<QWidget*>(upper),
                       static_cast<QWidget*>(lower)})
        child->installEventFilter(this);
    setTabOrder(nominal, upper);
    setTabOrder(upper, lower);
    setTabOrder(lower, standards);
    connect(standards, &QListWidget::currentRowChanged, this, &DimensionPane::chooseStandard);
    connect(standards, &QListWidget::itemClicked, this,
            [this](QListWidgetItem* item) { chooseStandard(standards->row(item)); });
    connect(nominal, &QLineEdit::textEdited, this,
            [this](const QString& text) { Q_EMIT numberEdited(descriptor.nominal, text); });
    connect(lower, &QLineEdit::textEdited, this,
            [this](const QString& text) { Q_EMIT numberEdited(descriptor.lower, text); });
    connect(upper, &QLineEdit::textEdited, this,
            [this](const QString& text) { Q_EMIT numberEdited(descriptor.upper, text); });
    connect(lower, &NumericEdit::leaveEditor, this, &DimensionPane::closeRequested);
    connect(upper, &NumericEdit::leaveEditor, this, &DimensionPane::closeRequested);
    connect(nominal, &NumericEdit::leaveEditor, this, &DimensionPane::closeRequested);
    connect(nominal, &NumericEdit::navigate, this,
            [this](int direction)
            {
                if (direction > 0)
                    standards->setFocus();
            });
    connect(lower, &NumericEdit::navigate, this,
            [this](int direction)
            {
                if (direction < 0)
                    upper->setFocus();
                else
                    standards->setFocus();
            });
    connect(upper, &NumericEdit::navigate, this,
            [this](int direction)
            {
                if (direction > 0)
                    lower->setFocus();
            });
    setParameters(p);
}
void DimensionPane::chooseStandard(int row)
{
    if (row < 0 || row >= designations.size())
        return;
    if (standards->model()->index(row, 0).flags().testFlag(Qt::ItemIsEnabled))
        Q_EMIT designationEdited(descriptor.key, designations[row]);
}
void DimensionPane::refreshGrades(const Parameters& p)
{
    const QSignalBlocker block(standards);
    const auto designation = p.s(descriptor.designation);
    for (int row = 0; row < designations.size(); ++row)
    {
        const auto name = designations[row];
        auto item = standards->item(row);
        try
        {
            const auto [lo, hi] = deviations(descriptor, p.n(descriptor.nominal), name);
            item->setData(Qt::UserRole + 1, lo);
            item->setData(Qt::UserRole + 2, hi);
            item->setToolTip(toleranceText(lo, hi));
            item->setFlags(Qt::ItemIsSelectable | Qt::ItemIsEnabled);
        }
        catch (const std::exception&)
        {
            item->setData(Qt::UserRole + 1, {});
            item->setData(Qt::UserRole + 2, {});
            item->setFlags(Qt::NoItemFlags);
        }
    }
    const auto row = designations.indexOf(designation);
    standards->setCurrentIndex(standards->model()->index(row, 0));
    if (row < 0)
        standards->clearSelection();
}
void DimensionPane::setParameters(const Parameters& p)
{
    refreshGrades(p);
    if (!nominal->hasFocus())
        nominal->setText(localized(p.n(descriptor.nominal)));
    if (!lower->hasFocus())
        lower->setText(localized(p.n(descriptor.lower)));
    if (!upper->hasFocus())
        upper->setText(localized(p.n(descriptor.upper)));
}
bool DimensionPane::eventFilter(QObject* object, QEvent* e)
{
    if (e->type() == QEvent::KeyPress)
    {
        const auto key = static_cast<QKeyEvent*>(e)->key();
        auto input = qobject_cast<NumericEdit*>(object);
        if (input && input->navigationMode() && static_cast<QKeyEvent*>(e)->modifiers() == Qt::NoModifier)
        {
            if (key == Qt::Key_Left)
            {
                if (input == nominal)
                    Q_EMIT closeRequested();
                else
                    nominal->setFocus();
                return true;
            }
            if (key == Qt::Key_Right)
            {
                if (input == nominal)
                    upper->setFocus();
                return true;
            }
            if (key == Qt::Key_Down)
            {
                if (input == upper)
                    lower->setFocus();
                else
                    standards->setFocus();
                return true;
            }
            if (key == Qt::Key_Up)
            {
                if (input == lower)
                    upper->setFocus();
                return true;
            }
        }
        if (object == standards && key == Qt::Key_Up &&
            (standards->currentRow() < 0 || standards->visualItemRect(standards->currentItem()).top() <= 0))
        {
            if (standards->currentRow() < 0 || standards->visualItemRect(standards->currentItem()).left() < 164)
                nominal->setFocus();
            else
                lower->setFocus();
            return true;
        }
        if (object == standards &&
            (key == Qt::Key_Escape || key == Qt::Key_Return || key == Qt::Key_Enter ||
             (key == Qt::Key_Left &&
              (standards->currentRow() < 0 || standards->visualItemRect(standards->currentItem()).left() < 164))))
        {
            Q_EMIT closeRequested();
            return true;
        }
    }
    return QFrame::eventFilter(object, e);
}
void DimensionPane::setDraftText(const QString& key, const QString& text)
{
    if (key == descriptor.nominal && nominal->text() != text)
        nominal->setText(text);
    else if (key == descriptor.lower && lower->text() != text)
        lower->setText(text);
    else if (key == descriptor.upper && upper->text() != text)
        upper->setText(text);
}
} // namespace pf::ui
