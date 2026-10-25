using System.ComponentModel;
using System.Drawing.Drawing2D;
using System.Globalization;
using Kontur.Core;

namespace Kontur.Desktop;

internal sealed class SketchCanvas : Control
{
    private Parameters? parameters;
    private readonly List<RectangleF> labels = [];
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal Parameters? Parameters { get => parameters; set { parameters = value; Invalidate(); } }
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal float SimulatedTextScale { get; set; } = 1;
    internal bool LabelsInside => labels.All(r => r.Left >= 0 && r.Top >= 0 && r.Right <= Width && r.Bottom <= Height);
    internal bool LabelsOverlap => labels.SelectMany((a, i) => labels.Skip(i + 1).Select(b => RectangleF.Intersect(a, b))).Any(r => r.Width > 1 && r.Height > 1);
    internal bool RearWallsClosed { get; private set; }
    internal bool LabelsClearOfMaterial { get; private set; }
    internal bool BoreLeadersClearOfMaterial { get; private set; }

    internal SketchCanvas()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        BackColor = Color.White;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
        labels.Clear();
        RearWallsClosed = false;
        LabelsClearOfMaterial = false;
        BoreLeadersClearOfMaterial = false;
        var dpi = Math.Clamp(DeviceDpi / 96F, 1F, 1.5F) * SimulatedTextScale;
        using var font = new Font("Segoe UI", 12F * dpi, FontStyle.Regular, GraphicsUnit.Pixel);
        using var toleranceFont = new Font("Segoe UI", 9.5F * dpi, FontStyle.Regular, GraphicsUnit.Pixel);
        using var headingFont = new Font("Segoe UI Semibold", 13F * dpi, FontStyle.Regular, GraphicsUnit.Pixel);
        using var ink = new SolidBrush(Color.FromArgb(35, 39, 44));
        using var muted = new SolidBrush(Theme.Muted);
        g.DrawString("Продольное сечение", headingFont, ink, 17, 14);
        g.DrawString("Размеры в мм", toleranceFont, muted, 17, Height - toleranceFont.GetHeight(g) - 12);
        if (parameters is null)
        {
            var message = "Проверьте размеры для обновления чертежа.";
            var size = g.MeasureString(message, font);
            g.DrawString(message, font, muted, Math.Max(15, (Width - size.Width) / 2), Height / 2);
            return;
        }
        var p = parameters;
        var leftReserve = 100F;
        var rightReserve = 225F;
        var topReserve = 185F;
        var bottomReserve = 110F;
        var availableWidth = Math.Max(100, Width - leftReserve - rightReserve);
        var availableHeight = Math.Max(100, Height - topReserve - bottomReserve);
        var scale = Math.Min(availableWidth / (float)p.Length, availableHeight / (float)p.FlangeDiameter);
        var w = (float)p.Length * scale;
        var height = (float)p.FlangeDiameter * scale;
        var left = leftReserve + (availableWidth - w) / 2;
        var right = left + w;
        var cy = topReserve + availableHeight / 2;
        var xb = right - (float)p.FlangeWidth * scale;
        var xh = right - (float)p.CounterboreDepth * scale;
        var rd = (float)p.FlangeDiameter / 2 * scale;
        var rb = (float)p.BodyDiameter / 2 * scale;
        var ri = (float)p.BoreDiameter / 2 * scale;
        var rc = (float)p.CounterboreDiameter / 2 * scale;

        // Both rear and front faces belong to the same closed section polygon.
        PointF[] upper =
        [
            new(left, cy - ri), new(left, cy - rb), new(xb, cy - rb),
            new(xb, cy - rd), new(right, cy - rd), new(right, cy - rc),
            new(xh, cy - rc), new(xh, cy - ri)
        ];
        var lower = upper.Select(point => new PointF(point.X, 2 * cy - point.Y)).ToArray();
        using var hatch = new HatchBrush(HatchStyle.BackwardDiagonal, Color.FromArgb(167, 172, 177), Color.FromArgb(248, 249, 250));
        using var outline = new Pen(Color.FromArgb(35, 39, 44), 1.7F);
        g.FillPolygon(hatch, upper);
        g.FillPolygon(hatch, lower);
        g.DrawPolygon(outline, upper);
        g.DrawPolygon(outline, lower);
        // Explicitly draw the two rear walls as well: no open-ended DrawLines path.
        g.DrawLine(outline, upper[0], upper[1]);
        g.DrawLine(outline, lower[0], lower[1]);
        RearWallsClosed = upper[0].X == upper[1].X && lower[0].X == lower[1].X && rb > ri;

        using var center = new Pen(Color.FromArgb(124, 131, 139), .8F) { DashStyle = DashStyle.DashDot };
        g.DrawLine(center, left - 23, cy, right + 145, cy);
        using var dimension = new Pen(Color.FromArgb(85, 93, 103), .9F);
        using var cap = new AdjustableArrowCap(3.1F, 4.5F, true);
        using var arrows = new Pen(Color.FromArgb(64, 72, 82), .95F) { CustomStartCap = cap, CustomEndCap = cap };
        using var leader = new Pen(Color.FromArgb(64, 72, 82), .95F) { CustomStartCap = cap };

        // L: below the part; b and h: separate, fixed levels above the part.
        var lengthY = cy + rd + 66;
        g.DrawLine(dimension, left, cy + rb + 6, left, lengthY + 7);
        g.DrawLine(dimension, right, cy + rd + 6, right, lengthY + 7);
        g.DrawLine(arrows, left, lengthY, right, lengthY);
        Label(g, "L " + F(p.Length), p.LengthLowerDeviation, p.LengthUpperDeviation,
            (left + right) / 2, lengthY + 10, font, toleranceFont, ink, centered: true);

        var bY = cy - rd - 47;
        g.DrawLine(dimension, xb, cy - rd - 5, xb, bY - 6);
        g.DrawLine(dimension, right, cy - rd - 5, right, bY - 6);
        HorizontalDimension(g, dimension, arrows, cap, xb, right, bY);
        Label(g, "b " + F(p.FlangeWidth), 0, 0, (xb + right) / 2, bY - 34, font, toleranceFont, ink, centered: true);

        var hY = cy - rd - 103;
        g.DrawLine(dimension, xh, cy - rc - 5, xh, hY - 6);
        g.DrawLine(dimension, right, cy - rd - 5, right, hY - 6);
        HorizontalDimension(g, dimension, arrows, cap, xh, right, hY);
        Label(g, "h " + F(p.CounterboreDepth), p.CounterboreDepthLowerDeviation, p.CounterboreDepthUpperDeviation,
            (xh + right) / 2, hY - 36, font, toleranceFont, ink, centered: true);

        // Db is on the left, D on the far right; labels use the same tolerance notation.
        var dbX = left - 39;
        g.DrawLine(dimension, left - 5, cy - rb, dbX - 6, cy - rb);
        g.DrawLine(dimension, left - 5, cy + rb, dbX - 6, cy + rb);
        g.DrawLine(arrows, dbX, cy - rb, dbX, cy + rb);
        Label(g, "⌀" + F(p.BodyDiameter), 0, 0, left - 72, cy, font, toleranceFont, ink, rotated: true);

        var dX = right + 162;
        g.DrawLine(dimension, right + 5, cy - rd, dX + 6, cy - rd);
        g.DrawLine(dimension, right + 5, cy + rd, dX + 6, cy + rd);
        g.DrawLine(arrows, dX, cy - rd, dX, cy + rd);
        Label(g, "⌀" + F(p.FlangeDiameter), p.FlangeLowerDeviation, p.FlangeUpperDeviation,
            right + 194, cy, font, toleranceFont, ink, rotated: true);

        // Bore leaders travel inside the open bore before leaving the front face.
        var boreSpan = xh - left;
        var dPoint = new PointF(left + boreSpan * .45F, cy - ri);
        var dInsideY = cy - Math.Min(23, ri * .55F);
        var dElbow = new PointF(dPoint.X + Math.Min(22, boreSpan * .25F), dInsideY);
        var dExit = new PointF(right + 8, dInsideY);
        var dShelf = new PointF(right + 25, cy - 23);
        var dEnd = new PointF(right + 140, cy - 23);
        g.DrawLine(leader, dPoint, dElbow);
        g.DrawLines(dimension, [dElbow, dExit, dShelf, dEnd]);
        Label(g, "⌀" + F(p.BoreDiameter), p.BoreLowerDeviation, p.BoreUpperDeviation,
            right + 25, cy - 62, font, toleranceFont, ink);

        var d2Y = cy + Math.Max(43, rc * .52F);
        var narrowCounterbore = right - xh < 12;
        var d2Point = new PointF(narrowCounterbore ? right : (xh + right) / 2, cy + rc);
        var d2InsideY = cy + Math.Min(d2Y - cy, rc * .55F);
        var d2Elbow = new PointF(narrowCounterbore ? right + 8 : right - (right - xh) * .22F, d2InsideY);
        var d2Exit = new PointF(right + 8, d2InsideY);
        var d2Shelf = new PointF(right + 25, d2Y);
        var d2End = new PointF(right + 140, d2Y);
        g.DrawLine(leader, d2Point, d2Elbow);
        g.DrawLines(dimension, [d2Elbow, d2Exit, d2Shelf, d2End]);
        Label(g, "⌀" + F(p.CounterboreDiameter), p.CounterboreLowerDeviation, p.CounterboreUpperDeviation,
            right + 25, d2Y - 36, font, toleranceFont, ink);
        using var materialPath = new GraphicsPath();
        materialPath.AddPolygon(upper);
        materialPath.AddPolygon(lower);
        using var materialRegion = new Region(materialPath);
        LabelsClearOfMaterial = labels.All(rect => !materialRegion.IsVisible(rect));
        BoreLeadersClearOfMaterial = RouteClear(upper, lower, [dPoint, dElbow, dExit, dShelf, dEnd])
            && RouteClear(upper, lower, [d2Point, d2Elbow, d2Exit, d2Shelf, d2End]);
    }

    private static void HorizontalDimension(Graphics g, Pen line, Pen arrows, AdjustableArrowCap cap, float left, float right, float y)
    {
        if (right - left >= 12)
        {
            g.DrawLine(arrows, left, y, right, y);
            return;
        }
        g.DrawLine(line, left, y, right, y);
        using var fromLeft = new Pen(line.Color, .95F) { CustomEndCap = cap };
        using var fromRight = new Pen(line.Color, .95F) { CustomStartCap = cap };
        g.DrawLine(fromLeft, left - 18, y, left, y);
        g.DrawLine(fromRight, right, y, right + 18, y);
    }

    private static bool RouteClear(PointF[] upper, PointF[] lower, PointF[] route)
    {
        for (var segment = 1; segment < route.Length; segment++)
        {
            var a = route[segment - 1];
            var b = route[segment];
            var length = MathF.Sqrt((b.X - a.X) * (b.X - a.X) + (b.Y - a.Y) * (b.Y - a.Y));
            var samples = Math.Max(16, (int)Math.Ceiling(length));
            for (var i = 1; i < samples; i++)
            {
                var fraction = i / (float)samples;
                var point = new PointF(a.X + (b.X - a.X) * fraction, a.Y + (b.Y - a.Y) * fraction);
                if (Inside(upper, point) || Inside(lower, point)) return false;
            }
        }
        return true;
    }

    private static bool Inside(PointF[] polygon, PointF point)
    {
        // Analytic containment avoids GDI Region's raster rounding at an arrow's boundary tip.
        var inside = false;
        for (int i = 0, previous = polygon.Length - 1; i < polygon.Length; previous = i++)
        {
            var a = polygon[i];
            var b = polygon[previous];
            if ((a.Y > point.Y) != (b.Y > point.Y) &&
                point.X < (b.X - a.X) * (point.Y - a.Y) / (b.Y - a.Y) + a.X) inside = !inside;
        }
        return inside;
    }

    private static string F(double value) => value.ToString("0.###", CultureInfo.GetCultureInfo("ru-RU"));
    private static string Signed(double value) => value > 0 ? "+" + F(value) : "−" + F(-value);

    private void Label(Graphics g, string nominal, double lower, double upper, float x, float y,
        Font font, Font small, Brush ink, bool centered = false, bool rotated = false)
    {
        using var format = new StringFormat(StringFormat.GenericTypographic) { FormatFlags = StringFormatFlags.NoWrap };
        var nominalSize = g.MeasureString(nominal, font, PointF.Empty, format);
        var symmetric = lower < 0 && Math.Abs(upper + lower) < .000001;
        var upperText = symmetric ? "±" + F(upper) : upper == 0 ? "" : Signed(upper);
        var lowerText = symmetric || lower == 0 ? "" : Signed(lower);
        var toleranceWidth = Math.Max(g.MeasureString(upperText, small, PointF.Empty, format).Width, g.MeasureString(lowerText, small, PointF.Empty, format).Width);
        var width = nominalSize.Width + (toleranceWidth > 0 ? toleranceWidth + 5 : 0);
        var height = Math.Max(nominalSize.Height, symmetric ? small.GetHeight(g) : small.GetHeight(g) * 1.65F);
        var save = g.Save();
        if (rotated)
        {
            labels.Add(new RectangleF(x - height / 2, y - width / 2, height, width));
            g.TranslateTransform(x, y);
            g.RotateTransform(-90);
            x = -width / 2;
            y = -height / 2;
        }
        else
        {
            if (centered) x -= width / 2;
            labels.Add(new RectangleF(x, y, width, height));
        }
        g.FillRectangle(Brushes.White, x - 2, y - 1, width + 4, height + 2);
        g.DrawString(nominal, font, ink, x, y + (height - nominalSize.Height) / 2, format);
        var tx = x + nominalSize.Width + 5;
        if (symmetric) g.DrawString(upperText, small, ink, tx, y + (height - small.GetHeight(g)) / 2, format);
        else
        {
            if (upperText.Length > 0) g.DrawString(upperText, small, ink, tx, y - 1, format);
            if (lowerText.Length > 0) g.DrawString(lowerText, small, ink, tx, y + height - small.GetHeight(g) + 1, format);
        }
        g.Restore(save);
    }
}
