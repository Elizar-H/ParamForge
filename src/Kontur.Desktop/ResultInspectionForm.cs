using System.ComponentModel;
using System.Drawing.Drawing2D;
using Kontur.Core;

namespace Kontur.Desktop;

internal sealed class ResultInspectionForm : Form
{
    private readonly DataGridView transitions = new() { Dock = DockStyle.Fill, ReadOnly = true, AllowUserToAddRows = false,
        AllowUserToDeleteRows = false, AllowUserToResizeRows = false, RowHeadersVisible = false, MultiSelect = false,
        SelectionMode = DataGridViewSelectionMode.FullRowSelect, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
        BackgroundColor = Color.White, BorderStyle = BorderStyle.None };
    private readonly RichTextBox code = new() { Dock = DockStyle.Fill, ReadOnly = true, WordWrap = false,
        DetectUrls = false, Font = new Font("Consolas", 10), BackColor = Color.White, BorderStyle = BorderStyle.None };
    private readonly TrajectoryCanvas trajectory = new() { Dock = DockStyle.Fill };
    private readonly Label status = Theme.Label("", Theme.Small);
    private readonly Label detail = Theme.Label("", Theme.Small);
    private Project? project;
    private bool updating;
    internal string? CurrentProjectId => project?.Id;
    internal int DisplayedMoveCount => trajectory.MoveCount;
    internal int SelectedMoveCount => trajectory.SelectedCount;
    internal int TransitionCount => transitions.Rows.Count;
    internal TrajectoryCanvas Trajectory => trajectory;

    internal ResultInspectionForm()
    {
        Text = "Траектория УП — ParamForge"; Font = Theme.Body; BackColor = Theme.Surface;
        AutoScaleMode = AutoScaleMode.Dpi;
        var screen = Screen.PrimaryScreen!.WorkingArea;
        ClientSize = new Size(Math.Min(1500, screen.Width - 80), Math.Min(940, screen.Height - 100));
        MinimumSize = new Size(960, 680);
        StartPosition = FormStartPosition.CenterParent;
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Padding = new Padding(12) };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 30)); root.RowStyles.Add(new RowStyle(SizeType.Absolute, 270));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); root.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        status.Dock = detail.Dock = DockStyle.Fill;
        root.Controls.Add(status, 0, 0); root.Controls.Add(transitions, 0, 1);
        var split = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Vertical, Size = new Size(1170, 500),
            SplitterDistance = 600, Panel1MinSize = 300, Panel2MinSize = 300 };
        var viewport = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Margin = Padding.Empty };
        viewport.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        viewport.RowStyles.Add(new RowStyle(SizeType.Absolute, 38)); viewport.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var toolbar = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, BackColor = Color.White, Margin = Padding.Empty };
        var fit = Theme.Button("Вписать"); fit.Width = 90; fit.Height = 30; fit.Margin = new Padding(0, 2, 8, 2);
        fit.Click += (_, _) => trajectory.Fit();
        var all = new CheckBox { Text = "Все переходы", AutoSize = true, Margin = new Padding(4, 9, 10, 0) };
        var rapids = new CheckBox { Text = "Холостые ходы", Checked = true, AutoSize = true, Margin = new Padding(4, 9, 0, 0) };
        all.CheckedChanged += (_, _) => { trajectory.ShowAll = all.Checked; trajectory.Invalidate(); };
        rapids.CheckedChanged += (_, _) => { trajectory.ShowRapids = rapids.Checked; trajectory.Invalidate(); };
        toolbar.Controls.AddRange([fit, all, rapids]);
        viewport.Controls.Add(toolbar, 0, 0); viewport.Controls.Add(trajectory, 0, 1);
        split.Panel1.Controls.Add(viewport); split.Panel2.Controls.Add(code);
        root.Controls.Add(split, 0, 2); root.Controls.Add(detail, 0, 3); Controls.Add(root);
        transitions.DefaultCellStyle.Font = Theme.Small; transitions.ColumnHeadersDefaultCellStyle.Font = Theme.Small;
        transitions.EnableHeadersVisualStyles = false;
        transitions.ColumnHeadersDefaultCellStyle.BackColor = Theme.Surface;
        transitions.ColumnHeadersDefaultCellStyle.ForeColor = Theme.Muted;
        transitions.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None;
        transitions.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
        transitions.GridColor = Theme.Border;
        transitions.DefaultCellStyle.SelectionBackColor = Color.FromArgb(224, 235, 246);
        transitions.DefaultCellStyle.SelectionForeColor = Theme.Ink;
        transitions.DefaultCellStyle.Padding = new Padding(4, 0, 4, 0);
        transitions.RowTemplate.Height = 28;
        foreach (var (key, title, weight) in new[] { ("id", "Переход", 9), ("title", "Содержание", 35), ("tool", "Инстр.", 7),
            ("passes", "Проходы", 8), ("feed", "f, мм/об", 11), ("rpm", "n, об/мин", 12), ("time", "G1, мин", 10) })
            transitions.Columns.Add(new DataGridViewTextBoxColumn { Name = key, HeaderText = title, FillWeight = weight, SortMode = DataGridViewColumnSortMode.NotSortable });
        transitions.SelectionChanged += (_, _) => SelectTransition();
        code.SelectionChanged += (_, _) => SelectFrame();
    }
    internal void SetResult(Project? value)
    {
        if (value is not null && project?.Id == value.Id) return;
        updating = true;
        try
        {
            project = value; transitions.Rows.Clear(); code.Text = value?.Nc ?? ""; trajectory.SetProject(value);
            if (value is null)
            {
                status.Text = "Параметры изменены. Исправьте ошибки для обновления результата."; detail.Text = ""; return;
            }
            status.Text = $"{CncProfiles.Name(value.Params.Controller)}    Программа: {value.NcFileName}";
            foreach (var t in value.Transitions)
            {
                var index = transitions.Rows.Add(t.Id, t.Title, t.Tool, t.Passes.Count, MainForm.F(t.Feed), MainForm.F(t.Rpm), t.CutMinutes.ToString("0.000"));
                transitions.Rows[index].Tag = t;
            }
            if (transitions.Rows.Count != 0) transitions.Rows[0].Selected = true;
        }
        finally { updating = false; }
        SelectTransition();
    }
    private void Highlight(int firstPhysical, int lastPhysical)
    {
        var caret = code.SelectionStart;
        code.SelectAll(); code.SelectionBackColor = Color.White;
        var start = code.GetFirstCharIndexFromLine(firstPhysical - 1);
        var after = code.GetFirstCharIndexFromLine(lastPhysical);
        if (start >= 0)
        {
            code.Select(start, (after < 0 ? code.TextLength : after) - start);
            code.SelectionBackColor = Color.FromArgb(255, 239, 202);
            code.Select(start, 0); code.ScrollToCaret();
        }
        else code.Select(Math.Min(caret, code.TextLength), 0);
    }
    private void SelectTransition()
    {
        if (updating || project is null || transitions.SelectedRows.Count == 0) return;
        updating = true;
        try
        {
            if (transitions.SelectedRows[0].Tag is not Transition t) return;
            var blocks = t.Blocks.ToHashSet(); trajectory.SelectBlocks(blocks);
            var movements = project.Verification.Moves.Where(m => blocks.Contains(m.Line)).ToList();
            if (movements.Count != 0) Highlight(movements.First().PhysicalLine, movements.Last().PhysicalLine);
            detail.Text = $"Инструмент {t.Tool}/D1    Подача {MainForm.F(t.Feed)} мм/об    Частота {MainForm.F(t.Rpm)} об/мин    Рабочее время {t.CutMinutes:0.000} мин";
        }
        finally { updating = false; }
    }
    private void SelectFrame()
    {
        if (updating || project is null) return;
        updating = true;
        try
        {
            var physical = code.GetLineFromCharIndex(code.SelectionStart) + 1;
            if (code.SelectionLength == 0) Highlight(physical, physical);
            var move = project.Verification.Moves.FirstOrDefault(m => m.PhysicalLine == physical);
            trajectory.SelectBlocks(move is null ? [] : [move.Line]);
            transitions.ClearSelection();
            if (move is null) { detail.Text = "Строка без перемещения инструмента."; return; }
            foreach (DataGridViewRow row in transitions.Rows)
                if (row.Tag is Transition t && t.Blocks.Contains(move.Line)) row.Selected = true;
            detail.Text = $"Кадр N{move.Line}    T{move.Tool}/D1    {move.Mode}    X {MainForm.F(move.X)}    Z {MainForm.F(move.Z)}"
                + (move.Mode == "G1" ? $"    Подача {MainForm.F(move.Feed!.Value)} мм/об    Частота {MainForm.F(move.Rpm!.Value)} об/мин" : "");
        }
        finally { updating = false; }
    }
    internal void SelectTransitionForSmoke(int index) { transitions.ClearSelection(); transitions.Rows[index].Selected = true; }
    internal void SelectFrameForSmoke(int physicalLine) { code.Select(code.GetFirstCharIndexFromLine(physicalLine - 1), 0); }
    internal void CaptureImage(string path) { using var bmp = new Bitmap(Width, Height); DrawToBitmap(bmp, new(Point.Empty, bmp.Size)); bmp.Save(path); }
}

internal sealed class TrajectoryCanvas : Control
{
    private Project? project;
    private HashSet<int> selected = [];
    private float zoom = 1;
    private PointF pan;
    private Point? drag;
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal bool ShowAll { get; set; }
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal bool ShowRapids { get; set; } = true;
    internal float Zoom => zoom;
    internal PointF Pan => pan;
    internal int MoveCount => project?.Verification.Moves.Count ?? 0;
    internal int SelectedCount => selected.Count;
    internal int VisibleSegmentCount { get; private set; }
    internal TrajectoryCanvas()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        BackColor = Color.White;
        AccessibleName = "Траектория обработки XZ";
    }
    internal void SetProject(Project? value) { project = value; selected = []; Fit(); }
    internal void SelectBlocks(HashSet<int> blocks) { selected = blocks; Invalidate(); }
    internal void Fit() { zoom = 1; pan = PointF.Empty; Invalidate(); }
    internal void ZoomAt(PointF point, float factor)
    {
        var next = Math.Clamp(zoom * factor, .25F, 32F);
        var center = new PointF(Width / 2F, Height / 2F);
        pan = new(point.X - center.X - (point.X - center.X - pan.X) * next / zoom,
            point.Y - center.Y - (point.Y - center.Y - pan.Y) * next / zoom);
        zoom = next; Invalidate();
    }
    protected override void OnMouseWheel(MouseEventArgs e) { base.OnMouseWheel(e); ZoomAt(e.Location, MathF.Pow(1.2F, e.Delta / 120F)); }
    protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); Focus(); }
    protected override void OnMouseDoubleClick(MouseEventArgs e) { base.OnMouseDoubleClick(e); Fit(); }
    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button is MouseButtons.Left or MouseButtons.Middle) { drag = e.Location; Capture = true; Cursor = Cursors.SizeAll; }
    }
    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (drag is not { } previous) return;
        pan = new(pan.X + e.X - previous.X, pan.Y + e.Y - previous.Y); drag = e.Location; Invalidate();
    }
    protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); drag = null; Capture = false; Cursor = Cursors.Default; }
    protected override void OnMouseCaptureChanged(EventArgs e) { base.OnMouseCaptureChanged(e); if (!Capture) { drag = null; Cursor = Cursors.Default; } }
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e); var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias; VisibleSegmentCount = 0;
        var u = DeviceDpi / 96F;
        if (project is null) return;
        var p = project.Params; var moves = project.Verification.Moves;
        var minZ = Math.Min(-p.Length, moves.Min(m => m.Z)); var maxZ = Math.Max(10, moves.Max(m => m.Z));
        var maxRadius = Math.Max(p.StockDiameter, moves.Max(m => m.X)) / 2;
        var availableWidth = Math.Max(1, Width - 80 * u); var availableHeight = Math.Max(1, Height - 100 * u);
        var scale = (float)Math.Min(availableWidth / (maxZ - minZ), availableHeight / maxRadius);
        var left = 40 * u + (availableWidth - (float)(maxZ - minZ) * scale) / 2;
        var top = 70 * u + (availableHeight - (float)maxRadius * scale) / 2;
        PointF Point(double z, double radius)
        {
            var x = left + (float)(z - minZ) * scale; var y = top + (float)(maxRadius - radius) * scale;
            return new(Width / 2F + (x - Width / 2F) * zoom + pan.X, Height / 2F + (y - Height / 2F) * zoom + pan.Y);
        }
        using var axis = new Pen(Color.LightSlateGray, .7F * u) { DashStyle = DashStyle.DashDot };
        g.DrawLine(axis, Point(minZ, 0), Point(maxZ, 0));
        using var outline = new Pen(Color.FromArgb(100, 111, 122), 1.3F * u);
        using var material = new SolidBrush(Color.FromArgb(244, 246, 248));
        var points = new[] { Point(-p.Length, p.BodyDiameter/2), Point(-p.FlangeWidth, p.BodyDiameter/2),
            Point(-p.FlangeWidth, p.FlangeDiameter/2), Point(0, p.FlangeDiameter/2), Point(0, p.CounterboreDiameter/2),
            Point(-p.CounterboreDepth, p.CounterboreDiameter/2), Point(-p.CounterboreDepth, p.BoreDiameter/2), Point(-p.Length, p.BoreDiameter/2) };
        g.FillPolygon(material, points); g.DrawPolygon(outline, points);
        using var t1 = new Pen(Color.FromArgb(39, 103, 176), 2F * u);
        using var t2 = new Pen(Color.FromArgb(13, 130, 122), 2F * u);
        using var rapid1 = new Pen(t1.Color, 1F * u) { DashStyle = DashStyle.Dash };
        using var rapid2 = new Pen(t2.Color, 1F * u) { DashStyle = DashStyle.Dash };
        using var context = new Pen(Color.FromArgb(204, 212, 220), .8F * u);
        using var contextRapid = new Pen(context.Color, .7F * u) { DashStyle = DashStyle.Dash };
        for (var pass = 0; pass < 2; pass++)
            for (var i = 1; i < moves.Count; i++)
            {
                var before = moves[i - 1]; var move = moves[i]; var active = selected.Contains(move.Line);
                if (before.Tool != move.Tool || (!ShowRapids && move.Mode != "G1") || (pass == 0 ? !ShowAll || active : !active)) continue;
                var pen = pass == 0 ? (move.Mode == "G1" ? context : contextRapid)
                    : move.Mode == "G1" ? (move.Tool == 1 ? t1 : t2) : (move.Tool == 1 ? rapid1 : rapid2);
                g.DrawLine(pen, Point(before.Z, before.X / 2), Point(move.Z, move.X / 2)); VisibleSegmentCount++;
            }
        if (selected.Count == 1 && moves.FirstOrDefault(m => selected.Contains(m.Line)) is { } current)
        {
            var point = Point(current.Z, current.X / 2);
            using var marker = new SolidBrush(current.Tool == 1 ? t1.Color : t2.Color);
            g.FillEllipse(marker, point.X - 3 * u, point.Y - 3 * u, 6 * u, 6 * u);
        }
        WorkCoordinates.Origin(g, Point(0, 0), u);
        WorkCoordinates.Legend(g, new(28 * u, 48 * u), u);
    }
}
