using System.ComponentModel;
using System.Drawing.Drawing2D;

namespace Kontur.Desktop;

internal sealed partial class TrajectoryCanvas
{
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal bool ShowStock
    {
        get; set;
    }
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal bool ShowGrid { get; set; } = true;
    internal int GridLineCount
    {
        get; private set;
    }
    internal bool StockRendered
    {
        get; private set;
    }
    internal int? MarkerTool
    {
        get; private set;
    }
    internal int WorkingArrowCount
    {
        get; private set;
    }
    internal int RapidArrowCount
    {
        get; private set;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        VisibleSegmentCount = GridLineCount = 0;
        WorkingArrowCount = RapidArrowCount = 0;
        StockRendered = false;
        MarkerTool = null;
        if (project is null || project.Verification.Moves.Count == 0)
        {
            return;
        }

        var u = DeviceDpi / 96F;
        var p = project.Params;
        var c = project.Calculations;
        var moves = project.Verification.Moves;
        var minZ = Math.Min(ShowStock ? -c.StockLength : -c.CutLength, moves.Min(m => m.Z));
        var maxZ = Math.Max(10, moves.Max(m => m.Z));
        var maxRadius = Math.Max(p.StockDiameter, moves.Max(m => m.X)) / 2 + (ShowStock ? 4 : 1);
        var bounds = new RectangleF(80 * u, 20 * u, Math.Max(1, Width - 100 * u), Math.Max(1, Height - 62 * u));
        var scale = (float)Math.Min(bounds.Width / (maxZ - minZ), bounds.Height / maxRadius);
        var left = bounds.Left + (bounds.Width - (float)(maxZ - minZ) * scale) / 2;
        var top = bounds.Top + (bounds.Height - (float)maxRadius * scale) / 2;
        PointF Point(double z, double radius)
        {
            var x = left + (float)(z - minZ) * scale;
            var y = top + (float)(maxRadius - radius) * scale;
            return new(Width / 2F + (x - Width / 2F) * zoom + pan.X, Height / 2F + (y - Height / 2F) * zoom + pan.Y);
        }
        using var numberFont = new Font("Segoe UI", 11F * u, FontStyle.Regular, GraphicsUnit.Pixel);
        var clip = g.Save();
        g.SetClip(bounds);
        if (ShowGrid)
        {
            using var grid = new Pen(Color.FromArgb(229, 233, 237), .65F * u);
            var zStep = NiceStep(70 * u / (scale * zoom));
            for (var z = Math.Ceiling(minZ / zStep) * zStep; z <= maxZ && GridLineCount < 400; z += zStep)
            {
                var x = Point(z, 0).X;
                if (x < bounds.Left || x > bounds.Right)
                {
                    continue;
                }

                g.DrawLine(grid, x, bounds.Top, x, bounds.Bottom);
                GridLineCount++;
            }
            var xStep = NiceStep(100 * u / (scale * zoom));
            for (double x = 0; x <= maxRadius * 2 && GridLineCount < 400; x += xStep)
            {
                var y = Point(0, x / 2).Y;
                if (y < bounds.Top || y > bounds.Bottom)
                {
                    continue;
                }

                g.DrawLine(grid, bounds.Left, y, bounds.Right, y);
                GridLineCount++;
            }
        }
        if (ShowStock)
        {
            var stock = new[] { Point(-c.StockLength, p.StockBore / 2), Point(-c.StockLength, p.StockDiameter / 2), Point(0, p.StockDiameter / 2), Point(0, p.StockBore / 2) };
            using var blankFill = new SolidBrush(Color.FromArgb(253, 250, 243));
            g.FillPolygon(blankFill, stock);
            using var blankPen = new Pen(Color.FromArgb(133, 111, 73), 1.1F * u) { DashStyle = DashStyle.Dash };
            g.DrawPolygon(blankPen, stock);
            var a = Point(-c.StockLength, p.StockDiameter / 2 + 3);
            var b = Point(c.JawsFrontZ, 0);
            var jaws = RectangleF.FromLTRB(a.X, a.Y, b.X, b.Y);
            using var jawFill = new SolidBrush(Color.FromArgb(238, 240, 242));
            g.FillRectangle(jawFill, jaws);
            var jawState = g.Save();
            g.SetClip(jaws, CombineMode.Intersect);
            using var hatch = new Pen(Color.FromArgb(200, 206, 213), .7F * u);
            for (var x = jaws.Left - jaws.Height; x < jaws.Right; x += 10 * u)
            {
                g.DrawLine(hatch, x, jaws.Bottom, x + jaws.Height, jaws.Top);
            }

            g.Restore(jawState);
            using var jawPen = new Pen(Color.FromArgb(154, 163, 172), 1F * u);
            g.DrawRectangle(jawPen, jaws.X, jaws.Y, jaws.Width, jaws.Height);
            if (jaws.Width > TextRenderer.MeasureText("Зажим", numberFont).Width + 10 * u)
            {
                TextRenderer.DrawText(
                    g,
                    "Зажим",
                    numberFont,
                    System.Drawing.Point.Round(new(jaws.Left + 4 * u, jaws.Top + 4 * u)),
                    Theme.Muted,
                    Color.FromArgb(238, 240, 242),
                    TextFormatFlags.NoPadding
                );
            }

            StockRendered = true;
        }
        var points = TargetProfile.Select(q => Point(q.Z, q.X / 2)).ToArray();
        using var material = new SolidBrush(Color.FromArgb(245, 248, 250));
        g.FillPolygon(material, points);
        using (var path = new GraphicsPath())
        {
            path.AddPolygon(points);
            var area = path.GetBounds();
            var saved = g.Save();
            g.SetClip(path, CombineMode.Intersect);
            using var hatch = new Pen(Color.FromArgb(210, 218, 226), .65F * u);
            for (var x = area.Left - area.Height; x < area.Right; x += 16 * u)
            {
                g.DrawLine(hatch, x, area.Bottom, x + area.Height, area.Top);
            }

            g.Restore(saved);
        }
        using var outline = new Pen(Color.FromArgb(48, 60, 72), 1.6F * u);
        g.DrawPolygon(outline, points);
        using var axis = new Pen(Color.FromArgb(123, 135, 148), .8F * u) { DashStyle = DashStyle.DashDot };
        g.DrawLine(axis, Point(minZ, 0), Point(maxZ, 0));
        g.DrawLine(axis, Point(0, 0), Point(0, maxRadius));
        using var t1 = new Pen(Color.FromArgb(27, 96, 181), 2.4F * u);
        using var t2 = new Pen(Color.FromArgb(0, 124, 112), 2.4F * u);
        using var rapid1 = new Pen(t1.Color, 1.2F * u) { DashStyle = DashStyle.Dash };
        using var rapid2 = new Pen(t2.Color, 1.2F * u) { DashStyle = DashStyle.Dash };
        using var context = new Pen(Color.FromArgb(86, 94, 105), 1.25F * u);
        using var contextRapid = new Pen(Color.FromArgb(151, 160, 170), .9F * u) { DashStyle = DashStyle.Dash };
        for (var pass = 0; pass < 2; pass++)
        {
            for (var i = 1; i < moves.Count; i++)
            {
                var before = moves[i - 1];
                var move = moves[i];
                var active = selected.Contains(move.Line);
                if (before.Tool != move.Tool || (!ShowRapids && move.Mode != "G1") || (pass == 0 ? !ShowAll || active : !active))
                {
                    continue;
                }

                var pen = pass == 0 ? (move.Mode == "G1" ? context : contextRapid) : move.Mode == "G1" ? (move.Tool == 1 ? t1 : t2) : (move.Tool == 1 ? rapid1 : rapid2);
                var a = Point(before.Z, before.X / 2);
                var b = Point(move.Z, move.X / 2);
                g.DrawLine(pen, a, b);
                VisibleSegmentCount++;
                if (active)
                {
                    if (DrawDirection(g, a, b, pen.Color, u, move.Mode == "G1"))
                    {
                        if (move.Mode == "G1")
                        {
                            WorkingArrowCount++;
                        }
                        else
                        {
                            RapidArrowCount++;
                        }
                    }
                }
            }
        }

        var current = selected.Count == 1 ? moves.FirstOrDefault(m => selected.Contains(m.Line)) : moves.LastOrDefault(m => selected.Contains(m.Line) && m.Mode == "G1");
        if (current is not null)
        {
            MarkerTool = current.Tool;
            var point = Point(current.Z, current.X / 2);
            var direction = current.Tool == 2 ? 1 : -1;
            using var brush = new SolidBrush(current.Tool == 1 ? t1.Color : t2.Color);
            g.FillPolygon(
                brush,
                [point, new(point.X + 11 * u, point.Y + direction * 7 * u), new(point.X + 3 * u, point.Y + direction * 13 * u)]
            );
            TextRenderer.DrawText(
                g,
                "T" + current.Tool,
                numberFont,
                System.Drawing.Point.Round(new(point.X + 13 * u, point.Y + direction * 14 * u)),
                Theme.Ink,
                Color.White,
                TextFormatFlags.NoPadding
            );
        }
        g.Restore(clip);
        WorkCoordinates.Origin(g, Point(0, 0), u);
        if (ShowGrid)
        {
            var zStep = NiceStep(70 * u / (scale * zoom));
            for (var z = Math.Ceiling(minZ / zStep) * zStep; z <= maxZ; z += zStep)
            {
                var x = Point(z, 0).X;
                if (x < bounds.Left || x > bounds.Right)
                {
                    continue;
                }

                var rounded = Math.Round(z, 3);
                var text = NumericText.Format(rounded == 0 ? 0 : rounded);
                var size = TextRenderer.MeasureText(text, numberFont);
                TextRenderer.DrawText(
                    g,
                    text,
                    numberFont,
                    System.Drawing.Point.Round(new(x - size.Width / 2, Height - 25 * u)),
                    Theme.Muted,
                    TextFormatFlags.NoPadding
                );
            }
            var xStep = NiceStep(100 * u / (scale * zoom));
            for (double x = 0; x <= maxRadius * 2; x += xStep)
            {
                var y = Point(0, x / 2).Y;
                if (y < bounds.Top || y > bounds.Bottom)
                {
                    continue;
                }

                TextRenderer.DrawText(
                    g,
                    NumericText.Format(Math.Round(x, 3)),
                    numberFont,
                    System.Drawing.Point.Round(new(45 * u, y - 8 * u)),
                    Theme.Muted,
                    TextFormatFlags.NoPadding
                );
            }
        }
        WorkCoordinates.Legend(g, new(22 * u, Height - 16 * u), u);
    }
    private static double NiceStep(double desired)
    {
        var power = Math.Pow(10, Math.Floor(Math.Log10(Math.Max(.001, desired))));
        var fraction = desired / power;
        return power * (fraction <= 1 ? 1 : fraction <= 2 ? 2 : fraction <= 5 ? 5 : 10);
    }
    private static bool DrawDirection(Graphics g, PointF from, PointF to, Color color, float u, bool working)
    {
        var dx = to.X - from.X;
        var dy = to.Y - from.Y;
        var length = MathF.Sqrt(dx * dx + dy * dy);
        if (length < 2 * u)
        {
            return false;
        }

        dx /= length;
        dy /= length;
        var size = working ? Math.Min(5 * u, length / 3) : Math.Min(4 * u, Math.Max(2.5F * u, length / 3));
        var at = working ? length * .6F : length - size;
        var x = from.X + dx * at;
        var y = from.Y + dy * at;
        var tip = new PointF(x + dx * size, y + dy * size);
        var a = new PointF(x - dx * size * .6F - dy * size * .52F, y - dy * size * .6F + dx * size * .52F);
        var b = new PointF(x - dx * size * .6F + dy * size * .52F, y - dy * size * .6F - dx * size * .52F);
        if (working)
        {
            using var brush = new SolidBrush(color);
            g.FillPolygon(brush, [tip, a, b]);
        }
        else
        {
            using var pen = new Pen(color, Math.Max(1, u));
            g.DrawLines(pen, new PointF[] { a, tip, b });
        }
        return true;
    }
}
