#include "part_3d.h"
#include "theme.h"
#include "controls.h"
#include "core/geometry.h"
#include <QMatrix4x4>
#include <QMouseEvent>
#include <QPainter>
#include <QWheelEvent>
#include <cmath>
#include <numbers>

namespace pf::ui
{
Part3D::Part3D(QWidget* parent) : QOpenGLWidget(parent)
{
    setObjectName("part3D");
    setMinimumSize(280, 280);
    setFocusPolicy(Qt::StrongFocus);
}
Part3D::~Part3D()
{
    if (context())
    {
        makeCurrent();
        buffer.destroy();
        shader.removeAllShaders();
        doneCurrent();
    }
}
void Part3D::setParameters(const Parameters& p)
{
    if (!drawablePart(p))
        return;
    parameters = p;
    dirty = true;
    update();
}
void Part3D::setSection(bool value)
{
    section = value;
    dirty = true;
    update();
}
void Part3D::resetView()
{
    pitch = -22;
    yaw = -35;
    zoom = 1;
    update();
}
void Part3D::initializeGL()
{
    initializeOpenGLFunctions();
    const char* vertex = R"(
        attribute vec3 position; attribute vec3 normal;
        uniform mat4 mvp; uniform mat3 normalMatrix;
        varying vec3 n;
        void main(){ n=normalize(normalMatrix*normal); gl_Position=mvp*vec4(position,1.0); }
    )";
    const char* fragment = R"(
        varying vec3 n; uniform vec3 color;
        void main(){ vec3 normal=normalize(n); float light=0.35+0.65*abs(dot(normal,normalize(vec3(-0.3,0.6,1.0)))); gl_FragColor=vec4(color*light,1.0); }
    )";
    if (!shader.addShaderFromSourceCode(QOpenGLShader::Vertex, vertex) ||
        !shader.addShaderFromSourceCode(QOpenGLShader::Fragment, fragment))
        return;
    shader.bindAttributeLocation("position", 0);
    shader.bindAttributeLocation("normal", 1);
    if (!shader.link() || !buffer.create())
        return;
    ready = true;
    glEnable(GL_DEPTH_TEST);
}
void Part3D::rebuild()
{
    vertices.clear();
    const auto profile = axisContour(parameters);
    const double length = parameters.n("length"), diameter = parameters.n("flangeDiameter");
    const float normalization = static_cast<float>(1.8 / std::max(length, diameter));
    const auto point = [&](const QPointF& p, double angle)
    {
        return QVector3D(static_cast<float>(p.x() + length / 2) * normalization,
                         static_cast<float>(p.y() * std::cos(angle)) * normalization,
                         static_cast<float>(p.y() * std::sin(angle)) * normalization);
    };
    const int count = section ? 96 : 128;
    const double start = section ? std::numbers::pi : 0;
    const double span = section ? std::numbers::pi * 1.5 : std::numbers::pi * 2;
    const double end = start + span;
    const auto triangle = [&](QVector3D a, QVector3D b, QVector3D c, QVector3D normal = {})
    {
        if (normal.isNull())
            normal = QVector3D::normal(a, b, c);
        vertices.append({a, normal});
        vertices.append({b, normal});
        vertices.append({c, normal});
    };
    for (qsizetype i = 0; i < profile.size(); ++i)
    {
        const auto a = profile[i], b = profile[(i + 1) % profile.size()];
        for (int step = 0; step < count; ++step)
        {
            const auto t0 = start + span * step / count, t1 = start + span * (step + 1) / count;
            const auto v0 = point(a, t0), v1 = point(b, t0), v2 = point(b, t1), v3 = point(a, t1);
            triangle(v0, v1, v2);
            triangle(v0, v2, v3);
        }
    }
    if (section)
    {
        QVector<int> indices;
        for (int i = 0; i < profile.size(); ++i)
            indices.append(i);
        const auto cross = [](const QPointF& a, const QPointF& b, const QPointF& c)
        { return (b.x() - a.x()) * (c.y() - a.y()) - (b.y() - a.y()) * (c.x() - a.x()); };
        double area = 0;
        for (qsizetype i = 0; i < profile.size(); ++i)
        {
            const auto& a = profile[i];
            const auto& b = profile[(i + 1) % profile.size()];
            area += a.x() * b.y() - b.x() * a.y();
        }
        const double direction = area > 0 ? 1 : -1;
        while (indices.size() >= 3)
        {
            bool found = false;
            for (qsizetype i = 0; i < indices.size(); ++i)
            {
                const int ia = indices[(i + indices.size() - 1) % indices.size()], ib = indices[i],
                          ic = indices[(i + 1) % indices.size()];
                const auto a = profile[ia], b = profile[ib], c = profile[ic];
                if (cross(a, b, c) * direction <= 1e-9)
                    continue;
                bool contains = false;
                for (auto candidate : indices)
                    if (candidate != ia && candidate != ib && candidate != ic)
                    {
                        const auto q = profile[candidate];
                        if (cross(a, b, q) * direction >= -1e-9 && cross(b, c, q) * direction >= -1e-9 &&
                            cross(c, a, q) * direction >= -1e-9)
                        {
                            contains = true;
                            break;
                        }
                    }
                if (contains)
                    continue;
                triangle(point(a, start), point(b, start), point(c, start),
                         {0, static_cast<float>(std::sin(start)), static_cast<float>(-std::cos(start))});
                triangle(point(a, end), point(b, end), point(c, end),
                         {0, static_cast<float>(-std::sin(end)), static_cast<float>(std::cos(end))});
                indices.removeAt(i);
                found = true;
                break;
            }
            if (!found)
                break;
        }
    }
    buffer.bind();
    buffer.allocate(vertices.constData(), static_cast<int>(vertices.size() * sizeof(Vertex)));
    buffer.release();
    dirty = false;
}
void Part3D::paintGL()
{
    const auto background = paper();
    glClearColor(background.redF(), background.greenF(), background.blueF(), 1);
    glClear(GL_COLOR_BUFFER_BIT | GL_DEPTH_BUFFER_BIT);
    if (!ready)
    {
        QPainter p(this);
        p.setPen(muted());
        p.drawText(rect(), Qt::AlignCenter, "3D-просмотр недоступен");
        return;
    }
    if (dirty)
        rebuild();
    QMatrix4x4 projection;
    projection.perspective(38, static_cast<float>(width()) / std::max(1, height()), .1f, 100.f);
    QMatrix4x4 view;
    view.translate(0, 0, -3.6f / zoom);
    QMatrix4x4 model;
    model.rotate(pitch, 1, 0, 0);
    model.rotate(yaw, 0, 1, 0);
    shader.bind();
    shader.setUniformValue("mvp", projection * view * model);
    shader.setUniformValue("normalMatrix", model.normalMatrix());
    shader.setUniformValue("color", dark() ? QVector3D(.78f, .78f, .78f) : QVector3D(.72f, .72f, .72f));
    buffer.bind();
    shader.enableAttributeArray(0);
    shader.enableAttributeArray(1);
    shader.setAttributeBuffer(0, GL_FLOAT, 0, 3, sizeof(Vertex));
    shader.setAttributeBuffer(1, GL_FLOAT, sizeof(QVector3D), 3, sizeof(Vertex));
    glDrawArrays(GL_TRIANGLES, 0, static_cast<GLsizei>(vertices.size()));
    shader.disableAttributeArray(0);
    shader.disableAttributeArray(1);
    buffer.release();
    shader.release();
}
void Part3D::resizeGL(int width, int height)
{
    glViewport(0, 0, width, height);
}
void Part3D::mousePressEvent(QMouseEvent* e)
{
    lastMouse = e->pos();
}
void Part3D::mouseMoveEvent(QMouseEvent* e)
{
    if (e->buttons().testFlag(Qt::LeftButton))
    {
        const auto delta = e->pos() - lastMouse;
        yaw += delta.x() * .5f;
        pitch += delta.y() * .5f;
        lastMouse = e->pos();
        update();
    }
}
void Part3D::wheelEvent(QWheelEvent* e)
{
    zoom = std::clamp(zoom * std::pow(1.12f, e->angleDelta().y() / 120.f), .4f, 2.5f);
    update();
}
} // namespace pf::ui
