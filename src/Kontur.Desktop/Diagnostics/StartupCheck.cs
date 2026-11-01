using System.Text.Json;
using Kontur.Core;

namespace Kontur.Desktop;

/// <summary>Opt-in release check. Uses the same startup path as a normal launch.</summary>
internal sealed class StartupCheck(string folder, int monitor)
{
    private static readonly JsonSerializerOptions ReportOptions = new() { WriteIndented = true };
    internal static StartupCheck? Parse(string[] args)
    {
        if (args.Length == 0)
        {
            return null;
        }
        if (args.Length != 4 || args[0] != "--check-startup" || args[2] != "--monitor" || !int.TryParse(args[3], out var index))
        {
            throw new ArgumentException("Usage: ParamForge --check-startup <new-folder> --monitor <index>");
        }

        var path = Path.GetFullPath(args[1]);
        if (Directory.Exists(path) && Directory.EnumerateFileSystemEntries(path).Any())
        {
            throw new IOException("Startup check requires an empty output folder.");
        }
        Directory.CreateDirectory(path);
        return new(path, index);
    }

    internal void Attach(MainForm form)
    {
        var screens = Screen.AllScreens;
        if (monitor < 0 || monitor >= screens.Length)
        {
            throw new ArgumentException("The requested startup monitor is not connected.");
        }
        var area = screens[monitor].WorkingArea;
        form.StartPosition = FormStartPosition.Manual;
        form.Location = new Point(area.Left + 30, area.Top + 30);
        form.Shown += (_, _) => form.BeginInvoke(() =>
        {
            try
            {
                if (!form.Visible || !form.Workspace.IsHandleCreated || !form.Sketch.IsHandleCreated || form.CurrentProject?.Verification.Ok != true)
                {
                    throw new InvalidOperationException("The main window did not finish initialization.");
                }
                form.CaptureForm(Path.Combine(folder, "geometry.png"));
                var startupDpi = form.DeviceDpi;
                form.OpenInspection();
                if (form.Inspection?.IsHandleCreated != true)
                {
                    throw new InvalidOperationException("The inspection tab did not open.");
                }
                form.SetController(CncProfiles.Sinumerik);
                form.CaptureForm(Path.Combine(folder, "inspection.png"));
                form.CaptureProcessingView(Path.Combine(folder, "processing.png"));
                var files = form.ExportToNewSubfolder(folder);
                var saved = Path.Combine(folder, "parameters.json");
                form.SaveToPath(saved);
                form.ImportFromPath(saved);
                var movedDpi = new List<int>();
                for (var index = 0; index < screens.Length; index++)
                {
                    var target = screens[index].WorkingArea;
                    form.Location = new Point(target.Left + 30, target.Top + 30);
                    form.CaptureForm(Path.Combine(folder, "moved-" + index + ".png"));
                    if (!form.IsHandleCreated || !form.Workspace.IsHandleCreated || form.CurrentProject?.Verification.Ok != true)
                    {
                        throw new InvalidOperationException("Window state was lost after moving to another monitor.");
                    }
                    movedDpi.Add(form.DeviceDpi);
                }
                Write(new
                {
                    ok = true,
                    version = Engine.Version,
                    monitor,
                    dpi = startupDpi,
                    movedDpi,
                    screens = screens.Length,
                    runtime = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
                    runtimeDirectory = System.Runtime.InteropServices.RuntimeEnvironment.GetRuntimeDirectory(),
                    files,
                    project = form.CurrentProject?.Id
                });
            }
            catch (Exception e)
            {
                Fail(e);
            }
            finally
            {
                form.Close();
            }
        });
    }

    internal void Fail(Exception exception)
    {
        Environment.ExitCode = 1;
        Write(new
        {
            ok = false,
            version = Engine.Version,
            monitor,
            error = exception.ToString()
        });
    }

    private void Write(object result) => File.WriteAllText(Path.Combine(folder, "startup.json"),
        JsonSerializer.Serialize(result, ReportOptions));
}
