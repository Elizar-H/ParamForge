#pragma once
#include "core/model.h"
#include <QOpenGLBuffer>
#include <QOpenGLFunctions>
#include <QOpenGLShaderProgram>
#include <QOpenGLWidget>
#include <QVector3D>

namespace pf::ui
{
class Part3D final : public QOpenGLWidget, protected QOpenGLFunctions
{
    Q_OBJECT
  public:
    explicit Part3D(QWidget* parent = nullptr);
    ~Part3D() override;
    void setParameters(const Parameters&);
    void setSection(bool);
    void resetView();

  protected:
    void initializeGL() override;
    void paintGL() override;
    void resizeGL(int, int) override;
    void mousePressEvent(QMouseEvent*) override;
    void mouseMoveEvent(QMouseEvent*) override;
    void wheelEvent(QWheelEvent*) override;

  private:
    struct Vertex
    {
        QVector3D position, normal;
    };
    Parameters parameters;
    QVector<Vertex> vertices;
    QOpenGLBuffer buffer;
    QOpenGLShaderProgram shader;
    QPoint lastMouse;
    float pitch = -22, yaw = -35, zoom = 1;
    bool section = false, dirty = true, ready = false;
    void rebuild();
};
} // namespace pf::ui
