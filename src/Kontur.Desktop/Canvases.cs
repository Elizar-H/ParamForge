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
    private readonly List<(PointF A, PointF B)> dimensionLines = [];
    private readonly Dictionary<string, string> texts = [];
    private string? hovered;
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal bool ShowAxes { get; set; } = true;
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal bool ShowTolerances { get; set; }
    internal float HatchStepDips { get; private set; }
    internal PointF WorkOrigin { get; private set; }

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
    internal float DrawingScale { get; private set; }
    internal float CornerRadiusPixels { get; private set; }
    internal int CornerArcCount { get; private set; }
    internal bool LabelsClearOfRearEdges { get; private set; }
    internal bool LabelsClearOfDimensionLines => labelMasks.All(r => dimensionLines.All(line => !Crosses(r, line.A, line.B)));
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
        labels.Clear(); hitAreas.Clear(); labelMasks.Clear(); texts.Clear(); dimensionLines.Clear();
        RearWallsClosed = LabelsClearOfMaterial = BoreDimensionsClearOfMaterial = LabelsClearOfRearEdges = false;
        DrawingScale = CornerRadiusPixels = 0; CornerArcCount = 0;
        var u = Math.Clamp(DeviceDpi / 96F, 1F, 2F) * SimulatedTextScale;
        using var font = new Font("Segoe UI", 13F * u, FontStyle.Regular, GraphicsUnit.Pixel);
        using var small = new Font("Segoe UI", 11F * u, FontStyle.Regular, GraphicsUnit.Pixel);
        using var heading = new Font("Segoe UI Semibold", 13F * u, FontStyle.Regular, GraphicsUnit.Pixel);
        using var ink = new SolidBrush(Theme.Ink);
        using var muted = new SolidBrush(Theme.Muted);
        g.DrawString("Продольный разрез", heading, ink, 17, 14);
        if (parameters is null)
        {
            const string message = "Проверьте размеры для построения чертежа.";
            var size = g.MeasureString(message, font);
            g.DrawString(message, font, muted, Math.Max(15, (Width - size.Width) / 2), Height / 2);
            return;
        }
        var p = parameters;
        if (ShowAxes) WorkCoordinates.Legend(g, new(53 * u, 78 * u), u);
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
            var textHeight = Math.Max(CaptionSize(g, "boreDiameter", "⌀" + F(p.BoreDiameter), font).Height,
                CaptionSize(g, "counterboreDiameter", "⌀" + F(p.CounterboreDiameter), font).Height);
            dOutside = (p.Length - p.CounterboreDepth) * scale < textHeight + 24 * u || p.BoreDiameter * scale < CaptionSize(g, "boreDiameter", "⌀" + F(p.BoreDiameter), font).Width + 10 * u;
            d2Outside = p.CounterboreDepth * scale < textHeight + 24 * u || p.CounterboreDiameter * scale < CaptionSize(g, "counterboreDiameter", "⌀" + F(p.CounterboreDiameter), font).Width + 10 * u;
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
        DrawingScale = scale;
        var radius = (float)ToolingProfile.CornerRadius * scale;
        CornerRadiusPixels = radius >= 1 ? radius : 0;
        using var upperPath = new GraphicsPath();
        if (CornerRadiusPixels == 0) upperPath.AddPolygon(upper);
        else
        {
            // Concave roots left by the insert nose; radius is drawn at the
            // nominal scale, never magnified to make it look more prominent.
            upperPath.StartFigure();
            upperPath.AddLine(left, cy - ri, left, cy - rb);
            upperPath.AddLine(left, cy - rb, xb - radius, cy - rb);
            upperPath.AddArc(xb - 2 * radius, cy - rb - 2 * radius, 2 * radius, 2 * radius, 90, -90);
            upperPath.AddLine(xb, cy - rb - radius, xb, cy - rd);
            upperPath.AddLine(xb, cy - rd, right, cy - rd);
            upperPath.AddLine(right, cy - rd, right, cy - rc);
            upperPath.AddLine(right, cy - rc, xh + radius, cy - rc);
            upperPath.AddArc(xh, cy - rc, 2 * radius, 2 * radius, 270, -90);
            upperPath.AddLine(xh, cy - rc + radius, xh, cy - ri);
            upperPath.AddLine(xh, cy - ri, left, cy - ri);
            upperPath.CloseFigure();
        }
        using var lowerPath = (GraphicsPath)upperPath.Clone();
        using var reflection = new Matrix(1, 0, 0, -1, 0, 2 * cy);
        lowerPath.Transform(reflection);
        CornerArcCount = upperPath.PathTypes.Count(type => (type & (byte)PathPointType.PathTypeMask) == (byte)PathPointType.Bezier3) / 3 * 2;
        using var materialFill = new SolidBrush(Color.FromArgb(249, 250, 251));
        g.FillPath(materialFill, upperPath); g.FillPath(materialFill, lowerPath);
        using (var hatchPath = new GraphicsPath())
        {
            hatchPath.AddPath(upperPath, false); hatchPath.AddPath(lowerPath, false);
            var bounds = hatchPath.GetBounds();
            HatchStepDips = Math.Clamp(MathF.Sqrt(bounds.Width * bounds.Height / (u * u)) / 28F, 8F, 18F);
            var step = HatchStepDips * u * MathF.Sqrt(2);
            var savedHatch = g.Save(); g.SetClip(hatchPath, CombineMode.Intersect);
            using var hatchPen = new Pen(Color.FromArgb(150, 157, 164), .65F * u);
            for (var x = bounds.Left - bounds.Height; x < bounds.Right; x += step)
                g.DrawLine(hatchPen, x, bounds.Bottom, x + bounds.Height, bounds.Top);
            g.Restore(savedHatch);
        }
        using var outline = new Pen(Theme.Ink, 1.7F * u);
        g.DrawPath(outline, upperPath); g.DrawPath(outline, lowerPath);
        using var center = new Pen(Color.FromArgb(124, 131, 139), .7F * u) { DashStyle = DashStyle.DashDot };
        g.DrawLine(center, left - 20 * u, cy, right + 20 * u, cy);
        // A full longitudinal cut also shows the rear semicircular edges projected
        // onto the cutting plane. The space between these edges remains a hole.
        g.DrawLine(outline, left, cy - ri, left, cy + ri);
        g.DrawLine(outline, xh, cy - rc, xh, cy + rc);
        g.DrawLine(outline, right, cy - rc, right, cy + rc);
        WorkOrigin = new(right, cy);
        RearWallsClosed = left < xh && xh < right && rb > ri && rd > rc;

        using var thin = new Pen(Color.FromArgb(85, 93, 103), .75F * u);
        using var cap = new AdjustableArrowCap(2.6F * u, 4F * u, true);
        using var arrows = new Pen(Color.FromArgb(64, 72, 82), .85F * u) { CustomStartCap = cap, CustomEndCap = cap };
        var lengthY = cy + rd + 48 * u;
        DimensionLine(g, thin, left, cy + rb + 5 * u, left, lengthY + 6 * u);
        DimensionLine(g, thin, right, cy + rd + 5 * u, right, lengthY + 6 * u);
        Horizontal(g, thin, arrows, cap, left, right, lengthY, u);
        HorizontalNumber("length", F(p.Length), left, right, lengthY, [left, right]);

        var bY = cy - rd - 32 * u;
        DimensionLine(g, thin, xb, cy - rd - 4 * u, xb, bY - 6 * u);
        DimensionLine(g, thin, right, cy - rd - 4 * u, right, bY - 6 * u);
        Horizontal(g, thin, arrows, cap, xb, right, bY, u);
        HorizontalNumber("flangeWidth", F(p.FlangeWidth), xb, right, bY, [xh, xb, right]);
        var hY = cy - rd - 78 * u;
        DimensionLine(g, thin, xh, cy - rc - 4 * u, xh, hY - 6 * u);
        DimensionLine(g, thin, right, cy - rd - 4 * u, right, hY - 6 * u);
        Horizontal(g, thin, arrows, cap, xh, right, hY, u);
        HorizontalNumber("counterboreDepth", F(p.CounterboreDepth), xh, right, hY, []);

        void HorizontalNumber(string key, string nominal, float start, float end, float y, float[] obstacles)
        {
            var size = CaptionSize(g, key, nominal, font); var preferred = (start + end) / 2;
            var candidates = new List<float> { preferred };
            foreach (var edge in obstacles) { candidates.Add(edge - size.Width / 2 - 8 * u); candidates.Add(edge + size.Width / 2 + 8 * u); }
            var x = candidates.OrderBy(c => Math.Abs(c - preferred)).FirstOrDefault(c =>
                c - size.Width / 2 >= 8 * u && c + size.Width / 2 <= Width - 8 * u &&
                obstacles.All(edge => edge < c - size.Width / 2 - 6 * u || edge > c + size.Width / 2 + 6 * u), preferred);
            if (x < start || x > end) DimensionLine(g, thin, Math.Clamp(x, start, end), y, x, y);
            Label(g, key, nominal, x, y - font.GetHeight(g) - 10 * u, font, ink, u);
        }

        var dbX = left - 36 * u;
        DimensionLine(g, thin, left - 4 * u, cy - rb, dbX - 5 * u, cy - rb);
        DimensionLine(g, thin, left - 4 * u, cy + rb, dbX - 5 * u, cy + rb);
        Vertical(g, thin, arrows, cap, dbX, cy - rb, cy + rb, u);
        Label(g, "bodyDiameter", "⌀" + F(p.BodyDiameter), dbX - CaptionSize(g, "bodyDiameter", "⌀" + F(p.BodyDiameter), font).Height / 2 - 7 * u, cy, font, ink, u, true);

        var outsideColumn = 0;
        var boreRoutes = new List<PointF[]>();
        void Bore(string key, double value, float radius, float fromX, float untilX, bool outside)
        {
            float x;
            if (outside)
            {
                x = right + (40 + 56 * outsideColumn++) * u;
                DimensionLine(g, thin, untilX, cy - radius, x + 5 * u, cy - radius);
                DimensionLine(g, thin, untilX, cy + radius, x + 5 * u, cy + radius);
                boreRoutes.Add([new(untilX, cy - radius), new(x, cy - radius), new(x, cy + radius), new(untilX, cy + radius)]);
            }
            else
            {
                x = Math.Clamp(fromX + (untilX - fromX) * .56F,
                    fromX + CaptionSize(g, key, "⌀" + F(value), font).Height + 12 * u, untilX - 10 * u);
                boreRoutes.Add([new(x, cy - radius), new(x, cy + radius)]);
            }
            Vertical(g, thin, arrows, cap, x, cy - radius, cy + radius, u);
            Label(g, key, "⌀" + F(value), x - CaptionSize(g, key, "⌀" + F(value), font).Height / 2 - 7 * u, cy, font, ink, u, true);
        }
        Bore("boreDiameter", p.BoreDiameter, ri, left, xh, dOutside);
        Bore("counterboreDiameter", p.CounterboreDiameter, rc, xh, right, d2Outside);
        var outerX = right + (40 + 56 * outsideColumn) * u;
        DimensionLine(g, thin, right + 4 * u, cy - rd, outerX + 5 * u, cy - rd);
        DimensionLine(g, thin, right + 4 * u, cy + rd, outerX + 5 * u, cy + rd);
        Vertical(g, thin, arrows, cap, outerX, cy - rd, cy + rd, u);
        Label(g, "flangeDiameter", "⌀" + F(p.FlangeDiameter), outerX - CaptionSize(g, "flangeDiameter", "⌀" + F(p.FlangeDiameter), font).Height / 2 - 7 * u, cy, font, ink, u, true);
        using var materialPath = new GraphicsPath(); materialPath.AddPath(upperPath, false); materialPath.AddPath(lowerPath, false);
        using var materialRegion = new Region(materialPath);
        LabelsClearOfMaterial = labels.Values.All(rect => !materialRegion.IsVisible(rect));
        BoreDimensionsClearOfMaterial = boreRoutes.All(route => RouteClear(upper, lower, route));
        if (ShowAxes) WorkCoordinates.Origin(g, WorkOrigin, u);
        LabelsClearOfRearEdges = labelMasks.All(rect => !IntersectsVertical(rect, left, cy - ri, cy + ri)
            && !IntersectsVertical(rect, xh, cy - rc, cy + rc) && !IntersectsVertical(rect, right, cy - rc, cy + rc));
    }

    private void Horizontal(Graphics g, Pen thin, Pen arrows, AdjustableArrowCap cap, float left, float right, float y, float u)
    {
        if (right - left >= 14 * u) { DimensionLine(g, arrows, left, y, right, y); return; }
        DimensionLine(g, thin, left, y, right, y);
        using var fromLeft = new Pen(thin.Color, .85F * u) { CustomEndCap = cap };
        using var fromRight = new Pen(thin.Color, .85F * u) { CustomStartCap = cap };
        DimensionLine(g, fromLeft, left - 17 * u, y, left, y);
        DimensionLine(g, fromRight, right, y, right + 17 * u, y);
    }
    private void Vertical(Graphics g, Pen thin, Pen arrows, AdjustableArrowCap cap, float x, float top, float bottom, float u)
    {
        if (bottom - top >= 14 * u) { DimensionLine(g, arrows, x, top, x, bottom); return; }
        DimensionLine(g, thin, x, top, x, bottom);
        using var above = new Pen(thin.Color, .85F * u) { CustomEndCap = cap };
        using var below = new Pen(thin.Color, .85F * u) { CustomStartCap = cap };
        DimensionLine(g, above, x, top - 17 * u, x, top);
        DimensionLine(g, below, x, bottom, x, bottom + 17 * u);
    }
    private void Label(Graphics g, string key, string text, float x, float y, Font font, Brush ink, float u, bool rotated = false)
    {
        using var format = new StringFormat(StringFormat.GenericTypographic) { FormatFlags = StringFormatFlags.NoWrap };
        var caption = Caption(key, text);
        var size = CaptionSize(g, key, text, font);
        using var deviationFont = new Font(font.FontFamily, font.Size * .72F, FontStyle.Regular, GraphicsUnit.Pixel);
        var mainSize = g.MeasureString(caption.Text, font, PointF.Empty, format);
        RectangleF bounds;
        var saved = g.Save();
        if (rotated)
        {
            bounds = new RectangleF(x - size.Height / 2, y - size.Width / 2, size.Height, size.Width);
            g.TranslateTransform(x, y); g.RotateTransform(-90);
            x = -size.Width / 2; y = -size.Height / 2;
        }
        else { y -= Math.Max(0, size.Height - font.GetHeight(g)); x -= size.Width / 2; bounds = new RectangleF(x, y, size.Width, size.Height); }
        labels[key] = bounds; texts[key] = caption.Text + (caption.Upper.Length + caption.Lower.Length == 0 ? "" : " " + caption.Upper + " / " + caption.Lower);
        var mask = bounds; mask.Inflate((rotated ? 2 : 3) * u, (rotated ? 3 : 2) * u); labelMasks.Add(mask);
        var hit = bounds; hit.Inflate(8 * u, 8 * u); hitAreas[key] = hit;
        using var background = new SolidBrush(key == hovered ? Color.FromArgb(228, 238, 248) : Color.White);
        g.FillRectangle(background, x - 3 * u, y - 2 * u, size.Width + 6 * u, size.Height + 4 * u);
        g.DrawString(caption.Text, font, ink, x, y + (size.Height - mainSize.Height) / 2, format);
        if (caption.Upper.Length != 0) g.DrawString(caption.Upper, deviationFont, ink, x + mainSize.Width + 2 * u, y, format);
        if (caption.Lower.Length != 0) g.DrawString(caption.Lower, deviationFont, ink, x + mainSize.Width + 2 * u, y + size.Height - deviationFont.GetHeight(g), format);
        g.Restore(saved);
    }
    private (string Text, string Upper, string Lower) Caption(string key, string nominal)
    {
        if (!ShowTolerances || parameters is null) return (nominal, "", "");
        var dimension = Tolerances.Dimensions.SingleOrDefault(d => d.Key == key);
        double lower, upper;
        if (dimension is null)
        {
            var tolerance = ToolingProfile.GeneralLinearTolerance(key == "bodyDiameter" ? parameters.BodyDiameter : parameters.FlangeWidth);
            lower = -tolerance; upper = tolerance;
        }
        else
        {
            var designation = Tolerances.Designation(parameters, dimension);
            if (designation.Length != 0 && !designation.StartsWith("IT", StringComparison.Ordinal)) return (nominal + " " + designation, "", "");
            lower = Tolerances.Number(parameters, dimension.LowerProperty); upper = Tolerances.Number(parameters, dimension.UpperProperty);
        }
        if (lower == -upper) return (nominal + " ±" + Tolerances.Format(upper), "", "");
        return (nominal, upper == 0 ? "" : Tolerances.Signed(upper), lower == 0 ? "" : Tolerances.Signed(lower));
    }
    private SizeF CaptionSize(Graphics g, string key, string nominal, Font font)
    {
        var caption = Caption(key, nominal);
        using var format = new StringFormat(StringFormat.GenericTypographic) { FormatFlags = StringFormatFlags.NoWrap };
        var size = g.MeasureString(caption.Text, font, PointF.Empty, format);
        if (caption.Upper.Length == 0 && caption.Lower.Length == 0) return size;
        using var small = new Font(font.FontFamily, font.Size * .72F, FontStyle.Regular, GraphicsUnit.Pixel);
        var width = Math.Max(g.MeasureString(caption.Upper, small, PointF.Empty, format).Width, g.MeasureString(caption.Lower, small, PointF.Empty, format).Width);
        return new(size.Width + width + 2 * font.Size / 13, Math.Max(size.Height, small.GetHeight(g) * 2));
    }

    private void DimensionLine(Graphics g, Pen pen, float x1, float y1, float x2, float y2)
    {
        dimensionLines.Add((new(x1, y1), new(x2, y2))); g.DrawLine(pen, x1, y1, x2, y2);
    }
    private static bool Crosses(RectangleF r, PointF a, PointF b) =>
        a.X == b.X ? r.Left <= a.X && r.Right >= a.X && r.Top <= Math.Max(a.Y,b.Y) && r.Bottom >= Math.Min(a.Y,b.Y)
        : a.Y == b.Y && r.Top <= a.Y && r.Bottom >= a.Y && r.Left <= Math.Max(a.X,b.X) && r.Right >= Math.Min(a.X,b.X);

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
