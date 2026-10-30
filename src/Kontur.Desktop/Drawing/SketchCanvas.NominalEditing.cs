using System.ComponentModel;
namespace Kontur.Desktop;

internal sealed partial class SketchCanvas : IMessageFilter
{
    private string? wheelKey;
    private int wheelRemainder;
    internal event Action<string>? NominalRequested;
    internal event Func<string, double, bool>? NominalSubmitted;
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal bool WheelEditingEnabled { get; set; } = true;
    // A protected area around every number prevents an adjacent line from being
    // mistaken for a drag handle when the user is trying to edit the number.
    private string? HitCaption(Point point) => labels.Where(pair =>
        {
            var area = pair.Value;
            area.Inflate(12 * DeviceDpi / 96F, 8 * DeviceDpi / 96F);
            return area.Contains(point);
        }).OrderBy(pair => Math.Pow(point.X - pair.Value.Left - pair.Value.Width / 2, 2) + Math.Pow(point.Y - pair.Value.Top - pair.Value.Height / 2, 2))
        .Select(pair => pair.Key).FirstOrDefault();
    private string? HitDragLine(Point point) => dragHandles.Select(pair => (pair.Key, Distance: SegmentDistance(point, pair.Value.A, pair.Value.B)))
        .Where(hit => hit.Distance <= 5 * DeviceDpi / 96F).OrderBy(hit => hit.Distance).Select(hit => hit.Key).FirstOrDefault();
    private void OpenNominalEditor(string key) => NominalRequested?.Invoke(key);
    internal bool AdjustNominalWheel(string key, int delta, Keys modifiers)
    {
        if (parameters is null || drag is not null)
        {
            return false;
        }

        if (wheelKey != key)
        {
            wheelKey = key;
            wheelRemainder = 0;
        }
        wheelRemainder += delta;
        var ticks = wheelRemainder / SystemInformation.MouseWheelScrollDelta;
        wheelRemainder %= SystemInformation.MouseWheelScrollDelta;
        if (ticks == 0)
        {
            return true;
        }

        var step = modifiers.HasFlag(Keys.Control) ? .001m : modifiers.HasFlag(Keys.Shift) ? .1m : .01m;
        var original = (double)typeof(Kontur.Core.Parameters).GetProperty(char.ToUpperInvariant(key[0]) + key[1..])!.GetValue(parameters)!;
        var accepted = NominalSubmitted?.Invoke(key, (double)Math.Round((decimal)original + step * ticks, 3, MidpointRounding.AwayFromZero)) == true;
        if (accepted)
        {
            hovered = key;
            hoverLine = false;
            Cursor = Cursors.Hand;
            Invalidate();
        }
        return accepted;
    }
    protected override void OnMouseWheel(MouseEventArgs e)
    {
        if (WheelEditingEnabled && HitCaption(e.Location) is { } key)
        {
            AdjustNominalWheel(key, e.Delta, InputModifiers);
            return;
        }
        wheelKey = null;
        wheelRemainder = 0;
        base.OnMouseWheel(e);
    }
    public bool PreFilterMessage(ref Message m)
    {
        if (!WheelEditingEnabled || m.Msg != 0x20A || FindForm()?.ContainsFocus != true)
        {
            return false;
        }

        var position = m.LParam.ToInt64();
        var point = PointToClient(new Point(unchecked((short)position), unchecked((short)(position >> 16))));
        if (!ClientRectangle.Contains(point) || HitCaption(point) is not { } key)
        {
            return false;
        }

        AdjustNominalWheel(key, unchecked((short)(m.WParam.ToInt64() >> 16)), InputModifiers);
        return true;
    }
    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Application.AddMessageFilter(this);
    }
    protected override void OnHandleDestroyed(EventArgs e)
    {
        Application.RemoveMessageFilter(this);
        base.OnHandleDestroyed(e);
    }
}
