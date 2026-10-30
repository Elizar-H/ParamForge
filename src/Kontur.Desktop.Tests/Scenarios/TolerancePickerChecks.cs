using System.Reflection;
using System.Text.Json;
using Kontur.Core;

namespace Kontur.Desktop;

internal static partial class UiSmoke
{
    private static void RunTolerancePickerChecks(MainForm form, string folder, Action<string, bool, object?> check)
    {
        form.SetToleranceView(false);
        form.ClientSize = new Size(1280, 850);
        form.SetParameters(Engine.Defaults);
        form.GenerateNow();
        RenderDrawing(form);
        var windows = Application.OpenForms.Count;
        OpenSideEditor(form, "length");
        var editor = form.ActiveDimensionEditor!;
        check(
            "v27_editor_is_part_of_main_window",
            editor.FindForm() == form && editor.Visible && Application.OpenForms.Count == windows,
            null
        );
        editor.SelectStandard("IT10");
        RenderDrawing(form);
        form.CaptureForm(Path.Combine(folder, "50-inline-length-editor.png"));
        CheckPickerLayout(editor, "length", check);
        var cancelState = JsonSerializer.Serialize(form.CurrentProject);
        editor.NominalInput.Text = "118";
        PressDialogKey(editor, Keys.Escape);
        check(
            "v27_cancel_restores_geometry_panel",
            form.ActiveDimensionEditor is null && JsonSerializer.Serialize(form.CurrentProject) == cancelState && Descendants(form).Any(c => c.Name == "dimension_edit_length" && c.Visible),
            null
        );
        OpenSideEditor(form, "length");
        editor = form.ActiveDimensionEditor!;
        editor.NominalInput.Text = "118";
        editor.CancelEditorButton.Focus();
        PressDialogKey(editor, Keys.Enter);
        check(
            "v27_enter_on_cancel_discards_draft",
            form.ActiveDimensionEditor is null && JsonSerializer.Serialize(form.CurrentProject) == cancelState,
            null
        );

        OpenSideEditor(form, "boreDiameter");
        editor = form.ActiveDimensionEditor!;
        editor.Picker!.ChoiceButton("JS7").PerformClick();
        RenderDrawing(form);
        editor.Picker.ChoiceButton("H8").Focus();
        PressDialogKey(editor, Keys.Enter);
        check(
            "v27_enter_on_class_selects_without_applying",
            ReferenceEquals(editor, form.ActiveDimensionEditor) && editor.Picker.SelectedDesignation == "H8" && JsonSerializer.Serialize(form.CurrentProject) == cancelState,
            null
        );
        editor.SelectStandard("JS7");
        editor.FocusNominal();
        form.CaptureForm(Path.Combine(folder, "51-inline-bore-editor.png"));
        CheckPickerLayout(editor, "bore", check);
        typeof(MainForm).GetMethod("EditDimension", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(form, ["boreDiameter"]);
        check(
            "v27_repeated_selection_keeps_one_editor",
            ReferenceEquals(editor, form.ActiveDimensionEditor) && Application.OpenForms.Count == windows,
            null
        );
        editor.NominalInput.Text = "50,001";
        editor.NominalInput.Focus();
        PressDialogKey(editor, Keys.Enter);
        check(
            "v27_apply_commits_standard_and_recalculates",
            form.ActiveDimensionEditor is null && form.CurrentProject!.Params.BoreTolerance == "JS7" && form.GetInput("boreLowerDeviation") == "-0,015" && !form.IsStale,
            null
        );

        form.SetParameters(Engine.Defaults);
        form.GenerateNow();
        OpenSideEditor(form, "length");
        editor = form.ActiveDimensionEditor!;
        editor.NominalInput.Text = "abc";
        typeof(MainForm).GetMethod("EditDimension", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(form, ["boreDiameter"]);
        check(
            "v27_invalid_draft_stays_visible_on_switch",
            ReferenceEquals(editor, form.ActiveDimensionEditor) && editor.ErrorText == "Введите число.",
            null
        );
        editor.NominalInput.Text = "118";
        typeof(MainForm).GetMethod("EditDimension", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(form, ["boreDiameter"]);
        check(
            "v27_switch_accepts_valid_draft",
            form.ActiveDimensionEditor?.DimensionKey == "boreDiameter" && form.GetInput("length") == "118" && !form.IsStale,
            null
        );
        form.ActiveDimensionEditor!.CancelEditorButton.PerformClick();

        foreach (var key in new[] { "length", "boreDiameter" })
        {
            form.Size = new Size(1100, 720);
            OpenSideEditor(form, key);
            editor = form.ActiveDimensionEditor!;
            editor.SelectStandard(key == "length" ? "IT16" : "JS16");
            RenderDrawing(form);
            CheckPickerLayout(editor, key + "_minimum", check);
            var scroll = Descendants(editor).OfType<Panel>().Single(c => c.AutoScroll);
            check(
                "v27_no_horizontal_scroll_" + key,
                !scroll.HorizontalScroll.Visible && editor.ApplyButton.Visible && editor.ApplyButton.Bottom <= editor.ApplyButton.Parent!.ClientSize.Height,
                null
            );
            check("v27_selected_details_stay_visible_" + key, FullyVisible(editor.NominalInput, form) && FullyVisible(editor.LowerInput!, form) && FullyVisible(editor.UpperInput!, form)
                && FullyVisible(Descendants(editor).OfType<Label>().Single(l => l.Text.StartsWith("Границы:", StringComparison.Ordinal)), form), null);
            check(
                "v27_sidebar_preserves_drawing_" + key,
                form.Sketch.LabelsInside && !form.Sketch.LabelsOverlap && form.Sketch.LabelsClearOfDimensionLines,
                null
            );
            form.CaptureForm(Path.Combine(folder, "52-inline-minimum-" + key + ".png"));
            editor.CancelEditorButton.PerformClick();
        }
        form.ClientSize = new Size(1280, 850);
        form.SetParameters(Engine.Defaults);
        form.GenerateNow();
        foreach (var targetDpi in new[] { 96, 120, 144, 192 })
        {
            using var scaled = new MainForm();
            scaled.Show();
            Application.DoEvents();
            OpenSideEditor(scaled, "boreDiameter");
            var scaledEditor = scaled.ActiveDimensionEditor!;
            scaledEditor.SelectStandard("JS7");
            scaled.SimulateDpiText(targetDpi);
            scaledEditor.PerformLayout();
            RenderDrawing(scaled);
            CheckCaptions(scaled, "editor_scale_" + targetDpi, check);
            CheckPickerLayout(scaledEditor, "scale_" + targetDpi, check);
            var scroll = Descendants(scaledEditor).OfType<Panel>().Single(c => c.AutoScroll);
            check("v27_scale_details_and_actions_" + targetDpi, !scroll.HorizontalScroll.Visible && FullyVisible(scaledEditor.NominalInput, scaled)
                && FullyVisible(scaledEditor.LowerInput!, scaled) && FullyVisible(scaledEditor.ApplyButton, scaled), null);
            scaled.CaptureForm(Path.Combine(folder, $"53-editor-scale-{targetDpi}.png"));
            scaledEditor.CancelEditorButton.PerformClick();
            scaled.Close();
        }
    }
    private static void OpenSideEditor(MainForm form, string key)
    {
        Descendants(form).OfType<Button>().Single(b => b.Name == "dimension_edit_" + key).PerformClick();
        form.PerformLayout();
        form.Refresh();
        Application.DoEvents();
    }
    private static void CheckPickerLayout(DimensionEditor editor, string name, Action<string, bool, object?> check)
    {
        var buttons = Descendants(editor.Picker!).OfType<Button>().ToArray();
        var evidence = buttons.Select(button => new
        {
            button.Text,
            button.Width,
            button.Height,
            measured = TextRenderer.MeasureText(button.Text, button.Font, Size.Empty, TextFormatFlags.SingleLine)
        }).ToArray();
        check(
            "v27_picker_choices_fit_" + name,
            evidence.All(b => b.Width >= b.measured.Width + 6 && b.Height >= b.measured.Height + 4),
            evidence
        );
        var labels = Descendants(editor.Picker!).OfType<Label>().Select(label => new
        {
            label.Text,
            label.Width,
            label.Height,
            measured = TextRenderer.MeasureText(label.Text, label.Font, Size.Empty, TextFormatFlags.SingleLine)
        }).ToArray();
        check(
            "v27_picker_labels_fit_" + name,
            labels.All(l => l.Width >= l.measured.Width + 6 && l.Height >= l.measured.Height),
            labels
        );
        check(
            "v27_picker_has_no_resizable_grid_" + name,
            !Descendants(editor).OfType<DataGridView>().Any() && buttons.Count(b => b.BackColor == Theme.Accent) == 1,
            null
        );
    }
    private static bool FullyVisible(Control control, Control root)
    {
        var bounds = control.Parent!.RectangleToScreen(control.Bounds);
        for (var parent = control.Parent; parent is not null; parent = parent.Parent)
        {
            if (!parent.RectangleToScreen(parent.ClientRectangle).Contains(bounds))
            {
                return false;
            }

            if (parent == root)
            {
                break;
            }
        }
        return control.Visible;
    }
}
