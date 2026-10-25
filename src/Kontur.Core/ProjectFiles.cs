using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace Kontur.Core;

/// <summary>Отдельное сохранение параметров и выпуск трёх согласованных документов.</summary>
public static class ProjectFiles
{
    public const int MaximumImportBytes = 1_000_000;
    internal static JsonSerializerOptions JsonOptions { get; } = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = true,
        PropertyNameCaseInsensitive = false
    };
    private static readonly UTF8Encoding Utf8 = new(false, true);
    private static readonly HashSet<string> ParameterKeys = ["partName", "material", .. Engine.Fields.Select(f => f.Key)];
    private static readonly HashSet<string> ProjectKeys = ["schema", "engineVersion", "id", "version", "params", "geometry", "calculations", "operations", "transitions", "moves", "nc", "routeHtml", "technologyHtml", "warnings", "verification"];

    public static Parameters ImportParameters(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length > MaximumImportBytes) throw new InvalidDataException("JSON больше 1 МБ не принимается.");
        var bytes = new byte[MaximumImportBytes + 1];
        var total = 0;
        while (total < bytes.Length)
        {
            var read = stream.Read(bytes, total, bytes.Length - total);
            if (read == 0) break;
            total += read;
        }
        if (total > MaximumImportBytes) throw new InvalidDataException("JSON больше 1 МБ не принимается.");
        var start = total >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF ? 3 : 0;
        try
        {
            var text = Utf8.GetString(bytes, start, total - start);
            using var document = JsonDocument.Parse(text, new JsonDocumentOptions { MaxDepth = 32 });
            var root = document.RootElement;
            EnsureObject(root, "Файл должен содержать объект параметров.");
            CheckDuplicates(root);
            if (root.TryGetProperty("project", out var project))
            {
                CheckKeys(root, ["project", "ok", "errors", "warnings"]);
                root = project;
                EnsureObject(root, "Некорректная структура project: требуется объект.");
                CheckDuplicates(root);
                if (!root.TryGetProperty("params", out _)) throw new InvalidDataException("В project отсутствует объект params.");
            }
            if (root.TryGetProperty("params", out var parameters))
            {
                CheckKeys(root, ProjectKeys);
                root = parameters;
            }
            EnsureObject(root, "Параметры должны быть объектом JSON.");
            CheckDuplicates(root);
            CheckKeys(root, ParameterKeys);
            foreach (var property in root.EnumerateObject())
            {
                if (property.Name is "partName" or "material")
                { if (property.Value.ValueKind != JsonValueKind.String) throw new InvalidDataException("Параметр " + property.Name + " должен быть строкой."); }
                else if (property.Value.ValueKind != JsonValueKind.Number || !property.Value.TryGetDouble(out var number) || !double.IsFinite(number))
                    throw new InvalidDataException("Параметр " + property.Name + " должен быть конечным числом JSON.");
            }
            var p = root.Deserialize<Parameters>(JsonOptions) ?? throw new InvalidDataException("Не удалось прочитать параметры.");
            var errors = Engine.Validate(p);
            if (errors.Count != 0) throw new InvalidDataException(string.Join(Environment.NewLine, errors.Select(e => e.Field + ": " + e.Message)));
            return p;
        }
        catch (JsonException ex) { throw new InvalidDataException("Некорректный JSON: " + ex.Message, ex); }
        catch (DecoderFallbackException ex) { throw new InvalidDataException("Файл должен быть в кодировке UTF-8.", ex); }
    }

    private static void EnsureObject(JsonElement value, string message)
    { if (value.ValueKind != JsonValueKind.Object) throw new InvalidDataException(message); }
    private static void CheckDuplicates(JsonElement value)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in value.EnumerateObject())
            if (!names.Add(property.Name)) throw new InvalidDataException("Параметр или поле повторено: " + property.Name);
    }
    private static void CheckKeys(JsonElement value, HashSet<string> allowed)
    {
        foreach (var property in value.EnumerateObject())
            if (!allowed.Contains(property.Name)) throw new InvalidDataException("Неизвестный параметр или поле: " + property.Name);
    }

    private static object ParameterDocument(Project p) => new { schema = "kontur-project-v2", engineVersion = p.Version, id = p.Id, @params = p.Params };
    private static string Json(object value) => JsonSerializer.Serialize(value, JsonOptions) + "\n";

    public static void SaveParameters(Project project, string path)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (Engine.Validate(project.Params).Count != 0) throw new InvalidDataException("Нельзя сохранить некорректные параметры.");
        var target = Path.GetFullPath(path);
        var temporary = Path.Combine(Path.GetDirectoryName(target)!, ".paramforge-" + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            var bytes = Utf8.GetBytes(Json(ParameterDocument(project)));
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            {
                stream.Write(bytes);
                stream.Flush(flushToDisk: true);
            }
            // Same-directory rename/replacement commits only a complete file.
            // A failed replacement leaves the existing parameter file intact.
            if (File.Exists(target)) File.Replace(temporary, target, null);
            else File.Move(temporary, target);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    public static IReadOnlyList<string> Export(Project project, string folder)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentException.ThrowIfNullOrWhiteSpace(folder);
        var actual = NcVerifier.Verify(project.Nc, project.Params);
        if (!project.Verification.Ok || !actual.Ok) throw new InvalidDataException("Экспорт требует успешной независимой статической проверки УП.");
        var canonical = Engine.Generate(project.Params);
        if (!canonical.Ok || canonical.Project is null)
            throw new InvalidDataException("Экспорт требует корректных исходных параметров.");
        var projectJson = Json(project);
        if (projectJson != Json(canonical.Project))
            throw new InvalidDataException("Результаты изменены или устарели: выполните расчёт перед экспортом.");
        // Все данные готовятся до записи; Word-файлы состоят из редактируемых абзацев и таблиц.
        var outputs = new (string Name, byte[] Data)[]
        {
            ("Маршрутная карта.docx", NativeDocxCards.Route(project)),
            ("Технологическая карта.docx", NativeDocxCards.Technology(project)),
            ("Управляющая программа.nc", Utf8.GetBytes(project.Nc))
        };
        if (Directory.Exists(folder) && Directory.EnumerateFileSystemEntries(folder).Any())
            throw new IOException("Папка для нового комплекта должна быть пустой. Существующие файлы не изменены.");
        Directory.CreateDirectory(folder);
        var files = new List<string>();
        foreach (var output in outputs)
        {
            var path = Path.GetFullPath(Path.Combine(folder, output.Name));
            // The folder check is advisory: another instance may create a file afterwards.
            // CreateNew keeps the first complete export from being overwritten in that race.
            using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                stream.Write(output.Data);
            files.Add(path);
        }
        return files.AsReadOnly();
    }
}
