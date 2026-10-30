namespace Kontur.Desktop;
// The whole cell selects its number, including the space outside the native textbox.
internal sealed class NumericFieldHost : Panel
{
    private readonly TextBox input;
    internal NumericFieldHost(TextBox input)
    {
        this.input = input;
        Dock = DockStyle.Fill;
        Margin = Padding.Empty;
        BackColor = Theme.Surface;
        Cursor = Cursors.IBeam;
        input.Dock = DockStyle.None;
        input.Margin = Padding.Empty;
        input.BackColor = BackColor;
        input.Enter += (_, _) => Invalidate();
        input.Leave += (_, _) => Invalidate();
        Controls.Add(input);
    }
    protected override void OnLayout(LayoutEventArgs e)
    {
        base.OnLayout(e);
        input.SetBounds(3, Math.Max(0, (Height - input.PreferredHeight) / 2), Math.Max(0, Width - 9), input.PreferredHeight);
    }
    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button == MouseButtons.Left)
        {
            input.Focus();
            input.SelectAll();
        }
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        if (input.Focused)
        {
            using var pen = new Pen(Theme.Accent);
            e.Graphics.DrawLine(pen, 3, Height - 2, Width - 6, Height - 2);
        }
    }
}
