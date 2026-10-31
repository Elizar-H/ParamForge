using System.ComponentModel;

namespace Kontur.Desktop;

/// <summary>One navigation list and one content area for application settings.</summary>
internal sealed class SettingsSections : UserControl
{
    private readonly ListBox navigation = new() { Dock = DockStyle.Fill, BorderStyle = BorderStyle.None, DrawMode = DrawMode.OwnerDrawFixed, IntegralHeight = false, BackColor = Theme.Surface };
    private readonly Panel content = new() { Dock = DockStyle.Fill, BackColor = Color.White };
    private readonly List<Panel> pages = [];
    internal IReadOnlyList<Panel> Pages => pages;
    internal Panel? SelectedPage => SelectedIndex < 0 ? null : pages[SelectedIndex];
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal int SelectedIndex
    {
        get => navigation.SelectedIndex; set => navigation.SelectedIndex = value;
    }
    internal Control Navigation => navigation;
    internal event Action? EnterPageRequested;

    internal SettingsSections()
    {
        Dock = DockStyle.Fill;
        Font = Theme.Detail;
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty, Padding = new Padding(16) };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, TextRenderer.MeasureText("Движения инструмента", Font).Width + 32));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        navigation.Font = Font;
        navigation.ItemHeight = TextRenderer.MeasureText("0", Font).Height + 16;
        navigation.AccessibleName = "Разделы заготовки и обработки";
        navigation.Margin = new Padding(0, 0, 16, 0);
        navigation.DrawItem += (_, e) =>
        {
            if (e.Index < 0)
            {
                return;
            }
            var selected = (e.State & DrawItemState.Selected) != 0;
            using var fill = new SolidBrush(selected ? Theme.Accent : Theme.Surface);
            e.Graphics.FillRectangle(fill, e.Bounds);
            var text = Rectangle.Inflate(e.Bounds, -10, 0);
            TextRenderer.DrawText(e.Graphics, navigation.Items[e.Index].ToString(), Font, text, selected ? Color.White : Theme.Ink,
                TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.NoPrefix);
            if ((e.State & DrawItemState.Focus) != 0)
            {
                e.DrawFocusRectangle();
            }
        };
        navigation.SelectedIndexChanged += (_, _) =>
        {
            foreach (var page in pages)
            {
                page.Visible = page == SelectedPage;
            }
            SelectedPage?.BringToFront();
        };
        navigation.PreviewKeyDown += (_, e) => { if (e.KeyCode is Keys.Down or Keys.Enter or Keys.Left or Keys.Right) { e.IsInputKey = true; } };
        navigation.KeyDown += (_, e) =>
        {
            if (e.Modifiers != Keys.None)
            {
                return;
            }
            if (e.KeyCode is Keys.Down or Keys.Enter)
            {
                e.Handled = e.SuppressKeyPress = true;
                EnterPageRequested?.Invoke();
            }
            else if (e.KeyCode is Keys.Left or Keys.Right)
            {
                e.Handled = e.SuppressKeyPress = true;
                SelectedIndex = Math.Clamp(SelectedIndex + (e.KeyCode == Keys.Right ? 1 : -1), 0, pages.Count - 1);
            }
        };
        layout.Controls.Add(navigation, 0, 0);
        layout.Controls.Add(content, 1, 0);
        Controls.Add(layout);
        FontChanged += (_, _) =>
        {
            layout.ColumnStyles[0].Width = TextRenderer.MeasureText("Движения инструмента", Font).Width + 32;
            navigation.ItemHeight = TextRenderer.MeasureText("0", Font).Height + 16;
            navigation.Invalidate();
        };
    }

    internal Panel AddPage(string title, string description)
    {
        var page = new Panel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = Color.White, Padding = new Padding(20), Text = title, Visible = false };
        var header = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 1, RowCount = 2, Margin = Padding.Empty };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        var heading = Theme.Label(title, Theme.Strong);
        heading.AutoSize = true;
        heading.Margin = new Padding(0, 0, 0, 6);
        var note = Theme.Label(description, Theme.Detail);
        note.AutoSize = true;
        note.ForeColor = Theme.Muted;
        note.Margin = new Padding(0, 0, 0, 18);
        header.Controls.Add(heading, 0, 0);
        header.Controls.Add(note, 0, 1);
        page.Controls.Add(header);
        pages.Add(page);
        content.Controls.Add(page);
        navigation.Items.Add(title);
        if (SelectedIndex < 0)
        {
            SelectedIndex = 0;
        }
        return page;
    }

    internal void ShowField(Control field)
    {
        var page = pages.FirstOrDefault(p => p.Contains(field));
        if (page is not null)
        {
            SelectedIndex = pages.IndexOf(page);
        }
    }
}
