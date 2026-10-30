namespace Kontur.Desktop;

internal static class NominalNavigation
{
    internal static void Attach(IReadOnlyList<TextBox> fields, Action? beforeMove = null)
    {
        for (var index = 0; index < fields.Count; index++)
        {
            var current = index;
            var field = fields[index];
            field.PreviewKeyDown += (_, e) => { if (e.KeyCode is Keys.Up or Keys.Down) { e.IsInputKey = true; } };
            field.KeyDown += (_, e) =>
            {
                if (e.Modifiers != Keys.None || e.KeyCode is not (Keys.Up or Keys.Down))
                {
                    return;
                }

                e.Handled = e.SuppressKeyPress = true;
                beforeMove?.Invoke();
                var next = Math.Clamp(current + (e.KeyCode == Keys.Down ? 1 : -1), 0, fields.Count - 1);
                fields[next].Focus();
                fields[next].SelectAll();
            };
        }
    }
}
