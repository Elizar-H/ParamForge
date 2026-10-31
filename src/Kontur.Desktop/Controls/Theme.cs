namespace Kontur.Desktop;

internal static class Theme
{
    internal static readonly Color Ink = Color.FromArgb(40, 44, 49);
    internal static readonly Color Muted = Color.FromArgb(100, 106, 113);
    internal static readonly Color Border = Color.FromArgb(216, 220, 224);
    internal static readonly Color Surface = Color.FromArgb(246, 246, 245);
    internal static readonly Color Accent = Color.FromArgb(58, 64, 70);
    internal static readonly Color Error = Color.FromArgb(164, 45, 45);
    internal static readonly Font Body = new("Segoe UI", 9F);
    internal static readonly Font Small = new("Segoe UI", 8.5F);
    internal static readonly Font Strong = new("Segoe UI Semibold", 10F);
    internal static readonly Font Numeric = new("Segoe UI", 10.5F);
    internal static readonly Font Detail = new("Segoe UI", 9.5F);

    internal static Button Button(string text, bool primary = false) => new()
    {
        Text = text,
        Font = Body,
        AutoEllipsis = true,
        FlatStyle = FlatStyle.Flat,
        FlatAppearance = { BorderColor = primary ? Accent : Border, BorderSize = 1 },
        BackColor = primary ? Accent : Color.White,
        ForeColor = primary ? Color.White : Ink,
        UseVisualStyleBackColor = false,
        Height = 36,
        Padding = new Padding(7, 0, 7, 0),
        TextAlign = ContentAlignment.MiddleCenter,
        Cursor = Cursors.Hand,
        Margin = new Padding(4, 4, 4, 4)
    };

    internal static Label Label(string text, Font? font = null) => new()
    {
        Text = text,
        Font = font ?? Body,
        ForeColor = Ink,
        AutoSize = false,
        TextAlign = ContentAlignment.MiddleLeft,
        Dock = DockStyle.Fill,
        Margin = Padding.Empty
    };
}
