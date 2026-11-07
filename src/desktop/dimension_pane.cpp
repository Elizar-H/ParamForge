#include "dimension_pane.h"
#include "core/tolerances.h"
#include "drawing.h"
#include "numeric_edit.h"
#include "theme.h"
#include <QAbstractItemView>
#include <QApplication>
#include <QGridLayout>
#include <QKeyEvent>
#include <QLabel>
#include <QListWidget>
#include <QPainter>
#include <QSignalBlocker>
#include <QStyledItemDelegate>
#include <algorithm>

namespace pf::ui
{
namespace
{
class DeviationDelegate final : public QStyledItemDelegate
{
  public:
    using QStyledItemDelegate::QStyledItemDelegate;
    void paint(QPainter* painter, const QStyleOptionViewItem& option, const QModelIndex& index) const override
    {
        if (!index.data(Qt::UserRole + 1).isValid())
        {
            QStyledItemDelegate::paint(painter, option, index);
            return;
        }
        const auto cell = option.rect.adjusted(2, 2, -7, -2);
        painter->fillRect(cell, option.state.testFlag(QStyle::State_Selected)
                                    ? selection()
                                    : (dark() ? QColor("#292929") : QColor("#eaeaea")));
        const auto color = option.palette.color(
            option.state.testFlag(QStyle::State_Selected) ? QPalette::HighlightedText : QPalette::Text);
        painter->save();
        painter->setPen(color);
        auto font = option.font;
        font.setBold(true);
        painter->setFont(font);
        painter->drawText(QRect(cell.left() + 8, cell.top(), 52, cell.height()), Qt::AlignVCenter | Qt::AlignLeft,
                          index.data().toString());
        painter->restore();
        drawDeviation(*painter, QRectF(cell).adjusted(72, 0, -3, 0), index.data(Qt::UserRole + 1).toDouble(),
                      index.data(Qt::UserRole + 2).toDouble(), color);
    }
    QSize sizeHint(const QStyleOptionViewItem&, const QModelIndex&) const override
    {
        return {164, 40};
    }
};
} // namespace
DimensionPane::DimensionPane(const Dimension& d, const Parameters& p, QWidget* parent) : QFrame(parent), descriptor(d)
{
    setObjectName("dimensionPane");
    auto layout = new QGridLayout(this);
    layout->setContentsMargins(10, 10, 10, 10);
    layout->setHorizontalSpacing(8);
    layout->setVerticalSpacing(4);
    auto fields = new QWidget(this);
    auto fieldLayout = new QGridLayout(fields);
    fieldLayout->setContentsMargins(0, 0, 0, 0);
    fieldLayout->setHorizontalSpacing(8);
    fieldLayout->setVerticalSpacing(4);
    fields->setFixedWidth(192);
    nominal = new NumericEdit(this);
    nominal->setObjectName("parameter_" + d.nominal);
    nominal->setAccessibleName("Номинал");
    nominal->setAlignment(Qt::AlignLeft);
    nominal->setMinimumWidth(86);
    nominal->setFixedHeight(48);
    nominal->revertOnEscape = false;
    fieldLayout->addWidget(new QLabel("Номинал", this), 0, 0);
    fieldLayout->addWidget(new QLabel("Отклонения", this), 0, 1);
    fieldLayout->addWidget(nominal, 1, 0, 2, 1);
    upper = new NumericEdit(this);
    lower = new NumericEdit(this);
    upper->setObjectName("upperDeviation");
    lower->setObjectName("lowerDeviation");
    upper->setAccessibleName("Верхнее отклонение");
    lower->setAccessibleName("Нижнее отклонение");
    upper->setAlignment(Qt::AlignLeft);
    lower->setAlignment(Qt::AlignLeft);
    upper->revertOnEscape = lower->revertOnEscape = false;
    upper->setFixedHeight(24);
    lower->setFixedHeight(24);
    upper->setToolTip("Верхнее отклонение");
    lower->setToolTip("Нижнее отклонение");
    fieldLayout->addWidget(upper, 1, 1);
    fieldLayout->addWidget(lower, 2, 1);
    layout->addWidget(fields, 0, 0, Qt::AlignLeft);
    for (auto input : {nominal, upper, lower})
        input->setFocusPolicy(Qt::StrongFocus);
    for (const auto& position : d.positions)
        for (int grade = 6; grade <= 16; ++grade)
            designations.append(position + QString::number(grade));
    standards = new QListWidget(this);
    standards->setObjectName("standardTolerance");
    standards->setAccessibleName("Стандартные отклонения");
    standards->setStyleSheet("QListWidget { padding: 0; } QListWidget::item { padding: 0; }");
    standards->setTabKeyNavigation(false);
    standards->setHorizontalScrollBarPolicy(Qt::ScrollBarAlwaysOff);
    standards->setVerticalScrollBarPolicy(Qt::ScrollBarAlwaysOff);
    standards->setFlow(QListView::TopToBottom);
    standards->setWrapping(true);
    standards->setResizeMode(QListView::Adjust);
    standards->setMovement(QListView::Static);
    standards->setGridSize({164, 40});
    const int maxRows = parent ? std::max(1, (parent->height() - 150) / 40) : 12;
    const int rowCount = std::min(maxRows, 11);
    const int columns = (designations.size() + rowCount - 1) / rowCount;
    standards->setFixedSize(columns * 164 + 2 * standards->frameWidth(), rowCount * 40 + 2 * standards->frameWidth());
    standards->setItemDelegate(new DeviationDelegate(standards));
    for (const auto& name : designations)
    {
        auto item = new QListWidgetItem(name, standards);
        item->setData(Qt::UserRole, name);
    }
    layout->addWidget(new QLabel("Квалитет", this), 1, 0);
    layout->addWidget(standards, 2, 0);
    for (auto child : {static_cast<QWidget*>(standards), static_cast<QWidget*>(nominal), static_cast<QWidget*>(upper),
                       static_cast<QWidget*>(lower)})
        child->installEventFilter(this);
    setTabOrder(nominal, upper);
    setTabOrder(upper, lower);
    setTabOrder(lower, standards);
    connect(standards, &QListWidget::currentRowChanged, this, &DimensionPane::chooseStandard);
    connect(standards, &QListWidget::itemClicked, this,
            [this](QListWidgetItem* item) { chooseStandard(standards->row(item)); });
    connect(nominal, &QLineEdit::textEdited, this,
            [this](const QString& text) { Q_EMIT numberEdited(descriptor.nominal, text); });
    connect(lower, &QLineEdit::textEdited, this,
            [this](const QString& text) { Q_EMIT numberEdited(descriptor.lower, text); });
    connect(upper, &QLineEdit::textEdited, this,
            [this](const QString& text) { Q_EMIT numberEdited(descriptor.upper, text); });
    connect(lower, &NumericEdit::leaveEditor, this, &DimensionPane::closeRequested);
    connect(upper, &NumericEdit::leaveEditor, this, &DimensionPane::closeRequested);
    connect(nominal, &NumericEdit::leaveEditor, this, &DimensionPane::closeRequested);
    connect(nominal, &NumericEdit::navigate, this,
            [this](int direction)
            {
                if (direction > 0)
                    standards->setFocus();
            });
    connect(lower, &NumericEdit::navigate, this,
            [this](int direction)
            {
                if (direction < 0)
                    upper->setFocus();
                else
                    standards->setFocus();
            });
    connect(upper, &NumericEdit::navigate, this,
            [this](int direction)
            {
                if (direction > 0)
                    lower->setFocus();
            });
    setParameters(p);
}
void DimensionPane::chooseStandard(int row)
{
    if (row < 0 || row >= designations.size())
        return;
    if (standards->model()->index(row, 0).flags().testFlag(Qt::ItemIsEnabled))
        Q_EMIT designationEdited(descriptor.key, designations[row]);
}
void DimensionPane::refreshGrades(const Parameters& p)
{
    const QSignalBlocker block(standards);
    const auto designation = p.s(descriptor.designation);
    for (int row = 0; row < designations.size(); ++row)
    {
        const auto name = designations[row];
        auto item = standards->item(row);
        try
        {
            const auto [lo, hi] = deviations(descriptor, p.n(descriptor.nominal), name);
            item->setData(Qt::UserRole + 1, lo);
            item->setData(Qt::UserRole + 2, hi);
            item->setToolTip(toleranceText(lo, hi));
            item->setFlags(Qt::ItemIsSelectable | Qt::ItemIsEnabled);
        }
        catch (const std::exception&)
        {
            item->setData(Qt::UserRole + 1, {});
            item->setData(Qt::UserRole + 2, {});
            item->setFlags(Qt::NoItemFlags);
        }
    }
    const auto row = designations.indexOf(designation);
    standards->setCurrentIndex(standards->model()->index(row, 0));
    if (row < 0)
        standards->clearSelection();
}
void DimensionPane::setParameters(const Parameters& p)
{
    refreshGrades(p);
    if (!nominal->hasFocus())
        nominal->setText(localized(p.n(descriptor.nominal)));
    if (!lower->hasFocus())
        lower->setText(localized(p.n(descriptor.lower)));
    if (!upper->hasFocus())
        upper->setText(localized(p.n(descriptor.upper)));
}
bool DimensionPane::eventFilter(QObject* object, QEvent* e)
{
    if (e->type() == QEvent::KeyPress)
    {
        const auto key = static_cast<QKeyEvent*>(e)->key();
        auto input = qobject_cast<NumericEdit*>(object);
        if (input && input->navigationMode() && static_cast<QKeyEvent*>(e)->modifiers() == Qt::NoModifier)
        {
            if (key == Qt::Key_Left)
            {
                if (input == nominal)
                    Q_EMIT closeRequested();
                else
                    nominal->setFocus();
                return true;
            }
            if (key == Qt::Key_Right)
            {
                if (input == nominal)
                    upper->setFocus();
                return true;
            }
            if (key == Qt::Key_Down)
            {
                if (input == upper)
                    lower->setFocus();
                else
                    standards->setFocus();
                return true;
            }
            if (key == Qt::Key_Up)
            {
                if (input == lower)
                    upper->setFocus();
                return true;
            }
        }
        if (object == standards && key == Qt::Key_Up &&
            (standards->currentRow() < 0 || standards->visualItemRect(standards->currentItem()).top() <= 0))
        {
            if (standards->currentRow() < 0 || standards->visualItemRect(standards->currentItem()).left() < 164)
                nominal->setFocus();
            else
                lower->setFocus();
            return true;
        }
        if (object == standards &&
            (key == Qt::Key_Escape || key == Qt::Key_Return || key == Qt::Key_Enter ||
             (key == Qt::Key_Left &&
              (standards->currentRow() < 0 || standards->visualItemRect(standards->currentItem()).left() < 164))))
        {
            Q_EMIT closeRequested();
            return true;
        }
    }
    return QFrame::eventFilter(object, e);
}
void DimensionPane::setDraftText(const QString& key, const QString& text)
{
    if (key == descriptor.nominal && nominal->text() != text)
        nominal->setText(text);
    else if (key == descriptor.lower && lower->text() != text)
        lower->setText(text);
    else if (key == descriptor.upper && upper->text() != text)
        upper->setText(text);
}
} // namespace pf::ui
