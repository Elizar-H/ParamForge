using System.ComponentModel;
using System.Drawing.Drawing2D;
using System.Globalization;
using Kontur.Core;

namespace Kontur.Desktop;

internal sealed class SketchCanvas : Control
{
    private Parameters? parameters;
    private readonly Dictionary<string, RectangleF> labels = [];
    private readonly Dictionary<string, RectangleF> hitAreas = [];
    private readonly List<RectangleF> labelMasks = [];
    private readonly Dictionary<string, string> texts = [];
    private string? hovered;
    internal event Action<string>? DimensionClicked;
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal Parameters? Parameters { get => parameters; set { parameters = value; hovered = null; Cursor = Cursors.Default; Invalidate(); } }
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal float SimulatedTextScale { get; set; } = 1;
    internal bool LabelsInside => labels.Values.All(r => r.Left >= 0 && r.Top >= 0 && r.Right <= Width && r.Bottom <= Height);
    internal bool LabelsOverlap => labels.Values.SelectMany((a, i) => labels.Values.Skip(i + 1).Select(b => RectangleF.Intersect(a, b))).Any(r => r.Width > 1 && r.Height > 1);
    internal bool RearWallsClosed { get; private set; }
    internal bool LabelsClearOfMaterial { get; private set; }
    internal bool BoreDimensionsClearOfMaterial { get; private set; }
    internal int DimensionCount => labels.Count;
    internal bool LabelsClearOfRearEdges { get; private set; }
    internal string DimensionText(string key) => texts.GetValueOrDefault(key, "");
    internal RectangleF DimensionHitBounds(string key) => hitAreas.GetValueOrDefault(key);
    internal string? HitDimension(Point point) => hitAreas.Where(pair => pair.Value.Contains(point))
        .OrderBy(pair => Math.Pow(point.X - (pair.Value.Left + pair.Value.Right) / 2, 2) + Math.Pow(point.Y - (pair.Value.Top + pair.Value.Bottom) / 2, 2))
        .Select(pair => pair.Key).FirstOrDefault();

    internal SketchCanvas()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        BackColor = Color.White;
        AccessibleName = "Продольный разрез детали";
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var key = HitDimension(e.Location);
        if (key == hovered) return;
        hovered = key;
        Cursor = key is null ? Cursors.Default : Cursors.Hand;
        Invalidate();
    }
    protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); hovered = null; Cursor = Cursors.Default; Invalidate(); }
    protected override void OnMouseClick(MouseEventArgs e)
    {
        base.OnMouseClick(e);
        if (e.Button == MouseButtons.Left && HitDimension(e.Location) is { } key) DimensionClicked?.Invoke(key);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
        labels.Clear(); hitAreas.Clear(); labelMasks.Clear(); texts.Clear();
        RearWallsClosed = LabelsClearOfMaterial = BoreDimensionsClearOfMaterial = LabelsClearOfRearEdges = false;
        var u = Math.Clamp(DeviceDpi / 96F, 1F, 2F) * SimulatedTextScale;
        using var font = new Font("Segoe UI", 13F * u, FontStyle.Regular, GraphicsUnit.Pixel);
        using var small = new Font("Segoe UI", 11F * u, FontStyle.Regular, GraphicsUnit.Pixel);
        using var heading = new Font("Segoe UI Semibold", 13F * u, FontStyle.Regular, GraphicsUnit.Pixel);
        using var ink = new SolidBrush(Theme.Ink);
        using var muted = new SolidBrush(Theme.Muted);
        g.DrawString("Продольный разрез", heading, ink, 17, 14);
        g.DrawString("Размеры в мм", small, muted, 17, Height - small.GetHeight(g) - 12);
        if (parameters is null)
        {
            const string message = "Проверьте размеры для построения чертежа.";
            var size = g.MeasureString(message, font);
            g.DrawString(message, font, muted, Math.Max(15, (Width - size.Width) / 2), Height / 2);
            return;
        }
        var p = parameters;
        var leftReserve = 75F * u;
        var rightReserve = 75F * u;
        var topReserve = 124F * u;
        var bottomReserve = 82F * u;
        float scale = 1, availableWidth = 1, availableHeight = 1;
        bool dOutside = false, d2Outside = false;
        for (var pass = 0; pass < 3; pass++)
        {
            availableWidth = Math.Max(60, Width - leftReserve - rightReserve);
            availableHeight = Math.Max(60, Height - topReserve - bottomReserve);
            scale = Math.Min(availableWidth / (float)p.Length, availableHeight / (float)p.FlangeDiameter);
            // A vertical dimension and its rotated number must fit in the white opening.
            var textHeight = font.GetHeight(g);
            dOutside = (p.Length - p.CounterboreDepth) * scale < textHeight / 2 + 34 * u || p.BoreDiameter * scale < g.MeasureString("⌀" + F(p.BoreDiameter), font).Width + 10 * u;
            d2Outside = p.CounterboreDepth * scale < textHeight / 2 + 34 * u || p.CounterboreDiameter * scale < g.MeasureString("⌀" + F(p.CounterboreDiameter), font).Width + 10 * u;
            var wantedReserve = (75 + 56 * ((dOutside ? 1 : 0) + (d2Outside ? 1 : 0))) * u;
            if (wantedReserve <= rightReserve) break;
            rightReserve = wantedReserve;
        }
        var width = (float)p.Length * scale;
        var left = leftReserve + (availableWidth - width) / 2;
        var right = left + width;
        var cy = topReserve + availableHeight / 2;
        var xb = right - (float)p.FlangeWidth * scale;
        var xh = right - (float)p.CounterboreDepth * scale;
        var rd = (float)p.FlangeDiameter / 2 * scale;
        var rb = (float)p.BodyDiameter / 2 * scale;
        var ri = (float)p.BoreDiameter / 2 * scale;
        var rc = (float)p.CounterboreDiameter / 2 * scale;
        PointF[] upper =
        [
            new(left, cy - ri), new(left, cy - rb), new(xb, cy - rb),
            new(xb, cy - rd), new(right, cy - rd), new(right, cy - rc),
            new(xh, cy - rc), new(xh, cy - ri)
        ];
        var lower = upper.Select(point => new PointF(point.X, 2 * cy - point.Y)).ToArray();
        using var hatch = new HatchBrush(HatchStyle.BackwardDiagonal, Color.FromArgb(167, 172, 177), Color.FromArgb(249, 250, 251));
        using var outline = new Pen(Theme.Ink, 1.7F * u);
        g.FillPolygon(hatch, upper); g.FillPolygon(hatch, lower);
        g.DrawPolygon(outline, upper); g.DrawPolygon(outline, lower);
        using var center = new Pen(Color.FromArgb(124, 131, 139), .7F * u) { DashStyle = DashStyle.DashDot };
        g.DrawLine(center, left - 20 * u, cy, right + 20 * u, cy);
        // A full longitudinal cut also shows the rear semicircular edges projected
        // onto the cutting plane. The space between these edges remains a hole.
        g.DrawLine(outline, left, cy - ri, left, cy + ri);
        g.DrawLine(outline, xh, cy - ri, xh, cy + ri);
        g.DrawLine(outline, right, cy - rc, right, cy + rc);
        RearWallsClosed = left < xh && xh < right && rb > ri && rd > rc;

        using var thin = new Pen(Color.FromArgb(85, 93, 103), .75F * u);
        using var cap = new AdjustableArrowCap(2.6F * u, 4F * u, true);
        using var arrows = new Pen(Color.FromArgb(64, 72, 82), .85F * u) { CustomStartCap = cap, CustomEndCap = cap };
        var lengthY = cy + rd + 48 * u;
        g.DrawLine(thin, left, cy + rb + 5 * u, left, lengthY + 6 * u);
        g.DrawLine(thin, right, cy + rd + 5 * u, right, lengthY + 6 * u);
        Horizontal(g, thin, arrows, cap, left, right, lengthY, u);
        Label(g, "length", F(p.Length), (left + right) / 2, lengthY - font.GetHeight(g) - 4 * u, font, ink, u);

        var bY = cy - rd - 32 * u;
        g.DrawLine(thin, xb, cy - rd - 4 * u, xb, bY - 6 * u);
        g.DrawLine(thin, right, cy - rd - 4 * u, right, bY - 6 * u);
        Horizontal(g, thin, arrows, cap, xb, right, bY, u);
        Label(g, "flangeWidth", F(p.FlangeWidth), (xb + right) / 2, bY - font.GetHeight(g) - 4 * u, font, ink, u);
        var hY = cy - rd - 78 * u;
        g.DrawLine(thin, xh, cy - rc - 4 * u, xh, hY - 6 * u);
        g.DrawLine(thin, right, cy - rd - 4 * u, right, hY - 6 * u);
        Horizontal(g, thin, arrows, cap, xh, right, hY, u);
        Label(g, "counterboreDepth", F(p.CounterboreDepth), (xh + right) / 2, hY - font.GetHeight(g) - 4 * u, font, ink, u);

        var dbX = left - 36 * u;
        g.DrawLine(thin, left - 4 * u, cy - rb, dbX - 5 * u, cy - rb);
        g.DrawLine(thin, left - 4 * u, cy + rb, dbX - 5 * u, cy + rb);
        Vertical(g, thin, arrows, cap, dbX, cy - rb, cy + rb, u);
        Label(g, "bodyDiameter", "⌀" + F(p.BodyDiameter), dbX - 14 * u, cy, font, ink, u, true);

        var outsideColumn = 0;
        var boreRoutes = new List<PointF[]>();
        void Bore(string key, double value, float radius, float fromX, float untilX, bool outside)
        {
            float x;
            if (outside)
            {
                x = right + (40 + 56 * outsideColumn++) * u;
                g.DrawLine(thin, untilX, cy - radius, x + 5 * u, cy - radius);
                g.DrawLine(thin, untilX, cy + radius, x + 5 * u, cy + radius);
                boreRoutes.Add([new(untilX, cy - radius), new(x, cy - radius), new(x, cy + radius), new(untilX, cy + radius)]);
            }
            else
            {
                x = Math.Clamp(fromX + (untilX - fromX) * .56F, fromX + font.GetHeight(g) / 2 + 24 * u, untilX - 10 * u);
                boreRoutes.Add([new(x, cy - radius), new(x, cy + radius)]);
            }
            Vertical(g, thin, arrows, cap, x, cy - radius, cy + radius, u);
            Label(g, key, "⌀" + F(value), x - 14 * u, cy, font, ink, u, true);
        }
        Bore("boreDiameter", p.BoreDiameter, ri, left, xh, dOutside);
        Bore("counterboreDiameter", p.CounterboreDiameter, rc, xh, right, d2Outside);
        var outerX = right + (40 + 56 * outsideColumn) * u;
        g.DrawLine(thin, right + 4 * u, cy - rd, outerX + 5 * u, cy - rd);
        g.DrawLine(thin, right + 4 * u, cy + rd, outerX + 5 * u, cy + rd);
        Vertical(g, thin, arrows, cap, outerX, cy - rd, cy + rd, u);
        Label(g, "flangeDiameter", "⌀" + F(p.FlangeDiameter), outerX - 14 * u, cy, font, ink, u, true);
        using var materialPath = new GraphicsPath(); materialPath.AddPolygon(upper); materialPath.AddPolygon(lower);
        using var materialRegion = new Region(materialPath);
        LabelsClearOfMaterial = labels.Values.All(rect => !materialRegion.IsVisible(rect));
        BoreDimensionsClearOfMaterial = boreRoutes.All(route => RouteClear(upper, lower, route));
        LabelsClearOfRearEdges = labelMasks.All(rect => !IntersectsVertical(rect, left, cy - ri, cy + ri)
            && !IntersectsVertical(rect, xh, cy - rc, cy + rc) && !IntersectsVertical(rect, right, cy - rc, cy + rc));
    }

    private static void Horizontal(Graphics g, Pen thin, Pen arrows, AdjustableArrowCap cap, float left, float right, float y, float u)
    {
        if (right - left >= 14 * u) { g.DrawLine(arrows, left, y, right, y); return; }
        g.DrawLine(thin, left, y, right, y);
        using var fromLeft = new Pen(thin.Color, .85F * u) { CustomEndCap = cap };
        using var fromRight = new Pen(thin.Color, .85F * u) { CustomStartCap = cap };
        g.DrawLine(fromLeft, left - 17 * u, y, left, y);
        g.DrawLine(fromRight, right, y, right + 17 * u, y);
    }
    private static void Vertical(Graphics g, Pen thin, Pen arrows, AdjustableArrowCap cap, float x, float top, float bottom, float u)
    {
        if (bottom - top >= 14 * u) { g.DrawLine(arrows, x, top, x, bottom); return; }
        g.DrawLine(thin, x, top, x, bottom);
        using var above = new Pen(thin.Color, .85F * u) { CustomEndCap = cap };
        using var below = new Pen(thin.Color, .85F * u) { CustomStartCap = cap };
        g.DrawLine(above, x, top - 17 * u, x, top);
        g.DrawLine(below, x, bottom, x, bottom + 17 * u);
    }
    private void Label(Graphics g, string key, string text, float x, float y, Font font, Brush ink, float u, bool rotated = false)
    {
        using var format = new StringFormat(StringFormat.GenericTypographic) { FormatFlags = StringFormatFlags.NoWrap };
        var size = g.MeasureString(text, font, PointF.Empty, format);
        RectangleF bounds;
        var saved = g.Save();
        if (rotated)
        {
            bounds = new RectangleF(x - size.Height / 2, y - size.Width / 2, size.Height, size.Width);
            g.TranslateTransform(x, y); g.RotateTransform(-90);
            x = -size.Width / 2; y = -size.Height / 2;
        }
        else { x -= size.Width / 2; bounds = new RectangleF(x, y, size.Width, size.Height); }
        labels[key] = bounds; texts[key] = text;
        var mask = bounds; mask.Inflate((rotated ? 2 : 3) * u, (rotated ? 3 : 2) * u); labelMasks.Add(mask);
        var hit = bounds; hit.Inflate(8 * u, 8 * u); hitAreas[key] = hit;
        using var background = new SolidBrush(key == hovered ? Color.FromArgb(228, 238, 248) : Color.White);
        g.FillRectangle(background, x - 3 * u, y - 2 * u, size.Width + 6 * u, size.Height + 4 * u);
        g.DrawString(text, font, ink, x, y, format);
        g.Restore(saved);
    }
    private static bool IntersectsVertical(RectangleF rect, float x, float top, float bottom) => rect.Left <= x && rect.Right >= x && rect.Bottom >= top && rect.Top <= bottom;
    private static string F(double value) => value.ToString("0.###", CultureInfo.GetCultureInfo("ru-RU"));
    private static bool RouteClear(PointF[] upper, PointF[] lower, PointF[] route)
    {
        for (var segment = 1; segment < route.Length; segment++)
        {
            var a = route[segment - 1]; var b = route[segment];
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
        var inside = false;
        for (int i = 0, previous = polygon.Length - 1; i < polygon.Length; previous = i++)
        {
            var a = polygon[i]; var b = polygon[previous];
            if ((a.Y > point.Y) != (b.Y > point.Y) && point.X < (b.X - a.X) * (point.Y - a.Y) / (b.Y - a.Y) + a.X) inside = !inside;
        }
        return inside;
    }
}
