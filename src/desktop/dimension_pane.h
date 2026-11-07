#pragma once
#include "core/model.h"
#include <QFrame>

class QListWidget;

namespace pf::ui
{
class NumericEdit;

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
