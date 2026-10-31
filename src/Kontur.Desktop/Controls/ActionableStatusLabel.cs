namespace Kontur.Desktop;

/// <summary>A diagnostic remains readable text and becomes a link when a field can be opened.</summary>
internal sealed class ActionableStatusLabel : LinkLabel
{
    private Func<string?>? resolve;
    private Action<string>? navigate;
    internal string? TargetField
    {
        get; private set;
    }

    internal ActionableStatusLabel()
    {
        Font = Theme.Small;
        Dock = DockStyle.Fill;
        Margin = Padding.Empty;
        AutoSize = false;
        LinkBehavior = LinkBehavior.HoverUnderline;
        TabStop = false;
        LinkClicked += (_, _) => Navigate();
    }

    internal void Bind(Func<string?> resolver, Action<string> action)
    {
        resolve = resolver;
        navigate = action;
        RefreshLink();
    }

    internal void Navigate()
    {
        if (TargetField is { } field)
        {
            navigate?.Invoke(field);
        }
    }

    protected override void OnTextChanged(EventArgs e)
    {
        base.OnTextChanged(e);
        RefreshLink();
    }

    protected override void OnForeColorChanged(EventArgs e)
    {
        base.OnForeColorChanged(e);
        RefreshLink();
    }

    private void RefreshLink()
    {
        TargetField = Text.Length > 0 && ForeColor == Theme.Error ? resolve?.Invoke() : null;
        Links.Clear();
        if (TargetField is not null)
        {
            Links.Add(0, Text.Length);
        }
        LinkColor = ActiveLinkColor = VisitedLinkColor = ForeColor;
        TabStop = TargetField is not null;
        Cursor = TargetField is null ? Cursors.Default : Cursors.Hand;
        AccessibleDescription = TargetField is null ? "" : "Открыть параметр для исправления";
    }
}
