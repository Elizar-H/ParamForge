#include "projects.h"
#include <QDir>
#include <QFile>
#include <QFileInfo>
#include <QJsonDocument>
#include <QLockFile>
#include <QSaveFile>
#include <nlohmann/json.hpp>
#include <set>
#include <stdexcept>
#include <vector>

namespace pf
{
namespace
{
[[noreturn]] void fail(const QString& message)
{
    throw std::runtime_error(message.toUtf8().constData());
}
void checkKeys(const QJsonObject& object, const QSet<QString>& allowed)
{
    for (auto i = object.begin(); i != object.end(); ++i)
        if (!allowed.contains(i.key()))
            fail("Неизвестное поле: " + i.key());
}
} // namespace
Parameters importParameters(const QString& path)
{
    QFile file(path);
    if (!file.open(QIODevice::ReadOnly))
        fail(file.errorString());
    if (file.size() > 1000000)
        fail("Файл параметров превышает 1 МБ.");
    const auto bytes = file.readAll();
    if (bytes.size() > 1000000)
        fail("Файл параметров превышает 1 МБ.");
    try
    {
        std::vector<std::set<std::string>> keys;
        const nlohmann::json::parser_callback_t callback =
            [&](int depth, nlohmann::json::parse_event_t event, nlohmann::json& value)
        {
            if (depth > 32)
                throw std::runtime_error("Слишком большая вложенность JSON.");
            if (event == nlohmann::json::parse_event_t::object_start)
                keys.emplace_back();
            if (event == nlohmann::json::parse_event_t::key && !keys.back().insert(value.get<std::string>()).second)
                throw std::runtime_error("Повторяющееся поле JSON.");
            if (event == nlohmann::json::parse_event_t::object_end)
                keys.pop_back();
            return true;
        };
        const auto validated = nlohmann::json::parse(bytes.begin(), bytes.end(), callback);
        if (!validated.is_object())
            fail("Файл должен содержать объект параметров.");
    }
    catch (const std::exception& e)
    {
        fail("Некорректный JSON UTF-8: " + QString::fromUtf8(e.what()));
    }
    auto root = QJsonDocument::fromJson(bytes).object();
    if (root.contains("project"))
    {
        checkKeys(root, {"project", "ok", "errors", "warnings"});
        if (!root["project"].isObject() || !root["project"].toObject()["params"].isObject())
            fail("В project требуется объект params.");
        root = root["project"].toObject();
    }
    if (root.contains("params"))
    {
        checkKeys(root, {"schema", "engineVersion", "id", "version", "ncFileName", "programName", "params", "geometry",
                         "calculations", "operations", "transitions", "moves", "nc", "routeHtml", "technologyHtml",
                         "warnings", "verification", "programs"});
        if (!root["params"].isObject())
            fail("Параметры должны быть объектом JSON.");
        root = root["params"].toObject();
    }
    const auto defaults = schema()["defaults"].toObject();
    for (auto i = root.begin(); i != root.end(); ++i)
    {
        if (!defaults.contains(i.key()))
            fail("Неизвестный параметр: " + i.key());
        const auto expected = defaults[i.key()];
        if (i->type() != expected.type())
            fail("Неверный тип параметра: " + i.key());
    }
    Parameters p(root);
    const auto errors = validate(p);
    if (!errors.isEmpty())
        fail(errors.first().message);
    return resolveStock(resolveTolerances(p));
}
void saveParameters(const Project& project, const QString& path)
{
    const auto errors = validate(project.parameters);
    if (!errors.isEmpty())
        fail(errors.first().message);
    QSaveFile file(path);
    file.setDirectWriteFallback(false);
    if (!file.open(QIODevice::WriteOnly))
        fail(file.errorString());
    const QJsonDocument document(QJsonObject{{"schema", "kontur-project-v3"},
                                             {"engineVersion", Version},
                                             {"id", project.id},
                                             {"params", project.parameters.json()}});
    const auto bytes = document.toJson(QJsonDocument::Indented);
    if (file.write(bytes) != bytes.size() || !file.commit())
        fail(file.errorString());
}
QStringList exportProject(const Project& project, const QString& folder)
{
    const auto canonical = generate(project.parameters);
    if (!canonical.ok() || projectJson(*canonical.project) != projectJson(project))
        fail("Результаты устарели: выполните расчёт перед выпуском.");
    QVector<std::pair<QString, QByteArray>> outputs = {{"Маршрутная карта.docx", routeCard(project)},
                                                       {"Технологическая карта.docx", technologyCard(project)}};
    for (const auto& p : project.programs)
    {
        if (!p.verification.ok || !verify(p.text, project.parameters, p.setup).ok)
            fail("УП не прошла статическую проверку.");
        outputs.append({p.fileName, p.text.toUtf8()});
    }
    QDir destination(QFileInfo(folder).absoluteFilePath());
    if (!destination.exists() && !QDir().mkpath(destination.path()))
        fail("Не удалось создать папку комплекта.");
    QLockFile lock(destination.filePath(".paramforge-export.lock"));
    if (!lock.tryLock(0))
        fail("В эту папку уже выполняется выпуск.");
    // Existing unrelated files are welcome. On a filename collision, create the next
    // complete set automatically, keeping original NC filenames and every earlier file.
    const auto parentFolder = destination;
    bool collision = false;
    for (const auto& output : outputs)
        collision |= destination.exists(output.first);
    bool createdFolder = false;
    if (collision)
    {
        int index = 2;
        QString name;
        do
        {
            name = "Комплект " + QString::number(index++);
        } while (parentFolder.exists(name));
        if (!destination.mkdir(name) || !destination.cd(name))
            fail("Не удалось создать папку комплекта.");
        createdFolder = true;
    }
    QStringList created;
    try
    {
        for (const auto& output : outputs)
        {
            const auto path = destination.filePath(output.first);
            QFile file(path);
            if (!file.open(QIODevice::WriteOnly | QIODevice::NewOnly))
                fail(file.errorString());
            created.append(path);
            if (file.write(output.second) != output.second.size() || !file.flush())
                fail(file.errorString());
        }
    }
    catch (...)
    {
        for (const auto& path : created)
            QFile::remove(path);
        if (createdFolder)
            parentFolder.rmdir(destination.dirName());
        throw;
    }
    return created;
}
} // namespace pf
