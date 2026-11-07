#pragma once
#include "controls.h"
#include "parameter_row.h"
#include "setup_drawing.h"
#include <QCheckBox>
#include <QListWidget>
#include <QMap>
#include <QPainter>
#include <QStackedWidget>
#include <algorithm>

namespace pf::ui
{
class ProcessDiagram final : public QWidget
{
    Q_OBJECT
  public:
    explicit ProcessDiagram(QWidget* parent = nullptr);
    QSize sizeHint() const override
    {
        return {600, 430};
    }
    int heightForWidth(int width) const override
    {
        return std::max(qRound(width * .72), 300);
    }
    void setParameters(const Parameters&);
    void setSetup(int);
    void resetView()
    {
        viewport.reset();
        update();
    }
    void highlight(const QString&);
    QString highlightedField() const
    {
        return focusedKey;
    }

  protected:
    void paintEvent(QPaintEvent*) override;
    void resizeEvent(QResizeEvent*) override;

  private:
    Parameters parameters;
    int setupIndex = 0;
    QString focusedKey;
    std::optional<SetupMapping> viewport;
};
class ProcessView final : public QWidget
{
    Q_OBJECT
  public:
    explicit ProcessView(QWidget* parent = nullptr);
    enum Section
    {
        Preparation,
        Tooling,
        Regimes,
        Metadata
    };
    void setParameters(const Parameters&);
    void setErrors(const QVector<Error>&);
    void focusField(const QString&);
    QListWidget* sections;
    QVector<ParameterRow*> rows;
    QToolButton* resetStock;
    ChoiceList* setupChoice;
    ProcessDiagram* diagram;
  Q_SIGNALS:
    void valueEdited(const QString&, const QString&);
    void stockResetRequested();
    void parentRequested();

  protected:
    bool eventFilter(QObject*, QEvent*) override;

  private:
    QStackedWidget* pages;
    QCheckBox* simulationBlank;
    QListWidget* secondFixture;
    QWidget* fixtureParameters = nullptr;
    QLabel* secondStock;
    QWidget* boringToolCard = nullptr;
    QWidget* bodyToolCard = nullptr;
    QWidget* partingParameters = nullptr;
    QLabel* boringToolFacts = nullptr;
    Parameters parameters;
    QMap<QString, int> fieldSections;
    QString focusedField;
    QVector<QVector<ParameterRow*>> pageRows;
    void focusFirst();
    void updateContext();
};
} // namespace pf::ui
