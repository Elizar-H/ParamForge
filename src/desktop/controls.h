#pragma once
#include "core/model.h"
#include <QButtonGroup>
#include <QColor>
#include <QComboBox>
#include <QFrame>
#include <QLabel>
#include <QLineEdit>
#include <QListWidget>
#include <QPainter>
#include <QSizeGrip>
#include <QSplitter>
#include <QStyledItemDelegate>
#include <QToolButton>
#include <QWidget>

namespace pf::ui
{
bool dark();
QColor ink();
QColor muted();
QColor border();
QColor accent();
QColor paper();
QColor selection();
void applyTheme(const QString& preference);
QString toleranceText(double lower, double upper);
void drawWorkOrigin(QPainter&, QPointF);
void drawCoordinateAxes(QPainter&, QPointF, double zDirection = 1);
void drawHatching(QPainter&, const QPainterPath&, QPointF origin, double scale, const QColor& color = {});
void drawCenterLine(QPainter&, QPointF, QPointF, double scale);
void drawDeviation(QPainter&, const QRectF&, double lower, double upper, const QColor&);
void drawDimensionArrow(QPainter&, QPointF from, QPointF to, bool bothEnds = true, double headLength = 10);
QColor rapidColor();
QColor feedColor();
QColor contourColor();

class Splitter final : public QSplitter
{
    Q_OBJECT
  public:
    explicit Splitter(QWidget* parent = nullptr);
    explicit Splitter(Qt::Orientation, QWidget* parent = nullptr);

  protected:
    QSplitterHandle* createHandle() override;
};

class ResizeGrip final : public QSizeGrip
{
    Q_OBJECT
  public:
    explicit ResizeGrip(QWidget* parent = nullptr);

  protected:
    void paintEvent(QPaintEvent*) override;
    bool event(QEvent*) override;
};

class ChoiceList final : public QComboBox
{
    Q_OBJECT
  public:
    explicit ChoiceList(QWidget* parent = nullptr);
    void showPopup() override;

  protected:
    void paintEvent(QPaintEvent*) override;
    void wheelEvent(QWheelEvent*) override;
    void keyPressEvent(QKeyEvent*) override;
    bool eventFilter(QObject*, QEvent*) override;
};

class DimensionButton final : public QToolButton
{
  public:
    explicit DimensionButton(QWidget* parent = nullptr) : QToolButton(parent) {}
    void setValue(const QString& nominal, double lower, double upper);
    void setNominalText(const QString&);

  protected:
    void paintEvent(QPaintEvent*) override;

  private:
    double lo = 0, hi = 0;
    QString nominal;
};

class ItemDelegate : public QStyledItemDelegate
{
  public:
    using QStyledItemDelegate::QStyledItemDelegate;
    void paint(QPainter*, const QStyleOptionViewItem&, const QModelIndex&) const override;
};

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
class ParameterRow final : public QWidget
{
    Q_OBJECT
  public:
    explicit ParameterRow(const Field& field, bool dimension, QWidget* parent = nullptr);
    QString key;
    NumericEdit* input = nullptr;
    ChoiceList* choices = nullptr;
    DimensionButton* dimensionValue = nullptr;
    void setParameters(const Parameters&);
    void setError(bool);
    void setCaption(const QString&);
  Q_SIGNALS:
    void edited(const QString& key, const QString& text);
    void navigate(int direction);
    void parentRequested();
    void dimensionRequested(const QString& key);
    void replaceRequested(const QString& key, const QString& text);
    void activated(const QString& key);

  protected:
    void keyPressEvent(QKeyEvent*) override;
    void focusInEvent(QFocusEvent*) override;
    void mousePressEvent(QMouseEvent*) override;
    void mouseReleaseEvent(QMouseEvent*) override;
    bool eventFilter(QObject*, QEvent*) override;

  private:
    bool erroneous = false;
    bool textValue = false;
    bool dimensionPressed = false;
    QPointF dimensionDown;
    QLabel* label;
    QLabel* symbol = nullptr;
};
class DimensionPane final : public QFrame
{
    Q_OBJECT
  public:
    explicit DimensionPane(const Dimension&, const Parameters&, QWidget* parent = nullptr);
    void setParameters(const Parameters&);
    void setDraftText(const QString& key, const QString& text);
    QListWidget* standards;
    NumericEdit* lower;
    NumericEdit* upper;
    NumericEdit* nominal;
  Q_SIGNALS:
    void designationEdited(const QString& key, const QString& designation);
    void numberEdited(const QString& key, const QString& text);
    void closeRequested();

  protected:
    bool eventFilter(QObject*, QEvent*) override;

  private:
    Dimension descriptor;
    QStringList designations;
    void refreshGrades(const Parameters&);
    void chooseStandard(int row);
};
} // namespace pf::ui
