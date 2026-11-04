#pragma once
#include "core/model.h"
#include "process_view.h"
#include <QAbstractTableModel>
#include <QCheckBox>
#include <QLabel>
#include <QPlainTextEdit>
#include <QSet>
#include <QSplitter>
#include <QTableView>

namespace pf::ui
{
class TransitionModel final : public QAbstractTableModel
{
    Q_OBJECT
  public:
    explicit TransitionModel(QObject* parent = nullptr) : QAbstractTableModel(parent) {}
    void setTransitions(const QVector<Transition>&);
    int rowCount(const QModelIndex& parent = {}) const override;
    int columnCount(const QModelIndex& parent = {}) const override;
    QVariant data(const QModelIndex&, int role) const override;
    QVariant headerData(int section, Qt::Orientation, int role) const override;
    QVector<Transition> transitions;
};
class TrajectoryCanvas final : public QWidget
{
    Q_OBJECT
  public:
    explicit TrajectoryCanvas(QWidget* parent = nullptr);
    void setProgram(const Parameters&, const Program&);
    void selectBlocks(const QSet<int>&, int activeFrame = 0);
    void showRapids(bool);
    void showAll(bool);
    void clear();
    void resetDocument()
    {
        view.reset();
        clear();
    }
    void resetView();
    QPointF projectPoint(double z, double radius) const;
  Q_SIGNALS:
    void blockClicked(int line, Qt::KeyboardModifiers);
    void clearSelectionRequested();

  protected:
    bool event(QEvent*) override;
    void paintEvent(QPaintEvent*) override;
    void mousePressEvent(QMouseEvent*) override;
    void mouseMoveEvent(QMouseEvent*) override;
    void mouseReleaseEvent(QMouseEvent*) override;
    void mouseDoubleClickEvent(QMouseEvent*) override;
    void wheelEvent(QWheelEvent*) override;
    void resizeEvent(QResizeEvent*) override;

  private:
    struct Segment
    {
        QPointF a, b;
        int line;
    };
    Parameters parameters;
    std::optional<Program> program;
    QSet<int> selected;
    QVector<Segment> segments;
    bool rapids = true, all = true;
    int activeFrame = 0;
    std::optional<SetupMapping> view;
    QPointF lastMouse, pressPoint;
    Qt::MouseButton pressed = Qt::NoButton;
    bool panning = false;
    SetupMapping& viewport();
};
class InspectionView final : public QWidget
{
    Q_OBJECT
  public:
    explicit InspectionView(QWidget* parent = nullptr);
    void setProject(const Project&);
    void setPending();
    void resetDocument();
    void clearSelection();
    QSet<QString> selectedTransitions() const
    {
        return selected;
    }
    QTableView* table;
    QPlainTextEdit* code;
    ChoiceList* setups;
    TransitionModel* transitions;
    TrajectoryCanvas* trajectory;

  Q_SIGNALS:
    void parentRequested();

  protected:
    void showEvent(QShowEvent*) override;
    bool eventFilter(QObject*, QEvent*) override;

  private:
    std::optional<Project> project;
    QSet<QString> selected;
    QLabel* detail;
    bool updating = false, pending = true;
    int activeFrame = 0;
    int loadedSetup = -1;
    QMap<int, QSet<QString>> selectedBySetup;
    QMap<int, int> frameBySetup;
    QSplitter* split;
    bool arrangePending = true;
    void arrangePanels();
    void loadSetup();
    void selectRows();
    void highlightSelection();
    void selectFrame(int line, bool keepSelection);
};
} // namespace pf::ui
