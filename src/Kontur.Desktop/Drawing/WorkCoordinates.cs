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
        g.DrawEllipse(pen, outer);
        g.DrawEllipse(pen, inner);
        g.DrawLine(pen, point.X - 11 * u, point.Y, point.X + 11 * u, point.Y);
        g.DrawLine(pen, point.X, point.Y - 11 * u, point.X, point.Y + 11 * u);
    }
    internal static void Legend(Graphics g, PointF origin, float u)
    {
        // Compact planar UCS-style glyph; turning keeps its physical X/Z directions.
        var xColor = Color.FromArgb(188, 67, 65);
        var zColor = Color.FromArgb(55, 105, 166);
        using var xPen = new Pen(xColor, 1.6F * u);
        using var zPen = new Pen(zColor, 1.6F * u);
        using var xBrush = new SolidBrush(xColor);
        using var zBrush = new SolidBrush(zColor);
        var xTip = new PointF(origin.X, origin.Y - 27 * u);
        var zTip = new PointF(origin.X + 30 * u, origin.Y);
        g.DrawLine(xPen, origin.X, origin.Y, xTip.X, xTip.Y + 4 * u);
        g.DrawLine(zPen, origin.X, origin.Y, zTip.X - 4 * u, zTip.Y);
        g.FillPolygon(xBrush, [xTip, new(xTip.X - 2.7F * u, xTip.Y + 5 * u), new(xTip.X + 2.7F * u, xTip.Y + 5 * u)]);
        g.FillPolygon(zBrush, [zTip, new(zTip.X - 5 * u, zTip.Y - 2.7F * u), new(zTip.X - 5 * u, zTip.Y + 2.7F * u)]);
        using var corner = new Pen(Color.FromArgb(110, 117, 125), .8F * u);
        var box = new RectangleF(origin.X - 2 * u, origin.Y - 2 * u, 4 * u, 4 * u);
        g.FillRectangle(Brushes.White, box);
        g.DrawRectangle(corner, box.X, box.Y, box.Width, box.Height);
        using var font = new Font("Segoe UI Semibold", 11.5F * u, FontStyle.Regular, GraphicsUnit.Pixel);
        var flags = TextFormatFlags.NoPadding | TextFormatFlags.SingleLine;
        TextRenderer.DrawText(g, "X", font, Point.Round(new(origin.X - 4 * u, origin.Y - 44 * u)), xColor, flags);
        TextRenderer.DrawText(g, "Z", font, Point.Round(new(origin.X + 35 * u, origin.Y - 8 * u)), zColor, flags);
    }
}
