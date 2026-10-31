namespace Kontur.Desktop;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, args) => ReportFailure(args.Exception);
        try
        {
            DesktopApplication.Initialize();
            Application.Run(new MainForm());
        }
        catch (Exception exception)
        {
            ReportFailure(exception);
        }
    }
    private static void ReportFailure(Exception exception)
    {
        var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ParamForge", "Logs");
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, "error-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff") + ".txt");
        File.WriteAllText(path, exception.ToString());
        MessageBox.Show("Не удалось открыть ParamForge. Подробности сохранены:\n" + path, "ParamForge", MessageBoxButtons.OK, MessageBoxIcon.Error);
        Application.Exit();
    }
}
