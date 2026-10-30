namespace Kontur.Desktop;

internal sealed class SelectAllTextBox : TextBox
{
    private Point down;
    protected override void OnEnter(EventArgs e)
    {
        base.OnEnter(e);
        SelectAll();
    }
    protected override void OnMouseDown(MouseEventArgs e)
    {
        down = e.Location;
        base.OnMouseDown(e);
    }
    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (e.Button == MouseButtons.Left && e.Clicks == 1 && Math.Abs(e.X - down.X) < SystemInformation.DragSize.Width / 2 && Math.Abs(e.Y - down.Y) < SystemInformation.DragSize.Height / 2)
        {
            SelectAll();
        }
    }
}
