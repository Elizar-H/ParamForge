namespace Kontur.Desktop;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        if (args.Length == 2 && args[0] == "--smoke-output")
        {
            UiSmoke.Run(Path.GetFullPath(args[1]));
            return;
        }
        Application.Run(new MainForm());
    }
}
