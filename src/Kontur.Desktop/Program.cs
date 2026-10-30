namespace Kontur.Desktop;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        DesktopApplication.Initialize();
        Application.Run(new MainForm());
    }
}
