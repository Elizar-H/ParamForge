using System.ComponentModel;
using System.Globalization;
using Kontur.Core;

namespace Kontur.Desktop;

internal sealed partial class SketchCanvas : Control
{
    private Parameters? parameters;
    private readonly Dictionary<string, RectangleF> labels = [];
    private readonly Dictionary<string, RectangleF> hitAreas = [];
    private readonly List<RectangleF> labelMasks = [];
    private readonly List<(PointF A, PointF B)> dimensionLines = [];
    private readonly Dictionary<string, string> texts = [];
    private string? hovered;
    private bool hoverLine;
    private readonly Dictionary<string, (PointF A, PointF B)> dragHandles = [];
    private DragState? drag;
    private bool suppressClick;
    // Synthetic input in the smoke runner must not inherit keys held in other apps.
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal Keys? SimulatedModifiers
    {
        get; set;
    }
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal bool SyntheticInput
    {
        get; set;
    }
    private Keys InputModifiers => SimulatedModifiers ?? ModifierKeys;
    private sealed record DragState(string Key, Point Start, double Nominal, float Scale, PointF Origin, float Direction, bool FromContour = false)
    {
        internal bool Active;
    }
    internal event Func<string, bool>? DimensionDragStarted;
    internal event Func<string, double, bool>? DimensionDragPreview;
    internal event Action<bool>? DimensionDragFinished;
    internal PointF DragHandle(string key) => dragHandles[key].A;
    internal bool IsDragging => drag?.Active == true;
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal bool ShowAxes { get; set; } = true;
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal bool ShowTolerances
    {
        get; set;
    }
    internal float HatchStepDips
    {
        get; private set;
    }
    internal PointF WorkOrigin
    {
        get; private set;
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal Parameters? Parameters
    {
        get => parameters; set
        {
            parameters = value;
            if (drag is null)
            {
                hovered = null;
                Cursor = Cursors.Default;
            }
            Invalidate();
        }
    }
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal float SimulatedTextScale { get; set; } = 1;
    internal bool LabelsInside => labels.Values.All(r => r.Left >= 0 && r.Top >= 0 && r.Right <= Width && r.Bottom <= Height);
    internal bool LabelsOverlap => labels.Values.SelectMany((a, i) => labels.Values.Skip(i + 1).Select(b => RectangleF.Intersect(a, b))).Any(r => r.Width > 1 && r.Height > 1);
    internal bool RearWallsClosed
    {
        get; private set;
    }
    internal bool LabelsClearOfMaterial
    {
        get; private set;
    }
    internal bool BoreDimensionsClearOfMaterial
    {
        get; private set;
    }
    internal int DimensionCount => labels.Count;
    internal float DrawingScale
    {
        get; private set;
    }
    internal float CornerRadiusPixels
    {
        get; private set;
    }
    internal int CornerArcCount
    {
        get; private set;
    }
    internal bool LabelsClearOfRearEdges
    {
        get; private set;
    }
    internal bool LabelsClearOfDimensionLines => labelMasks.All(r => dimensionLines.All(line => !Crosses(r, line.A, line.B)));
    internal string DimensionText(string key) => texts.GetValueOrDefault(key, "");
    internal RectangleF DimensionHitBounds(string key) => hitAreas.GetValueOrDefault(key);
    internal string? HitDimension(Point point)
    {
        var label = hitAreas.Where(pair => pair.Value.Contains(point))
            .OrderBy(pair => Math.Pow(point.X - (pair.Value.Left + pair.Value.Right) / 2, 2) + Math.Pow(point.Y - (pair.Value.Top + pair.Value.Bottom) / 2, 2))
            .Select(pair => pair.Key).FirstOrDefault();
        if (label is not null)
        {
            return label;
        }

        return dragHandles.Select(pair => (pair.Key, Distance: SegmentDistance(point, pair.Value.A, pair.Value.B)))
            .Where(pair => pair.Distance <= 6 * DeviceDpi / 96F).OrderBy(pair => pair.Distance).Select(pair => pair.Key).FirstOrDefault();
    }
    private static double SegmentDistance(PointF p, PointF a, PointF b)
    {
        var dx = b.X - a.X;
        var dy = b.Y - a.Y;
        var t = dx * dx + dy * dy == 0 ? 0 : Math.Clamp(((p.X - a.X) * dx + (p.Y - a.Y) * dy) / (dx * dx + dy * dy), 0, 1);
        return Math.Sqrt(Math.Pow(p.X - a.X - t * dx, 2) + Math.Pow(p.Y - a.Y - t * dy, 2));
    }
    internal SketchCanvas()
    {
        SetStyle(
            ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.Selectable,
            true
        );
        BackColor = Color.White;
        TabStop = true;
        AccessibleName = "Продольный разрез детали";

    }
    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        suppressClick = false;
        if (e.Button != MouseButtons.Left || parameters is null || DrawingScale <= 0)
        {
            return;
        }

        if (HitCaption(e.Location) is not null)
        {
            return;
        }

        var contour = HitContour(e.Location);
        var key = contour?.Key ?? HitDragLine(e.Location);
        if (key is null)
        {
            return;
        }

        Focus();
        var field = typeof(Parameters).GetProperty(char.ToUpperInvariant(key[0]) + key[1..])!;
        var diameter = key.Contains("Diameter", StringComparison.OrdinalIgnoreCase);
        drag = new(key, e.Location, (double)field.GetValue(parameters)!, DrawingScale, WorkOrigin,
            diameter ? (e.Y > WorkOrigin.Y ? 2 : -2) : -1, contour is not null);
        hovered = key;
        hoverLine = true;
        if (!SyntheticInput)
        {
            Capture = true;
        }
    }
    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (drag is { } state)
        {
            if (!state.Active)
            {
                if (Math.Abs(e.X - state.Start.X) < SystemInformation.DragSize.Width / 2 && Math.Abs(e.Y - state.Start.Y) < SystemInformation.DragSize.Height / 2)
                {
                    return;
                }

                if (state.FromContour && HitContour(state.Start, Math.Abs(e.X - state.Start.X) > Math.Abs(e.Y - state.Start.Y)) is { } edge)
                {
                    var nominal = (double)typeof(Parameters).GetProperty(char.ToUpperInvariant(edge.Key[0]) + edge.Key[1..])!.GetValue(parameters)!;
                    drag = state = state with
                    {
                        Key = edge.Key,
                        Nominal = nominal,
                        Direction = IsDiameter(edge.Key) ? (state.Start.Y > WorkOrigin.Y ? 2 : -2) : -1
                    };
                    hovered = state.Key;
                }
                if (DimensionDragStarted?.Invoke(state.Key) != true)
                {
                    EndDrag(false);
                    return;
                }
                state.Active = true;
            }
            var diameter = state.Key.Contains("Diameter", StringComparison.OrdinalIgnoreCase);
            var delta = (diameter ? e.Y - state.Start.Y : e.X - state.Start.X) * state.Direction / state.Scale;
            var step = InputModifiers.HasFlag(Keys.Control) ? .01 : InputModifiers.HasFlag(Keys.Shift) ? .1 : 1;
            var value = Math.Round(state.Nominal + Math.Round(delta / step, MidpointRounding.AwayFromZero) * step, 3, MidpointRounding.AwayFromZero);
            Cursor = DimensionDragPreview?.Invoke(state.Key, value) == true ? (diameter ? Cursors.SizeNS : Cursors.SizeWE) : Cursors.No;
            return;
        }
        var caption = HitCaption(e.Location);
        var contour = caption is null ? HitContour(e.Location) : null;
        var key = caption ?? contour?.Key ?? HitDragLine(e.Location);
        var cursor = key is null ? Cursors.Default : caption is not null ? Cursors.Hand : IsDiameter(key) ? Cursors.SizeNS : Cursors.SizeWE;
        if (key == hovered && Cursor == cursor)
        {
            return;
        }

        hovered = key;
        hoverLine = caption is null;
        Cursor = cursor;
        Invalidate();
    }
    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (e.Button == MouseButtons.Left)
        {
            EndDrag(true);
        }
    }
    protected override void OnMouseCaptureChanged(EventArgs e)
    {
        base.OnMouseCaptureChanged(e);
        if (!Capture && drag is not null)
        {
            EndDrag(false);
        }
    }
    protected override void WndProc(ref Message m)
    {
        if (SyntheticInput && m.Msg is >= 0x200 and <= 0x20D)
        {
            return;
        }

        base.WndProc(ref m);
    }
    private void EndDrag(bool commit)
    {
        var state = drag;
        drag = null;
        if (state is not null)
        {
            suppressClick = true;
        }

        if (state?.Active == true)
        {
            DimensionDragFinished?.Invoke(commit);
        }

        Capture = false;
        Cursor = Cursors.Default;
        Invalidate();
    }
    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == Keys.Escape && drag is not null)
        {
            EndDrag(false);
            return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }
    internal void CancelDrag() => EndDrag(false);
    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        if (drag is null)
        {
            hovered = null;
            Cursor = Cursors.Default;
            Invalidate();
        }
    }
    protected override void OnMouseClick(MouseEventArgs e)
    {
        base.OnMouseClick(e);
        if (suppressClick)
        {
            suppressClick = false;
            return;
        }
        if (e.Button == MouseButtons.Left && HitCaption(e.Location) is { } key)
        {
            OpenNominalEditor(key);
        }
    }
    private static bool Crosses(RectangleF r, PointF a, PointF b) =>
        a.X == b.X ? r.Left <= a.X && r.Right >= a.X && r.Top <= Math.Max(a.Y, b.Y) && r.Bottom >= Math.Min(a.Y, b.Y)
        : a.Y == b.Y && r.Top <= a.Y && r.Bottom >= a.Y && r.Left <= Math.Max(a.X, b.X) && r.Right >= Math.Min(a.X, b.X);

    private static bool IntersectsVertical(RectangleF rect, float x, float top, float bottom) => rect.Left <= x && rect.Right >= x && rect.Bottom >= top && rect.Top <= bottom;
    private static string F(double value) => value.ToString("0.###", CultureInfo.GetCultureInfo("ru-RU"));
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
                if (Inside(upper, point) || Inside(lower, point))
                {
                    return false;
                }
            }
        }
        return true;
    }
    private static bool Inside(PointF[] polygon, PointF point)
    {
        var inside = false;
        for (int i = 0, previous = polygon.Length - 1; i < polygon.Length; previous = i++)
        {
            var a = polygon[i];
            var b = polygon[previous];
            if ((a.Y > point.Y) != (b.Y > point.Y) && point.X < (b.X - a.X) * (point.Y - a.Y) / (b.Y - a.Y) + a.X)
            {
                inside = !inside;
            }
        }
        return inside;
    }
}
