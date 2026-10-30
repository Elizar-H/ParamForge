using Kontur.Core;

namespace Kontur.Desktop;

internal sealed class ResultInspectionForm : Form
{
    private readonly TransitionGrid transitions = new()
    {
        Dock = DockStyle.Fill,
        ReadOnly = true,
        AllowUserToAddRows = false,
        AllowUserToDeleteRows = false,
        AllowUserToResizeRows = false,
        AllowUserToResizeColumns = false,
        RowHeadersVisible = false,
        MultiSelect = false,
        SelectionMode = DataGridViewSelectionMode.FullRowSelect,
        AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
        StandardTab = true,
        BackgroundColor = Color.White,
        BorderStyle = BorderStyle.None
    };
    private readonly RichTextBox code = new()
    {
        Dock = DockStyle.Fill,
        ReadOnly = true,
        WordWrap = false,
        DetectUrls = false,
        Font = new Font("Consolas", 10),
        BackColor = Color.White,
        BorderStyle = BorderStyle.None
    };
    private readonly TrajectoryCanvas trajectory = new()
    {
        Dock = DockStyle.Fill
    };
    private readonly ComboBox controller = new()
    {
        DropDownStyle = ComboBoxStyle.DropDownList,
        Dock = DockStyle.Top,
        Font = Theme.Detail
    };
    private readonly Button parameters = Theme.Button("Размеры и допуски…");
    private readonly Label status = Theme.Label("", Theme.Detail);
    private readonly Label detail = Theme.Label("", Theme.Detail);
    private readonly Label selection = Theme.Label("", Theme.Strong);
    private readonly CheckBox all = new()
    {
        Text = "Все переходы",
        Checked = true,
        AutoSize = true
    };
    private readonly CheckBox rapids = new()
    {
        Text = "Холостые",
        Checked = true,
        AutoSize = true
    };
    private readonly TableLayoutPanel root;
    private readonly TableLayoutPanel header;
    private readonly FlowLayoutPanel toolbar;
    private Project? project;
    private bool updating;
    private string? lastTransition, activeTransition;
    internal event Func<string, bool>? ControllerRequested;
    internal event Action? ParametersRequested;
    internal string? CurrentProjectId => project?.Id;
    internal int DisplayedMoveCount => trajectory.MoveCount;
    internal int SelectedMoveCount => trajectory.SelectedCount;
    internal int TransitionCount => transitions.Rows.Count;
    internal TrajectoryCanvas Trajectory => trajectory;
    internal string DetailText => detail.Text;
    internal string SelectionText => selection.Text;
    internal string? SelectedTransitionId => transitions.SelectedRows.Count == 0 ? null : (transitions.SelectedRows[0].Tag as Transition)?.Id;
    internal ComboBox ControllerChoice => controller;
    internal Button ParametersButton => parameters;
    internal DataGridView TransitionTable => transitions;
    internal CheckBox AllTransitions => all;

    internal ParsedNcSummary TransitionSummary(int index) => ParsedNcSummary.From(project!.Verification.Moves, project.Transitions[index].Blocks.ToHashSet());
    internal ResultInspectionForm()
    {
        Text = "Траектория УП — ParamForge";
        Font = Theme.Detail;
        BackColor = Theme.Surface;
        AutoScaleMode = AutoScaleMode.Dpi;
        var screen = Screen.PrimaryScreen!.WorkingArea;
        ClientSize = new Size(Math.Min(1580, screen.Width - 80), Math.Min(980, screen.Height - 100));
        MinimumSize = new Size(1060, 740);
        StartPosition = FormStartPosition.CenterParent;
        root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            Padding = new Padding(12)
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 290));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        header = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 3,
            Margin = new Padding(0, 0, 0, 8)
        };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 250));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 230));
        controller.Items.AddRange(["AxiOMA Control", "SINUMERIK 840D sl"]);
        controller.AccessibleName = "Профиль ЧПУ в траектории";
        controller.Margin = new Padding(0, 4, 12, 4);
        controller.SelectedIndexChanged += (_, _) => ChangeController();
        parameters.Font = Theme.Detail;
        parameters.Dock = DockStyle.Top;
        parameters.AutoSize = true;
        parameters.Margin = new Padding(8, 0, 0, 4);
        parameters.Click += (_, _) => ParametersRequested?.Invoke();
        status.AutoEllipsis = true;
        header.Controls.Add(controller, 0, 0);
        header.Controls.Add(status, 1, 0);
        header.Controls.Add(parameters, 2, 0);
        root.Controls.Add(header, 0, 0);
        root.Controls.Add(transitions, 0, 1);
        var split = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Vertical,
            Size = new Size(1500, 500),
            SplitterDistance = 920,
            Panel1MinSize = 360,
            Panel2MinSize = 280
        };
        var viewport = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Margin = Padding.Empty
        };
        viewport.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        viewport.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        viewport.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        viewport.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        toolbar = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            WrapContents = true,
            AutoSize = true,
            BackColor = Color.White,
            Margin = Padding.Empty
        };
        var fit = Theme.Button("К началу");
        fit.Width = 130;
        fit.AutoSize = true;
        fit.Margin = new Padding(0, 2, 8, 2);
        fit.Click += (_, _) => trajectory.Fit();
        var stock = new ToolStripMenuItem("Заготовка и зажим")
        {
            CheckOnClick = true,
            Checked = true
        };
        var grid = new ToolStripMenuItem("Координатная сетка")
        {
            CheckOnClick = true,
            Checked = true
        };
        foreach (var check in new[]
        {
            all,
            rapids
        }

        )
        {
            check.Margin = new Padding(4, 9, 10, 0);
        }

        all.CheckedChanged += (_, _) =>
        {
            trajectory.ShowAll = all.Checked;
            trajectory.Invalidate();
        };
        rapids.CheckedChanged += (_, _) =>
        {
            trajectory.ShowRapids = rapids.Checked;
            trajectory.Invalidate();
        };
        stock.CheckedChanged += (_, _) =>
        {
            trajectory.ShowStock = stock.Checked;
            trajectory.Fit();
        };
        grid.CheckedChanged += (_, _) =>
        {
            trajectory.ShowGrid = grid.Checked;
            trajectory.Invalidate();
        };
        var options = new ToolStrip
        {
            GripStyle = ToolStripGripStyle.Hidden,
            BackColor = Color.White,
            Font = Theme.Detail,
            Margin = new Padding(4, 7, 0, 0),
            Padding = Padding.Empty
        };
        var display = new ToolStripDropDownButton("Вид");
        display.DropDownItems.AddRange([stock, grid]);
        options.Items.Add(display);
        toolbar.Controls.AddRange([fit, all, rapids, options]);
        toolbar.SizeChanged += (_, _) => FitTableHeight();
        selection.BackColor = Color.White;
        selection.Padding = new Padding(8, 0, 0, 0);
        viewport.Controls.Add(toolbar, 0, 0);
        viewport.Controls.Add(selection, 0, 1);
        viewport.Controls.Add(trajectory, 0, 2);
        split.Panel1.Controls.Add(viewport);
        split.Panel2.Controls.Add(code);
        root.Controls.Add(split, 0, 2);
        detail.Height = TextRenderer.MeasureText("0", detail.Font).Height * 2 + 8;
        detail.Padding = new Padding(0, 6, 0, 0);
        root.Controls.Add(detail, 0, 3);
        Controls.Add(root);
        transitions.DefaultCellStyle.Font = Theme.Detail;
        transitions.DefaultCellStyle.ForeColor = Theme.Ink;
        transitions.ColumnHeadersDefaultCellStyle.Font = Theme.Detail;
        transitions.EnableHeadersVisualStyles = false;
        transitions.ColumnHeadersDefaultCellStyle.BackColor = transitions.ColumnHeadersDefaultCellStyle.SelectionBackColor = Theme.Surface;
        transitions.ColumnHeadersDefaultCellStyle.ForeColor = transitions.ColumnHeadersDefaultCellStyle.SelectionForeColor = Theme.Ink;
        transitions.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
        transitions.ColumnHeadersHeight = TextRenderer.MeasureText("0", Theme.Detail).Height + 12;
        transitions.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None;
        transitions.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
        transitions.GridColor = Theme.Border;
        transitions.DefaultCellStyle.SelectionBackColor = Theme.Accent;
        transitions.DefaultCellStyle.SelectionForeColor = Color.White;
        transitions.DefaultCellStyle.Padding = new Padding(4, 2, 4, 2);
        transitions.RowTemplate.Height = TextRenderer.MeasureText("0", Theme.Detail).Height + 8;
        foreach (var (key, title, weight) in new[]
        {
            ("id", "Переход", 9),
            ("title", "Содержание", 36),
            ("tool", "Инстр.", 7),
            ("passes", "Проходы", 8),
            ("feed", "f, мм/об", 11),
            ("rpm", "n, об/мин", 12),
            ("time", "G1, мин", 10)
        }

        )
        {
            transitions.Columns.Add(new DataGridViewTextBoxColumn { Name = key, HeaderText = title, FillWeight = weight, MinimumWidth = TextRenderer.MeasureText(title, Theme.Detail).Width + 12, SortMode = DataGridViewColumnSortMode.NotSortable });
        }

        transitions.SelectionChanged += (_, _) => SelectTransition();
        transitions.CellClick += (_, e) =>
        {
            if (e.RowIndex >= 0)
            {
                SelectTransition();
            }
        };
        code.SelectionChanged += (_, _) => SelectFrame();
        root.Layout += (_, _) => FitTableHeight();
        transitions.SizeChanged += (_, _) => KeepSelectedRowVisible();
    }

    private void FitTableHeight()
    {
        var rowHeights = transitions.Rows.Cast<DataGridViewRow>().Select(r => r.Height).ToArray();
        var headerHeight = transitions.ColumnHeadersHeight + transitions.Margin.Vertical + 2;
        var preferred = headerHeight + rowHeights.Sum();
        var fixedHeight = header.Height + header.Margin.Vertical + detail.Height + root.Padding.Vertical + toolbar.Height + selection.Height + 8;
        var minimum = headerHeight + rowHeights.Take(3).Sum();
        var available = Math.Max(minimum, root.ClientSize.Height - fixedHeight - 200 * DeviceDpi / 96F);
        var height = headerHeight;
        foreach (var rowHeight in rowHeights)
        {
            if (height + rowHeight > available)
            {
                break;
            }

            height += rowHeight;
        }

        height = Math.Min(preferred, height);
        if (Math.Abs(root.RowStyles[1].Height - height) > .5F)
        {
            root.RowStyles[1].Height = height;
        }
    }

    private void KeepSelectedRowVisible()
    {
        if (transitions.CurrentCell is not { } cell || transitions.FirstDisplayedScrollingRowIndex < 0)
        {
            return;
        }

        var count = transitions.DisplayedRowCount(false);
        if (count == 0)
        {
            return;
        }

        var first = transitions.FirstDisplayedScrollingRowIndex;
        if (cell.RowIndex < first)
        {
            transitions.FirstDisplayedScrollingRowIndex = cell.RowIndex;
        }
        else if (cell.RowIndex >= first + count)
        {
            transitions.FirstDisplayedScrollingRowIndex = cell.RowIndex - count + 1;
        }
    }

    private void ChangeController()
    {
        if (updating || project is null)
        {
            return;
        }

        var previous = project.Params.Controller;
        var requested = controller.SelectedIndex == 1 ? CncProfiles.Sinumerik : CncProfiles.Axioma;
        if (requested == previous)
        {
            return;
        }

        if (ControllerRequested?.Invoke(requested) != true)
        {
            updating = true;
            controller.SelectedIndex = previous == CncProfiles.Sinumerik ? 1 : 0;
            updating = false;
        }
    }

    internal void SetResult(Project? value)
    {
        if (value is not null && project?.Id == value.Id)
        {
            return;
        }

        updating = true;
        try
        {
            lastTransition = SelectedTransitionId ?? lastTransition;
            activeTransition = null;
            project = value;
            transitions.Rows.Clear();
            code.Text = value?.Nc ?? "";
            trajectory.SetProject(value);
            controller.Enabled = value is not null;
            if (value is null)
            {
                status.Text = "Исправьте параметры для обновления УП.";
                detail.Text = "";
                selection.Text = "Траектория операции 010";
                return;
            }

            controller.SelectedIndex = value.Params.Controller == CncProfiles.Sinumerik ? 1 : 0;
            status.Text = "Программа: " + value.NcFileName;
            foreach (var t in value.Transitions)
            {
                var summary = ParsedNcSummary.From(value.Verification.Moves, t.Blocks.ToHashSet());
                var row = transitions.Rows.Add(
                    t.Id,
                    t.Title,
                    summary.Tools,
                    summary.Passes,
                    summary.Feeds,
                    summary.Speeds,
                    summary.CutMinutes.ToString("0.000")
                );
                transitions.Rows[row].Tag = t;
            }

            transitions.ClearSelection();
            var selectedRow = transitions.Rows.Cast<DataGridViewRow>().FirstOrDefault(r => (r.Tag as Transition)?.Id == lastTransition) ?? transitions.Rows[0];
            transitions.CurrentCell = selectedRow.Cells[0];
            selectedRow.Selected = true;
            FitTableHeight();
        }
        finally
        {
            updating = false;
        }

        SelectTransition();
    }

    private void Highlight(int firstPhysical, int lastPhysical)
    {
        var caret = code.SelectionStart;
        code.SelectAll();
        code.SelectionBackColor = Color.White;
        var start = code.GetFirstCharIndexFromLine(firstPhysical - 1);
        var after = code.GetFirstCharIndexFromLine(lastPhysical);
        if (start >= 0)
        {
            code.Select(start, (after < 0 ? code.TextLength : after) - start);
            code.SelectionBackColor = Color.FromArgb(255, 239, 202);
            code.Select(start, 0);
            code.ScrollToCaret();
        }
        else
        {
            code.Select(Math.Min(caret, code.TextLength), 0);
        }
    }

    private void SelectTransition()
    {
        if (updating || project is null || transitions.SelectedRows.Count == 0 || transitions.SelectedRows[0].Tag is not Transition t || activeTransition == t.Id)
        {
            return;
        }

        updating = true;
        try
        {
            lastTransition = activeTransition = t.Id;
            var blocks = t.Blocks.ToHashSet();
            trajectory.SelectBlocks(blocks);
            var moves = project.Verification.Moves.Where(m => blocks.Contains(m.Line)).ToList();
            if (moves.Count != 0)
            {
                Highlight(moves.First().PhysicalLine, moves.Last().PhysicalLine);
            }

            selection.Text = t.Id + "  " + t.Title;
            detail.Text = ParsedNcSummary.From(project.Verification.Moves, blocks).Display;
        }
        finally
        {
            updating = false;
        }
    }

    private void SelectFrame()
    {
        if (updating || project is null)
        {
            return;
        }

        updating = true;
        try
        {
            activeTransition = null;
            var physical = code.GetLineFromCharIndex(code.SelectionStart) + 1;
            if (code.SelectionLength == 0)
            {
                Highlight(physical, physical);
            }

            var move = project.Verification.Moves.FirstOrDefault(m => m.PhysicalLine == physical);
            trajectory.SelectBlocks(move is null ? [] : [move.Line]);
            transitions.ClearSelection();
            if (move is null)
            {
                detail.Text = ParsedNcSummary.From(project.Verification.Moves).Display;
                selection.Text = "Траектория операции 010";
                return;
            }

            foreach (DataGridViewRow row in transitions.Rows)
            {
                if (row.Tag is Transition t && t.Blocks.Contains(move.Line))
                {
                    row.Selected = true;
                    lastTransition = t.Id;
                }
            }

            selection.Text = $"Кадр N{move.Line}  {move.Mode}";
            detail.Text = $"Кадр N{move.Line}    T{move.Tool}/D1    {move.Mode}    X {NumericText.Format(move.X)}    Z {NumericText.Format(move.Z)}" + (move.Mode == "G1" ? $"    Подача {NumericText.Format(move.Feed!.Value)} мм/об    Частота {NumericText.Format(move.Rpm!.Value)} об/мин" : "");
        }
        finally
        {
            updating = false;
        }
    }

    internal void SelectTransitionForSmoke(int index)
    {
        transitions.ClearSelection();
        transitions.CurrentCell = transitions.Rows[index].Cells[0];
        transitions.Rows[index].Selected = true;
        SelectTransition();
    }

    internal void SelectFrameForSmoke(int physicalLine)
    {
        code.Select(code.GetFirstCharIndexFromLine(physicalLine - 1), 0);
    }

    internal void CaptureImage(string path)
    {
        Refresh();
        Application.DoEvents();
        using var bmp = new Bitmap(Width, Height);
        DrawToBitmap(bmp, new(Point.Empty, bmp.Size));
        bmp.Save(path);
    }
}
