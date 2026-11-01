using System.Runtime.InteropServices;

namespace Kontur.Desktop;

internal static class FailureLog
{
    internal static string? TryWrite(Exception exception)
    {
        foreach (var root in new[] { Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), Path.GetTempPath() })
        {
            try
            {
                var folder = Path.Combine(root, "ParamForge", "Logs");
                Directory.CreateDirectory(folder);
                var path = Path.Combine(folder, "error-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff", System.Globalization.CultureInfo.InvariantCulture) + ".txt");
                File.WriteAllText(path, $"ParamForge {Kontur.Core.Engine.Version}\n{RuntimeInformation.FrameworkDescription}\n{RuntimeInformation.OSDescription}\n\n{exception}");
                return path;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Security.SecurityException)
            {
                // Reporting a failure must not throw a second exception if logs cannot be written.
            }
        }
        return null;
    }
}
