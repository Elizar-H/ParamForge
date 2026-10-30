using System.Drawing.Drawing2D;

namespace Kontur.Desktop;

internal static class WorkCoordinates
{
    internal static void Origin(Graphics g, PointF point, float u)
    {
        using var pen = new Pen(Theme.Ink, 1F * u);
        using var background = new SolidBrush(Color.White);
        using var brush = new SolidBrush(Theme.Ink);
        // Workpiece zero W: SINUMERIK Fundamentals, 03/2013, section 1.3, page 22.
        var outer = new RectangleF(point.X - 8 * u, point.Y - 8 * u, 16 * u, 16 * u);
        var inner = new RectangleF(point.X - 5.5F * u, point.Y - 5.5F * u, 11 * u, 11 * u);
        g.FillEllipse(background, outer);
        g.FillPie(brush, inner.X, inner.Y, inner.Width, inner.Height, 0, 90);
        g.DrawEllipse(pen, outer); g.DrawEllipse(pen, inner);
        g.DrawLine(pen, point.X - 11 * u, point.Y, point.X + 11 * u, point.Y);
        g.DrawLine(pen, point.X, point.Y - 11 * u, point.X, point.Y + 11 * u);
    }
    internal static void Legend(Graphics g, PointF origin, float u)
    {
        using var cap = new AdjustableArrowCap(3, 4, true);
        using var pen = new Pen(Theme.Accent, 1.1F * u) { CustomEndCap = cap };
        using var brush = new SolidBrush(Theme.Accent);
        using var font = new Font("Segoe UI", 11 * u, FontStyle.Regular, GraphicsUnit.Pixel);
        g.DrawLine(pen, origin, new(origin.X, origin.Y - 25 * u));
        g.DrawLine(pen, origin, new(origin.X + 30 * u, origin.Y));
        g.DrawString("X", font, brush, origin.X - 4 * u, origin.Y - 42 * u);
        g.DrawString("Z", font, brush, origin.X + 34 * u, origin.Y - 7 * u);
    }
}
