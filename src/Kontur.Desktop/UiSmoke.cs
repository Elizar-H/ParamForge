using System.Text.Json;
using System.Reflection;
using System.Runtime.InteropServices;
using Kontur.Core;

namespace Kontur.Desktop;

internal static class UiSmoke
{
    internal static void Run(string folder)
    {
        Directory.CreateDirectory(folder);
        var checks = new List<object>();
        var failures = new List<string>();
        void Check(string name, bool passed, object? evidence = null)
        {
            checks.Add(new { name, passed, evidence });
            if (!passed) failures.Add(name);
        }
        try
        {
            using var form = new MainForm();
            form.Show();
            Application.DoEvents();
            Check("native_form", form.Visible && form.IsHandleCreated, new { type = form.GetType().BaseType?.FullName, actualDeviceDpi = form.DeviceDpi });
            Check("seventeen_main_numeric_inputs", form.MainNumericInputCount == 17, form.MainNumericInputCount);
            Check("default_generation", form.CurrentProject?.Verification.Ok == true && !form.IsStale);
            form.CaptureForm(Path.Combine(folder, "01-default-1280x850.png"));
            Check("default_labels_and_closed_walls", form.DrawingLabelsInside && !form.DrawingLabelsOverlap && form.RearWallsClosed && form.DrawingLabelsClearOfMaterial && form.DrawingBoreLeadersClearOfMaterial);

            form.SetInput("length", "132");
            Check("change_marks_stale", form.IsStale);
            var staleBlocked = false;
            try { form.ExportToNewSubfolder(Path.Combine(folder, "stale-must-not-export")); }
            catch (InvalidOperationException) { staleBlocked = true; }
            Check("stale_export_blocked", staleBlocked && !Directory.Exists(Path.Combine(folder, "stale-must-not-export")));
            Check("changed_length_recalculates", form.GenerateNow() && form.CurrentProject!.Params.Length == 132 && form.CurrentProject.Calculations.TargetLength == 131.5);
            form.SetInput("length", "ошибка");
            Check("invalid_input_rejected", !form.GenerateNow() && form.IsStale && form.StatusText == "Введите число.");
            form.CaptureForm(Path.Combine(folder, "02-invalid-input.png"));

            form.SetParameters(Engine.Defaults with { BodyDiameter = 76 });
            Check("thin_wall_geometry_rejected", !form.GenerateNow() && form.IsStale && form.StatusText == "Минимальная стенка — 3 мм.");
            form.CaptureForm(Path.Combine(folder, "02b-invalid-dimensions.png"));
            var geometryBlocked = false;
            var rejectedFolder = Path.Combine(folder, "invalid-geometry-must-not-export");
            try { form.ExportToNewSubfolder(rejectedFolder); }
            catch (InvalidOperationException) { geometryBlocked = true; }
            Check("invalid_geometry_export_blocked", geometryBlocked && !Directory.Exists(rejectedFolder));
            form.SetParameters(Engine.Defaults);
            form.SetInput("length", "-1");
            Check("negative_length_rejected", !form.GenerateNow() && form.IsStale && form.StatusText == "Длина L: 20…250 мм.");
            form.SetParameters(Engine.Defaults with { FlangeDiameter = 98, FlangeLowerDeviation = 3, FlangeUpperDeviation = 4 });
            Check("contradictory_nominal_drawing_rejected", !form.GenerateNow() && form.IsStale && form.StatusText == "Для чертежа нужно Db < D.");
            form.CaptureForm(Path.Combine(folder, "02c-invalid-nominal.png"));
            form.SetParameters(Engine.Defaults with { BoreLowerDeviation = 1, BoreUpperDeviation = 0 });
            Check("reversed_tolerance_has_clear_error", !form.GenerateNow() && form.IsStale && form.StatusText == "Верхнее отклонение ниже нижнего.");
            form.SetParameters(Engine.Defaults);
            form.SetInput("length", "116,0001");
            Check("excess_precision_has_clear_error", !form.GenerateNow() && form.IsStale && form.StatusText == "Не более 3 знаков после запятой.");

            var shortProfile = Engine.Defaults with { Length = 80, FlangeWidth = 8, CounterboreDepth = 10, BodyDiameter = 84, FlangeDiameter = 100, StockDiameter = 104 };
            var longProfile = Engine.Defaults with { Length = 160, FlangeWidth = 20, CounterboreDepth = 60, BodyDiameter = 110, FlangeDiameter = 140, StockDiameter = 144, BoreDiameter = 50, CounterboreDiameter = 80 };
            foreach (var (name, profile) in new[] { ("03-short", shortProfile), ("04-long", longProfile) })
            {
                form.SetParameters(profile);
                Check(name + "_valid", form.GenerateNow());
                form.CaptureForm(Path.Combine(folder, name + "-1280x850.png"));
                Check(name + "_drawing", form.DrawingLabelsInside && !form.DrawingLabelsOverlap && form.RearWallsClosed && form.DrawingLabelsClearOfMaterial && form.DrawingBoreLeadersClearOfMaterial);
            }
            var thinProfile = Engine.Defaults with { Length = 250, FlangeDiameter = 35, BodyDiameter = 30, FlangeWidth = 2, BoreDiameter = 20, CounterboreDiameter = 22, CounterboreDepth = 1, StockDiameter = 36, StockBore = 18 };
            var wideProfile = Engine.Defaults with { Length = 20, FlangeDiameter = 200, BodyDiameter = 180, FlangeWidth = 2, BoreDiameter = 130, CounterboreDiameter = 150, CounterboreDepth = 1, StockDiameter = 204, StockBore = 128 };
            foreach (var (name, profile) in new[] { ("09-extreme-thin", thinProfile), ("10-extreme-wide", wideProfile) })
            {
                form.SetParameters(profile);
                Check(name + "_valid", form.GenerateNow());
                form.CaptureForm(Path.Combine(folder, name + "-1280x850.png"));
                Check(name + "_drawing", form.DrawingLabelsInside && !form.DrawingLabelsOverlap && form.RearWallsClosed && form.DrawingLabelsClearOfMaterial && form.DrawingBoreLeadersClearOfMaterial);
                form.Size = new Size(1100, 720);
                form.CaptureForm(Path.Combine(folder, name + "-minimum.png"));
                Check(name + "_minimum_drawing", form.DrawingLabelsInside && !form.DrawingLabelsOverlap && form.RearWallsClosed && form.DrawingLabelsClearOfMaterial && form.DrawingBoreLeadersClearOfMaterial,
                    new { labelsInside = form.DrawingLabelsInside, labelsOverlap = form.DrawingLabelsOverlap, rearWallsClosed = form.RearWallsClosed, labelsClearOfMaterial = form.DrawingLabelsClearOfMaterial, boreLeadersClearOfMaterial = form.DrawingBoreLeadersClearOfMaterial });
                form.ClientSize = new Size(1280, 850);
            }
            foreach (var length in new[] { 80D, 116D, 160D })
                foreach (var depth in new[] { 10D, 28D, 60D })
                {
                    form.SetParameters(Engine.Defaults with { Length = length, CounterboreDepth = depth });
                    var valid = form.GenerateNow();
                    form.Refresh();
                    Application.DoEvents();
                    Check($"drawing_L{length}_h{depth}", valid && form.DrawingLabelsInside && !form.DrawingLabelsOverlap && form.RearWallsClosed && form.DrawingLabelsClearOfMaterial && form.DrawingBoreLeadersClearOfMaterial);
                }
            form.SetParameters(Engine.Defaults);
            form.GenerateNow();
            form.Size = new Size(1100, 720);
            form.CaptureForm(Path.Combine(folder, "05-minimum-1100x720.png"));
            Check("minimum_drawing", form.DrawingLabelsInside && !form.DrawingLabelsOverlap && form.RearWallsClosed && form.DrawingLabelsClearOfMaterial && form.DrawingBoreLeadersClearOfMaterial, new { form.Width, form.Height });
            form.ClientSize = new Size(1280, 850);

            var json = Path.Combine(folder, "parameters.json");
            form.SaveToPath(json);
            var invalidJson = Path.Combine(folder, "invalid-import.json");
            File.WriteAllText(invalidJson, "{\"length\":-1}");
            var invalidImport = false;
            try { form.ImportFromPath(invalidJson); }
            catch (InvalidDataException) { invalidImport = true; }
            Check("invalid_import_preserves_form", invalidImport && form.GetInput("length") == MainForm.F(Engine.Defaults.Length) && !form.IsStale);
            form.SetInput("length", "132");
            form.ImportFromPath(json);
            Check("valid_import", !form.IsStale && form.CurrentProject!.Params.Length == 116);

            var beforeCancel = JsonSerializer.Serialize(form.CurrentProject!);
            var beforeCancelFiles = FileSnapshot(folder);
            foreach (var action in new[] { "ExportDialog", "OpenParameters", "SaveParameters" })
            {
                var title = CancelOwnModal(form, () => InvokeAction(form, action));
                Check("cancel_" + action + "_preserves_project_and_files",
                    !form.IsStale && beforeCancel == JsonSerializer.Serialize(form.CurrentProject!) && beforeCancelFiles == FileSnapshot(folder), title);
            }
            var originalPartName = form.CurrentProject!.Params.PartName;
            var processingTitle = CancelOwnModal(form, () => InvokeAction(form, "OpenProcessing"), dialog =>
            {
                var partName = Descendants(dialog).OfType<TextBox>().First(box => box.Text == originalPartName);
                partName.Text = "Discarded QA edit";
            });
            Check("cancel_processing_discards_dialog_edits", !form.IsStale && beforeCancel == JsonSerializer.Serialize(form.CurrentProject!) && beforeCancelFiles == FileSnapshot(folder), processingTitle);

            var exportParent = Path.Combine(folder, "export-destination");
            Directory.CreateDirectory(exportParent);
            File.WriteAllText(Path.Combine(exportParent, "existing-user-file.txt"), "preserve this file");
            var previous = Path.Combine(exportParent, "Документы втулки");
            Directory.CreateDirectory(previous);
            File.WriteAllText(Path.Combine(previous, "previous-user-file.txt"), "preserve previous export");
            var files = form.ExportToNewSubfolder(exportParent);
            var expected = new[] { "Маршрутная карта.docx", "Технологическая карта.docx", "Управляющая программа.nc" };
            Check("exactly_three_deliverables", files.Count == 3 && files.Select(Path.GetFileName).Order().SequenceEqual(expected.Order()) && Directory.GetFiles(Path.GetDirectoryName(files[0])!).Length == 3, files);
            Check("previous_files_preserved", File.ReadAllText(Path.Combine(exportParent, "existing-user-file.txt")) == "preserve this file" && File.ReadAllText(Path.Combine(previous, "previous-user-file.txt")) == "preserve previous export" && Path.GetDirectoryName(files[0]) != previous);
            var firstExport = FileSnapshot(Path.GetDirectoryName(files[0])!);
            var fileAtNextName = Path.Combine(exportParent, "Документы втулки (3)");
            File.WriteAllText(fileAtNextName, "preserve occupied folder name");
            form.SetInput("length", "132");
            Check("repeat_export_recalculates_new_geometry", form.GenerateNow());
            var repeated = form.ExportToNewSubfolder(exportParent);
            Check("repeat_export_keeps_previous_bundle_and_skips_file_name",
                repeated.Count == 3 && Directory.GetFiles(Path.GetDirectoryName(repeated[0])!).Length == 3 &&
                Path.GetFileName(Path.GetDirectoryName(repeated[0])) == "Документы втулки (4)" &&
                firstExport == FileSnapshot(Path.GetDirectoryName(files[0])!) &&
                File.ReadAllText(fileAtNextName) == "preserve occupied folder name" &&
                File.ReadAllText(repeated[2]).Contains("L 131.5", StringComparison.Ordinal), repeated);
            form.SetParameters(Engine.Defaults);
            form.GenerateNow();
            form.CaptureProcessingDialog(Path.Combine(folder, "07-processing-dialog.png"));
            form.CaptureExportDialog(files, Path.Combine(folder, "08-export-result-dialog.png"));
        }
        catch (Exception ex) { failures.Add(ex.ToString()); }

        foreach (var targetDpi in new[] { 96, 120, 144 })
        {
            try
            {
                using var simulated = new MainForm();
                simulated.Show();
                Application.DoEvents();
                simulated.SimulateDpiText(targetDpi);
                simulated.CaptureForm(Path.Combine(folder, $"06-text-scale-simulation-{targetDpi}dpi.png"));
                Check("text_scale_simulation_" + targetDpi, simulated.DrawingLabelsInside && !simulated.DrawingLabelsOverlap && simulated.RearWallsClosed && simulated.DrawingLabelsClearOfMaterial && simulated.DrawingBoreLeadersClearOfMaterial,
                    new { simulatedDpi = targetDpi, actualWindowsDpi = simulated.DeviceDpi, scope = "application text scaling simulation; Windows DPI settings unchanged" });
                simulated.Close();
            }
            catch (Exception ex) { failures.Add("simulation " + targetDpi + ": " + ex); }
        }
        File.WriteAllText(Path.Combine(folder, "ui-smoke.json"), JsonSerializer.Serialize(new
        {
            ok = failures.Count == 0, framework = "Windows Forms", executedAtUtc = DateTime.UtcNow,
            dpiTesting = "Actual host DPI plus text/control scaling simulations. These simulations are not real Windows DPI-switch tests.",
            checks, failures
        }, new JsonSerializerOptions { WriteIndented = true }));
        Environment.ExitCode = failures.Count == 0 ? 0 : 1;
    }

    private static IEnumerable<Control> Descendants(Control root)
    {
        foreach (Control child in root.Controls)
        {
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }

    private static void InvokeAction(MainForm form, string name) =>
        typeof(MainForm).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(form, null);

    private static string FileSnapshot(string folder) => string.Join("\n", Directory.GetFiles(folder, "*", SearchOption.AllDirectories)
        .Order(StringComparer.Ordinal).Select(path => Path.GetRelativePath(folder, path) + ":" + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path)))));

    // Only this smoke process's owned popup is closed. No global keyboard input is sent.
    private static string CancelOwnModal(MainForm owner, Action show, Action<Form>? edit = null)
    {
        var cancelled = false;
        var title = "";
        using var watchdog = new System.Threading.Timer(_ => Environment.Exit(3), null, 10_000, Timeout.Infinite);
        using var timer = new System.Windows.Forms.Timer { Interval = 100 };
        timer.Tick += (_, _) =>
        {
            var popup = GetLastActivePopup(owner.Handle);
            if (popup == IntPtr.Zero || popup == owner.Handle) return;
            GetWindowThreadProcessId(popup, out var processId);
            if (processId != (uint)Environment.ProcessId) return;
            var dialog = Application.OpenForms.Cast<Form>().FirstOrDefault(form => form.Handle == popup);
            if (dialog is not null) edit?.Invoke(dialog);
            var text = new System.Text.StringBuilder(256);
            GetWindowText(popup, text, text.Capacity);
            title = text.ToString();
            cancelled = PostMessage(popup, 0x0010, IntPtr.Zero, IntPtr.Zero);
            if (cancelled) timer.Stop();
        };
        timer.Start();
        try { show(); }
        finally { timer.Stop(); }
        if (!cancelled) throw new InvalidOperationException("The owned modal dialog was not cancelled: " + title);
        return title;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetLastActivePopup(IntPtr owner);
    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetWindowTextW")]
    private static extern int GetWindowText(IntPtr window, System.Text.StringBuilder text, int capacity);
    [DllImport("user32.dll", EntryPoint = "PostMessageW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
}
