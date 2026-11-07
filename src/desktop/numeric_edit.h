#pragma once
#include <QLineEdit>
#include <QPoint>
#include <QRect>

namespace pf::ui
{
class NumericEdit final : public QLineEdit
{
    Q_OBJECT
  public:
    explicit NumericEdit(QWidget* parent = nullptr);
    void prepareReplacement();
    bool replacementArmed() const
    {
        return hasFocus() && replaceOnType;
    }
    bool navigationMode() const
    {
        return keyboardNavigation;
    }
    bool revertOnEscape = true;
  Q_SIGNALS:
    void navigate(int direction);
    void horizontalNavigate(int direction);
    void leaveEditor();

  protected:
    void focusInEvent(QFocusEvent*) override;
    void focusOutEvent(QFocusEvent*) override;
    void paintEvent(QPaintEvent*) override;
    void mousePressEvent(QMouseEvent*) override;
    void mouseReleaseEvent(QMouseEvent*) override;
    void mouseMoveEvent(QMouseEvent*) override;
    void mouseDoubleClickEvent(QMouseEvent*) override;
    void keyPressEvent(QKeyEvent*) override;
    void contextMenuEvent(QContextMenuEvent*) override;
    void inputMethodEvent(QInputMethodEvent*) override;

  private:
    QString original;
    bool freshFocus = true, replaceOnType = false, firstPress = false, pressed = false;
    bool keyboardNavigation = true;
    QPoint mouseDown;
    int dragAnchor = 0;
    QRect textBounds() const;
    int positionInReadout(QPoint) const;
    void beginEditing();
    void replaceValue(const QString&);
};
} // namespace pf::ui
