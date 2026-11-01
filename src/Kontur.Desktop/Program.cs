namespace Kontur.Desktop;

internal static class Program
{
    private static StartupCheck? startupCheck;
    private static bool reportingFailure;

    [STAThread]
    private static void Main(string[] args)
    {
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, args) => ReportFailure(args.Exception);
        try
        {
            startupCheck = StartupCheck.Parse(args);
            DesktopApplication.Initialize();
            using var form = new MainForm();
            startupCheck?.Attach(form);
            Application.Run(form);
        }
        catch (Exception exception)
        {
            ReportFailure(exception);
        }
    }
    private static void ReportFailure(Exception exception)
    {
        Environment.ExitCode = 1;
        if (reportingFailure)
        {
            return;
        }
        reportingFailure = true;
        var path = FailureLog.TryWrite(exception);
        try
        {
            if (startupCheck is not null)
            {
                startupCheck.Fail(exception);
            }
            else
            {
                MessageBox.Show("Не удалось продолжить работу ParamForge.\n" +
                    (path is null ? exception.Message : "Подробности сохранены:\n" + path),
                    "ParamForge", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
        finally
        {
            Application.Exit();
        }
    }
}
