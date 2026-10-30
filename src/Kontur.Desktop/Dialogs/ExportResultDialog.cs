using System.Diagnostics;

namespace Kontur.Desktop;

internal sealed class ExportResultDialog : Form
{
    internal ExportResultDialog(IReadOnlyList<string> files)
    {
        Text = "Файлы готовы";
        Font = Theme.Body;
        ClientSize = new Size(620, 340);
        MinimumSize = new Size(540, 340);
        StartPosition = FormStartPosition.CenterParent;
        MaximizeBox = false;
        MinimizeBox = false;
        BackColor = Color.White;
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(18),
            RowCount = 4,
            ColumnCount = 1
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 33));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 70));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        layout.Controls.Add(Theme.Label("Готово: 3 файла", Theme.Strong), 0, 0);
        var folder = Path.GetDirectoryName(files[0])!;
        layout.Controls.Add(
            new TextBox { Text = folder, ReadOnly = true, Multiline = true, BorderStyle = BorderStyle.None, BackColor = Color.White, Font = Theme.Small, Dock = DockStyle.Fill, ScrollBars = ScrollBars.Vertical, TabStop = false },
            0,
            1
        );
        var list = new ListBox
        {
            Dock = DockStyle.Fill,
            Font = Theme.Body,
            BorderStyle = BorderStyle.None,
            IntegralHeight = false
        };
        foreach (var path in files)
        {
            list.Items.Add(Path.GetFileName(path));
        }

        list.SelectedIndex = 0;
        layout.Controls.Add(list, 0, 2);
        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            WrapContents = false
        };
        var openFolder = Theme.Button("Открыть папку");
        openFolder.Width = 175;
        openFolder.Click += (_, _) => Open(folder);
        var openFile = Theme.Button("Открыть файл");
        openFile.Width = 165;
        openFile.Click += (_, _) =>
        {
            if (list.SelectedIndex >= 0)
            {
                Open(files[list.SelectedIndex]);
            }
        };
        var close = Theme.Button("Закрыть");
        close.Width = 120;
        close.DialogResult = DialogResult.OK;
        buttons.Controls.AddRange([openFolder, openFile, close]);
        layout.Controls.Add(buttons, 0, 3);
        Controls.Add(layout);
        AcceptButton = close;
        CancelButton = close;
        Shown += (_, _) => list.Focus();
    }

    private void Open(string path)
    {
        try
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Не удалось открыть", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }
}
