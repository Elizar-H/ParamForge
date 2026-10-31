using System.Reflection;
using System.Text.Json;
using Kontur.Core;

namespace Kontur.Desktop;

internal static partial class UiSmoke
{
    private static void RunWorkspaceAndDiagnosticsChecks(MainForm form, string folder, Action<string, bool, object?> check)
    {
        form.Inspection?.Close();
        form.Processing?.Close();
        form.Workspace.SelectedIndex = 0;
        form.SetParameters(Engine.Defaults with
        {
            AutoStock = true
        });
        form.GenerateNow();
        check("v212_valid_status_is_plain_text", form.Diagnostic.TargetField is null && !form.Diagnostic.TabStop, null);
        form.SetInput("length", "ошибка");
        form.GenerateNow();
        form.Diagnostic.Navigate();
        check("v212_numeric_error_link_selects_nominal", form.Diagnostic.TargetField == "length" && form.NominalField("length").Focused && form.NominalField("length").SelectedText == "ошибка", null);

        form.SetParameters(Engine.Defaults with
        {
            StockBore = 18
        });
        form.GenerateNow();
        var windowCount = Application.OpenForms.Count;
        form.Diagnostic.Navigate();
        var editor = form.Processing!;
        check("v213_stock_error_opens_embedded_stock_section", editor.Sections.SelectedIndex == 0 && editor.Field("stockBore").Focused
            && editor.Field("stockBore").SelectedText == editor.Field("stockBore").Text && Application.OpenForms.Count == windowCount, null);
        editor.Close();

        form.SetParameters(Engine.Defaults with
        {
            AutoStock = true,
            BoreDiameter = 41
        });
        form.GenerateNow();
        check("v213_fit_error_targets_both_stock_constraints", form.Diagnostic.TargetField == "stockBore", form.StatusText);
        form.CaptureForm(Path.Combine(folder, "93-clickable-diagnostic.png"));
        form.Diagnostic.Navigate();
        editor = form.Processing!;
        check("v213_fit_error_explains_empty_bore_interval", editor.Sections.SelectedIndex == 0 && editor.StockMode.Focused
            && editor.BoreLimitsText.Contains("Ø44", StringComparison.Ordinal) && editor.BoreLimitsText.Contains("Ø41", StringComparison.Ordinal), editor.BoreLimitsText);
        form.CaptureForm(Path.Combine(folder, "94-stock-bore-limits.png"));
        typeof(LinkLabel).GetMethod("OnLinkClicked", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(editor.EditRetract, [new LinkLabelLinkClickedEventArgs(editor.EditRetract.Links[0])]);
        check("v213_retract_link_selects_actual_field", editor.Sections.SelectedIndex == 3 && editor.Field("clearanceX").Focused && editor.Field("clearanceX").SelectedText == "2", null);
        editor.Field("clearanceX").Text = "0,5";
        editor.ApplyButton.PerformClick();
        check("v213_retract_fix_recalculates_automatic_bore", !form.IsStale && form.CurrentProject!.Params.ClearanceX == .5
            && form.CurrentProject.Params.StockBore == 41 && form.Diagnostic.TargetField is null && form.Processing == editor && !editor.IsDirty, editor.ErrorText);
        editor.Close();

        form.SetParameters(Engine.Defaults with
        {
            AutoStock = true
        });
        form.GenerateNow();
        form.OpenInspection();
        var view = form.Inspection!;
        check("v212_trajectory_is_embedded_without_new_window", Application.OpenForms.Count == windowCount && view.FindForm() == form, null);
        view.SelectTransitionForSmoke(3);
        form.Workspace.SelectedIndex = 0;
        form.OpenInspection();
        check("v212_repeat_open_reuses_tab_and_selection", form.Workspace.TabCount == 2 && ReferenceEquals(view, form.Inspection) && view.SelectedTransitionId == "010.04", null);
        view.Refresh();
        check("v213_trajectory_has_arrows_for_cut_and_retract", view.Trajectory.WorkingArrowCount > 0 && view.Trajectory.RapidArrowCount > 0, new
        {
            view.Trajectory.WorkingArrowCount,
            view.Trajectory.RapidArrowCount
        });
        var moves = form.CurrentProject!.Verification.Moves;
        var retractIndex = Enumerable.Range(1, moves.Count - 1).First(i => moves[i].Mode == "G0" && moves[i].Tool == moves[i - 1].Tool
            && moves[i].Z == moves[i - 1].Z && moves[i].X != moves[i - 1].X);
        view.SelectFrameForSmoke(moves[retractIndex].PhysicalLine);
        view.Refresh();
        check("v213_radial_rapid_frame_shows_its_direction", view.Trajectory.RapidArrowCount == 1 && view.Trajectory.WorkingArrowCount == 0, null);
        view.SelectTransitionForSmoke(3);
        form.CaptureForm(Path.Combine(folder, "95-trajectory-tab.png"));

        var workspace = (WorkspaceTabs)form.Workspace;
        var geometry = workspace.TabPages[0];
        check("v213_geometry_tab_cannot_close", workspace.CloseBounds(geometry).IsEmpty, null);
        var page = workspace.SelectedTab!;
        var close = workspace.CloseBounds(page);
        var closePoint = new Point(close.Left + close.Width / 2, close.Top + close.Height / 2);
        var baseline = JsonSerializer.Serialize(form.CurrentProject);
        SendMouse(workspace, "OnMouseDown", closePoint);
        SendMouse(workspace, "OnMouseUp", Point.Empty);
        check("v213_drag_away_from_close_keeps_tab", !view.IsDisposed && workspace.TabCount == 2, null);
        SendMouse(workspace, "OnMouseDown", closePoint);
        SendMouse(workspace, "OnMouseUp", closePoint);
        check("v213_cross_closes_and_disposes_tab_without_changing_project", view.IsDisposed && form.Inspection is null && workspace.TabCount == 1
            && workspace.SelectedTab == geometry && baseline == JsonSerializer.Serialize(form.CurrentProject), null);
        form.OpenInspection();
        check("v213_closed_trajectory_can_reopen", form.Inspection is { IsDisposed: false } && !ReferenceEquals(view, form.Inspection), null);

        editor = OpenProcessingForSmoke(form);
        check("v213_processing_is_embedded_without_new_window", Application.OpenForms.Count == windowCount && workspace.TabCount == 3 && editor.FindForm() == form, null);
        check("v2131_clean_settings_do_not_offer_apply", !editor.ApplyButton.Enabled && !editor.IsDirty, null);
        var originalFeed = editor.Field("feedRough").Text;
        editor.Field("feedRough").Text = "0,25";
        check("v2131_new_edit_enables_apply", editor.ApplyButton.Enabled && editor.IsDirty && editor.ApplyButton.Text == "Применить", null);
        editor.Field("feedRough").Text = originalFeed;
        check("v2131_reverting_edit_disables_apply", !editor.ApplyButton.Enabled && !editor.IsDirty, null);
        editor.Field("feedRough").Text = "ошибка";
        editor.ApplyButton.PerformClick();
        check("v2131_invalid_edit_remains_unapplied", editor.ApplyButton.Enabled && editor.IsDirty && editor.ErrorText.Length != 0, editor.ErrorText);
        editor.Field("feedRough").Text = "0,25";
        form.CaptureForm(Path.Combine(folder, "100-settings-unsaved.png"));
        form.Workspace.SelectedIndex = 0;
        form.SetInput("length", "118");
        form.GenerateNow();
        OpenProcessingForSmoke(form);
        check("v213_settings_rebase_keeps_unsaved_field_and_updates_geometry", editor.Field("feedRough").Text == "0,25" && editor.Field("length").Text == "118", null);
        editor.ApplyButton.PerformClick();
        check("v213_settings_merge_does_not_overwrite_geometry", form.CurrentProject!.Params.Length == 118 && form.CurrentProject.Params.FeedRough == .25 && !form.IsStale, editor.ErrorText);
        check("v2131_successful_apply_is_muted_and_confirmed", !editor.ApplyButton.Enabled && !editor.IsDirty && editor.ApplyButton.Text == "Применено"
            && editor.ApplyButton.BackColor == Theme.Surface, null);
        form.CaptureForm(Path.Combine(folder, "101-settings-applied.png"));
        editor.StockMode.SelectedIndex = 1;
        editor.StockMode.SelectedIndex = 0;
        check("v2131_returning_to_saved_stock_mode_is_clean", !editor.ApplyButton.Enabled && !editor.IsDirty, null);
        editor.StockMode.SelectedIndex = 1;
        editor.Field("stockDiameter").Text = "128";
        editor.Field("stockBore").Text = "44";
        editor.ApplyButton.PerformClick();
        editor.StockMode.SelectedIndex = 0;
        editor.ApplyButton.PerformClick();
        editor.StockMode.SelectedIndex = 1;
        check("v213_manual_stock_preview_survives_apply", editor.Field("stockDiameter").Text == "128" && editor.Field("stockBore").Text == "44", null);
        editor.ApplyButton.PerformClick();
        check("v213_restored_manual_stock_is_applied", form.CurrentProject!.Params is { AutoStock: false, StockDiameter: 128, StockBore: 44 } && !form.IsStale, editor.ErrorText);
        editor.Field("feedRough").Text = "0,3";
        baseline = JsonSerializer.Serialize(form.CurrentProject);
        PressWorkspaceClose(form);
        check("v213_ctrl_w_cancels_unapplied_settings", form.Processing is null && editor.IsDisposed && baseline == JsonSerializer.Serialize(form.CurrentProject), null);
        form.OpenInspection();
        PressWorkspaceClose(form);
        check("v213_ctrl_w_closes_trajectory", form.Inspection is null && workspace.TabCount == 1, null);
        PressWorkspaceClose(form);
        check("v213_ctrl_w_preserves_geometry", workspace.TabCount == 1 && form.Visible, null);

        editor = OpenProcessingForSmoke(form);
        var nav = editor.Sections.Navigation;
        nav.Focus();
        PressProcessingKey(nav, Keys.Right);
        check("v213_sections_support_left_right", editor.Sections.SelectedIndex == 1, null);
        PressProcessingKey(nav, Keys.Down);
        check("v213_down_enters_selected_section", editor.Field("gripLength").Focused && editor.Field("gripLength").SelectedText == editor.Field("gripLength").Text, null);
        PressProcessingKey(editor.Field("gripLength"), Keys.Enter);
        check("v213_enter_moves_to_next_setting_without_applying", editor.Field("chuckClearance").Focused && form.Processing == editor, null);
        editor.FocusField("length");
        editor.Field("length").Text = "119";
        PressProcessingKey(editor.Field("length"), Keys.Enter);
        check("v213_enter_moves_down_nominal_column", editor.Field("flangeDiameter").Focused && form.CurrentProject!.Params.Length == 118, null);
        editor.Field("flangeDiameter").Text = "abc";
        PressProcessingKey(editor.Field("flangeDiameter"), Keys.Enter);
        check("v213_invalid_number_keeps_focus", editor.Field("flangeDiameter").Focused && editor.Field("flangeDiameter").SelectedText == "abc", null);
        editor.Close();
        editor = OpenProcessingForSmoke(form);
        form.CaptureForm(Path.Combine(folder, "98-processing-tab.png"));
        form.Size = form.MinimumSize;
        form.CaptureForm(Path.Combine(folder, "99-processing-minimum.png"));
        check("v213_processing_actions_fit_minimum", FullyVisible(editor.ApplyButton, form) && FullyVisible(editor.CancelEditorButton, form), null);
        check("v213_tab_captions_fit", workspace.TabPages.Cast<TabPage>().All(workspace.CaptionFits), null);
        editor.Close();
        CaptureViewMenu(form, Path.Combine(folder, "96-view-menu.png"), check);
        check("v213_view_menu_has_only_drawing_options", form.MainMenuStrip!.Items.OfType<ToolStripMenuItem>().Single(i => i.Text == "Вид").DropDownItems.Count == 2, null);
        form.SetToleranceView(false);
        form.SetParameters(Engine.Defaults);
        form.GenerateNow();
    }

    private static void PressProcessingKey(Control control, Keys key) => typeof(Control).GetMethod("OnKeyDown", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(control, [new KeyEventArgs(key)]);
    private static void PressWorkspaceClose(MainForm form) => typeof(MainForm).GetMethod("ProcessCmdKey", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(form, [new Message(), Keys.Control | Keys.W]);

    private static void CaptureViewMenu(MainForm form, string path, Action<string, bool, object?> check)
    {
        var menu = form.MainMenuStrip!.Items.OfType<ToolStripMenuItem>().Single(i => i.Text == "Вид");
        form.SetToleranceView(true);
        menu.ShowDropDown();
        Application.DoEvents();
        var dropdown = (ToolStripDropDownMenu)menu.DropDown;
        check("v212_menu_uses_one_check_margin", dropdown.ShowCheckMargin && !dropdown.ShowImageMargin && dropdown.Renderer is ApplicationMenuRenderer, null);
        using var bitmap = new Bitmap(dropdown.Width, dropdown.Height);
        dropdown.DrawToBitmap(bitmap, new Rectangle(Point.Empty, dropdown.Size));
        bitmap.Save(path);
        menu.HideDropDown();
    }
}
