#pragma once
#include "inspection_view.h"
#include "part_view.h"
#include "process_view.h"
#include <QFrame>
#include <QFutureWatcher>
#include <QLabel>
#include <QMainWindow>
#include <QPushButton>
#include <QTableWidget>
#include <QTimer>

namespace pf::ui
{
class Window final : public QMainWindow
{
    Q_OBJECT
  public:
    explicit Window(QWidget* parent = nullptr);
    ~Window() override;
    const Parameters& parameters() const
    {
        return working;
    }
    const std::optional<Project>& currentProject() const
    {
        return project;
    }
    bool isReady() const
    {
        return project.has_value() && invalidDraft.isEmpty() && project->parameters == working;
    }
    void setParameters(const Parameters&, bool modified = true);
    void editValue(const QString&, const QString&);
    void selectPage(int);
    void setController(const QString&);
    bool saveProject(const QString&);
    bool openProject(const QString&);
    bool exportTo(const QString&);
    void showExportResult();
    PartView* part;
    ProcessView* process;
    InspectionView* inspection;
    QPushButton* exportButton;
    ChoiceList* controller;
    QVector<QToolButton*> pageButtons;

  protected:
    void closeEvent(QCloseEvent*) override;
    void resizeEvent(QResizeEvent*) override;
    bool eventFilter(QObject*, QEvent*) override;

  private:
    Parameters working;
    std::optional<Project> project;
    struct InvalidDraft
    {
        QString text, message;
    };
    QMap<QString, InvalidDraft> invalidDraft;
    QVector<Error> errors;
    QStackedWidget* pages;
    QPushButton* status = nullptr;
    QToolButton* statusToggle = nullptr;
    QFrame* statusDetails = nullptr;
    QLabel* statusDetailsTitle = nullptr;
    QTableWidget* statusDetailsTable = nullptr;
    QVector<Error> statusEntries;
    bool statusError = false;
    bool updatingStatusDetails = false;
    ResizeGrip* windowGrip = nullptr;
    QTimer generationTimer;
    QFutureWatcher<Generation> watcher;
    quint64 revision = 0, workerRevision = 0;
    bool loading = false, modified = false;
    bool interactiveDrag = false, modifiedBeforeDrag = false;
    QStringList exportedFiles;
    bool ensureReady();
    QString currentPath, themePreference;
    void refresh();
    void schedule();
    void startGeneration();
    void generationFinished();
    void showErrors(const QVector<Error>&);
    void setStatusMessage(const QString&, bool error = false, const QVector<Error>& entries = {});
    void updateStatusDetails();
    QVector<Error> currentErrors() const;
    void focusErrorField(const QString&);
    void editTolerance(const QString&, const QString&);
    void openDialog();
    void saveDialog(bool saveAs = false);
    void exportDialog();
    bool chooseExportDirectory();
    bool confirmSave();
};
} // namespace pf::ui
