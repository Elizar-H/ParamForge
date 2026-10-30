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

    internal ResultInspectionForm()
    {
        Text = "Траектория УП — ParamForge"; Font = Theme.Body; BackColor = Theme.Surface;
        AutoScaleMode = AutoScaleMode.Dpi; ClientSize = new Size(1200, 850); MinimumSize = new Size(960, 680);
        StartPosition = FormStartPosition.CenterParent;
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Padding = new Padding(12) };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 30)); root.RowStyles.Add(new RowStyle(SizeType.Absolute, 240));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); root.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        status.Dock = detail.Dock = DockStyle.Fill;
        root.Controls.Add(status, 0, 0); root.Controls.Add(transitions, 0, 1);
        var split = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Vertical, Size = new Size(1170, 500),
            SplitterDistance = 600, Panel1MinSize = 300, Panel2MinSize = 300 };
        split.Panel1.Controls.Add(trajectory); split.Panel2.Controls.Add(code);
        root.Controls.Add(split, 0, 2); root.Controls.Add(detail, 0, 3); Controls.Add(root);
        transitions.DefaultCellStyle.Font = Theme.Small; transitions.ColumnHeadersDefaultCellStyle.Font = Theme.Small;
        transitions.RowTemplate.Height = 25;
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
            status.Text = $"{CncProfiles.Name(value.Params.Controller)}; {value.NcFileName}";
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
            detail.Text = $"{t.Id}; {t.Tool}/D1; {t.Passes.Count} проходов; f={MainForm.F(t.Feed)} мм/об; n={MainForm.F(t.Rpm)} об/мин; G1={t.CutMinutes:0.000} мин";
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
            detail.Text = $"N{move.Line}; T{move.Tool}/D1; {move.Mode}; X={MainForm.F(move.X)} (диаметр), Z={MainForm.F(move.Z)} мм"
                + (move.Mode == "G1" ? $"; f={MainForm.F(move.Feed!.Value)} мм/об; n={MainForm.F(move.Rpm!.Value)} об/мин" : "");
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
    internal int MoveCount => project?.Verification.Moves.Count ?? 0;
    internal int SelectedCount => selected.Count;
    internal TrajectoryCanvas()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        BackColor = Color.White;
    }
    internal void SetProject(Project? value) { project = value; selected = []; Invalidate(); }
    internal void SelectBlocks(HashSet<int> blocks) { selected = blocks; Invalidate(); }
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e); var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
        var u = DeviceDpi / 96F;
        if (project is null) return;
        WorkCoordinates.Legend(g, new(35 * u, 53 * u), u);
        var p = project.Params; var moves = project.Verification.Moves;
        var minZ = Math.Min(-p.Length, moves.Min(m => m.Z)); var maxZ = Math.Max(10, moves.Max(m => m.Z));
        var maxRadius = Math.Max(p.StockDiameter, moves.Max(m => m.X)) / 2;
        var availableWidth = Math.Max(1, Width - 90 * u); var availableHeight = Math.Max(1, Height - 115 * u);
        var scale = (float)Math.Min(availableWidth / (maxZ - minZ), availableHeight / maxRadius);
        var left = 45 * u + (availableWidth - (float)(maxZ - minZ) * scale) / 2;
        var top = 85 * u + (availableHeight - (float)maxRadius * scale) / 2;
        PointF Point(double z, double radius) => new(left + (float)(z - minZ) * scale, top + (float)(maxRadius - radius) * scale);
        using var axis = new Pen(Color.LightSlateGray, .7F * u) { DashStyle = DashStyle.DashDot };
        g.DrawLine(axis, Point(minZ, 0), Point(maxZ, 0));
        using var outline = new Pen(Color.FromArgb(173, 183, 192), 1.2F * u);
        using var material = new SolidBrush(Color.FromArgb(243, 245, 247));
        var points = new[] { Point(-p.Length, p.BodyDiameter/2), Point(-p.FlangeWidth, p.BodyDiameter/2),
            Point(-p.FlangeWidth, p.FlangeDiameter/2), Point(0, p.FlangeDiameter/2), Point(0, p.CounterboreDiameter/2),
            Point(-p.CounterboreDepth, p.CounterboreDiameter/2), Point(-p.CounterboreDepth, p.BoreDiameter/2), Point(-p.Length, p.BoreDiameter/2) };
        g.FillPolygon(material, points); g.DrawPolygon(outline, points);
        using var t1 = new Pen(Color.FromArgb(46, 84, 125), 1.2F * u);
        using var t2 = new Pen(Color.FromArgb(14, 128, 124), 1.2F * u);
        using var rapid1 = new Pen(t1.Color, .8F * u) { DashStyle = DashStyle.Dash };
        using var rapid2 = new Pen(t2.Color, .8F * u) { DashStyle = DashStyle.Dash };
        using var highlight = new Pen(Color.FromArgb(225, 119, 28), 2.7F * u);
        using var highlightRapid = new Pen(highlight.Color, 2F * u) { DashStyle = DashStyle.Dash };
        for (var pass = 0; pass < 2; pass++)
            for (var i = 1; i < moves.Count; i++)
            {
                var before = moves[i - 1]; var move = moves[i];
                if (before.Tool != move.Tool || (pass == 1 && !selected.Contains(move.Line))) continue;
                var pen = pass == 1 ? (move.Mode == "G1" ? highlight : highlightRapid)
                    : move.Mode == "G1" ? (move.Tool == 1 ? t1 : t2) : (move.Tool == 1 ? rapid1 : rapid2);
                g.DrawLine(pen, Point(before.Z, before.X / 2), Point(move.Z, move.X / 2));
            }
        WorkCoordinates.Origin(g, Point(0, 0), u);
    }
}
