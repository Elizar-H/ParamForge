using System.Text;

namespace Kontur.Tests;

internal static partial class RegressionSuite
{
    private sealed class TempWorkspace : IDisposable
    {
        public string Root { get; } = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "Kontur.Tests-" + Guid.NewGuid().ToString("N")));
        public TempWorkspace() => Directory.CreateDirectory(Root);
        public string Write(string name, string content)
        {
            var path = Path.Combine(Root, name);
            File.WriteAllText(path, content, new UTF8Encoding(false));
            return path;
        }
        public void Dispose()
        {
            var temporaryRoot = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!Root.StartsWith(temporaryRoot, StringComparison.OrdinalIgnoreCase) || !Path.GetFileName(Root).StartsWith("Kontur.Tests-", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Refusing cleanup outside the test-owned temporary directory");
            }

            Directory.Delete(Root, true);
        }
    }

}
