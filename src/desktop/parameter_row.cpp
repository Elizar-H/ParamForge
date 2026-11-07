#include "parameter_row.h"
#include "core/tooling.h"
#include "controls.h"
#include "numeric_edit.h"
#include "theme.h"
#include <QAbstractItemView>
#include <QApplication>
#include <QFocusEvent>
#include <QKeyEvent>
#include <QLabel>
#include <QMouseEvent>
#include <QSignalBlocker>
#include <QVBoxLayout>
#include <cmath>

namespace pf::ui
{
ParameterRow::ParameterRow(const Field& f, bool hasTolerance, QWidget* parent) : QWidget(parent), key(f.key)
{
    setObjectName("row_" + key);
    setSizePolicy(QSizePolicy::Preferred, QSizePolicy::Fixed);
    setFocusPolicy(Qt::StrongFocus);
    setToolTip(f.note);
    auto layout = new QHBoxLayout(this);
    layout->setContentsMargins(6, 4, 6, 4);
    layout->setSpacing(8);
    QString name = f.label;
    if (hasTolerance)
    {
        const auto split = name.lastIndexOf(' ');
        symbol = new QLabel(name.mid(split + 1), this);
        symbol->setFixedWidth(26);
        symbol->setAlignment(Qt::AlignCenter);
        symbol->setAttribute(Qt::WA_TransparentForMouseEvents);
        layout->addWidget(symbol);
        name = name.left(split);
    }
    label = new QLabel(name + (!hasTolerance && !f.unit.isEmpty() ? ", " + f.unit : QString()), this);
    label->setAttribute(Qt::WA_TransparentForMouseEvents);
    label->setWordWrap(true);
    layout->addWidget(label, 1);
    if (hasTolerance)
    {
        dimensionValue = new DimensionButton(this);
        dimensionValue->setObjectName("dimension_" + key);
        dimensionValue->setFixedSize(128, 40);
        dimensionValue->setFocusPolicy(Qt::StrongFocus);
        dimensionValue->setAccessibleName("Размер: " + f.label);
        layout->addWidget(dimensionValue);
        connect(dimensionValue, &QToolButton::clicked, this, [this] { Q_EMIT dimensionRequested(key); });
        dimensionValue->installEventFilter(this);
        setFocusProxy(dimensionValue);
        return;
    }
    input = new NumericEdit(this);
    input->setObjectName("parameter_" + key);
    input->setAccessibleName(f.label);
    input->setFixedSize(88, 30);
    textValue = schema()["defaults"].toObject().value(key).isString();
    const bool stacked = textValue && key != "boringTool" && key != "outerTool" && key != "bodyTool" &&
                         key != "customBoringName" && key != "customOuterName" && key != "customBodyName";
    if (stacked)
    {
        layout->setDirection(QBoxLayout::TopToBottom);
        layout->setSpacing(3);
    }
    if (textValue)
    {
        input->setFixedWidth(210);
        input->setAlignment(Qt::AlignLeft);
        if (key == "customBoringName" || key == "customOuterName" || key == "customBodyName")
        {
            input->setMinimumWidth(140);
            input->setSizePolicy(QSizePolicy::Preferred, QSizePolicy::Fixed);
        }
    }
    if (key == "boringTool" || key == "outerTool" || key == "bodyTool")
    {
        choices = new ChoiceList(this);
        choices->setObjectName(key == "boringTool" ? "boringTools" : key == "bodyTool" ? "bodyTools" : "outerTools");
        choices->setAccessibleName(f.label);
        if (key == "boringTool")
        {
            for (const auto& tool : boringTools())
                choices->addItem(tool.name, tool.id);
            choices->addItem("Своя оправка", "custom");
        }
        else
        {
            choices->addItem(key == "bodyTool" ? "SCLCL 2525M12" : "SCLCR 2525M12",
                             key == "bodyTool" ? "sclcl" : "sclcr");
            choices->addItem("Свой резец", "custom");
        }
        choices->setFixedSize(210, 30);
        choices->setMinimumWidth(140);
        choices->setSizePolicy(QSizePolicy::Preferred, QSizePolicy::Fixed);
        input->hide();
        input->setFocusPolicy(Qt::NoFocus);
        layout->addWidget(choices);
        choices->installEventFilter(this);
        connect(choices, &QComboBox::activated, this,
                [this](int) { Q_EMIT edited(key, choices->currentData().toString()); });
    }
    else if (key == "material")
    {
        choices = new ChoiceList(this);
        choices->setObjectName("materialChoices");
        choices->setAccessibleName("Материал");
        choices->setEditable(true);
        choices->installEventFilter(this);
        choices->setInsertPolicy(QComboBox::NoInsert);
        choices->setLineEdit(input);
        input->setMinimumWidth(0);
        input->setMaximumWidth(QWIDGETSIZE_MAX);
        input->setMinimumHeight(0);
        input->setMaximumHeight(QWIDGETSIZE_MAX);
        input->setStyleSheet("QLineEdit { border: none; padding: 0; background: transparent; }");
        input->revertOnEscape = false;
        choices->addItems({"Сталь 20", "Сталь 45", "Сталь 40Х", "Сталь Ст3сп", "Сталь 09Г2С", "Сталь 12Х18Н10Т"});
        choices->setFixedSize(210, 30);
        layout->addWidget(choices);
        connect(choices, &ChoiceList::textActivated, this, [this](const QString& value) { Q_EMIT edited(key, value); });
    }
    else
        layout->addWidget(input);
    connect(input, &QLineEdit::textEdited, this, [this](const QString& text) { Q_EMIT edited(key, text); });
    if (stacked)
    {
        (choices ? static_cast<QWidget*>(choices) : input)->setMinimumWidth(0);
        (choices ? static_cast<QWidget*>(choices) : input)->setMaximumWidth(QWIDGETSIZE_MAX);
        (choices ? static_cast<QWidget*>(choices) : input)->setSizePolicy(QSizePolicy::Expanding, QSizePolicy::Fixed);
    }
    setFocusProxy(choices && !choices->isEditable() ? static_cast<QWidget*>(choices) : input);
    connect(input, &NumericEdit::horizontalNavigate, this,
            [this](int direction)
            {
                if (direction < 0)
                    Q_EMIT parentRequested();
                else if (choices)
                    choices->showPopup();
            });
    connect(input, &NumericEdit::leaveEditor, this, [this] { input->prepareReplacement(); });
    connect(input, &NumericEdit::navigate, this,
            [this](int d)
            {
                setFocus();
                Q_EMIT navigate(d);
            });
    input->installEventFilter(this);
}
void ParameterRow::setParameters(const Parameters& p)
{
    if (input && textValue)
    {
        const auto initial = schema()["defaults"].toObject().value(key).toString();
        const bool preset = !initial.isEmpty() && p.s(key) == initial;
        if (input->property("preset").toBool() != preset)
        {
            input->setProperty("preset", preset);
            input->style()->unpolish(input);
            input->style()->polish(input);
            input->update();
        }
    }
    if (choices && !input->hasFocus())
    {
        const QSignalBlocker blocker(choices);
        choices->setCurrentIndex(choices->isEditable() ? choices->findText(p.s(key)) : choices->findData(p.s(key)));
        if (choices->isEditable())
            choices->setEditText(p.s(key));
    }
    else if (input && !input->hasFocus())
        input->setText(textValue ? p.s(key) : localized(p.n(key)));
    if (dimensionValue)
    {
        const auto& d = dimension(key);
        const auto lo = p.n(d.lower), hi = p.n(d.upper);
        dimensionValue->setValue(localized(p.n(key)), lo, hi);
    }
}
void ParameterRow::setError(bool value)
{
    if (erroneous == value)
        return;
    erroneous = value;
    auto editor = choices ? static_cast<QWidget*>(choices) : input ? static_cast<QWidget*>(input) : dimensionValue;
    editor->setStyleSheet(value ? "border-color: #bd6161;" : "");
    update();
}
void ParameterRow::setCaption(const QString& text)
{
    label->setText(text);
}
void ParameterRow::keyPressEvent(QKeyEvent* e)
{
    if (e->key() == Qt::Key_Up || e->key() == Qt::Key_Down)
    {
        Q_EMIT navigate(e->key() == Qt::Key_Up ? -1 : 1);
        return;
    }
    if (e->key() == Qt::Key_Left)
    {
        Q_EMIT parentRequested();
        return;
    }
    if (dimensionValue && (e->key() == Qt::Key_Right || e->key() == Qt::Key_Return || e->key() == Qt::Key_Enter))
    {
        Q_EMIT dimensionRequested(key);
        return;
    }
    if (e->key() == Qt::Key_Return || e->key() == Qt::Key_Enter || e->key() == Qt::Key_Right)
    {
        if (choices && (e->key() == Qt::Key_Right || !choices->isEditable()))
        {
            choices->showPopup();
            return;
        }
        input->setFocus();
        return;
    }
    if (!e->text().isEmpty() && (e->modifiers() == Qt::NoModifier || e->modifiers() == Qt::ShiftModifier) &&
        (textValue || e->text()[0].isDigit() || QString("-+.,").contains(e->text()[0])))
    {
        if (dimensionValue)
        {
            Q_EMIT replaceRequested(key, e->text());
            return;
        }
        if (choices && !choices->isEditable())
        {
            choices->showPopup();
            return;
        }
        input->setFocus();
        input->prepareReplacement();
        QApplication::sendEvent(input, e);
        return;
    }
    QWidget::keyPressEvent(e);
}
void ParameterRow::mousePressEvent(QMouseEvent* e)
{
    if (e->button() == Qt::LeftButton && choices && !choices->isEditable())
    {
        choices->showPopup();
        return;
    }
    if (e->button() == Qt::LeftButton && dimensionValue)
    {
        dimensionPressed = true;
        dimensionDown = e->position();
        dimensionValue->setFocus(Qt::MouseFocusReason);
        e->accept();
        return;
    }
    if (e->button() == Qt::LeftButton && input && input->isEnabled())
    {
        input->setFocus(Qt::MouseFocusReason);
        input->prepareReplacement();
    }
    QWidget::mousePressEvent(e);
}
void ParameterRow::mouseReleaseEvent(QMouseEvent* e)
{
    if (e->button() == Qt::LeftButton && dimensionPressed)
    {
        dimensionPressed = false;
        if (rect().contains(e->position().toPoint()) &&
            (e->position() - dimensionDown).manhattanLength() < QApplication::startDragDistance())
            Q_EMIT dimensionRequested(key);
        e->accept();
        return;
    }
    QWidget::mouseReleaseEvent(e);
}
void ParameterRow::focusInEvent(QFocusEvent* e)
{
    QWidget::focusInEvent(e);
    Q_EMIT activated(key);
    update();
}
bool ParameterRow::eventFilter(QObject* object, QEvent* e)
{
    if (choices && (object == choices || object == input) && e->type() == QEvent::KeyPress &&
        !choices->view()->isVisible() && (object != input || input->navigationMode()))
    {
        const auto keyEvent = static_cast<QKeyEvent*>(e);
        if (keyEvent->key() == Qt::Key_Left)
        {
            Q_EMIT parentRequested();
            return true;
        }
        if (keyEvent->key() == Qt::Key_Up || keyEvent->key() == Qt::Key_Down)
        {
            Q_EMIT navigate(keyEvent->key() == Qt::Key_Up ? -1 : 1);
            return true;
        }
        if (keyEvent->key() == Qt::Key_Right ||
            (!choices->isEditable() && (keyEvent->key() == Qt::Key_Return || keyEvent->key() == Qt::Key_Enter)))
        {
            choices->showPopup();
            return true;
        }
    }
    if (object == input && choices && e->type() == QEvent::KeyPress &&
        static_cast<QKeyEvent*>(e)->key() == Qt::Key_Right && input->navigationMode())
    {
        choices->showPopup();
        return true;
    }
    if (e->type() == QEvent::FocusIn)
    {
        Q_EMIT activated(key);
        update();
    }
    if (object == dimensionValue && e->type() == QEvent::KeyPress)
    {
        const auto keyEvent = static_cast<QKeyEvent*>(e);
        if (keyEvent->key() == Qt::Key_Right)
        {
            Q_EMIT dimensionRequested(key);
            return true;
        }
        if (keyEvent->key() == Qt::Key_Left)
        {
            Q_EMIT parentRequested();
            return true;
        }
        keyPressEvent(static_cast<QKeyEvent*>(e));
        return e->isAccepted();
    }
    return QWidget::eventFilter(object, e);
}
} // namespace pf::ui
