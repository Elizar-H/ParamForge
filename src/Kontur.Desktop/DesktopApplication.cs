namespace Kontur.Desktop;

/// <summary>Uses the same WinForms and DPI initialization in the application and its UI checks.</summary>
internal static class DesktopApplication
{
    internal static void Initialize() => ApplicationConfiguration.Initialize();
}
