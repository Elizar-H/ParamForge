#pragma once
#include "parameter_row.h"
#include "dimension_pane.h"
#include "controls.h"
#include "part_3d.h"
#include <QMap>
#include <QScrollArea>
#include <QStackedWidget>
#include <QVBoxLayout>

namespace pf::ui
{
class PartCanvas final : public QWidget
{
    Q_OBJECT
  public:
    explicit PartCanvas(QWidget* parent = nullptr);
    void setParameters(const Parameters&);
    QRectF dimensionBounds(const QString& key) const;
    QPointF projectPoint(double z, double radius) const;
    void setShowTolerances(bool);
    void resetView();
    QPointF edgePoint(const QString&, int side = 1) const;
    void setDragAllowed(bool allowed)
    {
        dragAllowed = allowed;
    }
    bool isDragging() const
    {
        return dragging && moved;
    }
  Q_SIGNALS:
    void dragStarted();
    void nominalDragged(const QString&, double);
    void editRequested(const QString&);
    void dragFinished();
    void dragCancelled(const pf::Parameters&);

  protected:
    bool event(QEvent*) override;
    void paintEvent(QPaintEvent*) override;
    void mousePressEvent(QMouseEvent*) override;
    void mouseMoveEvent(QMouseEvent*) override;
    void mouseReleaseEvent(QMouseEvent*) override;
    void keyPressEvent(QKeyEvent*) override;
    void wheelEvent(QWheelEvent*) override;
    void resizeEvent(QResizeEvent*) override;

  private:
    Parameters parameters, baseline;
    QMap<QString, QRectF> hits;
    QString dragKey, pressedDimension;
    QPointF down;
    QVector<std::pair<QString, int>> candidates;
    QVector<std::pair<QString, int>> edgesAt(QPointF) const;
    double constrainedValue(const QString&, double, double) const;
    int dragSide = 1;
    void keepView();
    double scale = 1;
    double originY = 0;
    bool showTolerances = true;
    bool dragging = false, moved = false;
    bool dragAllowed = true, viewReady = false;
};
class PartView final : public QWidget
{
    Q_OBJECT
  public:
    explicit PartView(QWidget* parent = nullptr);
    void setParameters(const Parameters&);
    void setErrors(const QVector<Error>&);
    void focusField(const QString&);
    void openDimension(const QString&);
    void closeDimension(bool restoreFocus = true);
    QVector<ParameterRow*> rows;
    PartCanvas* canvas;
    Part3D* model;
    QStackedWidget* views;
    DimensionPane* dimensionPane = nullptr;
  Q_SIGNALS:
    void dragStarted();
    void numberEdited(const QString&, const QString&);
    void designationEdited(const QString&, const QString&);
    void nominalDragged(const QString&, double);
    void dragFinished();
    void dragCancelled(const pf::Parameters&);
    void parentRequested();
    void dimensionOpened();

  protected:
    bool eventFilter(QObject*, QEvent*) override;
    void hideEvent(QHideEvent*) override;
    void resizeEvent(QResizeEvent*) override;

  private:
    Parameters parameters, validParameters;
    QVBoxLayout* form;
    QScrollArea* scroll;
    QString openedDimension;
};
} // namespace pf::ui
