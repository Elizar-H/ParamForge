namespace Kontur.Desktop;

/// <summary>Application pages share one window and retain their controls and selection.</summary>
internal sealed class WorkspaceTabs : TabControl
{
    private readonly HashSet<TabPage> closable = [];
    private TabPage? pressedClose, hoveredClose;
    internal event Action<TabPage>? CloseRequested;
    internal void SetClosable(TabPage page, bool value)
    {
        if (value)
        {
            closable.Add(page);
        }
        else
        {
            closable.Remove(page);
        }
        Invalidate();
    }
    internal Rectangle CloseBounds(TabPage page)
    {
        if (!closable.Contains(page) || !TabPages.Contains(page))
        {
            return Rectangle.Empty;
        }
        var tab = GetTabRect(TabPages.IndexOf(page));
        var size = Math.Max(14, (int)Math.Round(Font.Height * .8F));
        return new Rectangle(tab.Right - size - 7, tab.Top + (tab.Height - size) / 2, size, size);
    }
    internal bool CaptionFits(TabPage page)
    {
        var tab = GetTabRect(TabPages.IndexOf(page));
        var close = CloseBounds(page);
        var width = tab.Width - 12 - (close.IsEmpty ? 0 : close.Width + 8);
        return TextRenderer.MeasureText(page.Text, Font, Size.Empty, TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix).Width <= width;
    }

    internal WorkspaceTabs()
    {
        Dock = DockStyle.Fill;
        Name = "WorkspaceTabs";
        Font = Theme.Detail;
        DrawMode = TabDrawMode.OwnerDrawFixed;
        Padding = new Point(Math.Max(32, Font.Height + 12), 7);
        FontChanged += (_, _) => Padding = new Point(Math.Max(32, Font.Height + 12), 7);
        DrawItem += (_, e) =>
        {
            var selected = e.Index == SelectedIndex;
            using var brush = new SolidBrush(selected ? Color.White : Theme.Surface);
            e.Graphics.FillRectangle(brush, e.Bounds);
            var page = TabPages[e.Index];
            var caption = e.Bounds;
            caption.Inflate(-6, 0);
            var close = CloseBounds(page);
            if (!close.IsEmpty)
            {
                caption.Width -= close.Width + 8;
            }
            TextRenderer.DrawText(e.Graphics, page.Text, Font, caption, Theme.Ink,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
            if (!close.IsEmpty)
            {
                if (hoveredClose == page || pressedClose == page)
                {
                    using var hover = new SolidBrush(Theme.Border);
                    e.Graphics.FillRectangle(hover, close);
                }
                var inset = Math.Max(3, close.Width / 4);
                using var cross = new Pen(Theme.Muted, Math.Max(1.2F, DeviceDpi / 96F));
                e.Graphics.DrawLine(cross, close.Left + inset, close.Top + inset, close.Right - inset, close.Bottom - inset);
                e.Graphics.DrawLine(cross, close.Left + inset, close.Bottom - inset, close.Right - inset, close.Top + inset);
            }
            if (selected)
            {
                using var pen = new Pen(Theme.Accent, Math.Max(2, DeviceDpi / 48F));
                e.Graphics.DrawLine(pen, e.Bounds.Left + 10, e.Bounds.Bottom - 2, e.Bounds.Right - 10, e.Bounds.Bottom - 2);
            }
        };
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        pressedClose = e.Button == MouseButtons.Left ? closable.FirstOrDefault(p => CloseBounds(p).Contains(e.Location)) : null;
        if (pressedClose is not null)
        {
            Capture = true;
            Invalidate();
            return;
        }
        base.OnMouseDown(e);
    }
    protected override void OnMouseUp(MouseEventArgs e)
    {
        var page = pressedClose;
        pressedClose = null;
        Capture = false;
        if (page is not null)
        {
            if (CloseBounds(page).Contains(e.Location))
            {
                CloseRequested?.Invoke(page);
            }
            Invalidate();
            return;
        }
        if (e.Button == MouseButtons.Middle)
        {
            var middle = closable.FirstOrDefault(p => GetTabRect(TabPages.IndexOf(p)).Contains(e.Location));
            if (middle is not null)
            {
                CloseRequested?.Invoke(middle);
                return;
            }
        }
        base.OnMouseUp(e);
    }
    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var page = closable.FirstOrDefault(p => CloseBounds(p).Contains(e.Location));
        if (page != hoveredClose)
        {
            hoveredClose = page;
            Invalidate();
        }
    }
    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        hoveredClose = null;
        Invalidate();
    }
    protected override void OnMouseCaptureChanged(EventArgs e)
    {
        base.OnMouseCaptureChanged(e);
        if (!Capture)
        {
            pressedClose = null;
            Invalidate();
        }
    }
}
