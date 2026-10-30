namespace Kontur.Desktop.Tests;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length != 2 || args[0] != "--smoke-output")
        {
            Console.Error.WriteLine("Usage: Kontur.Desktop.Tests --smoke-output <new-output-directory>");
            return 2;
        }

        var folder = Path.GetFullPath(args[1]);
        if (Directory.Exists(folder) && Directory.EnumerateFileSystemEntries(folder).Any())
        {
            Console.Error.WriteLine("Use a new output directory so export checks do not reuse previous files.");
            return 2;
        }

        DesktopApplication.Initialize();
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);
        UiSmoke.Run(folder);
        return Environment.ExitCode;
    }
}
