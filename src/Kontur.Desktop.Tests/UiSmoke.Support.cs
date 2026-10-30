using System.Reflection;

using System.Runtime.InteropServices;

using Kontur.Core;

namespace Kontur.Desktop;

internal static partial class UiSmoke
{


    private static void CheckCaptions(MainForm form, string name, Action<string, bool, object?> check)
    {
        var labels = Descendants(form).OfType<Label>().Where(label => label.Name.StartsWith("dimension_caption_", StringComparison.Ordinal)).ToArray();
        var expected = new Dictionary<string, string>
        {
            ["length"] = "Длина",
            ["flangeDiameter"] = "Диаметр бурта",
            ["boreDiameter"] = "Диаметр отверстия",
            ["counterboreDiameter"] = "Диаметр расточки",
            ["counterboreDepth"] = "Глубина расточки",
            ["bodyDiameter"] = "Диаметр тела",
            ["flangeWidth"] = "Ширина бурта"
        };
        var evidence = labels.Select(label => new
        {
            key = label.Name["dimension_caption_".Length..],
            text = label.Text.Replace('\n', ' '),
            availableWidth = label.ClientSize.Width - label.Padding.Horizontal,
            availableHeight = label.ClientSize.Height - label.Padding.Vertical,
            measuredWidth = label.Text.Split('\n').Max(line => TextRenderer.MeasureText(line, label.Font).Width),
            measuredHeight = TextRenderer.MeasureText(label.Text, label.Font).Height
        }).ToArray();
        check(name + "_full_dimension_captions_fit", evidence.Length == 7 && evidence.All(item =>
            expected.GetValueOrDefault(item.key) == item.text && item.measuredWidth <= item.availableWidth && item.measuredHeight <= item.availableHeight), evidence);
    }

    private static void CheckButtonPair(string name, Button first, Button second, Action<string, bool, object?> check)
    {
        check(name + "_buttons_align", first.Top == second.Top && first.Size == second.Size && first.Font.Equals(second.Font) &&
            first.Padding == second.Padding && first.Margin == second.Margin && first.TextAlign == second.TextAlign &&
            first.FlatStyle == second.FlatStyle && first.FlatAppearance.BorderSize == second.FlatAppearance.BorderSize &&
            first.UseCompatibleTextRendering == second.UseCompatibleTextRendering,
            new
            {
                first = ButtonEvidence(first),
                second = ButtonEvidence(second)
            });
    }

    private static object ButtonEvidence(Button button) => new
    {
        button.Text,
        button.Left,
        button.Top,
        button.Width,
        button.Height,
        font = button.Font.ToString(),
        padding = button.Padding.ToString(),
        margin = button.Margin.ToString(),
        textAlign = button.TextAlign.ToString(),
        borderWidth = button.FlatAppearance.BorderSize,
        borderColor = button.FlatAppearance.BorderColor.ToString()
    };


    private static void RenderDrawing(MainForm form)
    {
        form.PerformLayout();
        form.Refresh();
        Application.DoEvents();
        var drawing = form.DrawingControl;
        using var bitmap = new Bitmap(drawing.Width, drawing.Height);
        drawing.DrawToBitmap(bitmap, new Rectangle(Point.Empty, drawing.Size));
    }

    private static bool SameOtherParameters(Parameters before, Parameters after, string edited)
    {
        var name = char.ToUpperInvariant(edited[0]) + edited[1..];
        return typeof(Parameters).GetProperties().Where(property => property.Name != name).All(property => Equals(property.GetValue(before), property.GetValue(after)));
    }

    private static void CaptureDialog(Control dialog, string path)
    {
        dialog.Refresh();
        Application.DoEvents();
        using var bitmap = new Bitmap(dialog.Width, dialog.Height);
        dialog.DrawToBitmap(bitmap, new Rectangle(Point.Empty, dialog.Size));
        bitmap.Save(path);
    }

    private static void PressDialogKey(Form dialog, Keys key) =>
        typeof(Form).GetMethod("ProcessDialogKey", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(dialog, [key]);

    private static void PressDialogKey(DimensionEditor editor, Keys key) =>
        typeof(DimensionEditor).GetMethod("ProcessDialogKey", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(editor, [key]);


    // Tolerances use the explicit ± action. Caption clicks are covered separately as nominal-only edits.
    private static void EditToleranceFromPanel(MainForm owner, string key, Action<DimensionEditor> edit, bool refresh = true)
    {
        if (refresh)
        {
            owner.Refresh();
            Application.DoEvents();
        }
        Descendants(owner).OfType<Button>().Single(b => b.Name == "dimension_edit_" + key).PerformClick();
        var editor = owner.ActiveDimensionEditor ?? throw new InvalidOperationException("Tolerance button did not open editor: " + key);
        edit(editor);
        if (owner.ActiveDimensionEditor is not null)
        {
            var message = "Editor did not finish: " + key + ": " + editor.ErrorText;
            editor.CancelEditorButton.PerformClick();
            throw new InvalidOperationException(message);
        }
    }


    private static IEnumerable<Control> Descendants(Control root)
    {
        foreach (Control child in root.Controls)
        {
            yield return child;
            foreach (var descendant in Descendants(child))
            {
                yield return descendant;
            }
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
            if (popup == IntPtr.Zero || popup == owner.Handle)
            {
                return;
            }

            GetWindowThreadProcessId(popup, out var processId);
            if (processId != (uint)Environment.ProcessId)
            {
                return;
            }

            var dialog = Application.OpenForms.Cast<Form>().FirstOrDefault(form => form.Handle == popup);
            if (dialog is not null)
            {
                edit?.Invoke(dialog);
            }

            var text = new System.Text.StringBuilder(256);
            GetWindowText(popup, text, text.Capacity);
            title = text.ToString();
            cancelled = PostMessage(popup, 0x0010, IntPtr.Zero, IntPtr.Zero);
            if (cancelled)
            {
                timer.Stop();
            }
        };
        timer.Start();
        try
        {
            show();
        }
        finally { timer.Stop(); }
        if (!cancelled)
        {
            throw new InvalidOperationException("The owned modal dialog was not cancelled: " + title);
        }

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
