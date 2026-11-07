#include "theme.h"
#include <QApplication>
#include <QPalette>
#include <QSettings>
#include <QStyleHints>

namespace pf::ui
{
bool dark()
{
    return qApp->palette().color(QPalette::Base).lightness() < 128;
}
QColor ink()
{
    return qApp->palette().color(QPalette::Text);
}
QColor muted()
{
    return dark() ? QColor("#aaaaaa") : QColor("#626262");
}
QColor border()
{
    return dark() ? QColor("#494949") : QColor("#cccccc");
}
QColor rapidColor()
{
    return dark() ? QColor("#ef7770") : QColor("#b4342e");
}
QColor feedColor()
{
    return dark() ? QColor("#64c586") : QColor("#1c7d42");
}
QColor contourColor()
{
    return dark() ? QColor("#6fa9d4") : QColor("#376d99");
}
QColor paper()
{
    return qApp->palette().color(QPalette::Base);
}
QColor selection()
{
    return dark() ? QColor("#414141") : QColor("#dedede");
}
void applyTheme(const QString& preference)
{
    QApplication::setEffectEnabled(Qt::UI_AnimateCombo, false);
    auto hints = qApp->styleHints();
    hints->setColorScheme(preference == "dark"    ? Qt::ColorScheme::Dark
                          : preference == "light" ? Qt::ColorScheme::Light
                                                  : Qt::ColorScheme::Unknown);
    const bool useDark = hints->colorScheme() == Qt::ColorScheme::Dark;
    QPalette p;
    const QColor base = useDark ? QColor("#242424") : QColor("#ffffff"),
                 surface = useDark ? QColor("#1b1b1b") : QColor("#f2f2f2");
    const QColor text = useDark ? QColor("#eeeeee") : QColor("#222222"),
                 line = useDark ? QColor("#494949") : QColor("#cccccc");
    p.setColor(QPalette::Window, surface);
    p.setColor(QPalette::Base, base);
    p.setColor(QPalette::AlternateBase, surface);
    p.setColor(QPalette::WindowText, text);
    p.setColor(QPalette::Text, text);
    p.setColor(QPalette::Button, base);
    p.setColor(QPalette::ButtonText, text);
    p.setColor(QPalette::Highlight, useDark ? QColor("#555555") : QColor("#d5d5d5"));
    p.setColor(QPalette::HighlightedText, text);
    p.setColor(QPalette::ToolTipBase, base);
    p.setColor(QPalette::ToolTipText, text);
    p.setColor(QPalette::Disabled, QPalette::Text, useDark ? QColor("#777777") : QColor("#999999"));
    p.setColor(QPalette::Disabled, QPalette::ButtonText, p.color(QPalette::Disabled, QPalette::Text));
    qApp->setPalette(p);
    qApp->setStyleSheet(QString(R"(
        QWidget { font-family: "Segoe UI"; font-size: 10pt; }
        QLineEdit, QComboBox { padding: 3px 5px; border: 1px solid %1; border-radius: 2px; background: %2; color: %3; }
        QLineEdit:focus, QComboBox:focus { border-color: %4; }
        QLineEdit[preset="true"] { color: %6; }
        QLineEdit[preset="true"]:focus { color: %3; }
        QToolButton:focus { border-color: %4; }
        QComboBox { combobox-popup: 0; padding-right: 25px; }
        QComboBox::drop-down { border-left: 1px solid %1; width: 22px; }
        QPushButton, QToolButton { padding: 5px 10px; border: 1px solid %1; border-radius: 2px; background: %2; color: %3; }
        QPushButton:hover, QToolButton:hover { background: %5; }
        QToolButton:checked { background: %5; border-color: %4; }
        QToolButton[choice="true"]:checked { background: %7; color: %8; }
        QPushButton:disabled { color: %6; }
        QToolButton[pageButton="true"] { padding: 6px 12px; border: 1px solid transparent; background: transparent; }
        QToolButton[pageButton="true"]:checked { background: %5; color: %3; }
        QToolButton[pageButton="true"]:focus { border-color: transparent; background: %5; }
        QPushButton#export { background: %7; color: %8; border: none; padding: 6px 12px; }
        QPushButton#export:disabled { background: %1; color: %6; }
        QScrollArea, QStackedWidget { border: none; background: transparent; }
        QListWidget { border: none; padding: 6px; background: transparent; outline: none; }
        QListWidget::item { padding: 7px 10px; border-radius: 2px; }
        QListWidget::item:selected { background: %5; color: %3; }
        QHeaderView::section { background: %9; padding: 8px; border: none; border-bottom: 1px solid %1; color: %6; font-size: 10pt; }
        QTableView { border: none; gridline-color: %1; background: %2; }
        QTableView::item { padding: 5px; }
        QPlainTextEdit { border: none; background: %2; color: %3; }
        QMenu { padding: 5px; border: 1px solid %1; background: %2; }
        QMenu::item { padding: 7px 24px; }
        QMenu::item:selected { background: %5; }
        QToolTip { border: 1px solid %1; padding: 5px; }
        QFrame#dimensionPane { border: 1px solid %1; border-radius: 2px; background: %9; }
        QFrame#toolCard, QFrame#outerToolCard { border: 1px solid %1; border-radius: 2px; background: %2; }
        QListWidget#processSections { border: none; border-right: 1px solid %1; padding: 4px; }
        QStackedWidget#processParameters { border: none; border-right: 1px solid %1; }
        QWidget#partParameters { background: %9; }
        QScrollBar:vertical { background: %9; width: 12px; margin: 0; }
        QScrollBar:horizontal { background: %9; height: 12px; margin: 0; }
        QScrollBar::handle { background: %10; border: 2px solid %9; border-radius: 4px; }
        QScrollBar::handle:vertical { min-height: 28px; }
        QScrollBar::handle:horizontal { min-width: 28px; }
        QScrollBar::handle:hover { background: %6; }
        QScrollBar::add-line, QScrollBar::sub-line { width: 0; height: 0; }
        QScrollBar::add-page, QScrollBar::sub-page { background: transparent; }
        QWidget#trajectoryPanel { background: %2; }
        QStatusBar { border-top: 1px solid %1; }
        QPushButton#validationStatus { border: none; background: transparent; text-align: left; padding: 2px 8px; }
        QPushButton#validationStatus[error="true"] { color: %11; }
        QToolButton#statusDetailsToggle { border: 1px solid transparent; background: transparent; padding: 2px 6px; }
        QToolButton#statusDetailsToggle:hover, QToolButton#statusDetailsToggle:focus,
        QToolButton#statusDetailsToggle:checked { background: %5; border-color: %1; }
        QFrame#statusDetails { border: 1px solid %1; border-radius: 2px; background: %2; }
        QWidget#trajectoryToolbar { background: %2; border-top: 1px solid %1; }
    )")
                            .arg(line.name(), base.name(), text.name(), useDark ? "#eeeeee" : "#272727",
                                 useDark ? "#3c3c3c" : "#dedede", useDark ? "#aaaaaa" : "#626262",
                                 useDark ? "#dddddd" : "#353535", useDark ? "#1b1b1b" : "#ffffff", surface.name(),
                                 useDark ? "#858585" : "#929292", useDark ? "#e69e9e" : "#a34343"));
    QSettings().setValue("appearance/theme", preference);
}
} // namespace pf::ui
