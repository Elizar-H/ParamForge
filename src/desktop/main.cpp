#include "window.h"
#include "theme.h"
#include <QApplication>
#include <QDir>
#include <QFile>
#include <QJsonDocument>
#include <QLibraryInfo>
#include <QLocale>
#include <QScreen>
#include <QSettings>
#include <QStyleFactory>
#include <QSurfaceFormat>
#include <QTimer>
#include <QTranslator>
#include <QWindow>
#include <functional>

int main(int argc, char** argv)
{
    QGuiApplication::setHighDpiScaleFactorRoundingPolicy(Qt::HighDpiScaleFactorRoundingPolicy::PassThrough);
    QSurfaceFormat format;
    format.setDepthBufferSize(24);
    format.setStencilBufferSize(8);
    QSurfaceFormat::setDefaultFormat(format);
    QApplication app(argc, argv);
    app.setOrganizationName("ParamForge");
    app.setApplicationName("ParamForge");
    app.setApplicationVersion(pf::Version);
    app.setStyle(QStyleFactory::create("Fusion"));
    QLocale::setDefault(QLocale(QLocale::Russian, QLocale::Russia));
    QTranslator russian;
    if (russian.load("qtbase_ru", QCoreApplication::applicationDirPath() + "/translations") ||
        russian.load("qtbase_ru", QLibraryInfo::path(QLibraryInfo::TranslationsPath)))
        app.installTranslator(&russian);
    const auto arguments = app.arguments();
    const auto check = arguments.indexOf("--check-startup");
    if (check >= 0 && check + 1 < arguments.size())
    {
        QSettings::setDefaultFormat(QSettings::IniFormat);
        QSettings::setPath(QSettings::IniFormat, QSettings::UserScope, QDir(arguments[check + 1]).filePath("settings"));
    }
    pf::ui::applyTheme(QSettings().value("appearance/theme", "system").toString());
    pf::ui::Window window;
    app.setWindowIcon(window.windowIcon());
    const auto monitor = arguments.indexOf("--monitor");
    if (monitor >= 0 && monitor + 1 < arguments.size())
    {
        window.winId();
        const int index = arguments[monitor + 1].toInt();
        const auto screens = QGuiApplication::screens();
        if (index >= 0 && index < screens.size())
        {
            window.windowHandle()->setScreen(screens[index]);
            const auto bounds = screens[index]->availableGeometry();
            window.setMinimumSize(std::min(940, bounds.width() - 30), std::min(600, bounds.height() - 40));
            window.setGeometry(QRect(bounds.topLeft() + QPoint(20, 20),
                                     QSize(std::min(1280, bounds.width() - 40), std::min(820, bounds.height() - 60))));
        }
    }
    window.show();
    if (check >= 0 && check + 1 < arguments.size())
    {
        const QDir folder(arguments[check + 1]);
        QDir().mkpath(folder.path());
        auto timer = new QTimer(&window);
        timer->setInterval(150);
        auto phase = std::make_shared<int>(0);
        auto ticks = std::make_shared<int>(0);
        QObject::connect(
            timer, &QTimer::timeout, &window,
            [&, folder, timer, phase, ticks]
            {
                if (++*ticks > 100)
                {
                    app.exit(1);
                    return;
                }
                if (!window.isReady())
                    return;
                try
                {
                    if (*phase < 3)
                    {
                        window.selectPage(*phase);
                        window.grab().save(folder.filePath("page-" + QString::number(*phase) + ".png"));
                        ++*phase;
                        return;
                    }
                    if (*phase == 3)
                    {
                        window.selectPage(0);
                        window.part->views->setCurrentIndex(1);
                        ++*phase;
                        return;
                    }
                    if (*phase == 4)
                    {
                        if (!window.part->model->isValid() || window.part->model->grabFramebuffer().isNull())
                        {
                            app.exit(1);
                            return;
                        }
                        window.grab().save(folder.filePath("3d.png"));
                        window.setController("sinumerik840dsl");
                        ++*phase;
                        return;
                    }
                    if (*phase == 5)
                    {
                        if (!window.saveProject(folder.filePath("project.json")) ||
                            !window.openProject(folder.filePath("project.json")))
                        {
                            app.exit(1);
                            return;
                        }
                        ++*phase;
                        return;
                    }
                    const bool ok = window.currentProject().has_value() && window.exportTo(folder.filePath("cards"));
                    QFile report(folder.filePath("startup.json"));
                    if (!report.open(QIODevice::WriteOnly))
                    {
                        app.exit(1);
                        return;
                    }
                    report.write(QJsonDocument(QJsonObject{{"ok", ok},
                                                           {"version", pf::Version},
                                                           {"dpi", window.screen()->logicalDotsPerInch()},
                                                           {"monitor", window.screen()->name()},
                                                           {"programs", 2}})
                                     .toJson());
                    timer->stop();
                    app.exit(ok ? 0 : 1);
                }
                catch (...)
                {
                    app.exit(1);
                }
            });
        timer->start();
    }
    return app.exec();
}
