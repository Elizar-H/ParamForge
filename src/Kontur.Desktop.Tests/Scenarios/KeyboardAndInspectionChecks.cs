using System.Reflection;
using Kontur.Core;
namespace Kontur.Desktop;

internal static partial class UiSmoke
{
    private static void RunKeyboardAndInspectionChecks(MainForm form, string folder, Action<string, bool, object?> check)
    {
        form.SetParameters(Engine.Defaults with
        {
            AutoStock = true
        });
        form.GenerateNow();
        var keys = new[] { "length", "flangeDiameter", "boreDiameter", "counterboreDiameter", "counterboreDepth", "bodyDiameter", "flangeWidth" };
        var menu = Descendants(form).OfType<MenuStrip>().Single();
        check(
            "v211_wheel_has_no_menu_switch",
            form.Sketch.WheelEditingEnabled && menu.Items.OfType<ToolStripMenuItem>().SelectMany(m => m.DropDownItems.OfType<ToolStripItem>()).All(i => !i.Text!.Contains("колес", StringComparison.OrdinalIgnoreCase)),
            null
        );
        for (var i = 0; i < keys.Length - 1; i++)
        {
            form.NominalField(keys[i]).Focus();
            PressNominalKey(form, keys[i], Keys.Down);
            var next = form.NominalField(keys[i + 1]);
            check("v211_down_focuses_and_selects_" + keys[i + 1], next.Focused && next.SelectedText == next.Text, null);
            PressNominalKey(form, keys[i + 1], Keys.Up);
            var previous = form.NominalField(keys[i]);
            check("v211_up_focuses_and_selects_" + keys[i], previous.Focused && previous.SelectedText == previous.Text, null);
        }
        form.NominalField("length").Text = "118";
        PressNominalKey(form, "length", Keys.Down);
        check(
            "v211_keyboard_confirms_valid_nominal_before_next_field",
            form.CurrentProject!.Params.Length == 118 && form.NominalField("flangeDiameter").Focused,
            null
        );
        form.NominalField("flangeDiameter").Text = "90";
        PressNominalKey(form, "flangeDiameter", Keys.Down);
        check(
            "v211_keyboard_can_continue_through_incomplete_geometry",
            form.GetInput("flangeDiameter") == "90" && form.NominalField("boreDiameter").Focused && form.IsStale,
            null
        );
        form.SetParameters(Engine.Defaults with
        {
            AutoStock = true
        });
        form.GenerateNow();
        using (var processing = new ProcessingDialog(form.CurrentProject!.Params, [], true))
        {
            processing.Show(form);
            Application.DoEvents();
            processing.Field("length").Focus();
            typeof(Control).GetMethod("OnKeyDown", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(processing.Field("length"), [new KeyEventArgs(Keys.Down)]);
            check(
                "v211_target_table_supports_nominal_arrows",
                processing.Field("flangeDiameter").Focused && processing.Field("flangeDiameter").SelectedText == processing.Field("flangeDiameter").Text,
                null
            );
            processing.Close();
        }
        form.OpenInspection();
        var view = form.Inspection!;
        view.Refresh();
        Application.DoEvents();
        check(
            "v211_viewer_opens_with_full_program_context",
            view.AllTransitions.Checked && view.Trajectory.ShowAll && view.Trajectory.VisibleSegmentCount > view.SelectedMoveCount,
            null
        );
        var grid = view.TransitionTable;
        check(
            "v211_headers_keep_dark_text_on_light_background",
            grid.ColumnHeadersDefaultCellStyle.SelectionBackColor == Theme.Surface && grid.ColumnHeadersDefaultCellStyle.SelectionForeColor == Theme.Ink,
            null
        );
        view.SelectTransitionForSmoke(2);
        var id = view.SelectedTransitionId;
        var count = view.SelectedMoveCount;
        var detail = view.DetailText;
        grid.CurrentCell = grid.Rows[2].Cells[1];
        typeof(DataGridView).GetMethod("OnCellClick", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(grid, [new DataGridViewCellEventArgs(1, 2)]);
        check(
            "v211_changing_columns_keeps_transition_and_details",
            view.SelectedTransitionId == id && view.SelectedMoveCount == count && view.DetailText == detail,
            null
        );
        check(
            "v211_plot_caption_identifies_selected_transition",
            view.SelectionText.Contains("010.03", StringComparison.Ordinal) && view.SelectionText.Contains("Тело", StringComparison.Ordinal),
            view.SelectionText
        );
        view.ControllerChoice.SelectedIndex = 1;
        check(
            "v211_viewer_controller_updates_main_and_program",
            form.CurrentProject!.Params.Controller == CncProfiles.Sinumerik && view.CurrentProjectId == form.CurrentProject.Id && view.SelectedTransitionId == id && form.CurrentProject.NcFileName.EndsWith(".MPF", StringComparison.Ordinal),
            null
        );
        view.ControllerChoice.SelectedIndex = 0;
        check(
            "v211_viewer_controller_returns_to_axioma",
            form.CurrentProject!.Params.Controller == CncProfiles.Axioma && view.SelectedTransitionId == id && !form.IsStale,
            null
        );
        view.Refresh();
        Application.DoEvents();
        check(
            "v211_plot_has_stock_grid_and_tool_marker",
            view.Trajectory.StockRendered && view.Trajectory.GridLineCount > 0 && view.Trajectory.MarkerTool == 1,
            null
        );
        view.CaptureImage(Path.Combine(folder, "90-full-trajectory.png"));
        using (var timer = new System.Windows.Forms.Timer { Interval = 80 })
        {
            timer.Tick += (_, _) =>
            {
                if (Application.OpenForms.OfType<ProcessingDialog>().FirstOrDefault() is not { } dialog)
                {
                    return;
                }

                timer.Stop();
                dialog.Field("bodyDiameter").Text = "98";
                dialog.Field("bodyLowerDeviation").Text = "-0,1";
                dialog.Field("bodyUpperDeviation").Text = "0,3";
                Descendants(dialog).OfType<Button>().Single(b => b.Text == "Применить").PerformClick();
            };
            timer.Start();
            view.ParametersButton.PerformClick();
            timer.Stop();
        }
        check(
            "v211_viewer_parameter_editor_updates_one_shared_project",
            form.CurrentProject!.Params.BodyDiameter == 98 && form.CurrentProject.Calculations.TargetBodyDiameter == 98.1 && view.CurrentProjectId == form.CurrentProject.Id && view.SelectedTransitionId == id,
            null
        );
        view.SelectTransitionForSmoke(3);
        view.Refresh();
        view.CaptureImage(Path.Combine(folder, "91-viewer-edited-parameters.png"));
        view.Size = view.MinimumSize;
        view.Refresh();
        Application.DoEvents();
        var layout = Descendants(view).OfType<TableLayoutPanel>().First(t => t.RowCount == 4);
        var selectedBounds = grid.GetRowDisplayRectangle(grid.CurrentCell!.RowIndex, false);
        check("v211_minimum_viewer_controls_visible", FullyVisible(view.ControllerChoice, view) && FullyVisible(view.ParametersButton, view) && view.Trajectory.Width >= 300 && view.Trajectory.Height >= 240,
            new
            {
                view.Trajectory.Width,
                view.Trajectory.Height,
                view.ClientSize,
                root = layout.ClientSize,
                rows = layout.GetRowHeights(),
                style = layout.RowStyles[1].Height
            });
        check(
            "v211_minimum_table_keeps_selected_row_whole",
            selectedBounds.Top >= grid.ColumnHeadersHeight && selectedBounds.Bottom <= grid.ClientSize.Height,
            new
            {
                selectedBounds,
                grid.ClientSize
            }
        );
        view.CaptureImage(Path.Combine(folder, "92-viewer-minimum.png"));
        view.Close();
        form.SetParameters(Engine.Defaults with
        {
            AutoStock = true
        });
        form.GenerateNow();
    }
}
