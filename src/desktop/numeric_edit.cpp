#include "numeric_edit.h"
#include "theme.h"
#include <QApplication>
#include <QClipboard>
#include <QContextMenuEvent>
#include <QFocusEvent>
#include <QInputMethodEvent>
#include <QKeyEvent>
#include <QMenu>
#include <QMouseEvent>
#include <QPainter>
#include <QStyleOptionFrame>
#include <QStylePainter>
#include <QTextLayout>
#include <algorithm>

namespace pf::ui
{
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
} // namespace pf::ui
