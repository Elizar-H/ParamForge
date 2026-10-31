namespace Kontur.Desktop;

internal sealed partial class ProcessingView
{
    private void AttachKeyboardNavigation()
    {
        Sections.EnterPageRequested += FocusFirstField;
        foreach (var box in boxes.Values)
        {
            box.PreviewKeyDown += (_, e) => { if (e.KeyCode is Keys.Up or Keys.Down or Keys.Enter) { e.IsInputKey = true; } };
            box.KeyDown += (_, e) =>
            {
                if (e.Modifiers != Keys.None || e.KeyCode is not (Keys.Up or Keys.Down or Keys.Enter))
                {
                    return;
                }
                e.Handled = e.SuppressKeyPress = true;
                if (e.KeyCode == Keys.Enter && Property(box.Name["processing_".Length..]).PropertyType == typeof(double) && !NumericText.TryParse(box.Text, out var parsed))
                {
                    errors.SetError(box, "Введите число.");
                    box.SelectAll();
                    return;
                }
                var fields = PageFields().Where(b => b.Parent != box.Parent || b.Parent is not TableLayoutPanel table
                    || table.GetColumn(b) == table.GetColumn(box)).ToArray();
                var index = Array.IndexOf(fields, box);
                var next = index + (e.KeyCode == Keys.Up ? -1 : 1);
                if (index < 0 || next < 0 || next >= fields.Length)
                {
                    Sections.Navigation.Focus();
                    return;
                }
                fields[next].Focus();
                fields[next].SelectAll();
            };
        }
        stockMode.PreviewKeyDown += (_, e) => { if (e.KeyCode == Keys.Enter) { e.IsInputKey = true; } };
        stockMode.KeyDown += (_, e) =>
        {
            if (e.KeyData != Keys.Enter)
            {
                return;
            }
            e.Handled = e.SuppressKeyPress = true;
            FocusFirstField(false);
        };
    }

    private IEnumerable<TextBox> PageFields() => boxes.Values.Where(b => b.Enabled && !b.ReadOnly && Sections.SelectedPage?.Contains(b) == true)
        .OrderBy(b => b.Parent is TableLayoutPanel table ? table.GetRow(b) : b.TabIndex);

    private void FocusFirstField() => FocusFirstField(true);
    private void FocusFirstField(bool includeMode)
    {
        if (includeMode && Sections.SelectedIndex == 0)
        {
            stockMode.Focus();
            return;
        }
        if (PageFields().FirstOrDefault() is { } first)
        {
            first.Focus();
            first.SelectAll();
        }
    }
}
