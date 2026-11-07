#include "controls.h"
#include "drawing.h"
#include "theme.h"
#include <QAbstractItemView>
#include <QApplication>
#include <QKeyEvent>
#include <QLineEdit>
#include <QMouseEvent>
#include <QPainter>
#include <QScreen>
#include <QSignalBlocker>
#include <QStylePainter>
#include <QWheelEvent>
#include <algorithm>

namespace pf::ui
{
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
} // namespace pf::ui
