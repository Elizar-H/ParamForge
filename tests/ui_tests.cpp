#include "core/engine.h"
#include "core/geometry.h"
#include "core/planning.h"
#include "core/tolerances.h"
#include "desktop/dimension_pane.h"
#include "desktop/drawing.h"
#include "desktop/numeric_edit.h"
#include "desktop/theme.h"
#include "desktop/window.h"
#include <QAbstractItemView>
#include <QAbstractNativeEventFilter>
#include <QApplication>
#include <QCheckBox>
#include <QClipboard>
#include <QContextMenuEvent>
#include <QDialogButtonBox>
#include <QDir>
#include <QFile>
#include <QInputMethodEvent>
#include <QItemSelectionModel>
#include <QJsonDocument>
#include <QMenu>
#include <QMessageBox>
#include <QMimeData>
#include <QPainterPath>
#include <QScreen>
#include <QScrollArea>
#include <QScrollBar>
#include <QSettings>
#include <QStyleFactory>
#include <QStyleHints>
#include <QTemporaryDir>
#include <QWindow>
#include <QtTest>
#ifdef Q_OS_WIN
#include <windows.h>
#endif

class TestMouseIsolation final : public QAbstractNativeEventFilter
{
    bool nativeEventFilter(const QByteArray&, void* message, qintptr*) override
    {
#ifdef Q_OS_WIN
        // Keep real input on the user's other screen out of this test process.
        // QtTest injects QPA events directly, so these checks still exercise the widgets.
        const auto native = static_cast<MSG*>(message);
        if ((native->message >= WM_MOUSEFIRST && native->message <= WM_MOUSELAST) ||
            (native->message >= WM_KEYFIRST && native->message <= WM_KEYLAST))
            return true;
        if (native->message == WM_KILLFOCUS)
        {
            DWORD nextProcess = 0;
            GetWindowThreadProcessId(reinterpret_cast<HWND>(native->wParam), &nextProcess);
            return nextProcess != GetCurrentProcessId();
        }
        return false;
#else
        Q_UNUSED(message);
        return false;
#endif
    }
};

class UiTests final : public QObject
{
    Q_OBJECT
  private:
    QString output;
    TestMouseIsolation mouseIsolation;
    pf::ui::NumericEdit* nominalEditor(pf::ui::Window& window, const QString& key = "length")
    {
        if (!window.part->dimensionPane || window.part->dimensionPane->nominal->objectName() != "parameter_" + key)
            window.part->openDimension(key);
        return window.part->dimensionPane->nominal;
    }
    bool showActive(pf::ui::Window& window)
    {
        const auto screens = QGuiApplication::screens();
        const auto screen = screens[std::min(1, int(screens.size()) - 1)];
        const auto available = screen->availableGeometry();
        window.winId();
        window.windowHandle()->setScreen(screen);
        window.setMinimumSize(std::min(940, available.width() - 30), std::min(600, available.height() - 40));
        window.resize(std::min(1280, available.width() - 40), std::min(820, available.height() - 60));
        window.move(available.topLeft() + QPoint(15, 15));
        window.setAttribute(Qt::WA_ShowWithoutActivating);
        window.show();
        if (!QTest::qWaitForWindowExposed(&window))
            return false;
        // Exercise Qt focus without taking the foreground from the user's first screen.
        QT_WARNING_PUSH
        QT_WARNING_DISABLE_DEPRECATED
        QApplication::setActiveWindow(&window);
        QT_WARNING_POP
        QApplication::processEvents();
        window.part->canvas->repaint();
        return true;
    }
    void chooseStandard(pf::ui::DimensionPane* pane, const QString& designation)
    {
        QVERIFY(pane->standards->isVisible());
        for (int index = 0; index < pane->standards->count(); ++index)
            if (pane->standards->item(index)->data(Qt::UserRole).toString() == designation)
            {
                pane->standards->scrollToItem(pane->standards->item(index));
                QTest::mouseClick(pane->standards->viewport(), Qt::LeftButton, Qt::NoModifier,
                                  pane->standards->visualItemRect(pane->standards->item(index)).center());
                return;
            }
        QFAIL("Standard not found");
    }
    void chooseFromList(pf::ui::ChoiceList* choice, int index)
    {
        const QPointer<pf::ui::ChoiceList> guard(choice);
        QVERIFY(choice->isVisible());
        for (auto parent = choice->parentWidget(); parent; parent = parent->parentWidget())
            if (auto scroll = qobject_cast<QScrollArea*>(parent))
                scroll->ensureWidgetVisible(choice, 0, 8);
        QApplication::processEvents();
        const auto anchor = choice->mapTo(choice->window(), QPoint(choice->width() - 12, choice->height() / 2));
        QTest::mouseMove(choice->window()->windowHandle(), anchor);
        QTest::mouseClick(choice->window()->windowHandle(), Qt::LeftButton, Qt::NoModifier, anchor);
        QTRY_VERIFY(choice->view()->isVisible());
        const auto popup = choice->view()->window();
        QVERIFY(popup->geometry().top() >= choice->mapToGlobal(QPoint(0, choice->height())).y() ||
                popup->geometry().bottom() <= choice->mapToGlobal(QPoint()).y());
        QVERIFY(choice->screen()->availableGeometry().contains(popup->geometry()));
        const auto item = choice->model()->index(index, 0);
        choice->view()->scrollTo(item);
        const auto point = choice->view()->viewport()->mapTo(popup, choice->view()->visualRect(item).center());
        QTest::mouseMove(popup->windowHandle(), point);
        QTest::mouseClick(popup->windowHandle(), Qt::LeftButton, Qt::NoModifier, point);
        QVERIFY(guard);
        QTRY_VERIFY(guard && !guard->view()->isVisible());
        QCOMPARE(choice->currentIndex(), index);
        QTRY_VERIFY(guard && guard->window()->isActiveWindow());
    }
    void screenshot(pf::ui::Window& w, const QString& name)
    {
        QApplication::processEvents();
        if (!output.isEmpty())
        {
            QDir().mkpath(output);
            QVERIFY(w.grab().save(QDir(output).filePath(name + ".png")));
        }
    }
  private Q_SLOTS:
    void initTestCase()
    {
        QCoreApplication::setOrganizationName("ParamForgeTests");
        QCoreApplication::setApplicationName("ParamForgeTests");
        QSettings::setDefaultFormat(QSettings::IniFormat);
        output = qEnvironmentVariable("PARAMFORGE_QA_DIR");
        if (!output.isEmpty())
            QSettings::setPath(QSettings::IniFormat, QSettings::UserScope, QDir(output).filePath("settings"));
        qApp->setStyle(QStyleFactory::create("Fusion"));
        pf::ui::applyTheme("light");
        qApp->installNativeEventFilter(&mouseIsolation);
    }
    void startupAndInput()
    {
        pf::ui::Window w;
        QVERIFY(showActive(w));
        QTRY_VERIFY_WITH_TIMEOUT(w.isReady(), 5000);
        QCOMPARE(w.pageButtons.size(), 3);
        auto input = nominalEditor(w);
        QTest::mouseClick(input, Qt::LeftButton);
        QVERIFY(!input->hasSelectedText());
        input->setCursorPosition(1);
        QTest::keyClick(input, Qt::Key_A, Qt::ControlModifier);
        QCOMPARE(input->selectedText(), input->text());
        QTest::keyClicks(input, "110");
        QTRY_VERIFY(w.isReady());
        QCOMPARE(w.parameters().n("length"), 110.);
        QTest::keyClick(input, Qt::Key_Escape);
        QTRY_VERIFY(w.isReady());
        QCOMPARE(w.parameters().n("length"), 110.);
        QVERIFY(!w.part->dimensionPane);
        screenshot(w, "01-part-light");
    }
    void hierarchicalNavigation()
    {
        pf::ui::Window w;
        QVERIFY(showActive(w));
        QTRY_VERIFY(w.isReady());
        QTest::mouseClick(w.pageButtons[1], Qt::LeftButton);
        QCOMPARE(QApplication::focusWidget(), w.pageButtons[1]);
        QTest::keyClick(w.windowHandle(), Qt::Key_Down);
        QCOMPARE(QApplication::focusWidget(), w.process->sections);
        QCOMPARE(w.process->sections->currentRow(), pf::ui::ProcessView::Preparation);
        QTest::keyClick(w.windowHandle(), Qt::Key_Right);
        QCOMPARE(QApplication::focusWidget(), w.findChild<pf::ui::NumericEdit*>("parameter_stockDiameter"));
        for (int i = 0; i < 2; ++i)
            QTest::keyClick(w.windowHandle(), Qt::Key_Down);
        auto row = w.findChild<pf::ui::ParameterRow*>("row_blankLength");
        auto next = w.findChild<pf::ui::ParameterRow*>("row_blankOverhang");
        QVERIFY(row);
        QCOMPARE(QApplication::focusWidget(), row->input);
        QVERIFY(row->input->replacementArmed());
        QTest::keyClicks(row->input, "180");
        QTest::keyClick(w.windowHandle(), Qt::Key_Down);
        QCOMPARE(QApplication::focusWidget(), next->input);
        QTest::keyClick(w.windowHandle(), Qt::Key_Up);
        QCOMPARE(QApplication::focusWidget(), row->input);
        QTest::keyClick(row->input, Qt::Key_Return);
        QCOMPARE(QApplication::focusWidget(), row->input);
        QVERIFY(row->input->replacementArmed());
        QTRY_VERIFY(w.isReady());
        QCOMPARE(w.parameters().n("blankLength"), 180.);
        QTest::keyClick(w.windowHandle(), Qt::Key_Left);
        QTRY_COMPARE(QApplication::focusWidget(), w.process->sections);
        const auto item = w.process->sections->item(0);
        QTest::mouseClick(w.process->sections->viewport(), Qt::LeftButton, Qt::NoModifier,
                          w.process->sections->visualItemRect(item).center());
        QCOMPARE(w.process->sections->currentRow(), 0);
        QCOMPARE(QApplication::focusWidget(), w.findChild<pf::ui::NumericEdit*>("parameter_stockDiameter"));
        screenshot(w, "02-fixture-light");
    }
    void longStatusMessages()
    {
        pf::ui::Window w;
        QVERIFY(showActive(w));
        QTRY_VERIFY(w.isReady());
        auto status = w.findChild<QPushButton*>("validationStatus");
        auto toggle = w.findChild<QToolButton*>("statusDetailsToggle");
        auto details = w.findChild<QFrame*>("statusDetails");
        auto table = w.findChild<QTableWidget*>("statusDetailsTable");
        QVERIFY(status && toggle && details && table);
        QVERIFY(!toggle->isVisible());
        QVERIFY(!details->isVisible());
        for (const auto& theme : {QString("light"), QString("dark")})
        {
            pf::ui::applyTheme(theme);
            w.resize(std::min(940, w.screen()->availableGeometry().width() - 30), w.height());
            w.editValue("length", "240");
            QVERIFY(!w.isReady());
            QVERIFY(status->text().contains("растачивается"));
            QTRY_VERIFY(toggle->isVisible());
            QCOMPARE(toggle->text(), QString("1"));
            QVERIFY(!toggle->icon().isNull());
            QVERIFY(!status->isVisible());
            const auto size = w.size();
            QTest::mouseClick(toggle, Qt::LeftButton);
            QVERIFY(details->isVisible());
            QCOMPARE(w.size(), size);
            QCOMPARE(table->rowCount(), 1);
            QCOMPARE(table->item(0, 0)->text(), QString("1"));
            QVERIFY(table->item(0, 1)->text().contains(status->text()));
            QCOMPARE(table->editTriggers(), QAbstractItemView::NoEditTriggers);
            QTRY_COMPARE(table->verticalScrollBar()->maximum(), 0);
            QVERIFY(w.centralWidget()->rect().contains(details->geometry()));
            QVERIFY(toggle->isChecked());
            screenshot(w, "status-expanded-" + theme);
            table->setFocus();
            QTest::keyClick(table, Qt::Key_Escape);
            QVERIFY(!details->isVisible());
            QTest::mouseClick(toggle, Qt::LeftButton);
            w.showFullScreen();
            QApplication::processEvents();
            QVERIFY(details->isVisible());
            QVERIFY(w.centralWidget()->rect().contains(details->geometry()));
            screenshot(w, "status-fullscreen-" + theme);
            w.showNormal();
            w.setParameters(pf::Parameters(), false);
            QTRY_VERIFY(w.isReady());
            QVERIFY(!details->isVisible());
            QVERIFY(!toggle->isVisible());
        }
        QTemporaryDir folder;
        QVERIFY(w.exportTo(folder.filePath("kit")));
        QCOMPARE(status->text(), QString("Комплект сохранён"));
        QVERIFY(!toggle->isVisible());
        pf::ui::applyTheme("light");
    }
    void multipleStatusErrors()
    {
        pf::ui::Window w;
        QVERIFY(showActive(w));
        QTRY_VERIFY(w.isReady());
        auto toggle = w.findChild<QToolButton*>("statusDetailsToggle");
        auto details = w.findChild<QFrame*>("statusDetails");
        auto table = w.findChild<QTableWidget*>("statusDetailsTable");
        w.editValue("length", "240");
        QCOMPARE(toggle->text(), QString("1"));
        w.editValue("feedRough", "abc");
        QCOMPARE(toggle->text(), QString("2"));
        w.editValue("depthOutside", "abc");
        QCOMPARE(toggle->text(), QString("3"));
        w.editValue("feedRough", "0,2");
        QCOMPARE(toggle->text(), QString("2"));
        QTemporaryDir folder;
        QVERIFY(!w.exportTo(folder.filePath("invalid")));
        QCOMPARE(toggle->text(), QString("2"));
        QTest::mouseClick(toggle, Qt::LeftButton);
        QVERIFY(details->isVisible());
        QCOMPARE(table->rowCount(), 2);
        QCOMPARE(table->item(0, 0)->text(), QString("1"));
        QCOMPARE(table->item(1, 0)->text(), QString("2"));
        QVERIFY(table->item(1, 1)->text().contains("растачивается"));
        QVERIFY(table->item(0, 1)->text().contains(pf::field("depthOutside").label));
        screenshot(w, "status-multiple-errors");
        const auto cell = table->visualRect(table->model()->index(0, 1));
        QTest::mouseClick(table->viewport(), Qt::LeftButton, Qt::NoModifier, cell.bottomRight() - QPoint(8, 4));
        QVERIFY(details->isVisible());
        QVERIFY(toggle->isChecked());
        QVERIFY(w.pageButtons[1]->isChecked());
        QCOMPARE(w.process->sections->currentRow(), pf::ui::ProcessView::Regimes);
        const auto depth = w.findChild<pf::ui::NumericEdit*>("parameter_depthOutside");
        QCOMPARE(QApplication::focusWidget(), depth);
        QTest::keyClicks(depth, "2");
        QCOMPARE(toggle->text(), QString("1"));
        QVERIFY(details->isVisible());
        QCOMPARE(table->rowCount(), 1);
        QVERIFY(table->item(0, 1)->text().contains("растачивается"));
        const auto remainingCell = table->visualRect(table->model()->index(0, 1));
        QTest::mouseClick(table->viewport(), Qt::LeftButton, Qt::NoModifier,
                          remainingCell.bottomRight() - QPoint(8, 4));
        QVERIFY(details->isVisible());
        QVERIFY(w.pageButtons[0]->isChecked());
        QVERIFY(w.part->dimensionPane);
        const auto length = w.part->dimensionPane->nominal;
        QCOMPARE(QApplication::focusWidget(), length);
        QTest::keyClicks(length, "116");
        QTRY_VERIFY(w.isReady());
        QVERIFY(!details->isVisible());
        QVERIFY(!toggle->isChecked());
        QVERIFY(!toggle->isVisible());
        w.editValue("boringClamp", "100");
        w.editValue("length", "240");
        QCOMPARE(toggle->text(), QString("2"));
        w.editValue("checker", "\n");
        QCOMPARE(toggle->text(), QString("3"));
        QTest::mouseClick(toggle, Qt::LeftButton);
        QCOMPARE(table->rowCount(), 3);
        QVERIFY(table->item(0, 1)->text().contains("растачивается"));
        QVERIFY(table->item(1, 1)->text().contains("Длина зажима оправки"));
        QVERIFY(table->item(2, 1)->text().contains("Реквизит"));
        screenshot(w, "status-errors-across-sections");
    }
    void toolbarBackgroundNavigation()
    {
        pf::ui::Window w;
        QVERIFY(showActive(w));
        QTRY_VERIFY(w.isReady());
        auto toolbar = w.findChild<QWidget*>("mainToolbar");
        QVERIFY(toolbar);
        for (const auto theme : {"light", "dark"})
        {
            pf::ui::applyTheme(theme);
            for (int page = 0; page < w.pageButtons.size(); ++page)
            {
                w.selectPage(page);
                QWidget* content = page == 0   ? static_cast<QWidget*>(w.part->rows.first()->dimensionValue)
                                   : page == 1 ? static_cast<QWidget*>(w.process->sections)
                                               : static_cast<QWidget*>(w.inspection->table);
                for (const auto point : {QPoint(toolbar->width() - 16, toolbar->height() / 2),
                                         QPoint(w.pageButtons[0]->x() - 2, toolbar->height() / 2)})
                {
                    QVERIFY(!toolbar->childAt(point));
                    content->setFocus();
                    QTest::mouseClick(w.windowHandle(), Qt::LeftButton, Qt::NoModifier, toolbar->mapTo(&w, point));
                    QCOMPARE(QApplication::focusWidget(), w.pageButtons[page]);
                    QVERIFY(w.pageButtons[page]->isChecked());
                }
                QTest::keyClick(w.windowHandle(), Qt::Key_Left);
                auto previous = page == 0 ? w.findChild<QToolButton*>("fileMenuButton") : w.pageButtons[page - 1];
                QCOMPARE(QApplication::focusWidget(), previous);
                QTest::keyClick(w.windowHandle(), Qt::Key_Right);
                QCOMPARE(QApplication::focusWidget(), w.pageButtons[page]);
                QTest::keyClick(w.windowHandle(), Qt::Key_Down);
                QCOMPARE(QApplication::focusWidget(), content);
            }
            screenshot(w, QString("toolbar-focus-") + theme);
        }
        pf::ui::applyTheme("light");
    }
    void blankParameterAreaNavigation()
    {
        pf::ui::Window w;
        QVERIFY(showActive(w));
        QTRY_VERIFY(w.isReady());
        for (const auto& theme : {QString("light"), QString("dark")})
        {
            pf::ui::applyTheme(theme);
            w.selectPage(0);
            auto area = w.part->findChild<QScrollArea*>();
            QVERIFY(area);
            w.pageButtons[0]->setFocus();
            const auto point =
                area->widget()->mapTo(&w, QPoint(area->widget()->width() / 2, area->widget()->height() - 12));
            QTest::mouseClick(w.windowHandle(), Qt::LeftButton, Qt::NoModifier, point);
            QCOMPARE(QApplication::focusWidget(), w.part->rows.first()->dimensionValue);
            QVERIFY(!w.part->dimensionPane);
            QTest::keyClick(w.windowHandle(), Qt::Key_Down);
            QCOMPARE(QApplication::focusWidget(), w.part->rows[1]->dimensionValue);
            QTest::keyClick(w.windowHandle(), Qt::Key_1);
            QVERIFY(w.part->dimensionPane);
            QTest::keyClicks(w.part->dimensionPane->nominal, "28");
            QTest::keyClick(w.windowHandle(), Qt::Key_Escape);

            w.selectPage(1);
            auto pages = w.process->findChild<QStackedWidget*>("processParameters");
            QVERIFY(pages);
            for (int section = 0; section < w.process->sections->count(); ++section)
            {
                w.process->sections->setCurrentRow(section);
                w.pageButtons[1]->setFocus();
                QApplication::processEvents();
                auto scroll = qobject_cast<QScrollArea*>(pages->currentWidget());
                QVERIFY(scroll);
                scroll->verticalScrollBar()->setValue(0);
                const auto background = scroll->widget();
                const bool scrolls = background->height() > scroll->viewport()->height();
                const bool topMargin =
                    scrolls || section == pf::ui::ProcessView::Tooling || section == pf::ui::ProcessView::Metadata;
                const auto blank =
                    background->mapTo(&w, QPoint(background->width() / 2, topMargin ? 6 : background->height() - 24));
                QTest::mouseClick(w.windowHandle(), Qt::LeftButton, Qt::NoModifier, blank);
                const QStringList first = {"stockDiameter", "outerTool", "depthOutside", "partName"};
                const QStringList next = {"stockBore", "outerToolNumber", "depthInside", "material"};
                auto firstRow = w.findChild<pf::ui::ParameterRow*>("row_" + first[section]);
                QVERIFY(firstRow);
                QCOMPARE(QApplication::focusWidget(), firstRow->focusProxy());
                QTest::keyClick(w.windowHandle(), Qt::Key_Down);
                auto nextRow = w.findChild<pf::ui::ParameterRow*>("row_" + next[section]);
                QVERIFY(nextRow && nextRow->isAncestorOf(QApplication::focusWidget()));
                QTest::keyClick(w.windowHandle(), Qt::Key_Left);
                QCOMPARE(QApplication::focusWidget(), w.process->sections);
            }
            w.process->setupChoice->setCurrentIndex(1);
            w.process->sections->setCurrentRow(pf::ui::ProcessView::Preparation);
            QApplication::processEvents();
            auto scroll = qobject_cast<QScrollArea*>(pages->currentWidget());
            auto facts = w.process->findChild<QListWidget*>("secondFixtureFacts");
            QVERIFY(facts && scroll);
            QTest::mouseClick(
                w.windowHandle(), Qt::LeftButton, Qt::NoModifier,
                w.process->findChild<QWidget*>("fixtureParameters")
                    ->mapTo(&w, QPoint(40, w.process->findChild<QWidget*>("fixtureParameters")->height() - 3)));
            QCOMPARE(QApplication::focusWidget(), facts);
            facts->setCurrentRow(0);
            QTest::keyClick(w.windowHandle(), Qt::Key_Down);
            QCOMPARE(facts->currentRow(), 1);
            QTest::keyClick(w.windowHandle(), Qt::Key_Left);
            QCOMPARE(QApplication::focusWidget(), w.process->sections);
            screenshot(w, "blank-panel-focus-" + theme);
            w.process->setupChoice->setCurrentIndex(0);
        }
        pf::ui::applyTheme("light");
    }
    void groupedTechnologyParameters()
    {
        pf::ui::Window w;
        QVERIFY(showActive(w));
        QTRY_VERIFY(w.isReady());
        w.selectPage(1);
        QCOMPARE(w.process->sections->count(), 4);
        const auto row = [&w](const QString& key) { return w.findChild<pf::ui::ParameterRow*>("row_" + key); };
        const auto editorVisible = [&w, row](const QString& key)
        {
            auto pages = w.process->findChild<QStackedWidget*>("processParameters");
            auto scroll = qobject_cast<QScrollArea*>(pages->currentWidget());
            auto editor = row(key)->input;
            return scroll->viewport()->rect().contains(
                QRect(editor->mapTo(scroll->viewport(), QPoint()), editor->size()));
        };
        for (const auto& theme : {QString("light"), QString("dark")})
        {
            pf::ui::applyTheme(theme);
            w.process->sections->setCurrentRow(pf::ui::ProcessView::Preparation);
            for (const auto& key : {"stockDiameter", "stockBore", "blankLength", "blankOverhang", "trimAllowance",
                                    "frontFacingAllowance"})
                QVERIFY(row(key)->isVisible());
            QVERIFY(!row("cutoffWidth")->isVisible());
            QVERIFY(!row("gripLength"));
            QVERIFY(!row("chuckClearance"));
            row("blankLength")->setFocus();
            QTest::keyClicks(row("blankLength")->input, "180");
            QTest::keyClick(w.windowHandle(), Qt::Key_Return);
            QTRY_VERIFY(w.isReady());
            QCOMPARE(w.parameters().n("blankLength"), 180.);
            QTest::keyClick(w.windowHandle(), Qt::Key_Down);
            QCOMPARE(QApplication::focusWidget(), row("blankOverhang")->input);
            QVERIFY(editorVisible("blankOverhang"));
            QTest::keyClick(w.windowHandle(), Qt::Key_Up);
            QTest::keyClick(w.windowHandle(), Qt::Key_Up);
            QCOMPARE(QApplication::focusWidget(), row("stockBore")->input);
            auto fixture = w.process->findChild<QWidget*>("fixtureParameters");
            QVERIFY(fixture && !fixture->isVisible());
            row("frontFacingAllowance")->setFocus();
            QTest::keyClick(w.windowHandle(), Qt::Key_Down);
            QCOMPARE(QApplication::focusWidget(), row("stockDiameter")->input);
            screenshot(w, "preparation-groups-" + theme);

            w.process->sections->setCurrentRow(pf::ui::ProcessView::Regimes);
            QVERIFY(row("rpmMax")->isVisible());
            QVERIFY(row("clearanceX")->isVisible());
            QVERIFY(row("clearanceZ")->isVisible());
            row("rpmMax")->setFocus();
            QTest::keyClick(w.windowHandle(), Qt::Key_Down);
            QCOMPARE(QApplication::focusWidget(), row("clearanceX")->input);
            QVERIFY(editorVisible("clearanceX"));
            QTest::keyClick(w.windowHandle(), Qt::Key_Down);
            QCOMPARE(QApplication::focusWidget(), row("clearanceZ")->input);
            QVERIFY(editorVisible("clearanceZ"));
            QTest::keyClick(w.windowHandle(), Qt::Key_Up);
            QTest::keyClick(w.windowHandle(), Qt::Key_Up);
            QCOMPARE(QApplication::focusWidget(), row("rpmMax")->input);
            auto approaches = w.process->findChild<QWidget*>("approachParameters");
            QVERIFY(approaches);
            QTest::mouseClick(w.windowHandle(), Qt::LeftButton, Qt::NoModifier, approaches->mapTo(&w, QPoint(12, 3)));
            QCOMPARE(QApplication::focusWidget(), row("clearanceX")->input);
            screenshot(w, "regime-groups-" + theme);
        }
        pf::ui::applyTheme("light");
    }
    void spatialDimensionNavigation()
    {
        pf::ui::Window w;
        QVERIFY(showActive(w));
        QTRY_VERIFY(w.isReady());
        auto row = w.part->rows[1];
        row->setFocus();
        QCOMPARE(QApplication::focusWidget(), row->dimensionValue);
        QTest::keyClick(w.windowHandle(), Qt::Key_Right);
        auto pane = w.part->dimensionPane;
        QVERIFY(pane);
        QCOMPARE(QApplication::focusWidget(), pane->nominal);
        QTest::keyClick(w.windowHandle(), Qt::Key_Right);
        QCOMPARE(QApplication::focusWidget(), pane->upper);
        QTest::keyClicks(pane->upper, "0,1");
        QCOMPARE(pane->upper->text(), QString("0,1"));
        QTest::keyClick(w.windowHandle(), Qt::Key_Down);
        QCOMPARE(QApplication::focusWidget(), pane->lower);
        QTest::keyClicks(pane->lower, "-0.1");
        QTest::keyClick(w.windowHandle(), Qt::Key_Left);
        QCOMPARE(QApplication::focusWidget(), pane->nominal);
        QVERIFY(pane->nominal->replacementArmed());
        QTest::keyClick(w.windowHandle(), Qt::Key_Down);
        QCOMPARE(QApplication::focusWidget(), pane->standards);
        pane->standards->setCurrentRow(0);
        QTest::keyClick(w.windowHandle(), Qt::Key_Right);
        QCOMPARE(QApplication::focusWidget(), pane->standards);
        QVERIFY(pane->standards->visualItemRect(pane->standards->currentItem()).left() > 0);
        QTest::keyClick(w.windowHandle(), Qt::Key_Up);
        QCOMPARE(QApplication::focusWidget(), pane->lower);
        QTest::keyClick(w.windowHandle(), Qt::Key_Left);
        QCOMPARE(QApplication::focusWidget(), pane->nominal);
        QTest::keyClick(w.windowHandle(), Qt::Key_Left);
        QVERIFY(!w.part->dimensionPane);
        QCOMPARE(QApplication::focusWidget(), row->dimensionValue);
        screenshot(w, "dimension-field-focus");
        w.selectPage(1);
        w.process->focusField("stockDiameter");
        auto input = w.findChild<pf::ui::NumericEdit*>("parameter_stockDiameter");
        const auto original = input->text();
        QTest::keyClick(w.windowHandle(), Qt::Key_Right);
        QCOMPARE(input->text(), original);
        QVERIFY(input->replacementArmed());
        QTest::mouseClick(input, Qt::LeftButton);
        QTest::mouseClick(input, Qt::LeftButton);
        input->setCursorPosition(input->text().size());
        QTest::keyClick(w.windowHandle(), Qt::Key_Left);
        QCOMPARE(QApplication::focusWidget(), input);
        QCOMPARE(input->cursorPosition(), input->text().size() - 1);
    }
    void windowLayout()
    {
        pf::ui::Window w;
        QVERIFY(showActive(w));
        QTRY_VERIFY(w.isReady());
#ifdef Q_OS_WIN
        QVERIFY(!w.windowFlags().testFlag(Qt::ExpandedClientAreaHint));
        QVERIFY(!w.windowFlags().testFlag(Qt::FramelessWindowHint));
        QVERIFY(w.frameGeometry().height() > w.geometry().height());
        const auto footer = w.exportButton->mapTo(&w, QPoint());
        QVERIFY(footer.y() > w.part->mapTo(&w, w.part->rect().bottomLeft()).y());
#endif
        for (int page = 0; page < 3; ++page)
        {
            w.selectPage(page);
            w.showNormal();
            w.resize(w.minimumSize());
            QTest::qWait(60);
            QVector<QWidget*> controls = {w.pageButtons[0], w.pageButtons[1],
                                          w.pageButtons[2], w.controller,
                                          w.exportButton,   w.findChild<QToolButton*>("fileMenuButton")};
            if (page == 0)
                controls += {w.part->rows[0]->dimensionValue, w.findChild<QToolButton*>("view2D"),
                             w.findChild<QToolButton*>("view3D")};
            else if (page == 1)
                controls += {w.process->resetStock, w.findChild<pf::ui::NumericEdit*>("parameter_stockDiameter")};
            else
                controls += {w.inspection->setups};
            QVector<QSize> sizes;
            QVector<QFont> fonts;
            for (auto widget : controls)
            {
                QVERIFY(widget);
                sizes.append(widget->size());
                fonts.append(widget->font());
            }
            const auto normalCanvas = w.part->canvas->size();
            screenshot(w, QString("layout-%1-normal").arg(page));
            w.showMaximized();
            QTRY_VERIFY(w.isMaximized());
            QTest::qWait(80);
            for (qsizetype i = 0; i < controls.size(); ++i)
            {
                QCOMPARE(controls[i]->size(), sizes[i]);
                QCOMPARE(controls[i]->font(), fonts[i]);
            }
            if (page == 0)
                QVERIFY(w.part->canvas->width() > normalCanvas.width());
            screenshot(w, QString("layout-%1-maximized").arg(page));
        }
    }
    void panelResizing()
    {
        pf::ui::Window w;
        QVERIFY(showActive(w));
        QTRY_VERIFY(w.isReady());
        for (const auto& theme : {QString("light"), QString("dark")})
        {
            pf::ui::applyTheme(theme);
            int checked = 0;
            for (int page = 0; page < 3; ++page)
            {
                w.selectPage(page);
                QApplication::processEvents();
                for (auto split : w.findChildren<QSplitter*>())
                {
                    if (!split->isVisible())
                        continue;
                    auto handle = split->handle(1);
                    QVERIFY(handle && handle->isVisible());
                    const auto start = handle->mapTo(&w, handle->rect().center());
                    const auto capture = QRect(start - QPoint(32, 32), QSize(64, 64));
                    QTest::mouseMove(w.windowHandle(), QPoint(30, 20));
                    if (!output.isEmpty())
                        QVERIFY(w.grab(capture).save(
                            QDir(output).filePath(QString("resize-%1-%2-rest.png").arg(theme).arg(checked))));
                    QTest::mouseMove(w.windowHandle(), start);
                    if (!output.isEmpty())
                        QVERIFY(w.grab(capture).save(
                            QDir(output).filePath(QString("resize-%1-%2-hover.png").arg(theme).arg(checked))));
                    const auto before = split->sizes();
                    int minimum = 0, maximum = 0;
                    split->getRange(1, &minimum, &maximum);
                    const int position = split->orientation() == Qt::Horizontal ? handle->x() : handle->y();
                    const int offset = maximum - position >= position - minimum ? std::min(16, maximum - position)
                                                                                : -std::min(16, position - minimum);
                    QVERIFY2(offset != 0, "The splitter needs room to resize at this window size.");
                    const auto delta = split->orientation() == Qt::Horizontal ? QPoint(offset, 0) : QPoint(0, offset);
                    QTest::mousePress(w.windowHandle(), Qt::LeftButton, Qt::NoModifier, start);
                    QTest::mouseMove(w.windowHandle(), start + delta);
                    QTest::mouseRelease(w.windowHandle(), Qt::LeftButton, Qt::NoModifier, start + delta);
                    QTRY_VERIFY(split->sizes() != before);
                    split->setSizes(before);
                    ++checked;
                }
            }
            QCOMPARE(checked, 2);
            auto grip = w.findChild<pf::ui::ResizeGrip*>();
            QVERIFY(grip && grip->isVisible());
            const auto checkCorner = [&]
            {
                QCOMPARE(grip->mapTo(&w, grip->rect().bottomRight()), w.rect().bottomRight());
                QVERIFY(w.exportButton->mapTo(&w, w.exportButton->rect().bottomRight()).x() <
                        grip->mapTo(&w, QPoint()).x());
            };
            checkCorner();
            if (!output.isEmpty())
            {
                QVERIFY(grip->grab().save(QDir(output).filePath("resize-corner-" + theme + ".png")));
                QVERIFY(w.grab(QRect(w.width() - 160, w.height() - 64, 160, 64))
                            .save(QDir(output).filePath("resize-corner-context-" + theme + ".png")));
            }
            const auto originalSize = w.size();
            w.resize(originalSize + QSize(-40, -30));
            QApplication::processEvents();
            checkCorner();
            w.showMaximized();
            QApplication::processEvents();
            QVERIFY(!grip->isVisible());
            w.showNormal();
            w.resize(originalSize);
            QApplication::processEvents();
            QVERIFY(grip->isVisible());
            checkCorner();
        }
        pf::ui::applyTheme("light");
    }
    void centeredPartAndSectionReset()
    {
        pf::ui::Window w;
        QVERIFY(showActive(w));
        QTRY_VERIFY(w.isReady());
        const auto center = [&]
        {
            auto canvas = w.part->canvas;
            canvas->repaint();
            return std::abs(canvas->projectPoint(-w.parameters().n("length") / 2, 0).x() - canvas->width() / 2.) < .01;
        };
        QVERIFY(center());
        auto collapse = w.findChild<QToolButton*>("collapseParameters");
        QVERIFY(collapse);
        const int expanded = w.part->canvas->width();
        QTest::mouseClick(collapse, Qt::LeftButton);
        QTRY_VERIFY(w.part->canvas->width() > expanded);
        QVERIFY(center());
        w.part->openDimension("length");
        QVERIFY(w.part->dimensionPane);
        QTRY_COMPARE(w.part->canvas->width(), expanded);
        w.part->closeDimension();
        w.editValue("length", "200");
        QVERIFY(!w.isReady());
        QVERIFY2(center(), qPrintable(QString("length=%1 width=%2 front=%3 center=%4 edge=%5")
                                          .arg(w.parameters().n("length"))
                                          .arg(w.part->canvas->width())
                                          .arg(w.part->canvas->projectPoint(0, 0).x())
                                          .arg(w.part->canvas->projectPoint(-w.parameters().n("length") / 2, 0).x())
                                          .arg(w.part->canvas->edgePoint("length").x())));
        w.selectPage(1);
        w.process->sections->setCurrentRow(pf::ui::ProcessView::Tooling);
        auto tools = w.findChild<pf::ui::ChoiceList*>("boringTools");
        QVERIFY(tools);
        chooseFromList(tools, tools->findData("custom"));
        w.editValue("boringOverhang", "210");
        QTRY_VERIFY(w.isReady());
        w.process->setupChoice->setCurrentIndex(1);
        w.process->focusField("boringOverhang");
        QCOMPARE(w.process->setupChoice->currentIndex(), 0);
        QVERIFY(w.findChild<pf::ui::NumericEdit*>("parameter_boringOverhang")->hasFocus());
        screenshot(w, "tool-selection-custom");
        for (auto area : w.process->findChildren<QScrollArea*>())
            if (area->isVisible())
                QVERIFY(area->horizontalScrollBar()->maximum() == 0);
        pf::ui::applyTheme("dark");
        screenshot(w, "tool-selection-custom-dark");
        pf::ui::applyTheme("light");
        w.editValue("trimAllowance", "4");
        w.editValue("frontFacingAllowance", "5");
        w.editValue("gripLength", "30");
        w.editValue("feedRough", "0.25");
        QTRY_VERIFY(w.isReady());
        w.process->sections->setCurrentRow(0);
        QTest::mouseClick(w.process->resetStock, Qt::LeftButton);
        QTRY_VERIFY(w.isReady());
        QCOMPARE(w.parameters().n("trimAllowance"), 2.);
        QCOMPARE(w.parameters().n("frontFacingAllowance"), 2.);
        QCOMPARE(w.parameters().n("gripLength"), 30.);
        QCOMPARE(w.parameters().n("feedRough"), .25);
        QVERIFY(w.parameters().b("autoStock"));
        w.process->sections->setFocus();
        w.process->sections->setCurrentRow(pf::ui::ProcessView::Metadata);
        QTest::keyClick(w.process->sections, Qt::Key_Down);
        QCOMPARE(w.process->sections->currentRow(), 0);
        w.process->sections->setCurrentRow(pf::ui::ProcessView::Metadata);
        auto material = w.findChild<pf::ui::ParameterRow*>("row_material");
        material->setFocus();
        QTest::keyClick(material, Qt::Key_Right);
        QTRY_VERIFY(material->choices->view()->isVisible());
        QTest::keyClick(material->choices->view(), Qt::Key_Escape);
        auto value = w.findChild<pf::ui::NumericEdit*>("parameter_partName");
        value->setFocus();
        QVERIFY(value->replacementArmed());
        QTest::keyClick(value, Qt::Key_Left);
        QCOMPARE(QApplication::focusWidget(), w.process->sections);
        QVERIFY(!value->hasSelectedText());
    }
    void dropdownKeyboardAndNcExit()
    {
        pf::ui::Window w;
        QVERIFY(showActive(w));
        QTRY_VERIFY(w.isReady());
        w.selectPage(1);
        w.process->focusField("material");
        auto material = w.findChild<pf::ui::ParameterRow*>("row_material");
        const auto initialMaterial = w.parameters().s("material");
        QTest::keyClick(material->input, Qt::Key_Down);
        QVERIFY(!material->input->hasFocus());
        QCOMPARE(w.parameters().s("material"), initialMaterial);
        w.process->focusField("material");
        QTest::keyClick(material->input, Qt::Key_Right);
        QTRY_VERIFY(material->choices->view()->isVisible());
        material->choices->view()->setCurrentIndex(material->choices->model()->index(2, 0));
        QTest::keyClick(material->choices->view(), Qt::Key_Left);
        QVERIFY(!material->choices->view()->isVisible());
        QCOMPARE(w.parameters().s("material"), initialMaterial);
        QTest::keyClick(material->input, Qt::Key_Right);
        QTRY_VERIFY(material->choices->view()->isVisible());
        material->choices->view()->setCurrentIndex(material->choices->model()->index(2, 0));
        QTest::keyClick(material->choices->view(), Qt::Key_Right);
        QCOMPARE(w.parameters().s("material"), QString("Сталь 40Х"));
        QVERIFY(material->input->hasFocus());
        for (auto combo : {w.controller, w.process->setupChoice, w.inspection->setups})
        {
            w.selectPage(combo == w.inspection->setups ? 2 : 1);
            combo->setFocus();
            const int original = combo->currentIndex(), next = 1 - original;
            QTest::keyClick(combo, Qt::Key_Right);
            QTRY_VERIFY(combo->view()->isVisible());
            QTest::keyClick(combo->view(), next > original ? Qt::Key_Down : Qt::Key_Up);
            QCOMPARE(combo->view()->currentIndex().row(), next);
            QCOMPARE(combo->currentIndex(), original);
            QTest::keyClick(combo->view(), Qt::Key_Left);
            QCOMPARE(combo->currentIndex(), original);
            QTest::keyClick(combo, Qt::Key_Right);
            QTRY_VERIFY(combo->view()->isVisible());
            combo->view()->setCurrentIndex(combo->model()->index(next, 0));
            QTest::keyClick(combo->view(), Qt::Key_Right);
            QCOMPARE(combo->currentIndex(), next);
        }
        w.selectPage(2);
        for (auto surface : {static_cast<QWidget*>(w.inspection->table), static_cast<QWidget*>(w.inspection->code),
                             static_cast<QWidget*>(w.inspection->trajectory)})
        {
            surface->setFocus();
            QTest::keyClick(surface, Qt::Key_Left);
            QVERIFY(w.pageButtons[2]->hasFocus());
        }
        QTest::keyClick(w.pageButtons[2], Qt::Key_Left);
        QVERIFY(w.pageButtons[1]->isChecked());
        QVERIFY(w.findChild<QMenu*>("themeMenu"));
        QVERIFY(!w.findChild<QToolButton*>("settings"));
    }
    void toleranceRecalculation()
    {
        pf::ui::Window w;
        QVERIFY(showActive(w));
        QTRY_VERIFY(w.isReady());
        QTest::mouseClick(w.process->resetStock, Qt::LeftButton);
        QTRY_VERIFY(w.isReady());
        const auto before = w.part->canvas->geometry();
        w.part->openDimension("flangeDiameter");
        auto pane = w.part->dimensionPane;
        QVERIFY(pane);
        QVERIFY(pane->nominal->width() <= 100 && pane->upper->width() <= 100);
        chooseStandard(pane, "h7");
        w.editValue("flangeDiameter", "130");
        QTRY_VERIFY(w.isReady());
        QCOMPARE(w.parameters().n("flangeLowerDeviation"), -.040);
        w.editValue("flangeDiameter", "200");
        QTRY_VERIFY(w.isReady());
        QCOMPARE(w.parameters().n("flangeLowerDeviation"), -.046);
        QCOMPARE(w.parameters().s("flangeTolerance"), QString("h7"));
        QCOMPARE(w.part->canvas->geometry(), before);
        screenshot(w, "03-tolerance-light");
    }
    void multiSelection()
    {
        pf::ui::Window w;
        QVERIFY(showActive(w));
        QTRY_VERIFY(w.isReady());
        w.selectPage(2);
        auto view = w.inspection;
        auto model = view->transitions;
        view->table->selectionModel()->clearSelection();
        view->table->selectionModel()->select(model->index(0, 0),
                                              QItemSelectionModel::Select | QItemSelectionModel::Rows);
        view->table->selectionModel()->select(model->index(2, 0),
                                              QItemSelectionModel::Select | QItemSelectionModel::Rows);
        QCOMPARE(view->selectedTransitions().size(), 2);
        QVERIFY(view->selectedTransitions().contains("010.01"));
        QVERIFY(view->selectedTransitions().contains("010.03"));
        const int expected = model->transitions[0].blocks.size() + model->transitions[2].blocks.size();
        QCOMPARE(view->code->extraSelections().size(), expected);
        const auto selected = view->selectedTransitions();
        w.setController("sinumerik840dsl");
        QTRY_VERIFY(w.isReady());
        QCOMPARE(view->selectedTransitions(), selected);
        view->code->setFocus();
        QTest::keyClick(view->code, Qt::Key_A, Qt::ControlModifier);
        QCOMPARE(view->code->textCursor().selectedText().size(), view->code->toPlainText().size());
        view->code->moveCursor(QTextCursor::Start);
        screenshot(w, "04-multiselection-light");
        view->setups->setCurrentIndex(1);
        QCOMPARE(model->rowCount(), 2);
        QApplication::processEvents();
        QVERIFY(view->trajectory->isVisible());
        QVERIFY(view->trajectory->height() >= 180);
        QVERIFY(view->code->height() >= 220);
        auto bottom = view->code->parentWidget();
        auto split = qobject_cast<QSplitter*>(view->table->parentWidget());
        QVERIFY(bottom->y() - view->table->geometry().bottom() <= split->handleWidth() + 2);
        screenshot(w, "05-second-setup-light");
    }
    void themeAnd3D()
    {
        pf::ui::Window w;
        QVERIFY(showActive(w));
        QTRY_VERIFY(w.isReady());
        pf::ui::applyTheme("dark");
        QVERIFY(pf::ui::dark());
        QTest::mouseClick(w.findChild<QToolButton*>("view3D"), Qt::LeftButton);
        QTest::qWait(400);
        QVERIFY(w.part->model->isValid());
        QVERIFY(!w.part->model->grabFramebuffer().isNull());
        screenshot(w, "06-part-3d-dark");
        w.findChild<QCheckBox*>("modelSection")->setChecked(true);
        QTest::qWait(200);
        screenshot(w, "07-part-3d-section-dark");
        w.selectPage(1);
        w.process->setupChoice->setCurrentIndex(1);
        QTest::qWait(80);
        screenshot(w, "08-second-fixture-dark");
        w.selectPage(2);
        QTest::qWait(80);
        QVERIFY(w.inspection->trajectory->isVisible());
        QVERIFY(w.inspection->trajectory->height() >= 180);
        QVERIFY(w.inspection->code->height() >= 220);
        auto bottom = w.inspection->code->parentWidget();
        auto split = qobject_cast<QSplitter*>(w.inspection->table->parentWidget());
        QVERIFY(bottom->y() - w.inspection->table->geometry().bottom() <= split->handleWidth() + 2);
        const auto canvas = w.inspection->trajectory;
        QVERIFY2(canvas->parentWidget()->rect().contains(canvas->geometry()),
                 qPrintable(QString("Trajectory canvas %1,%2 %3x%4 exceeds its panel %5x%6")
                                .arg(canvas->x())
                                .arg(canvas->y())
                                .arg(canvas->width())
                                .arg(canvas->height())
                                .arg(canvas->parentWidget()->width())
                                .arg(canvas->parentWidget()->height())));
        screenshot(w, "09-nc-dark");
        pf::ui::applyTheme("light");
    }
    void fileWorkflow()
    {
        QTemporaryDir temporary;
        pf::ui::Window w;
        QVERIFY(showActive(w));
        QTRY_VERIFY(w.isReady());
        w.editValue("feedFinish", "0,08");
        QTRY_VERIFY(w.isReady());
        QVERIFY(w.saveProject(temporary.filePath("деталь.json")));
        w.editValue("feedFinish", "0,09");
        QTRY_VERIFY(w.isReady());
        QVERIFY(w.openProject(temporary.filePath("деталь.json")));
        QTRY_VERIFY(w.isReady());
        QCOMPARE(w.parameters().n("feedFinish"), .08);
        QVERIFY(w.exportTo(temporary.filePath("комплект")));
        QCOMPARE(QDir(temporary.filePath("комплект/Комплект 1")).entryList(QDir::Files).size(), 4);
        w.editValue("length", "-");
        QVERIFY(!w.isReady());
        QVERIFY(w.exportButton->isEnabled());
        QVERIFY(!w.exportTo(temporary.filePath("invalid")));
        w.editValue("gripLength", "30");
        QCOMPARE(nominalEditor(w)->text(), QString("-"));
        w.editValue("length", "110");
        QTRY_VERIFY(w.isReady());
    }
    void secondFixtureContext()
    {
        pf::ui::Window w;
        QVERIFY(showActive(w));
        QTRY_VERIFY(w.isReady());
        w.editValue("gripLength", "30");
        QTRY_VERIFY(w.isReady());
        w.selectPage(1);
        w.process->sections->setCurrentRow(pf::ui::ProcessView::Preparation);
        w.process->setupChoice->setCurrentIndex(1);
        QVERIFY(!w.findChild<pf::ui::ParameterRow*>("row_gripLength"));
        auto facts = w.findChild<QListWidget*>("secondFixtureFacts");
        QVERIFY(facts->isVisible());
        QCOMPARE(facts->count(), 3);
        QVERIFY(facts->item(0)->text().contains("Захват за Ø100"));
        w.process->sections->setFocus();
        QTest::keyClick(w.process->sections, Qt::Key_Right);
        QCOMPARE(QApplication::focusWidget(), w.findChild<pf::ui::NumericEdit*>("parameter_trimAllowance"));
        QTest::keyClick(w.windowHandle(), Qt::Key_Down);
        QCOMPARE(QApplication::focusWidget(), facts);
        QCOMPARE(w.process->diagram->highlightedField(), QString("gripLength"));
        QTest::keyClick(facts, Qt::Key_Left);
        QCOMPARE(QApplication::focusWidget(), w.process->sections);
        screenshot(w, "10-second-fixture-context");
    }
    void dragCancellation()
    {
        pf::ui::Window w;
        QVERIFY(showActive(w));
        QTRY_VERIFY(w.isReady());
        const auto original = w.parameters();
        auto canvas = w.part->canvas;
        const auto start = canvas->edgePoint("flangeWidth").toPoint();
        QTest::mousePress(canvas, Qt::LeftButton, Qt::NoModifier, start);
        QTest::mouseMove(canvas, start - QPoint(25, 0));
        QTRY_VERIFY(w.parameters().n("flangeWidth") != original.n("flangeWidth"));
        QTest::keyClick(canvas, Qt::Key_Escape);
        QTest::mouseRelease(canvas, Qt::LeftButton, Qt::NoModifier, start - QPoint(25, 0));
        QTRY_VERIFY(w.isReady());
        QCOMPARE(w.parameters(), original);
    }
    void directInputAndPopup()
    {
        pf::ui::Window w;
        QVERIFY(showActive(w));
        QTRY_VERIFY(w.isReady());
        auto input = nominalEditor(w);
        w.pageButtons[0]->setFocus();
        QTest::mousePress(input, Qt::LeftButton);
        QVERIFY(!input->hasSelectedText()); // replacement is armed without visible selection
        QTest::mouseRelease(input, Qt::LeftButton);
        QTest::keyClicks(input, "110");
        QCOMPARE(input->text(), QString("110"));
        QTest::mouseClick(input, Qt::LeftButton, Qt::NoModifier, QPoint(input->width() - 15, input->height() / 2));
        QVERIFY(!input->hasSelectedText());
        QTRY_VERIFY(w.isReady());
        w.part->openDimension("boreDiameter");
        QVERIFY(w.part->dimensionPane);
        auto upper = w.part->dimensionPane->upper;
        upper->setFocus();
        upper->selectAll();
        QTest::keyClicks(upper, "0,");
        QVERIFY(!w.isReady());
        QTest::mouseClick(w.part->rows[0]->dimensionValue, Qt::LeftButton);
        QVERIFY(w.part->dimensionPane);
        QCOMPARE(QApplication::focusWidget(), w.part->dimensionPane->nominal);
        QCOMPARE(w.part->dimensionPane->nominal->text(), QString("110"));
        QVERIFY(!w.part->dimensionPane->nominal->hasSelectedText());
        w.part->openDimension("boreDiameter");
        QCOMPARE(w.part->dimensionPane->upper->text(), QString("0,"));
        w.part->closeDimension();
        // An unfinished tolerance must survive an attempted drag and Escape elsewhere.
        auto canvas = w.part->canvas;
        const auto p = canvas->edgePoint("flangeWidth").toPoint();
        const auto old = w.parameters();
        QTest::mousePress(canvas, Qt::LeftButton, Qt::NoModifier, p);
        QTest::mouseMove(canvas, p - QPoint(30, 0));
        QTest::keyClick(canvas, Qt::Key_Escape);
        QTest::mouseRelease(canvas, Qt::LeftButton, Qt::NoModifier, p - QPoint(30, 0));
        QCOMPARE(w.parameters(), old);
        w.part->openDimension("boreDiameter");
        QCOMPARE(w.part->dimensionPane->upper->text(), QString("0,"));
    }
    void edgeDraggingAndFixedOrigin()
    {
        pf::ui::Window w;
        QVERIFY(showActive(w));
        QTest::mouseClick(w.process->resetStock, Qt::LeftButton);
        QTRY_VERIFY(w.isReady());
        w.editValue("length", "110");
        QTRY_VERIFY(w.isReady());
        const auto defaults = w.parameters();
        auto canvas = w.part->canvas;
        for (const auto& key : {"length", "flangeWidth", "counterboreDepth", "flangeDiameter", "bodyDiameter",
                                "boreDiameter", "counterboreDiameter"})
            for (int side : {1, -1})
            {
                w.setParameters(defaults);
                QTRY_VERIFY(w.isReady());
                canvas->repaint();
                const auto origin = canvas->projectPoint(0, 0);
                const auto unit = QLineF(origin, canvas->projectPoint(1, 0)).length();
                const bool horizontal = QStringList{"length", "flangeWidth", "counterboreDepth"}.contains(key);
                const auto start = canvas->edgePoint(key, side).toPoint();
                const int direction = QString(key) == "length" ? -1 : 1;
                const auto delta = horizontal ? QPoint(-direction * std::max(4, qRound(unit * 2)), 0)
                                              : QPoint(0, -side * std::max(4, qRound(unit)));
                QTest::mouseMove(w.windowHandle(), canvas->mapTo(&w, start));
                QTRY_COMPARE(canvas->cursor().shape(), horizontal ? Qt::SizeHorCursor : Qt::SizeVerCursor);
                QTest::mousePress(w.windowHandle(), Qt::LeftButton, Qt::NoModifier, canvas->mapTo(&w, start));
                QTest::mouseMove(w.windowHandle(), canvas->mapTo(&w, start + delta));
                const double change =
                    (horizontal ? -delta.x() * (QString(key) == "length" ? 2 : 1) : -2 * side * delta.y()) / unit;
                QTRY_COMPARE(w.parameters().n(key), defaults.n(key) + std::round(change));
                QCOMPARE(canvas->projectPoint(0, 0).y(), origin.y());
                QVERIFY(std::abs(canvas->projectPoint(-w.parameters().n("length") / 2, 0).x() - canvas->width() / 2.) <
                        .01);
                QCOMPARE(QLineF(canvas->projectPoint(0, 0), canvas->projectPoint(1, 0)).length(), unit);
                QTest::mouseMove(w.windowHandle(), canvas->mapTo(&w, start + delta * 2));
                QTRY_COMPARE(w.parameters().n(key), defaults.n(key) + std::round(change * 2));
                QTest::mouseRelease(w.windowHandle(), Qt::LeftButton, Qt::NoModifier,
                                    canvas->mapTo(&w, start + delta * 2));
                QTRY_VERIFY(w.isReady());
                QVERIFY(std::abs(canvas->projectPoint(-w.parameters().n("length") / 2, 0).x() - canvas->width() / 2.) <
                        .01);
                w.editValue(key, pf::number(defaults.n(key) + direction * 3));
                QTRY_VERIFY(w.isReady());
                QVERIFY(std::abs(canvas->projectPoint(-w.parameters().n("length") / 2, 0).x() - canvas->width() / 2.) <
                        .01);
            }
        w.setParameters(defaults);
        QTRY_VERIFY(w.isReady());
        canvas->repaint();
        const auto start = canvas->edgePoint("flangeWidth").toPoint();
        QTest::mousePress(canvas, Qt::LeftButton, Qt::NoModifier, start);
        QTest::mouseMove(canvas, start - QPoint(2500, 0));
        QVERIFY(pf::validate(w.parameters()).isEmpty());
        QVERIFY(w.parameters().n("flangeWidth") < w.parameters().n("length"));
        QTest::mouseRelease(canvas, Qt::LeftButton, Qt::NoModifier, start - QPoint(2500, 0));
        QTRY_VERIFY(w.isReady());
    }
    void visibleSetupSwitchesAndPan()
    {
        pf::ui::Window w;
        QVERIFY(showActive(w));
        QTRY_VERIFY(w.isReady());
        w.selectPage(1);
        for (int section = 0; section < w.process->sections->count(); ++section)
        {
            w.process->sections->setCurrentRow(section);
            for (int index : {0, 1})
            {
                chooseFromList(w.process->setupChoice, index);
                QCOMPARE(w.process->setupChoice->currentIndex(), index);
                QCOMPARE(w.inspection->setups->currentIndex(), index);
            }
        }
        w.process->focusField("depthInside");
        QCOMPARE(w.process->setupChoice->currentIndex(), 0);
        QVERIFY(w.findChild<pf::ui::NumericEdit*>("parameter_depthInside")->hasFocus());
        w.selectPage(2);
        QApplication::processEvents();
        auto table = w.inspection->table;
        table->selectionModel()->select(w.inspection->transitions->index(2, 0),
                                        QItemSelectionModel::Select | QItemSelectionModel::Rows);
        auto selected = w.inspection->selectedTransitions();
        auto canvas = w.inspection->trajectory;
        canvas->repaint();
        auto origin = canvas->projectPoint(0, 0);
        for (auto button : {Qt::LeftButton, Qt::MiddleButton})
        {
            QPoint start(30, 50), delta(45, 18);
            QTest::mousePress(canvas, button, Qt::NoModifier, start);
            QTest::mouseMove(canvas, start + delta);
            QTest::mouseRelease(canvas, button, Qt::NoModifier, start + delta);
            QCOMPARE(canvas->projectPoint(0, 0), origin + delta);
            origin += delta;
            QCOMPARE(w.inspection->selectedTransitions(), selected);
        }
        const auto before = canvas->projectPoint(-30, 10);
        QWheelEvent wheel(before, canvas->mapToGlobal(before.toPoint()), {}, QPoint(0, 120), Qt::NoButton,
                          Qt::NoModifier, Qt::NoScrollPhase, false);
        QApplication::sendEvent(canvas, &wheel);
        QVERIFY(QLineF(before, canvas->projectPoint(-30, 10)).length() < .001);
        origin = canvas->projectPoint(0, 0) - QPointF(canvas->width() / 2., canvas->height() / 2.);
        chooseFromList(w.inspection->setups, 1);
        chooseFromList(w.inspection->setups, 0);
        QCOMPARE(w.inspection->selectedTransitions(), selected);
        QCOMPARE(canvas->projectPoint(0, 0) - QPointF(canvas->width() / 2., canvas->height() / 2.), origin);
        for (int index : {1, 0})
        {
            chooseFromList(w.controller, index);
            QTRY_VERIFY(w.isReady());
            QCOMPARE(w.parameters().s("controller"), index ? QString("sinumerik840dsl") : QString("axioma"));
        }
    }
    void fixedNcPartAcrossSetups()
    {
        for (const auto& controller : {"axioma", "sinumerik840dsl"})
        {
            pf::ui::Window w;
            QVERIFY(showActive(w));
            w.setController(controller);
            QTRY_VERIFY(w.isReady());
            w.selectPage(2);
            QApplication::processEvents();
            auto canvas = w.inspection->trajectory;
            const auto length = w.currentProject()->calculations.targetLength;
            const auto contour = pf::axisContour(w.parameters(), true);
            QCOMPARE(w.inspection->setups->itemText(0), QString("010"));
            QCOMPARE(w.inspection->setups->itemText(1), QString("020"));
            QCOMPARE(w.process->setupChoice->itemText(0), QString("010"));
            QCOMPARE(w.process->setupChoice->itemText(1), QString("020"));
            for (int gesture = 0; gesture < 2; ++gesture)
            {
                canvas->repaint();
                if (gesture)
                {
                    const QPoint start(30, 50), shift(37, 17);
                    QTest::mousePress(canvas, Qt::MiddleButton, Qt::NoModifier, start);
                    QTest::mouseMove(canvas, start + shift);
                    QTest::mouseRelease(canvas, Qt::MiddleButton, Qt::NoModifier, start + shift);
                    const auto pivot = canvas->rect().center();
                    QWheelEvent wheel(pivot, canvas->mapToGlobal(pivot), {}, QPoint(0, 120), Qt::NoButton,
                                      Qt::NoModifier, Qt::NoScrollPhase, false);
                    QApplication::sendEvent(canvas, &wheel);
                }
                QVector<QPointF> points;
                for (auto point : contour)
                    points.append(canvas->projectPoint(point.x(), point.y()));
                const auto position = canvas->mapTo(&w, QPoint());
                const auto size = canvas->size();
                screenshot(w, QString("nc-stable-010-%1-%2").arg(controller).arg(gesture));
                chooseFromList(w.inspection->setups, 1);
                QApplication::processEvents();
                canvas->repaint();
                QCOMPARE(canvas->mapTo(&w, QPoint()), position);
                QCOMPARE(canvas->size(), size);
                for (int i = 0; i < contour.size(); ++i)
                    QVERIFY(QLineF(canvas->projectPoint(-length - contour[i].x(), contour[i].y()), points[i]).length() <
                            .001);
                screenshot(w, QString("nc-stable-020-%1-%2").arg(controller).arg(gesture));
                chooseFromList(w.inspection->setups, 0);
                QApplication::processEvents();
                for (int i = 0; i < contour.size(); ++i)
                    QVERIFY(QLineF(canvas->projectPoint(contour[i].x(), contour[i].y()), points[i]).length() < .001);
            }
        }
    }
    void exportDialogAndImmediateSave()
    {
        QTemporaryDir dir;
        pf::ui::Window w;
        QVERIFY(showActive(w));
        QTRY_VERIFY(w.isReady());
        w.editValue("feedFinish", "0,08");
        QVERIFY(w.saveProject(dir.filePath("деталь.json"))); // no wait for the debounce timer
        QVERIFY(w.exportTo(dir.path()));                     // the JSON already exists in this directory
        QVERIFY(w.exportTo(dir.path()));                     // a second complete set is allocated automatically
        bool inspected = false;
        QTimer::singleShot(50, &w,
                           [&]
                           {
                               auto dialog = w.findChild<QDialog*>("exportResult");
                               if (!dialog)
                                   return;
                               auto files = dialog->findChild<QListWidget*>("exportedFiles");
                               inspected = files && files->count() == 4;
                               if (files)
                                   for (int i = 0; i < files->count(); ++i)
                                       inspected &= QFileInfo::exists(files->item(i)->data(Qt::UserRole).toString());
                               dialog->reject();
                           });
        w.showExportResult();
        QVERIFY(inspected);
        QCOMPARE(QSettings().value("files/exportDirectory").toString(), dir.path());
        inspected = false;
        QTimer::singleShot(
            50, &w,
            [&]
            {
                auto dialog = w.findChild<QDialog*>("exportResult");
                if (!dialog)
                    return;
                auto files = dialog->findChild<QListWidget*>("exportedFiles");
                inspected = files && files->count() == 4;
                if (files)
                    for (int i = 0; i < files->count(); ++i)
                        inspected &= QFileInfo(files->item(i)->data(Qt::UserRole).toString()).absolutePath() ==
                                     dir.filePath("Комплект 3");
                if (!output.isEmpty())
                    QVERIFY(dialog->grab().save(QDir(output).filePath("export-result-parent-folder.png")));
                dialog->reject();
            });
        QTest::mouseClick(w.exportButton, Qt::LeftButton);
        QVERIFY(inspected);
        QCOMPARE(QSettings().value("files/exportDirectory").toString(), dir.path());
        w.editValue("feedFinish", "0,09");
        bool russian = false;
        QTimer::singleShot(50, &w,
                           [&]
                           {
                               auto dialog = w.findChild<QMessageBox*>();
                               if (!dialog)
                                   return;
                               QStringList labels;
                               for (auto button : dialog->buttons())
                                   labels << button->text();
                               russian = labels.contains("Сохранить") && labels.contains("Не сохранять") &&
                                         labels.contains("Отмена");
                               dialog->reject();
                           });
        QVERIFY(!w.close());
        QVERIFY(russian);
    }
    void savePromptPosition()
    {
        for (const auto& theme : {QString("light"), QString("dark")})
        {
            pf::ui::applyTheme(theme);
            pf::ui::Window w;
            QVERIFY(showActive(w));
            QTRY_VERIFY(w.isReady());
            w.editValue("feedFinish", "0,09");
            for (int state = 0; state < 7; ++state)
            {
                if (state == 1 || state == 5)
                    w.showMaximized();
                else if (state == 3 || state == 6)
                    w.showMinimized();
                else
                    w.showNormal();
                QApplication::processEvents();
                for (int trigger = 0; trigger < 2; ++trigger)
                {
                    bool inspected = false;
                    QRect bounds, parentBounds;
                    QScreen* screen = nullptr;
                    QTimer::singleShot(100, &w, [&]
                    {
                        if (const auto dialog = w.findChild<QMessageBox*>())
                        {
                            inspected = dialog->isVisible();
                            bounds = dialog->frameGeometry();
                            parentBounds = w.isMinimized() ? w.normalGeometry() : w.frameGeometry();
                            screen = dialog->screen();
                            if (!output.isEmpty())
                                dialog->grab().save(QDir(output).filePath(
                                    QString("save-prompt-%1-%2-%3.png").arg(theme).arg(state).arg(trigger)));
                            dialog->reject();
                        }
                    });
                    if (trigger == 0)
                        QVERIFY(!w.close());
                    else
                        w.findChild<QMenu*>("fileMenu")->actions().first()->trigger();
                    QVERIFY(inspected);
                    qInfo() << theme << state << trigger << "dialog" << bounds << "parent" << parentBounds;
                    QCOMPARE(screen, w.screen());
                    QVERIFY(screen->availableGeometry().contains(bounds));
                    QVERIFY(QLineF(bounds.center(), parentBounds.center()).length() < 60);
                    QVERIFY(w.isVisible());
                    QVERIFY(!w.isMinimized());
                    QCOMPARE(w.isMaximized(), state == 1 || state == 5 || state == 6);
                    QCOMPARE(w.parameters().n("feedFinish"), .09);
                }
            }
        }
        pf::ui::applyTheme("light");
    }
    void exportFailureFeedback()
    {
        pf::ui::Window w;
        QVERIFY(showActive(w));
        QTRY_VERIFY(w.isReady());
        w.editValue("boringTool", "a40t");
        bool reported = false;
        QTimer::singleShot(50, &w,
                           [&]
                           {
                               const auto dialog = w.findChild<QMessageBox*>();
                               if (dialog)
                               {
                                   reported = dialog->windowTitle() == "Документы не выпущены" &&
                                              dialog->text().contains("оправк", Qt::CaseInsensitive);
                                   dialog->accept();
                               }
                           });
        w.exportButton->click();
        QVERIFY(reported);
    }
    void popupAndStateBoundaries()
    {
        pf::ui::Window w;
        QVERIFY(showActive(w));
        QTRY_VERIFY(w.isReady());
        w.showMaximized();
        QApplication::processEvents();
        w.part->openDimension("bodyDiameter");
        QVERIFY(w.part->dimensionPane);
        w.showNormal();
        w.resize(w.minimumSize());
        QApplication::processEvents();
        QVERIFY(!w.part->dimensionPane);
        w.part->openDimension("bodyDiameter");
        auto pane = w.part->dimensionPane;
        QVERIFY(pane);
        QVERIFY2(w.part->rect().contains(pane->geometry()), qPrintable(QString("popup %1x%2 at %3,%4 in %5x%6")
                                                                           .arg(pane->width())
                                                                           .arg(pane->height())
                                                                           .arg(pane->x())
                                                                           .arg(pane->y())
                                                                           .arg(w.part->width())
                                                                           .arg(w.part->height())));
        QVERIFY(pane->rect().contains(QRect(pane->lower->mapTo(pane, QPoint()), pane->lower->size())));
        chooseStandard(pane, "h7");
        QCOMPARE(w.parameters().s("bodyTolerance"), QString("h7"));
        for (int row = 0; row < pane->standards->count(); ++row)
        {
            QVERIFY(pane->standards->item(row)->data(Qt::UserRole).toString() != "m");
            QVERIFY(pane->standards->item(row)->text() != "Общий");
        }
        QTest::mouseClick(pane->upper, Qt::LeftButton);
        QTest::keyClick(pane->upper, Qt::Key_Escape);
        QCOMPARE(w.parameters().s("bodyTolerance"), QString("h7"));
        QVERIFY(!w.part->dimensionPane);
        QTRY_VERIFY(w.isReady());
        w.editValue("length", "-");
        w.selectPage(2);
        chooseFromList(w.inspection->setups, 1);
        QVERIFY(w.inspection->table->isEnabled());
        QVERIFY(w.inspection->trajectory->isEnabled());
        QCOMPARE(w.findChild<QLabel*>("ncSelectionSummary")->text(),
                 QString("Показана предыдущая УП: параметры изменены."));
        w.selectPage(0);
        auto input = nominalEditor(w);
        input->setFocus();
        pf::Parameters other;
        other.set("length", 110);
        w.setParameters(other, false);
        QVERIFY(!w.part->dimensionPane);
        input = nominalEditor(w);
        QCOMPARE(input->text(), QString("110"));
        QTest::mouseClick(input, Qt::LeftButton);
        QTest::keyClick(input, Qt::Key_Escape);
        QCOMPARE(w.parameters().n("length"), 110.);
        QTRY_VERIFY(w.isReady());
        screenshot(w, "popup-boundary-after-reset");
    }
    void compactToleranceEditing()
    {
        pf::ui::Window w;
        QVERIFY(showActive(w));
        QTRY_VERIFY(w.isReady());
        for (auto row : w.part->rows)
            QVERIFY(!row->input && row->dimensionValue);
        QCOMPARE(pf::ui::toleranceText(-11, 0), QString("0\n−11"));
        QCOMPARE(pf::ui::toleranceText(0, .22), QString("+0,22\n0"));
        QCOMPARE(pf::ui::toleranceText(-.22, .22), QString("±0,22"));
        w.part->openDimension("length");
        auto pane = w.part->dimensionPane;
        QVERIFY(!pane->findChild<QComboBox*>());
        pane->standards->setCurrentRow(0);
        pane->standards->setFocus();
        QTest::keyClick(pane->standards, Qt::Key_Up);
        QCOMPARE(QApplication::focusWidget(), pane->nominal);
        chooseStandard(pane, "IT12");
        QCOMPARE(pane->standards->item(6)->text(), QString("IT12"));
        QCOMPARE(pane->standards->item(6)->data(Qt::UserRole + 2).toDouble(), pf::toleranceWidth(116, 12) / 2);
        QVERIFY(!pane->upper->isReadOnly());
        QVERIFY(!pane->lower->isReadOnly());
        QTest::mouseClick(pane->upper, Qt::LeftButton);
        QVERIFY(!pane->upper->hasSelectedText());
        QTest::keyClicks(pane->upper, "-");
        QCOMPARE(w.parameters().s("lengthTolerance"), QString());
        QVERIFY(!w.isReady());
        QTest::keyClick(pane->upper, Qt::Key_Escape);
        QVERIFY(!w.part->dimensionPane);
        w.part->openDimension("length");
        QCOMPARE(w.part->dimensionPane->upper->text(), QString("-"));
        chooseStandard(w.part->dimensionPane, "IT12");
        QTRY_VERIFY(w.isReady());
        chooseStandard(w.part->dimensionPane, "IT16");
        QCOMPARE(w.parameters().s("lengthTolerance"), QString("IT16"));
        w.part->closeDimension();
        w.part->openDimension("flangeDiameter");
        pane = w.part->dimensionPane;
        chooseStandard(pane, "h7");
        QTest::mouseClick(pane->lower, Qt::LeftButton);
        QTest::keyClicks(pane->lower, "-0.11");
        QTRY_VERIFY(w.isReady());
        QCOMPARE(w.parameters().s("flangeTolerance"), QString());
        QCOMPARE(w.parameters().n("flangeLowerDeviation"), -.11);
        QCOMPARE(w.parameters().n("flangeUpperDeviation"), 0.);
        screenshot(w, "12-tolerance-direct-light");
        chooseStandard(pane, "h7");
        QCOMPARE(w.parameters().s("flangeTolerance"), QString("h7"));
        pf::ui::applyTheme("dark");
        screenshot(w, "13-tolerance-direct-dark");
        w.part->closeDimension();
        w.findChild<QCheckBox*>("drawingTolerances")->setChecked(true);
        screenshot(w, "14-drawing-deviations");
        pf::ui::applyTheme("light");
    }
    void unifiedDimensionEditing()
    {
        pf::ui::Window w;
        QVERIFY(showActive(w));
        QTest::mouseClick(w.process->resetStock, Qt::LeftButton);
        QTRY_VERIFY(w.isReady());
        auto row = w.part->rows[1];
        QVERIFY(row->dimensionValue && !row->input);
        const auto unchangedDrawing = w.part->canvas->grab().toImage();
        QTest::mouseClick(row->dimensionValue, Qt::LeftButton);
        auto pane = w.part->dimensionPane;
        QVERIFY(pane);
        QCOMPARE(QApplication::focusWidget(), pane->nominal);
        QCOMPARE(w.part->canvas->grab().toImage(), unchangedDrawing);
        const auto unchangedParameters = w.parameters();
        for (auto target : {static_cast<QWidget*>(pane->upper), static_cast<QWidget*>(pane->lower),
                            static_cast<QWidget*>(pane->standards)})
        {
            QTest::keyClick(w.windowHandle(), Qt::Key_Tab);
            QCOMPARE(QApplication::focusWidget(), target);
            QCOMPARE(w.parameters(), unchangedParameters);
        }
        chooseStandard(pane, "h7");
        w.part->focusField("flangeUpperDeviation");
        QCOMPARE(w.part->dimensionPane, pane);
        QCOMPARE(QApplication::focusWidget(), pane->upper);
        w.part->focusField("flangeDiameter");
        QCOMPARE(w.part->dimensionPane, pane);
        QCOMPARE(QApplication::focusWidget(), pane->nominal);
        QTest::mouseClick(pane->nominal, Qt::LeftButton);
        QTest::keyClicks(pane->nominal, "200");
        QTRY_VERIFY(w.isReady());
        QCOMPARE(w.parameters().n("flangeDiameter"), 200.);
        QCOMPARE(w.parameters().n("flangeLowerDeviation"), -.046);
        QCOMPARE(w.parameters().s("flangeTolerance"), QString("h7"));
        QVERIFY(row->dimensionValue->text().startsWith("200 "));
        if (!output.isEmpty())
            QVERIFY(pane->grab().save(QDir(output).filePath("18-compact-dimension-editor.png")));
        QTest::keyClick(pane->nominal, Qt::Key_Escape);
        QVERIFY(!w.part->dimensionPane);
        QTest::keyClick(w.windowHandle(), Qt::Key_1);
        QTest::keyClick(w.windowHandle(), Qt::Key_3);
        QTest::keyClick(w.windowHandle(), Qt::Key_0);
        QVERIFY(w.part->dimensionPane);
        QCOMPARE(w.part->dimensionPane->nominal->text(), QString("130"));
        QTRY_VERIFY(w.isReady());
        QCOMPARE(w.parameters().n("flangeLowerDeviation"), -.040);
        w.editValue("flangeDiameter", "-");
        QCOMPARE(row->dimensionValue->text().section(' ', 0, 0), QString("-"));
        w.part->closeDimension();
        QTest::mouseClick(row->dimensionValue, Qt::LeftButton);
        QCOMPARE(w.part->dimensionPane->nominal->text(), QString("-"));
        QVERIFY(!w.exportTo(output + "/invalid-unified-dimension"));
        screenshot(w, "17-unified-dimension-editor");
    }
    void replacementInputPaths()
    {
        struct ClipboardRestore
        {
            QMimeData* saved = new QMimeData;
            ClipboardRestore()
            {
                const auto current = QApplication::clipboard()->mimeData();
                for (const auto& format : current->formats())
                    saved->setData(format, current->data(format));
            }
            ~ClipboardRestore()
            {
                QApplication::clipboard()->setMimeData(saved);
            }
        } clipboard;
        const auto prepareClipboard = []
        {
            auto data = new QMimeData;
            data->setText("110");
#ifdef Q_OS_WIN
            // Keep test values out of clipboard-history monitors.
            data->setData("application/x-qt-windows-mime;value=\"Clipboard Viewer Ignore\"", QByteArray(1, '\0'));
            data->setData("application/x-qt-windows-mime;value=\"ExcludeClipboardContentFromMonitorProcessing\"",
                          QByteArray(1, '\0'));
#endif
            QApplication::clipboard()->setMimeData(data);
            return QApplication::clipboard()->text() == "110";
        };
        pf::ui::Window w;
        QVERIFY(showActive(w));
        QTRY_VERIFY(w.isReady());
        auto input = nominalEditor(w);
        for (const auto& shortcut : {QKeySequence(Qt::CTRL | Qt::Key_V), QKeySequence(Qt::SHIFT | Qt::Key_Insert)})
        {
            w.pageButtons[0]->setFocus();
            QTRY_VERIFY_WITH_TIMEOUT(prepareClipboard(), 3000);
            QTest::mouseClick(input, Qt::LeftButton);
            QVERIFY(!input->hasSelectedText());
            QSignalSpy edits(input, &QLineEdit::textEdited);
            QTest::keySequence(input, shortcut);
            QCOMPARE(input->text(), QString("110"));
            QCOMPARE(edits.size(), 1);
            QCOMPARE(edits[0][0].toString(), QString("110"));
            QTest::mouseClick(input, Qt::LeftButton);
            QTest::keyClick(input, Qt::Key_Home);
            QTest::keyClick(input, Qt::Key_Delete);
            QCOMPARE(input->text(), QString("10")); // second click edits one character
        }
        w.pageButtons[0]->setFocus();
        QTest::mouseClick(input, Qt::LeftButton);
        QInputMethodEvent ime;
        ime.setCommitString("110");
        QApplication::sendEvent(input, &ime);
        QCOMPARE(input->text(), QString("110"));
        w.pageButtons[0]->setFocus();
        QTest::mouseClick(input, Qt::LeftButton);
        QApplication::clipboard()->setText("109");
        QTimer::singleShot(20, input,
                           []
                           {
                               auto menu = qobject_cast<QMenu*>(QApplication::activePopupWidget());
                               if (!menu)
                                   return;
                               for (auto action : menu->actions())
                                   if (action->text() == "Вставить")
                                   {
                                       QTest::mouseClick(menu, Qt::LeftButton, Qt::NoModifier,
                                                         menu->actionGeometry(action).center());
                                       return;
                                   }
                               menu->close();
                           });
        QContextMenuEvent context(QContextMenuEvent::Mouse, input->rect().center(),
                                  input->mapToGlobal(input->rect().center()));
        QApplication::sendEvent(input, &context);
        QCOMPARE(input->text(), QString("109"));
        QTRY_VERIFY(w.isReady());
    }
    void rapidClicksAndHoverInput()
    {
        pf::ui::Window w;
        QVERIFY(showActive(w));
        QTRY_VERIFY(w.isReady());
        for (int page : {0, 1})
        {
            w.selectPage(page);
            w.process->sections->setCurrentRow(0);
            auto input = page == 0 ? nominalEditor(w) : w.findChild<pf::ui::NumericEdit*>("parameter_stockDiameter");
            w.pageButtons[page]->setFocus();
            auto native = input->window()->windowHandle();
            const auto point = input->mapTo(input->window(), input->rect().center());
            QTest::mouseClick(native, Qt::LeftButton, Qt::NoModifier, point, 1);
            QTest::mouseMove(native, point + QPoint(20, 0), 1);
            QVERIFY(!input->hasSelectedText());
            QTest::keyClicks(input, page == 0 ? "110" : "126");
            QCOMPARE(input->text(), page == 0 ? QString("110") : QString("126"));
            QTest::mouseDClick(native, Qt::LeftButton, Qt::NoModifier, point, 1);
            QVERIFY(!input->hasSelectedText());
            QTest::keyClick(input, Qt::Key_Home);
            QTest::keyClick(input, Qt::Key_Delete);
            QCOMPARE(input->text(), page == 0 ? QString("10") : QString("26"));
            QTest::keyClick(input, Qt::Key_A, Qt::ControlModifier);
            QCOMPARE(input->selectedText(), input->text());
            QTest::keyClicks(input, page == 0 ? "110" : "126");
            QTRY_VERIFY(w.isReady());
        }
    }
    void replacementHasNoCaretOrSelectionFlash()
    {
        pf::ui::Window w;
        QVERIFY(showActive(w));
        QTRY_VERIFY(w.isReady());
        w.part->openDimension("length");
        const auto pane = w.part->dimensionPane;
        const QVector<pf::ui::NumericEdit*> inputs = {pane->nominal, pane->upper, pane->lower};
        const QStringList values = {"110", "0.22", "-0.22"};
        for (int index = 0; index < inputs.size(); ++index)
        {
            auto input = inputs[index];
            bool unexpectedSelection = false;
            auto monitor = connect(input, &QLineEdit::selectionChanged, this,
                                   [&] { unexpectedSelection |= input->hasSelectedText(); });
            w.pageButtons[0]->setFocus();
            const auto original = input->text();
            const auto point = input->mapTo(&w, QPoint(input->width() / 2, input->height() / 2));
            QTest::mouseClick(w.windowHandle(), Qt::LeftButton, Qt::NoModifier, point, 1);
            QVERIFY(input->replacementArmed());
            QVERIFY(!unexpectedSelection);
            auto armed = input->grab();
            QTest::qWait(qApp->styleHints()->cursorFlashTime() / 2 + 60);
            QCOMPARE(input->grab().toImage(), armed.toImage()); // No caret may blink in the replacement state.
            if (!output.isEmpty())
                QVERIFY(armed.save(QDir(output).filePath(QString("cursor-%1-replacement.png").arg(index))));
            QTest::mouseClick(w.windowHandle(), Qt::LeftButton, Qt::NoModifier, point, 1);
            QVERIFY(!input->replacementArmed());
            QVERIFY(!unexpectedSelection);
            const auto position = input->cursorPosition();
            auto expected = original;
            expected.insert(position, "7");
            QTest::keyClicks(input, "7");
            QCOMPARE(input->text(), expected);
            QVERIFY(!unexpectedSelection);
            if (!output.isEmpty())
                QVERIFY(input->grab().save(QDir(output).filePath(QString("cursor-%1-editing.png").arg(index))));
            w.pageButtons[0]->setFocus();
            QTest::mouseClick(w.windowHandle(), Qt::LeftButton, Qt::NoModifier, point, 1);
            QVERIFY(input->replacementArmed());
            QSignalSpy edits(input, &QLineEdit::textEdited);
            QTest::keyClicks(input, values[index].left(1));
            QCOMPARE(edits.size(), 1);
            QCOMPARE(input->text(), values[index].left(1));
            QVERIFY(!input->replacementArmed());
            QVERIFY(!unexpectedSelection);
            QTest::keyClick(input, Qt::Key_Z, Qt::ControlModifier);
            QCOMPARE(input->text(), expected);
            disconnect(monitor);
            QTest::keyClick(input, Qt::Key_A, Qt::ControlModifier);
            QTest::keyClicks(input, values[index]);
            QTRY_VERIFY(w.isReady());
        }
        // Tab focus must enter replacement without QLineEdit's default select-all.
        bool selectedOnTab = false;
        connect(pane->upper, &QLineEdit::selectionChanged, this,
                [&] { selectedOnTab |= pane->upper->hasSelectedText(); });
        pane->nominal->setFocus();
        QTest::keyClick(w.windowHandle(), Qt::Key_Tab);
        QCOMPARE(QApplication::focusWidget(), pane->upper);
        QVERIFY(pane->upper->replacementArmed());
        QVERIFY(!selectedOnTab);
    }
    void replacementAfterDoubleClickFocusReturn()
    {
        pf::ui::Window w;
        QVERIFY(showActive(w));
        QTRY_VERIFY(w.isReady());
        w.part->openDimension("length");
        auto input = w.part->dimensionPane->nominal;
        auto native = w.windowHandle();
        const auto point = input->mapTo(&w, input->rect().center());
        QTest::mouseClick(native, Qt::LeftButton, Qt::NoModifier, point); // Clear prior native double-click timing.
        w.pageButtons[0]->setFocus();
        QTest::mouseClick(native, Qt::LeftButton, Qt::NoModifier, point, 1);
        QVERIFY(input->replacementArmed());
        struct Counter final : QObject
        {
            int doubles = 0;
            bool eventFilter(QObject*, QEvent* e) override
            {
                if (e->type() == QEvent::MouseButtonDblClick)
                    ++doubles;
                return false;
            }
        } counter;
        input->installEventFilter(&counter);
        w.pageButtons[0]->setFocus();
        QTest::mouseClick(native, Qt::LeftButton, Qt::NoModifier, point, 1);
        QCOMPARE(counter.doubles, 1);
        QVERIFY(input->replacementArmed());
        QVERIFY(!input->hasSelectedText());
        QTest::keyClicks(input, "110");
        QCOMPARE(input->text(), QString("110"));
        QTRY_VERIFY(w.isReady());
    }
    void stableViewButtonsAndMetadataReadout()
    {
        pf::ui::Window w;
        QVERIFY(showActive(w));
        QTRY_VERIFY(w.isReady());
        auto two = w.findChild<QToolButton*>("view2D");
        auto three = w.findChild<QToolButton*>("view3D");
        auto section = w.findChild<QCheckBox*>("modelSection");
        const auto twoPosition = two->mapTo(&w, QPoint());
        const auto threePosition = three->mapTo(&w, QPoint());
        for (int repeat = 0; repeat < 3; ++repeat)
        {
            QTest::mouseClick(three, Qt::LeftButton);
            QApplication::processEvents();
            QCOMPARE(two->mapTo(&w, QPoint()), twoPosition);
            QCOMPARE(three->mapTo(&w, QPoint()), threePosition);
            QVERIFY(section->mapTo(&w, QPoint()).x() < twoPosition.x());
            const auto sectionRight = section->mapTo(&w, QPoint(section->width(), 0)).x();
            QVERIFY(twoPosition.x() - sectionRight >= 0);
            QVERIFY(twoPosition.x() - sectionRight <= 40);
            QVERIFY(section->isVisible());
            if (repeat == 0)
            {
                QTest::mouseClick(section, Qt::LeftButton);
                screenshot(w, "section-toolbar-3d");
            }
            QTest::mouseClick(two, Qt::LeftButton);
            QApplication::processEvents();
            QCOMPARE(two->mapTo(&w, QPoint()), twoPosition);
            QCOMPARE(three->mapTo(&w, QPoint()), threePosition);
        }
        w.editValue("partName", "Long part name with a deliberately hidden ending");
        w.selectPage(1);
        w.process->focusField("partName");
        auto name = w.findChild<pf::ui::NumericEdit*>("parameter_partName");
        QVERIFY(name->width() >= 200);
        QTest::keyClick(name, Qt::Key_End);
        w.process->sections->setFocus();
        QCOMPARE(name->cursorPosition(), 0);
        QVERIFY(!name->hasSelectedText());
        screenshot(w, "metadata-prefix-and-width");
    }
    void dimensionOpensOnReleaseAndDefaultMetadata()
    {
        pf::ui::Window w;
        QVERIFY(showActive(w));
        QTRY_VERIFY(w.isReady());
        QVERIFY(!w.windowIcon().isNull());
        QVERIFY(w.windowIcon().availableSizes().contains(QSize(32, 32)));
        auto button = w.findChild<QToolButton*>("dimension_length");
        QVERIFY(button);
        QTest::mousePress(button, Qt::LeftButton);
        QVERIFY(!w.part->dimensionPane);
        QTest::mouseRelease(button, Qt::LeftButton);
        QVERIFY(w.part->dimensionPane);
        w.part->closeDimension();
        QTest::mousePress(button, Qt::LeftButton);
        QTest::mouseRelease(button, Qt::LeftButton, Qt::NoModifier, QPoint(-5, -5));
        QVERIFY(!w.part->dimensionPane);
        const auto canvas = w.part->canvas;
        const auto hit = canvas->dimensionBounds("length").center().toPoint();
        QTest::mousePress(canvas, Qt::LeftButton, Qt::NoModifier, hit);
        QVERIFY(!w.part->dimensionPane);
        QTest::mouseRelease(canvas, Qt::LeftButton, Qt::NoModifier, hit);
        QVERIFY(w.part->dimensionPane);
        w.part->closeDimension();
        const auto outside = QPoint(2, 2);
        QTest::mousePress(canvas, Qt::LeftButton, Qt::NoModifier, hit);
        QTest::mouseMove(canvas, outside);
        QTest::mouseRelease(canvas, Qt::LeftButton, Qt::NoModifier, outside);
        QVERIFY(!w.part->dimensionPane);
        QTest::mousePress(canvas, Qt::LeftButton, Qt::NoModifier, hit);
        QTest::mouseMove(canvas, outside);
        QTest::mouseMove(canvas, hit);
        QTest::mouseRelease(canvas, Qt::LeftButton, Qt::NoModifier, hit);
        QVERIFY(!w.part->dimensionPane);
        QTest::mouseClick(button, Qt::LeftButton);
        QVERIFY(w.part->dimensionPane);
        w.part->closeDimension();
        w.selectPage(1);
        w.process->focusField("organization");
        const auto organization = w.findChild<pf::ui::NumericEdit*>("parameter_organization");
        QCOMPARE(organization->text(), QString("МГТУ «СТАНКИН»"));
        QVERIFY(organization->property("preset").toBool());
        w.process->sections->setFocus();
        screenshot(w, "personal-default-metadata");
        for (auto input : w.findChildren<pf::ui::NumericEdit*>())
            QVERIFY(input->alignment().testFlag(Qt::AlignLeft));
        QTest::mouseClick(organization, Qt::LeftButton);
        QTest::keyClicks(organization, "My workshop");
        QTRY_COMPARE(w.parameters().s("organization"), QString("My workshop"));
        QVERIFY(!organization->property("preset").toBool());
        QTemporaryDir folder;
        QVERIFY(w.saveProject(folder.filePath("project.json")));
        pf::ui::Window reopened;
        QVERIFY(reopened.openProject(folder.filePath("project.json")));
        QCOMPARE(reopened.parameters().s("organization"), QString("My workshop"));
        QCOMPARE(reopened.parameters().s("developer"), QString("Худяков Е. Н."));
        QCOMPARE(reopened.parameters().s("checker"), QString("Абросимов М. А."));
    }
    void explicitSelectionKeepsItsAnchor()
    {
        pf::ui::Window w;
        QVERIFY(showActive(w));
        auto input = new pf::ui::NumericEdit(&w);
        input->setText("0123456789");
        input->setAlignment(Qt::AlignLeft);
        input->setGeometry(350, 60, 240, 34);
        input->show();
        input->setFocus();
        QTest::mouseClick(input, Qt::LeftButton);
        QTest::mouseClick(input, Qt::LeftButton);
        QTest::keyClick(input, Qt::Key_Home);
        QTest::keyClick(input, Qt::Key_Right);
        QTest::keyClick(input, Qt::Key_Right);
        input->grab();
        const auto pointFor = [input, &w](int position)
        {
            int first = -1, last = -1;
            for (int x = 0; x < input->width(); ++x)
                if (input->cursorPositionAt(QPoint(x, input->height() / 2)) == position)
                {
                    if (first < 0)
                        first = x;
                    last = x;
                }
            return input->mapTo(&w, QPoint((first + last) / 2, input->height() / 2));
        };
        QTest::mouseClick(w.windowHandle(), Qt::LeftButton, Qt::ShiftModifier, pointFor(6));
        QCOMPARE(input->selectedText(), QString("2345"));
        QTest::mouseClick(w.windowHandle(), Qt::LeftButton, Qt::ShiftModifier, pointFor(8));
        QCOMPARE(input->selectedText(), QString("234567"));
        QTest::mousePress(w.windowHandle(), Qt::LeftButton, Qt::ShiftModifier, pointFor(7));
        QTest::mouseMove(w.windowHandle(), pointFor(9));
        QTest::mouseRelease(w.windowHandle(), Qt::LeftButton, Qt::ShiftModifier, pointFor(9));
        QCOMPARE(input->selectedText(), QString("2345678"));
        QTest::mouseClick(w.windowHandle(), Qt::LeftButton, Qt::ShiftModifier, pointFor(1));
        QCOMPARE(input->selectedText(), QString("1"));
        QTest::mouseClick(w.windowHandle(), Qt::LeftButton, Qt::ShiftModifier, pointFor(0));
        QCOMPARE(input->selectedText(), QString("01"));
    }
    void readableChoicePopups()
    {
        pf::ui::Window w;
        QVERIFY(showActive(w));
        QTRY_VERIFY(w.isReady());
        const auto checkPopup = [this](pf::ui::ChoiceList* choice, const QString& name)
        {
            const auto point = choice->mapTo(choice->window(), QPoint(choice->width() - 12, choice->height() / 2));
            QTest::mouseClick(choice->window()->windowHandle(), Qt::LeftButton, Qt::NoModifier, point);
            QTRY_VERIFY(choice->view()->isVisible());
            QRect previous;
            for (int row = 0; row < choice->count(); ++row)
            {
                const auto rect = choice->view()->visualRect(choice->model()->index(row, 0));
                QCOMPARE(rect.height(), 30);
                QVERIFY(choice->view()->viewport()->rect().contains(rect));
                QVERIFY(!previous.intersects(rect));
                previous = rect;
            }
            const auto popup = choice->view()->window();
            QVERIFY(popup->geometry().top() >= choice->mapToGlobal(QPoint(0, choice->height())).y() ||
                    popup->geometry().bottom() <= choice->mapToGlobal(QPoint()).y());
            if (!output.isEmpty())
                QVERIFY(popup->grab().save(QDir(output).filePath(name + ".png")));
            QTest::keyClick(popup->windowHandle(), Qt::Key_Escape);
            QTRY_VERIFY(!choice->view()->isVisible());
        };
        checkPopup(w.controller, "choice-controller");
        w.selectPage(1);
        checkPopup(w.process->setupChoice, "choice-process-setup");
        w.selectPage(2);
        checkPopup(w.inspection->setups, "choice-nc-setup");
        w.selectPage(1);
        w.process->focusField("material");
        checkPopup(w.findChild<pf::ui::ChoiceList*>("materialChoices"), "choice-steel");
    }
    void allStandardTolerancesVisible()
    {
        pf::ui::Window w;
        QVERIFY(showActive(w));
        QTRY_VERIFY(w.isReady());
        for (const auto& descriptor : pf::dimensions())
        {
            w.part->openDimension(descriptor.key);
            auto pane = w.part->dimensionPane;
            QApplication::processEvents();
            QVERIFY(w.part->rect().contains(pane->geometry()));
            QVERIFY(!pane->standards->verticalScrollBar()->isVisible());
            QVERIFY(!pane->standards->horizontalScrollBar()->isVisible());
            for (int row = 0; row < pane->standards->count(); ++row)
                QVERIFY(pane->standards->viewport()->rect().contains(
                    pane->standards->visualItemRect(pane->standards->item(row))));
            chooseStandard(pane, descriptor.positions.last() + "16");
            QCOMPARE(w.parameters().s(descriptor.designation), descriptor.positions.last() + "16");
            if (!output.isEmpty())
                QVERIFY(pane->grab().save(QDir(output).filePath("all-tolerances-" + descriptor.key + ".png")));
            w.part->closeDimension();
        }
    }
    void editableStockAndReadablePreviousNc()
    {
        pf::ui::Window w;
        QTemporaryDir folder;
        QVERIFY(showActive(w));
        QTRY_VERIFY(w.isReady());
        QVERIFY(w.parameters().b("autoStock"));
        QVERIFY(w.part->findChild<QCheckBox*>("drawingTolerances")->isChecked());
        w.selectPage(1);
        auto bore = w.findChild<pf::ui::NumericEdit*>("parameter_stockBore");
        QVERIFY(bore->isEnabled());
        QTest::mouseClick(bore, Qt::LeftButton);
        QTest::keyClicks(bore, "44");
        QTRY_VERIFY(w.isReady());
        QVERIFY(!w.parameters().b("autoStock"));
        QCOMPARE(w.parameters().n("stockBore"), 44.);
        w.editValue("stockBore", "-");
        QVERIFY(!w.isReady());
        QVERIFY(!w.parameters().b("autoStock"));
        w.selectPage(2);
        QVERIFY(w.inspection->code->isReadOnly());
        QVERIFY(w.inspection->code->isEnabled());
        QVERIFY(w.inspection->table->isEnabled());
        QVERIFY(w.inspection->trajectory->isEnabled());
        QCOMPARE(w.inspection->code->palette().color(QPalette::Text), pf::ui::ink());
        for (int setup : {1, 0})
        {
            chooseFromList(w.inspection->setups, setup);
            QVERIFY(!w.inspection->code->toPlainText().isEmpty());
            QCOMPARE(w.process->setupChoice->currentIndex(), setup);
        }
        const auto origin = w.inspection->trajectory->projectPoint(0, 0);
        QTest::mousePress(w.inspection->trajectory, Qt::MiddleButton, Qt::NoModifier, QPoint(30, 50));
        QTest::mouseMove(w.inspection->trajectory, QPoint(65, 70));
        QTest::mouseRelease(w.inspection->trajectory, Qt::MiddleButton, Qt::NoModifier, QPoint(65, 70));
        QCOMPARE(w.inspection->trajectory->projectPoint(0, 0), origin + QPointF(35, 20));
        QVERIFY(!w.exportTo(folder.path()));
        QVERIFY(QDir(folder.path()).entryList(QDir::Files).isEmpty());
        screenshot(w, "previous-nc-during-invalid-input");
        w.editValue("stockBore", "40");
        QCOMPARE(w.parameters().n("stockBore"), 40.);
        QCOMPARE(bore->text(), QString("40"));
        const auto errors = pf::validate(w.parameters());
        QVERIFY(std::any_of(errors.begin(), errors.end(), [](const auto& error)
                            { return error.field == "clearanceX" && error.message.contains("44"); }));
        w.selectPage(1);
        w.process->sections->setCurrentRow(0);
        const auto before = w.process->diagram->grab().toImage();
        w.process->sections->setFocus();
        QTest::mouseClick(bore, Qt::LeftButton);
        QTest::keyClicks(bore, "20");
        QCOMPARE(w.parameters().n("stockBore"), 20.);
        QCOMPARE(bore->text(), QString("20"));
        QVERIFY(!w.parameters().b("autoStock"));
        QVERIFY(w.process->diagram->grab().toImage() != before);
        QVERIFY(!w.exportTo(folder.path()));
        QVERIFY(QDir(folder.path()).entryList(QDir::Files).isEmpty());
        screenshot(w, "manual-stock-bore-tool-incompatible");
        QTest::mouseClick(w.process->resetStock, Qt::LeftButton);
        QTRY_VERIFY(w.isReady());
        QVERIFY(w.parameters().b("autoStock"));
        QCOMPARE(w.parameters().n("stockBore"), 45.);
        w.editValue("stockBore", "-");
        QTest::mouseClick(w.process->resetStock, Qt::LeftButton);
        QTRY_VERIFY(w.isReady());
        QVERIFY(bore->text() != "-");
    }
    void steelPresetsAndCustomMaterial()
    {
        pf::ui::Window w;
        QTemporaryDir folder;
        QVERIFY(showActive(w));
        QTRY_VERIFY(w.isReady());
        w.selectPage(1);
        w.process->focusField("material");
        auto choices = w.findChild<pf::ui::ChoiceList*>("materialChoices");
        QVERIFY(choices && choices->isEditable());
        QCOMPARE(choices->count(), 6);
        const auto before = w.parameters();
        chooseFromList(choices, 2);
        QTRY_VERIFY(w.isReady());
        QCOMPARE(w.parameters().s("material"), QString("Сталь 40Х"));
        auto expected = before;
        expected.set("material", "Сталь 40Х");
        QCOMPARE(w.parameters(), expected); // Selecting a grade does not silently alter cutting modes.
        auto input = choices->lineEdit();
        w.process->sections->setFocus();
        QTest::mouseClick(input, Qt::LeftButton);
        QTest::keyClicks(input, "Custom steel 17");
        QTRY_VERIFY(w.isReady());
        QCOMPARE(w.parameters().s("material"), QString("Custom steel 17"));
        QVERIFY(w.saveProject(folder.filePath("material.json")));
        QVERIFY(w.openProject(folder.filePath("material.json")));
        QTRY_VERIFY(w.isReady());
        QCOMPARE(choices->currentText(), QString("Custom steel 17"));
        chooseFromList(choices, 5);
        QTRY_VERIFY(w.isReady());
        QCOMPARE(w.parameters().s("material"), QString("Сталь 12Х18Н10Т"));
        QVERIFY(w.exportTo(folder.path()));
        screenshot(w, "steel-material-choice");
    }
    void automaticPartFitAndStableExport()
    {
        QTemporaryDir folder;
        pf::ui::Window w;
        QVERIFY(showActive(w));
        QTRY_VERIFY(w.isReady());
        QTest::mouseClick(w.process->resetStock, Qt::LeftButton);
        QTRY_VERIFY(w.isReady());
        QVERIFY(!w.findChild<QToolButton*>("fitPart"));
        auto canvas = w.part->canvas;
        const auto origin = canvas->projectPoint(0, 0);
        const auto originalScale = QLineF(origin, canvas->projectPoint(1, 0)).length();
        w.part->openDimension("length");
        QCOMPARE(QLineF(origin, canvas->projectPoint(1, 0)).length(), originalScale);
        w.part->closeDimension();
        w.editValue("flangeDiameter", "200");
        QVERIFY(w.exportButton->isEnabled());
        QVERIFY(!w.isReady());
        QVERIFY(w.exportTo(folder.path())); // capture the current dimensions before debounce finishes
        QCOMPARE(w.currentProject()->parameters.n("flangeDiameter"), 200.);
        for (const auto& program : w.currentProject()->programs)
        {
            QFile file(folder.filePath("Комплект 1/" + program.fileName));
            QVERIFY(file.open(QIODevice::ReadOnly));
            QCOMPARE(QString::fromUtf8(file.readAll()), program.text);
        }
        canvas->repaint();
        QVERIFY(std::abs(canvas->projectPoint(-w.parameters().n("length") / 2, 0).x() - canvas->width() / 2.) < .01);
        QVERIFY(QLineF(canvas->projectPoint(0, 0), canvas->projectPoint(1, 0)).length() < originalScale);
        QVERIFY(canvas->rect().contains(canvas->edgePoint("flangeDiameter").toPoint()));
        w.editValue("flangeDiameter", "-");
        QVERIFY(w.exportButton->isEnabled());
        QVERIFY(!w.exportTo(folder.filePath("invalid")));
        QVERIFY(!QDir(folder.filePath("invalid")).exists());
    }
    void clearNcSelectionAndLocalControls()
    {
        pf::ui::Window w;
        QVERIFY(showActive(w));
        QTRY_VERIFY(w.isReady());
        w.selectPage(2);
        QApplication::processEvents();
        auto view = w.inspection;
        const auto toolbar = w.findChild<QWidget*>("trajectoryToolbar");
        QVERIFY(toolbar);
        QCOMPARE(toolbar->parentWidget(), view->trajectory->parentWidget());
        QVERIFY(toolbar->geometry().bottom() < view->trajectory->geometry().top());
        for (auto surface : {static_cast<QWidget*>(view->table->viewport()), view->code->viewport(),
                             static_cast<QWidget*>(view->trajectory)})
        {
            view->table->selectRow(1);
            QVERIFY(!view->code->extraSelections().isEmpty());
            QTest::mouseClick(surface, Qt::RightButton);
            QVERIFY(view->selectedTransitions().isEmpty());
            QVERIFY(view->table->selectionModel()->selectedRows().isEmpty());
            QVERIFY(view->code->extraSelections().isEmpty());
            chooseFromList(view->setups, 1);
            chooseFromList(view->setups, 0);
            QVERIFY(view->selectedTransitions().isEmpty());
            QVERIFY(view->code->extraSelections().isEmpty());
        }
        view->table->selectRow(2);
        view->code->setFocus();
        QTest::keyClick(view->code, Qt::Key_A, Qt::ControlModifier);
        QTest::keyClick(view->code, Qt::Key_Escape);
        QVERIFY(!view->code->textCursor().hasSelection());
        QVERIFY(view->selectedTransitions().isEmpty());
        auto fit = w.findChild<QToolButton*>("fitTrajectory");
        QVERIFY(fit);
        fit->setFocus();
        QTest::mouseClick(fit, Qt::LeftButton);
        view->trajectory->grab(); // flush the reset viewport before deriving screen coordinates
        const auto moves = w.currentProject()->programs[0].verification.moves;
        bool clicked = false;
        for (qsizetype i = 1; i < moves.size(); ++i)
        {
            if (moves[i].mode != "G1" || moves[i].tool != moves[i - 1].tool)
                continue;
            const auto point =
                view->trajectory->projectPoint((moves[i].z + moves[i - 1].z) / 2, (moves[i].x + moves[i - 1].x) / 4)
                    .toPoint();
            if (!view->trajectory->rect().adjusted(10, 10, -10, -10).contains(point))
                continue;
            QSignalSpy block(view->trajectory, &pf::ui::TrajectoryCanvas::blockClicked);
            QTest::mouseClick(view->trajectory, Qt::LeftButton, Qt::NoModifier, point);
            QCOMPARE(block.size(), 1);
            QVERIFY(!view->selectedTransitions().isEmpty());
            QTRY_COMPARE(QApplication::focusWidget(), view->trajectory);
            QTest::keyClick(w.windowHandle(), Qt::Key_Escape);
            QVERIFY(view->selectedTransitions().isEmpty());
            QVERIFY(view->code->extraSelections().isEmpty());
            clicked = true;
            break;
        }
        QVERIFY(clicked);
        w.setController("sinumerik840dsl");
        QTRY_VERIFY(w.isReady());
        QVERIFY(view->selectedTransitions().isEmpty());
        QVERIFY(view->code->extraSelections().isEmpty());
        screenshot(w, "15-nc-colors-and-controls");
    }
    void editableStockDimensions()
    {
        pf::ui::Window w;
        QVERIFY(showActive(w));
        QTRY_VERIFY(w.isReady());
        w.selectPage(1);
        w.process->sections->setCurrentRow(pf::ui::ProcessView::Preparation);
        const auto input = [&](const QString& key) { return w.findChild<pf::ui::NumericEdit*>("parameter_" + key); };
        QCOMPARE(input("blankLength")->text(), QString("157,5"));
        QCOMPARE(input("blankOverhang")->text(), QString("132,5"));
        w.process->focusField("stockBore");
        QTest::keyClick(w.windowHandle(), Qt::Key_Down);
        QCOMPARE(QApplication::focusWidget(), input("blankLength"));
        QTest::keyClicks(input("blankLength"), "180");
        QTest::keyClick(w.windowHandle(), Qt::Key_Return);
        QTRY_VERIFY(w.isReady());
        QVERIFY(!w.parameters().b("autoBlankLength"));
        w.editValue("blankOverhang", "140");
        QTRY_VERIFY(w.isReady());
        QVERIFY(!input("gripLength"));
        QVERIFY(!input("chuckClearance"));
        QCOMPARE(pf::calculate(w.parameters()).gripLength, 40.);
        QCOMPARE(pf::calculate(w.parameters()).chuckClearance, 17.5);
        w.editValue("length", "117");
        QTRY_VERIFY(w.isReady());
        QCOMPARE(w.parameters().n("blankLength"), 180.);
        QCOMPARE(w.parameters().n("blankOverhang"), 140.);
        for (const auto& theme : {QString("light"), QString("dark")})
        {
            pf::ui::applyTheme(theme);
            w.process->focusField("blankOverhang");
            screenshot(w, "stock-length-overhang-" + theme);
        }
        w.editValue("blankLength", "150");
        QVERIFY(!w.isReady());
        QCOMPARE(w.parameters().n("blankLength"), 150.);
        w.editValue("blankLength", "-");
        QTest::mouseClick(w.process->resetStock, Qt::LeftButton);
        QTRY_VERIFY(w.isReady());
        QVERIFY(w.parameters().b("autoBlankLength"));
        QVERIFY(w.parameters().b("autoBlankOverhang"));
        QCOMPARE(w.parameters().n("blankLength"), 158.5);
        QCOMPARE(w.parameters().n("blankOverhang"), 133.5);
        w.editValue("blankLength", "180");
        w.editValue("blankOverhang", "145");
        QTRY_VERIFY(w.isReady());
        QVERIFY(!w.parameters().b("autoBlankLength"));
        QCOMPARE(w.parameters().n("blankLength"), 180.);
        QCOMPARE(pf::calculate(w.parameters()).gripLength, 35.);
        pf::ui::applyTheme("light");
    }
    void technologyExtensionLines()
    {
        const auto result = pf::generate(pf::Parameters());
        QVERIFY(result.ok());
        const auto& project = *result.project;
        QImage image(900, 650, QImage::Format_ARGB32);
        image.fill(pf::ui::paper());
        QPainter painter(&image);
        const auto& setup = project.programs[0].setup;
        const auto drawing = pf::ui::drawSetup(painter, image.rect(), project.parameters, setup, "gripLength");
        painter.end();
        QCOMPARE(drawing.extensionLines.size(), 2);
        QCOMPARE(drawing.extensionLines[0].p1(),
                 drawing.map(setup.jawsFrontZ - setup.gripLength, -setup.gripDiameter / 2 - 15));
        QCOMPARE(drawing.extensionLines[1].p1(), drawing.map(setup.jawsFrontZ, -setup.gripDiameter / 2 - 15));
        QCOMPARE(drawing.extensionLines[0].p2(), drawing.parameterIndicator.p1());
        QCOMPARE(drawing.extensionLines[1].p2(), drawing.parameterIndicator.p2());
        const auto calculated = pf::calculate(project.parameters);
        {
            QPainter check(&image);
            const auto gap = pf::ui::drawSetup(check, image.rect(), project.parameters, setup, "chuckClearance");
            QCOMPARE(gap.extensionLines[0].p1(),
                     gap.map(calculated.partingBackZ, -project.parameters.n("stockDiameter") / 2));
            QCOMPARE(gap.extensionLines[1].p1(), gap.map(setup.jawsFrontZ, -setup.gripDiameter / 2));
        }
        for (const auto& program : project.programs)
        {
            QPainter check(&image);
            const auto clearance =
                pf::ui::drawSetup(check, image.rect(), project.parameters, program.setup, "clearanceZ");
            QCOMPARE(clearance.extensionLines.size(), 2);
            const auto faceZ = program.setup.flipped ? project.parameters.n("trimAllowance") : 0.;
            const auto faceRadius =
                program.setup.flipped ? calculated.targetBodyDiameter / 2 : project.parameters.n("stockDiameter") / 2;
            QCOMPARE(clearance.extensionLines[0].p1(),
                     clearance.map(faceZ, faceRadius + project.parameters.n("clearanceX")));
            QCOMPARE(clearance.extensionLines[0].p2(), clearance.parameterIndicator.p1());
            QCOMPARE(clearance.extensionLines[1].p1(), clearance.map(faceZ + project.parameters.n("clearanceZ"),
                                                                     faceRadius + project.parameters.n("clearanceX")));
            QCOMPARE(clearance.extensionLines[1].p2(), clearance.parameterIndicator.p2());
            check.end();
            QPainter radialCheck(&image);
            const auto radial =
                pf::ui::drawSetup(radialCheck, image.rect(), project.parameters, program.setup, "clearanceX");
            QCOMPARE(radial.extensionLines.size(), 2);
            QCOMPARE(radial.extensionLines[0].p1(), radial.map(faceZ + project.parameters.n("clearanceZ"), faceRadius));
            QCOMPARE(radial.extensionLines[1].p1(), radial.map(faceZ + project.parameters.n("clearanceZ"),
                                                               faceRadius + project.parameters.n("clearanceX")));
        }
    }
    void depthDimensionsStartAtAllowanceEdge()
    {
        const auto generated = pf::generate(pf::Parameters());
        QVERIFY(generated.ok());
        const auto& project = *generated.project;
        const auto& p = project.parameters;
        const auto& c = project.calculations;
        for (const auto& key : {"depthOutside", "depthInside", "finishAllowance"})
        {
            QImage image(900, 650, QImage::Format_ARGB32);
            image.fill(pf::ui::paper());
            QPainter painter(&image);
            const auto drawing = pf::ui::drawSetup(painter, image.rect(), p, project.programs[0].setup, key);
            painter.end();
            const bool inside = QString(key) == "depthInside";
            const auto& transition = project.programs[0].transitions[inside ? 6 : 2];
            const bool rough = QString(key) != "finishAllowance";
            const auto z = inside ? 0 : -c.targetFlangeWidth;
            const auto from = rough ? transition.startDiameter / 2 : c.targetBodyDiameter / 2;
            const auto to = rough ? transition.passes.first() / 2 : from + p.n(key);
            if (rough)
            {
                QCOMPARE(drawing.cuttingPasses.size(), transition.passes.size());
                for (int i = 0; i < transition.passes.size(); ++i)
                {
                    QCOMPARE(drawing.cuttingPasses[i].p1(), drawing.map(transition.zStart, transition.passes[i] / 2));
                    QCOMPARE(drawing.cuttingPasses[i].p2(), drawing.map(transition.zEnd, transition.passes[i] / 2));
                }
            }
            QCOMPARE(drawing.extensionLines.size(), 2);
            QCOMPARE(drawing.extensionLines[0].p1(), drawing.map(z, from));
            QCOMPARE(drawing.extensionLines[1].p1(), drawing.map(z, to));
            QCOMPARE(drawing.extensionLines[0].p2(), drawing.parameterIndicator.p1());
            QCOMPARE(drawing.extensionLines[1].p2(), drawing.parameterIndicator.p2());
            QVERIFY(drawing.parameterIndicator.x1() > drawing.map(0, 0).x());
            QVERIFY(std::abs(drawing.parameterIndicator.length() / drawing.scale - std::abs(to - from)) < 1e-8);
        }
    }
    void clampDimensionCaptionOutsideJaws()
    {
        const auto generated = pf::generate(pf::Parameters());
        QVERIFY(generated.ok());
        const auto& project = *generated.project;
        for (const auto size : {QSize(360, 350), QSize(900, 650), QSize(700, 1000)})
            for (const auto& program : project.programs)
            {
                QImage image(size, QImage::Format_ARGB32);
                image.fill(pf::ui::paper());
                QPainter painter(&image);
                const auto drawing =
                    pf::ui::drawSetup(painter, image.rect(), project.parameters, program.setup, "gripLength");
                painter.end();
                const auto& setup = program.setup;
                const auto jawRadius = setup.gripDiameter / 2;
                const QRectF jaw(drawing.map(setup.jawsFrontZ - setup.gripLength, -jawRadius),
                                 drawing.map(setup.jawsFrontZ, -jawRadius - 15));
                QVERIFY(!drawing.parameterLabel.intersects(jaw.normalized()));
                QVERIFY(drawing.parameterLabel.top() >= jaw.normalized().bottom() + 7);
                QVERIFY(drawing.parameterLabel.bottom() < drawing.parameterIndicator.y1());
                QCOMPARE(drawing.parameterLabel.center().x(), drawing.parameterIndicator.center().x());
                QVERIFY(QRectF(image.rect()).contains(drawing.parameterLabel));
            }
    }
    void workOriginCrosshairs()
    {
        for (const auto& theme : {QString("light"), QString("dark")})
        {
            pf::ui::applyTheme(theme);
            QImage image(64, 64, QImage::Format_ARGB32);
            image.fill(pf::ui::paper());
            QPainter painter(&image);
            painter.setRenderHint(QPainter::Antialiasing);
            pf::ui::drawWorkOrigin(painter, QPointF(32, 32));
            painter.end();
            for (const auto offset : {QPoint(12, 0), QPoint(-12, 0), QPoint(0, 12), QPoint(0, -12), QPoint(4, 0),
                                      QPoint(-4, 0), QPoint(0, 4), QPoint(0, -4)})
            {
                const auto color = image.pixelColor(QPoint(32, 32) + offset);
                QVERIFY(std::abs(color.lightness() - pf::ui::paper().lightness()) > 60);
            }
            for (const auto point : {QPoint(28, 28), QPoint(25, 27)})
            {
                int contrast = 0;
                for (int x = -1; x <= 1; ++x)
                    for (int y = -1; y <= 1; ++y)
                        contrast = std::max(contrast, std::abs(image.pixelColor(point + QPoint(x, y)).lightness() -
                                                               pf::ui::paper().lightness()));
                QVERIFY(contrast > 60);
            }
            image.save(output + "/work-origin-" + theme + ".png");
        }
        pf::ui::applyTheme("light");
    }
    void technologyAxesInCorner()
    {
        const auto generated = pf::generate(pf::Parameters());
        QVERIFY(generated.ok());
        for (const auto size : {QSize(360, 350), QSize(900, 650), QSize(700, 1000)})
            for (const auto& program : generated.project->programs)
            {
                QImage image(size, QImage::Format_ARGB32);
                image.fill(pf::ui::paper());
                QPainter painter(&image);
                const auto bounds = QRectF(image.rect()).adjusted(8, 8, -8, -8);
                auto viewport = pf::ui::fitSetup(bounds, generated.project->parameters, program.setup, false, true);
                const auto initial = pf::ui::drawSetup(painter, bounds, generated.project->parameters, program.setup,
                                                       "blankOverhang", false, viewport);
                viewport.origin += QPointF(80, -60);
                viewport.scale *= .7;
                const auto shifted = pf::ui::drawSetup(painter, bounds, generated.project->parameters, program.setup,
                                                       "chuckClearance", false, viewport);
                QCOMPARE(shifted.axesOrigin, initial.axesOrigin);
                QCOMPARE(shifted.axesOrigin.y(), bounds.bottom() - 16);
                const auto left = shifted.axesOrigin.x() - (shifted.zDirection < 0 ? 58 : 0);
                QCOMPARE(left, bounds.left() + 20);
            }
    }
    void compactPanelsAndTopAlignment()
    {
        pf::ui::Window w;
        QVERIFY(showActive(w));
        QTRY_VERIFY(w.isReady());
        const auto panel = w.findChild<QWidget*>("partParameters");
        const auto collapse = w.findChild<QToolButton*>("collapseParameters");
        QVERIFY(panel && collapse);
        QCOMPARE(panel->mapTo(&w, QPoint()).y(), w.part->canvas->mapTo(&w, QPoint()).y());
        QCOMPARE(collapse->mapTo(&w, collapse->rect().bottomRight()).y(),
                 w.part->canvas->mapTo(&w, w.part->canvas->rect().bottomRight()).y());
        const auto origin = w.part->canvas->projectPoint(0, 0);
        const auto unit = w.part->canvas->projectPoint(1, 0) - origin;
        w.part->openDimension("length");
        QCOMPARE(w.part->canvas->projectPoint(0, 0), origin);
        QCOMPARE(w.part->canvas->projectPoint(1, 0) - origin, unit);
        w.part->closeDimension();
        w.selectPage(1);
        QApplication::processEvents();
        auto sections = w.process->sections;
        auto parameters = w.findChild<QStackedWidget*>("processParameters");
        QVERIFY(parameters);
        const auto sectionWidth = sections->width(), parameterWidth = parameters->width();
        QVERIFY(sectionWidth < 150 && parameterWidth < 350);
        QVERIFY(w.process->diagram->width() > w.process->width() * .45);
        const auto normalHeight = w.process->diagram->height();
        const auto top = w.process->diagram->mapTo(w.process, QPoint()).y();
        w.showMaximized();
        QApplication::processEvents();
        QCOMPARE(sections->width(), sectionWidth);
        QCOMPARE(parameters->width(), parameterWidth);
        QCOMPARE(w.process->diagram->mapTo(w.process, QPoint()).y(), top);
        QVERIFY(w.process->diagram->height() >= normalHeight);
        screenshot(w, "compact-technology-top");
        pf::ui::PartCanvas canvas;
        canvas.resize(500, 650);
        canvas.setParameters(w.parameters());
        canvas.grab();
        const auto firstTop = canvas.projectPoint(0, w.parameters().n("flangeDiameter") / 2).y();
        canvas.resize(500, 950);
        canvas.grab();
        QCOMPARE(canvas.projectPoint(0, w.parameters().n("flangeDiameter") / 2).y(), firstTop);
    }
    void stableSetupOrientationAndHatching()
    {
        const auto g = pf::generate(pf::Parameters());
        QVERIFY(g.ok());
        const auto& p = g.project->parameters;
        const QRectF area(0, 0, 900, 650);
        const auto first = pf::ui::fitSetup(area, p, g.project->programs[0].setup, false, true);
        const auto second = pf::ui::fitSetup(area, p, g.project->programs[1].setup, false, true);
        for (auto point : pf::axisContour(p, true))
            QVERIFY(QLineF(first.map(point.x(), point.y()),
                           second.map(-g.project->calculations.targetLength - point.x(), point.y()))
                        .length() < .001);
        const auto render = [&](pf::ui::SetupMapping mapping)
        {
            QImage image(900, 650, QImage::Format_ARGB32);
            image.fill(pf::ui::paper());
            QPainter painter(&image);
            pf::ui::drawSetup(painter, area, p, g.project->programs[0].setup, {}, false, mapping);
            return image;
        };
        const auto before = render(first);
        auto moved = first;
        const QPoint shift(23, 11);
        moved.origin += shift;
        const auto after = render(moved);
        const auto patch = QRect(first.map(-60, 35).toPoint() - QPoint(7, 7), QSize(14, 14));
        QCOMPARE(before.copy(patch), after.copy(patch.translated(shift)));
        pf::ui::Window w;
        QVERIFY(showActive(w));
        QTRY_VERIFY(w.isReady());
        auto collapse = w.findChild<QToolButton*>("collapseParameters");
        collapse->click();
        QApplication::processEvents();
        QCOMPARE(collapse->mapTo(&w, collapse->rect().bottomRight()).y(),
                 w.part->canvas->mapTo(&w, w.part->canvas->rect().bottomRight()).y());
        w.selectPage(1);
        w.editValue("cutoffWidth", "4,5");
        QTRY_VERIFY(w.isReady());
        QCOMPARE(w.currentProject()->calculations.cutoffWidth, 4.5);
        w.process->focusField("cutoffWidth");
        QCOMPARE(w.process->sections->currentRow(), int(pf::ui::ProcessView::Tooling));
        QCOMPARE(w.process->diagram->highlightedField(), QString("cutoffWidth"));
        auto parting = w.process->findChild<QWidget*>("partingParameters");
        QVERIFY(parting && parting->isVisible());
        screenshot(w, "cutoff-width-adjustment");
        w.process->setupChoice->setCurrentIndex(1);
        QVERIFY(!parting->isVisible());
        QVERIFY(w.process->diagram->highlightedField() != "cutoffWidth");
    }
    void invalidGeometryViews()
    {
        pf::ui::Window w;
        QVERIFY(showActive(w));
        QTRY_VERIFY(w.isReady());
        w.part->views->setCurrentIndex(1);
        QTest::qWait(200);
        QVERIFY(w.part->model->isValid());
        w.part->views->setCurrentIndex(0);
        w.editValue("length", "110");
        QTRY_VERIFY(w.isReady());
        w.editValue("flangeWidth", "116");
        QVERIFY(!w.isReady());
        w.part->views->setCurrentIndex(1);
        QTest::qWait(100);
        const auto lastValid = w.part->model->grabFramebuffer();
        w.part->views->setCurrentIndex(0);
        w.editValue("flangeWidth", "12");
        QTRY_VERIFY(w.isReady());
        w.part->views->setCurrentIndex(1);
        QTest::qWait(100);
        QCOMPARE(w.part->model->grabFramebuffer(), lastValid);
        w.selectPage(1);
        const auto diagram = w.process->diagram->grab().toImage();
        w.editValue("boreDiameter", "110");
        QCOMPARE(w.process->diagram->grab().toImage(), diagram);
    }
    void interruptedDrag()
    {
        auto w = std::make_unique<pf::ui::Window>();
        QVERIFY(showActive(*w));
        QTRY_VERIFY(w->isReady());
        auto canvas = w->part->canvas;
        const auto start = canvas->edgePoint("flangeWidth").toPoint();
        const auto origin = canvas->projectPoint(0, 0);
        QTest::mousePress(canvas, Qt::LeftButton, Qt::NoModifier, start);
        QTest::mouseMove(canvas, start - QPoint(20, 0));
        QVERIFY(canvas->isDragging());
        const auto accepted = w->parameters();
        QWheelEvent wheel(start, canvas->mapToGlobal(start), {}, QPoint(0, 120), Qt::LeftButton, Qt::NoModifier,
                          Qt::NoScrollPhase, false);
        QApplication::sendEvent(canvas, &wheel);
        QCOMPARE(canvas->projectPoint(0, 0), origin);
        QEvent deactivate(QEvent::WindowDeactivate);
        QApplication::sendEvent(canvas, &deactivate);
        QVERIFY(!canvas->isDragging());
        QTest::mouseMove(canvas, start - QPoint(40, 0));
        QCOMPARE(w->parameters(), accepted);
        QTest::mouseRelease(canvas, Qt::LeftButton, Qt::NoModifier, start - QPoint(40, 0));
        QTRY_VERIFY(w->isReady());
        // Destruction while a grab is active must not re-enter a destroyed future watcher.
        QTest::mousePress(canvas, Qt::LeftButton, Qt::NoModifier, canvas->edgePoint("flangeWidth").toPoint());
        QTest::mouseMove(canvas, start - QPoint(50, 0));
        w.reset();
        QWidget receiver;
        QTest::mouseRelease(&receiver, Qt::LeftButton);
    }
    void sectionLabelsKeepEdgesVisible()
    {
        for (const auto size : {QSize(500, 350), QSize(700, 500)})
            for (const double diameter : {124., 200.})
            {
                auto parameters = pf::Parameters();
                parameters.set("autoStock", true);
                parameters.set("flangeDiameter", diameter);
                parameters = pf::resolveStock(pf::resolveTolerances(parameters));
                QVERIFY(pf::validate(parameters).isEmpty());
                pf::ui::PartCanvas canvas;
                canvas.resize(size);
                canvas.setParameters(parameters);
                canvas.setShowTolerances(true);
                const auto image = canvas.grab();
                QPainterPath edges;
                const auto contour = pf::axisContour(parameters);
                for (const int side : {1, -1})
                {
                    edges.moveTo(canvas.projectPoint(contour[0].x(), side * contour[0].y()));
                    for (qsizetype i = 1; i < contour.size(); ++i)
                        edges.lineTo(canvas.projectPoint(contour[i].x(), side * contour[i].y()));
                    edges.closeSubpath();
                }
                for (const auto& edge : pf::sectionProjectionEdges(parameters))
                {
                    edges.moveTo(canvas.projectPoint(edge.x1(), edge.y1()));
                    edges.lineTo(canvas.projectPoint(edge.x2(), edge.y2()));
                }
                QPainterPathStroker stroker;
                stroker.setWidth(2);
                const auto visibleEdges = stroker.createStroke(edges);
                for (const auto& d : pf::dimensions())
                {
                    const auto label = canvas.dimensionBounds(d.key);
                    QVERIFY2(!visibleEdges.intersects(label), qPrintable(d.key));
                    QVERIFY2(QRectF(canvas.rect()).contains(label), qPrintable(d.key));
                }
                if (!output.isEmpty())
                    QVERIFY(image.save(
                        QDir(output).filePath(QString("16-section-%1-%2.png").arg(size.width()).arg(diameter))));
            }
    }
    void sectionProjection()
    {
        pf::ui::Window w;
        QVERIFY(showActive(w));
        QTRY_VERIFY(w.isReady());
        auto canvas = w.part->canvas;
        const auto image = canvas->grab().toImage();
        if (!output.isEmpty())
            image.save(QDir(output).filePath("11-section-projection-canvas.png"));
        const auto color = pf::ui::ink();
        const auto hasEdge = [&](double z, double radius)
        {
            const auto point = canvas->projectPoint(z, radius) * image.devicePixelRatio();
            for (int y = qRound(point.y()) - 2; y <= qRound(point.y()) + 2; ++y)
                for (int x = qRound(point.x()) - 2; x <= qRound(point.x()) + 2; ++x)
                {
                    const auto pixel = image.pixelColor(x, y);
                    // Thin edges can share coverage across two pixels at fractional DPI.
                    if (std::abs(pixel.lightness() - pf::ui::paper().lightness()) >
                        .6 * std::abs(color.lightness() - pf::ui::paper().lightness()))
                        return true;
                }
            return false;
        };
        // Independent CAD half-body projection: both faces and the bore shoulder remain visible in the hole.
        for (const double z : {-116., -28., 0.})
            for (const double radius : {-10., 10.})
                QVERIFY2(hasEdge(z, radius), qPrintable(QString("Missing section edge Z%1 R%2").arg(z).arg(radius)));
        QVERIFY(!hasEdge(-70, 10));
        screenshot(w, "11-section-projection");
    }
    void draftRecovery()
    {
        pf::ui::Window w;
        QVERIFY(showActive(w));
        QTRY_VERIFY(w.isReady());
        w.part->openDimension("boreDiameter");
        auto upper = w.part->dimensionPane->upper;
        upper->setFocus();
        upper->selectAll();
        QTest::keyClicks(upper, "-");
        QVERIFY(!w.isReady());
        w.part->closeDimension();
        w.editValue("gripLength", "30");
        w.part->openDimension("boreDiameter");
        QCOMPARE(w.part->dimensionPane->upper->text(), QString("-"));
        QVERIFY(!w.isReady());
        upper = w.part->dimensionPane->upper;
        upper->setFocus();
        upper->selectAll();
        QTest::keyClicks(upper, "0.5");
        QTRY_VERIFY(w.isReady());
        w.selectPage(1);

        w.editValue("stockDiameter", "-");
        QVERIFY(!w.isReady());
        QTest::mouseClick(w.process->resetStock, Qt::LeftButton);
        QTRY_VERIFY(w.isReady());
        auto stock = w.findChild<pf::ui::NumericEdit*>("parameter_stockDiameter");
        QVERIFY(stock->isEnabled());
        QVERIFY(stock->text() != "-");
    }
    void hollowSecondSetup()
    {
        const auto g = pf::generate(pf::Parameters());
        QVERIFY(g.ok());
        for (bool positiveOnly : {false, true})
        {
            QImage image(900, 650, QImage::Format_ARGB32);
            image.fill(pf::ui::paper());
            QPainter painter(&image);
            const auto mapping = pf::ui::drawSetup(painter, image.rect(), g.project->parameters,
                                                   g.project->programs[1].setup, {}, positiveOnly);
            painter.end();
            QCOMPARE(image.pixelColor(mapping.map(1, 10).toPoint()), pf::ui::paper());
            QCOMPARE(image.pixelColor(mapping.map(1, 32).toPoint()), pf::ui::selection());
        }
    }
    void machiningIndicators()
    {
        for (const auto& controller : {"axioma", "sinumerik840dsl"})
        {
            pf::Parameters parameters;
            parameters.set("controller", controller);
            const auto g = pf::generate(parameters);
            QVERIFY(g.ok());
            for (const auto& program : g.project->programs)
                for (const auto& feed : {"feedRough", "feedFinish"})
                {
                    QImage image(900, 650, QImage::Format_ARGB32);
                    image.fill(pf::ui::paper());
                    QPainter painter(&image);
                    const auto mapping =
                        pf::ui::drawSetup(painter, image.rect(), g.project->parameters, program.setup, feed);
                    painter.end();
                    const auto direction = mapping.parameterIndicator.p2() - mapping.parameterIndicator.p1();
                    bool matched = false;
                    const auto& moves = program.verification.moves;
                    for (qsizetype i = 1; i < moves.size(); ++i)
                    {
                        if (moves[i].mode != "G1" || moves[i].feed != parameters.n(feed))
                            continue;
                        const auto cut =
                            mapping.map(moves[i].z, moves[i].x / 2) - mapping.map(moves[i - 1].z, moves[i - 1].x / 2);
                        QVERIFY(QPointF::dotProduct(direction, cut) > 0);
                        QVERIFY(std::abs(direction.x() * cut.y() - direction.y() * cut.x()) < 1e-6);
                        matched = true;
                        break;
                    }
                    QVERIFY(matched);
                }
            QImage image(900, 650, QImage::Format_ARGB32);
            image.fill(pf::ui::paper());
            QPainter painter(&image);
            const auto mapping = pf::ui::drawSetup(painter, image.rect(), g.project->parameters,
                                                   g.project->programs[0].setup, "depthInside");
            painter.end();
            const auto finishedBore = mapping.map(0, g.project->calculations.targetBoreDiameter / 2).y();
            // The highlighted layer is the first actual pass of counterbore roughing.
            QCOMPARE(mapping.parameterIndicator.p1().y(), finishedBore);
            const auto& rough = g.project->programs[0].transitions[6];
            QCOMPARE(mapping.parameterIndicator.p2().y(), mapping.map(0, rough.passes.first() / 2).y());
            const auto& c = g.project->calculations;
            const auto shoulder =
                mapping.map(-c.targetCounterboreDepth, (c.targetBoreDiameter + c.targetCounterboreDiameter) / 4)
                    .toPoint();
            bool visible = false;
            for (int y = shoulder.y() - 2; y <= shoulder.y() + 2; ++y)
                for (int x = shoulder.x() - 2; x <= shoulder.x() + 2; ++x)
                {
                    const auto pixel = image.pixelColor(x, y), edge = pf::ui::ink();
                    visible |= std::abs(pixel.red() - edge.red()) + std::abs(pixel.green() - edge.green()) +
                                   std::abs(pixel.blue() - edge.blue()) <
                               80;
                }
            QVERIFY2(visible, "The depth label must leave the counterbore shoulder visible.");
        }
    }
    void fileMenuArrowNavigation()
    {
        pf::ui::Window w;
        QVERIFY(showActive(w));
        QTRY_VERIFY(w.isReady());
        auto file = w.findChild<QToolButton*>("fileMenuButton");
        auto menu = w.findChild<QMenu*>("fileMenu");
        auto theme = w.findChild<QMenu*>("themeMenu");
        QVERIFY(file && menu && theme);
        const auto first = menu->actions().first();
        const auto last = menu->actions().last();
        w.selectPage(1);
        w.pageButtons[1]->setFocus();
        QTest::keyClick(w.pageButtons[1], Qt::Key_Left);
        QTest::keyClick(w.pageButtons[0], Qt::Key_Left);
        QCOMPARE(QApplication::focusWidget(), file);
        for (int i = 0; i < 4; ++i)
        {
            QTest::keyClick(w.windowHandle(), Qt::Key_Left);
            QCOMPARE(QApplication::focusWidget(), file);
            QVERIFY(!menu->isVisible());
        }
        QTest::keyClick(file, Qt::Key_Down);
        QTRY_VERIFY(menu->isVisible());
        QCOMPARE(menu->activeAction(), first);
        QTest::keyClick(menu, Qt::Key_Down);
        QCOMPARE(menu->activeAction(), menu->actions()[1]);
        QTest::keyClick(menu, Qt::Key_Left);
        QVERIFY(!menu->isVisible());
        QCOMPARE(QApplication::focusWidget(), file);
        QTest::keyClick(w.windowHandle(), Qt::Key_Left);
        QCOMPARE(QApplication::focusWidget(), file);
        QTest::keyClick(file, Qt::Key_Up);
        QTRY_VERIFY(menu->isVisible());
        QCOMPARE(menu->activeAction(), last);
        QTest::keyClick(menu, Qt::Key_Up);
        QCOMPARE(menu->activeAction(), theme->menuAction());
        QTest::keyClick(menu, Qt::Key_Right);
        QTRY_VERIFY(theme->isVisible());
        QVERIFY(menu->isVisible());
        QVERIFY(theme->activeAction()->isChecked());
        QVERIFY(menu->screen()->availableGeometry().contains(menu->frameGeometry()));
        QVERIFY(theme->screen()->availableGeometry().contains(theme->frameGeometry()));
        if (!output.isEmpty())
        {
            QVERIFY(menu->grab().save(QDir(output).filePath("file-menu-before.png")));
            QVERIFY(theme->grab().save(QDir(output).filePath("theme-menu-before.png")));
        }
        const auto original = theme->activeAction();
        QSignalSpy activated(original, &QAction::triggered);
        QTest::keyClick(theme, Qt::Key_Down);
        QVERIFY(theme->activeAction() != original);
        QTest::keyClick(theme, Qt::Key_Left);
        QVERIFY(!theme->isVisible());
        QVERIFY(menu->isVisible());
        QVERIFY(original->isChecked());
        QCOMPARE(activated.count(), 0);
        QCOMPARE(menu->activeAction(), theme->menuAction());
        QTest::keyClick(menu, Qt::Key_Right);
        QTRY_VERIFY(theme->isVisible());
        QCOMPARE(theme->activeAction(), original);
        QTest::keyClick(theme, Qt::Key_Down);
        auto selected = theme->activeAction();
        QSignalSpy chosen(selected, &QAction::triggered);
        QTest::keyClick(theme, Qt::Key_Right);
        QVERIFY(!theme->isVisible());
        QVERIFY(!menu->isVisible());
        QVERIFY(selected->isChecked());
        QCOMPARE(chosen.count(), 1);
        QCOMPARE(QApplication::focusWidget(), file);
        QTest::keyClick(w.windowHandle(), Qt::Key_Left);
        QCOMPARE(QApplication::focusWidget(), file);
        screenshot(w, "file-theme-arrow-chosen");
        QTest::keyClick(file, Qt::Key_Down);
        menu->setActiveAction(theme->menuAction());
        QTest::keyClick(menu, Qt::Key_Return);
        QTRY_VERIFY(theme->isVisible());
        if (!output.isEmpty())
        {
            QVERIFY(menu->grab().save(QDir(output).filePath("file-menu-after.png")));
            QVERIFY(theme->grab().save(QDir(output).filePath("theme-menu-after.png")));
        }
        QTest::keyClick(theme, Qt::Key_Escape);
        QVERIFY(!theme->isVisible());
        QVERIFY(menu->isVisible());
        QTest::keyClick(menu, Qt::Key_Escape);
        QVERIFY(!menu->isVisible());
        QCOMPARE(QApplication::focusWidget(), file);
        QTest::keyClick(file, Qt::Key_Right);
        QCOMPARE(QApplication::focusWidget(), w.pageButtons[0]);
        QTest::keyClick(file, Qt::Key_Down);
        menu->setActiveAction(theme->menuAction());
        QTest::keyClick(menu, Qt::Key_Right);
        QTRY_VERIFY(theme->isVisible());
        QTest::keyClick(theme, Qt::Key_Return);
        QVERIFY(!menu->isVisible());
        QCOMPARE(QApplication::focusWidget(), file);
        first->setEnabled(false);
        QTest::keyClick(file, Qt::Key_Down);
        QCOMPARE(menu->activeAction(), menu->actions()[1]);
        QTest::keyClick(menu, Qt::Key_Up);
        QCOMPARE(menu->activeAction(), last);
        QTest::keyClick(menu, Qt::Key_Left);
        first->setEnabled(true);
        QSignalSpy opened(menu, &QMenu::aboutToShow);
        connect(
            menu, &QMenu::aboutToShow, menu, [menu] { QTimer::singleShot(0, menu, &QMenu::close); },
            Qt::SingleShotConnection);
        QTest::mouseClick(file, Qt::LeftButton);
        QTRY_VERIFY(!menu->isVisible());
        QCOMPARE(opened.count(), 1);
        pf::ui::applyTheme("light");
    }
    void simulationBlankAndToolAssignments()
    {
        pf::ui::Window w;
        QVERIFY(showActive(w));
        w.setController("sinumerik840dsl");
        QTRY_VERIFY(w.isReady());
        w.selectPage(1);
        auto blank = w.findChild<QCheckBox*>("simulationBlank");
        QVERIFY(blank && blank->isVisible() && blank->isChecked());
        QVERIFY(w.currentProject()->programs[0].text.contains("WORKPIECE"));
        QTest::mouseClick(blank, Qt::LeftButton);
        QTRY_VERIFY(w.isReady());
        QVERIFY(!w.parameters().b("simulationBlank"));
        QVERIFY(!w.currentProject()->programs[0].text.contains("WORKPIECE"));
        QTemporaryDir kit;
        QVERIFY(w.exportTo(kit.filePath("without-blank")));
        w.process->focusField("frontFacingAllowance");
        QTest::keyClick(w.windowHandle(), Qt::Key_Down);
        QCOMPARE(QApplication::focusWidget(), blank);
        QTest::keyClick(blank, Qt::Key_Space);
        QTest::keyClick(blank, Qt::Key_Down);
        QCOMPARE(QApplication::focusWidget(), w.findChild<pf::ui::NumericEdit*>("parameter_stockDiameter"));
        QTRY_VERIFY(w.isReady());
        QVERIFY(w.parameters().b("simulationBlank"));
        QVERIFY(w.currentProject()->programs[0].text.contains("WORKPIECE"));
        w.process->sections->setCurrentRow(pf::ui::ProcessView::Tooling);
        auto outer = w.findChild<pf::ui::ChoiceList*>("outerTools");
        QVERIFY(outer);
        chooseFromList(outer, outer->findData("custom"));
        w.editValue("customOuterName", "My turning tool");
        w.editValue("outerToolNumber", "7");
        w.editValue("outerCorrector", "2");
        w.editValue("boringToolNumber", "9");
        w.editValue("boringCorrector", "3");
        auto body = w.findChild<pf::ui::ChoiceList*>("bodyTools");
        QVERIFY(body);
        auto outerCard = w.findChild<QWidget*>("outerToolCard");
        QVERIFY(outerCard && outerCard->isAncestorOf(outer) && outerCard->isAncestorOf(body));
        chooseFromList(body, body->findData("custom"));
        w.editValue("customBodyName", "My reverse tool");
        w.editValue("bodyToolNumber", "12");
        w.editValue("bodyCorrector", "4");
        QTRY_VERIFY(w.isReady());
        QVERIFY(w.currentProject()->programs[0].text.contains("T7 D2"));
        QVERIFY(w.currentProject()->programs[0].text.contains("T9 D3"));
        QVERIFY(w.currentProject()->programs[0].text.contains("T12 D4"));
        for (const auto theme : {"light", "dark"})
        {
            pf::ui::applyTheme(theme);
            w.process->focusField("outerToolNumber");
            QCOMPARE(w.process->diagram->highlightedField(), QString("outerTooling"));
            screenshot(w, QString("tools-outer-") + theme);
            w.process->focusField("bodyToolNumber");
            QCOMPARE(w.process->diagram->highlightedField(), QString("bodyTooling"));
            screenshot(w, QString("tools-body-") + theme);
            w.process->focusField("boringToolNumber");
            QCOMPARE(w.process->diagram->highlightedField(), QString("boringTooling"));
            screenshot(w, QString("tools-boring-") + theme);
            for (const auto key : {"depthOutside", "depthInside"})
            {
                w.process->focusField(key);
                screenshot(w, QString("passes-") + key + "-" + theme);
            }
        }
        w.process->setupChoice->setCurrentIndex(1);
        w.process->focusField("outerToolNumber");
        QCOMPARE(w.process->setupChoice->currentIndex(), 1);
        QVERIFY(w.findChild<pf::ui::NumericEdit*>("parameter_outerToolNumber")->isVisible());
        QVERIFY(!w.findChild<pf::ui::NumericEdit*>("parameter_bodyToolNumber")->isVisible());
        QVERIFY(!blank->isVisible());
        w.selectPage(0);
        w.pageButtons[0]->setFocus();
        QTest::keyClick(w.windowHandle(), Qt::Key_Left);
        auto file = w.findChild<QToolButton*>("fileMenuButton");
        QCOMPARE(QApplication::focusWidget(), file);
        QTest::keyClick(file, Qt::Key_Right);
        QCOMPARE(QApplication::focusWidget(), w.pageButtons[0]);
        QVERIFY(w.saveProject(kit.filePath("tools.json")));
        QVERIFY(w.openProject(kit.filePath("tools.json")));
        QTRY_VERIFY(w.isReady());
        QCOMPARE(w.parameters().n("outerToolNumber"), 7.);
        QCOMPARE(w.parameters().s("customOuterName"), QString("My turning tool"));
        QCOMPARE(w.parameters().n("bodyToolNumber"), 12.);
        QCOMPARE(w.parameters().s("customBodyName"), QString("My reverse tool"));
        pf::ui::applyTheme("light");
    }
    void processDrawings()
    {
        pf::ui::Window w;
        QVERIFY(showActive(w));
        QTRY_VERIFY(w.isReady());
        w.selectPage(1);
        auto process = w.process;
        process->sections->setCurrentRow(pf::ui::ProcessView::Tooling);
        QCOMPARE(process->diagram->highlightedField(), QString("tooling"));
        process->sections->setCurrentRow(0);
        QVERIFY(process->diagram->highlightedField() != "tooling");
        const QVector<QStringList> fields = {
            {"stockDiameter", "stockBore", "blankLength", "blankOverhang", "trimAllowance", "frontFacingAllowance"},
            {"cutoffWidth"},
            {"depthOutside", "depthInside", "finishAllowance", "cuttingSpeed", "feedRough", "feedFinish", "rpmMax",
             "clearanceX", "clearanceZ"},
            {}};
        for (int section = 0; section < process->sections->count(); ++section)
        {
            process->sections->setCurrentRow(section);
            const int setups = 2;
            for (int setup = 0; setup < setups; ++setup)
            {
                process->setupChoice->setCurrentIndex(setup);
                const auto prefix = QString("process-%1-%2").arg(section).arg(setup);
                if (fields[section].isEmpty() || (section == pf::ui::ProcessView::Preparation && setup == 1))
                    screenshot(w, prefix);
                for (const auto& key : fields[section])
                {
                    auto row = w.findChild<pf::ui::ParameterRow*>("row_" + key);
                    if (!row->isVisible())
                        continue;
                    row->setFocus();
                    QCOMPARE(process->diagram->highlightedField(), key);
                    screenshot(w, prefix + "-" + key);
                }
                for (auto area : process->findChildren<QScrollArea*>())
                    if (area->isVisible())
                        QVERIFY2(area->horizontalScrollBar()->maximum() == 0,
                                 qPrintable(QString("Horizontal overflow section %1: %2 > %3")
                                                .arg(section)
                                                .arg(area->widget()->minimumSizeHint().width())
                                                .arg(area->viewport()->width())));
            }
        }
    }
};
QTEST_MAIN(UiTests)
#include "ui_tests.moc"
