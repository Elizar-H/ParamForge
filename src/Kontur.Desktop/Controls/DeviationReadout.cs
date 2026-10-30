using System.ComponentModel;
using Kontur.Core;
namespace Kontur.Desktop;
// The tolerance itself is the edit action; there is no separate +/- cell.
internal sealed class DeviationReadout : Button
{
    private string numeric = "";
    private bool showNumbers;
    internal bool TextFits
    {
        get; private set;
    }
    internal int NumericLineCount => numeric.Contains('\n') ? 2 : 1;
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal bool ShowNumbers
    {
        get => showNumbers; set
        {
            showNumbers = value;
            Text = value ? numeric : "±";
            Invalidate();
        }
    }
    internal DeviationReadout()
    {
        SetStyle(
            ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw,
            true
        );
        Dock = DockStyle.Fill;
        Font = Theme.Detail;
        Cursor = Cursors.Hand;
        Margin = Padding.Empty;
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        BackColor = Theme.Surface;
        TextAlign = ContentAlignment.MiddleLeft;
        Padding = Padding.Empty;
    }
    internal void SetValues(double lo, double hi, string designation)
    {
        numeric = lo == -hi ? "±" + NumericText.Format(hi) : lo == 0 ? Tolerances.Signed(hi) : hi == 0 ? Tolerances.Signed(lo)
            : Tolerances.Signed(hi) + "\n" + Tolerances.Signed(lo);
        Text = showNumbers ? numeric : "±";
        AccessibleName = $"Отклонения: нижнее {Tolerances.Signed(lo)}, верхнее {Tolerances.Signed(hi)}. Изменить.";
        Invalidate();
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(BackColor);
        var flags = TextFormatFlags.NoPadding | TextFormatFlags.SingleLine | TextFormatFlags.PreserveGraphicsClipping;
        var lines = Text.Split('\n');
        var height = TextRenderer.MeasureText("0", Font, Size.Empty, flags).Height;
        var y = (Height - height * lines.Length) / 2;
        TextFits = lines.All(line => TextRenderer.MeasureText(line, Font, Size.Empty, flags).Width <= Width - 6) && height * lines.Length <= Height;
        foreach (var line in lines)
        {
            TextRenderer.DrawText(e.Graphics, line, Font, new Rectangle(3, y, Math.Max(0, Width - 6), height), Theme.Ink, flags);
            y += height;
        }
        if (Focused)
        {
            ControlPaint.DrawFocusRectangle(e.Graphics, ClientRectangle, Theme.Muted, BackColor);
        }
    }
}
