#pragma once
#include <QComboBox>
#include <QSizeGrip>
#include <QSplitter>
#include <QStyledItemDelegate>
#include <QToolButton>

namespace pf::ui
{
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
} // namespace pf::ui
