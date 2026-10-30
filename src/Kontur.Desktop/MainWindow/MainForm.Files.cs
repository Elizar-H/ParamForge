using Kontur.Core;

namespace Kontur.Desktop;

internal sealed partial class MainForm
{
    private void OpenParameters()
    {
        using var dialog = new OpenFileDialog
        {
            Title = "Открыть параметры",
            Filter = "Параметры JSON (*.json)|*.json",
            CheckFileExists = true
        };
        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        try
        {
            ImportFromPath(dialog.FileName);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            FileError(ex.Message);
        }
    }

    internal void ImportFromPath(string path)
    {
        var imported = ProjectFiles.ImportParameters(path);
        var result = Engine.Generate(imported);
        if (!result.Ok || result.Project is null)
        {
            throw new InvalidDataException(string.Join(Environment.NewLine, result.Errors.Select(e => e.Message)));
        }

        SetParameters(imported);
        project = result.Project;
        stale = false;
    }

    private void SaveParameters()
    {
        if (!GenerateNow())
        {
            return;
        }

        using var dialog = new SaveFileDialog
        {
            Title = "Сохранить параметры",
            Filter = "Параметры JSON (*.json)|*.json",
            FileName = "Параметры втулки.json",
            DefaultExt = "json",
            AddExtension = true,
            OverwritePrompt = true
        };
        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        try
        {
            SaveToPath(dialog.FileName);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            FileError(ex.Message);
        }
    }

    internal void SaveToPath(string path)
    {
        if (stale || project is null)
        {
            throw new InvalidOperationException("Параметры требуют проверки.");
        }

        ProjectFiles.SaveParameters(project, path);
    }

    private void ExportDialog()
    {
        if (!GenerateNow())
        {
            return;
        }

        using var picker = new FolderBrowserDialog
        {
            Description = "Куда сохранить документы?",
            UseDescriptionForTitle = true,
            ShowNewFolderButton = true
        };
        if (picker.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        try
        {
            var files = ExportToNewSubfolder(picker.SelectedPath);
            status.ForeColor = Theme.Muted;
            status.Height = TextRenderer.MeasureText("0", status.Font).Height * 3 + 8;
            status.Text = "Готово: 3 файла.";
            using var result = new ExportResultDialog(files);
            result.ShowDialog(this);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            FileError(ex.Message);
        }
    }

    internal IReadOnlyList<string> ExportToNewSubfolder(string parentFolder)
    {
        if (stale || project is null)
        {
            throw new InvalidOperationException("Параметры изменены; сначала выполните проверку.");
        }

        var folder = Path.Combine(parentFolder, "Документы втулки");
        for (var suffix = 2; Directory.Exists(folder) || File.Exists(folder); suffix++)
        {
            folder = Path.Combine(parentFolder, "Документы втулки (" + suffix + ")");
        }

        return ProjectFiles.Export(project, folder);
    }

    private void FileError(string message) => MessageBox.Show(this, message, "Не удалось сохранить или открыть файл", MessageBoxButtons.OK, MessageBoxIcon.Error);
}
