using System.Drawing.Drawing2D;

namespace Kontur.Desktop;

/// <summary>Draws one centered, DPI-scaled check instead of a native bitmap check.</summary>
internal sealed class ApplicationMenuRenderer : ToolStripProfessionalRenderer
{
    internal ApplicationMenuRenderer() : base(new NeutralColors()) { RoundedEdges = false; }

    internal static void Configure(ToolStripDropDownItem item)
    {
        item.DropDown.Renderer = new ApplicationMenuRenderer();
        if (item.DropDown is ToolStripDropDownMenu menu)
        {
            menu.ShowCheckMargin = true;
            menu.ShowImageMargin = false;
        }
    }

    protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
    {
        var dpi = e.ToolStrip?.DeviceDpi ?? 96;
        var size = Math.Min(e.Item.Height - 6, (int)Math.Round(12 * dpi / 96F));
        var area = e.ImageRectangle;
        var x = area.Left + (area.Width - size) / 2F;
        var y = area.Top + (area.Height - size) / 2F;
        using var pen = new Pen(Theme.Ink, Math.Max(1.5F, dpi / 64F));
        var smoothing = e.Graphics.SmoothingMode;
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        e.Graphics.DrawLines(pen, new PointF[] { new(x + size * .12F, y + size * .52F), new(x + size * .42F, y + size * .8F), new(x + size * .9F, y + size * .2F) });
        e.Graphics.SmoothingMode = smoothing;
    }

    private sealed class NeutralColors : ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground => Color.White;
        public override Color ImageMarginGradientBegin => Color.White;
        public override Color ImageMarginGradientMiddle => Color.White;
        public override Color ImageMarginGradientEnd => Color.White;
        public override Color MenuItemSelected => Theme.Surface;
        public override Color MenuItemBorder => Theme.Border;
        public override Color MenuBorder => Theme.Border;
        public override Color CheckBackground => Color.White;
        public override Color CheckSelectedBackground => Theme.Surface;
    }
}
