#pragma once
#include "core/model.h"
#include <QPointF>
#include <QWidget>

class QLabel;

namespace pf::ui
{
class NumericEdit;
class ChoiceList;
class DimensionButton;

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
} // namespace pf::ui
