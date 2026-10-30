using System.ComponentModel;
using Kontur.Core;

namespace Kontur.Desktop;

internal sealed partial class TrajectoryCanvas : Control
{
    private Project? project;
    private HashSet<int> selected = [];
    private float zoom = 1;
    private PointF pan;
    private Point? drag;
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal bool ShowAll { get; set; } = true;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal bool ShowRapids { get; set; } = true;
    internal float Zoom => zoom;
    internal PointF Pan => pan;
    internal int MoveCount => project?.Verification.Moves.Count ?? 0;
    internal int SelectedCount => selected.Count;
    internal int VisibleSegmentCount
    {
        get; private set;
    }

    internal IReadOnlyList<(double Z, double X)> TargetProfile
    {
        get
        {
            if (project is null)
            {
                return [];
            }

            var c = project.Calculations;
            return [(-c.CutLength, c.TargetBodyDiameter), (-c.TargetFlangeWidth, c.TargetBodyDiameter), (-c.TargetFlangeWidth, c.TargetFlangeDiameter), (0, c.TargetFlangeDiameter), (0, c.TargetCounterboreDiameter), (-c.TargetCounterboreDepth, c.TargetCounterboreDiameter), (-c.TargetCounterboreDepth, c.TargetBoreDiameter), (-c.CutLength, c.TargetBoreDiameter)];
        }
    }

    internal TrajectoryCanvas()
    {
        SetStyle(
            ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw,
            true
        );
        BackColor = Color.White;
        AccessibleName = "Траектория обработки XZ";
    }

    internal void SetProject(Project? value)
    {
        project = value;
        selected = [];
        Fit();
    }

    internal void SelectBlocks(HashSet<int> blocks)
    {
        selected = blocks;
        Invalidate();
    }

    internal void Fit()
    {
        zoom = 1;
        pan = PointF.Empty;
        Invalidate();
    }

    internal void ZoomAt(PointF point, float factor)
    {
        var next = Math.Clamp(zoom * factor, .25F, 32F);
        var center = new PointF(Width / 2F, Height / 2F);
        pan = new(
            point.X - center.X - (point.X - center.X - pan.X) * next / zoom,
            point.Y - center.Y - (point.Y - center.Y - pan.Y) * next / zoom
        );
        zoom = next;
        Invalidate();
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        ZoomAt(e.Location, MathF.Pow(1.2F, e.Delta / 120F));
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        base.OnMouseEnter(e);
        Focus();
    }

    protected override void OnMouseDoubleClick(MouseEventArgs e)
    {
        base.OnMouseDoubleClick(e);
        Fit();
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button is MouseButtons.Left or MouseButtons.Middle)
        {
            drag = e.Location;
            Capture = true;
            Cursor = Cursors.SizeAll;
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (drag is not { } previous)
        {
            return;
        }

        pan = new(pan.X + e.X - previous.X, pan.Y + e.Y - previous.Y);
        drag = e.Location;
        Invalidate();
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        drag = null;
        Capture = false;
        Cursor = Cursors.Default;
    }

    protected override void OnMouseCaptureChanged(EventArgs e)
    {
        base.OnMouseCaptureChanged(e);
        if (!Capture)
        {
            drag = null;
            Cursor = Cursors.Default;
        }
    }
}
