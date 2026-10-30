namespace Kontur.Desktop;

internal sealed partial class SketchCanvas
{
    private sealed record ContourEdge(string Key, PointF A, PointF B);
    private readonly List<ContourEdge> contourEdges = [];
    private static bool IsDiameter(string key) => key.Contains("Diameter", StringComparison.OrdinalIgnoreCase);
    private ContourEdge? HitContour(Point point, bool? horizontalMotion = null) => contourEdges
        .Where(edge => horizontalMotion is null || horizontalMotion == !IsDiameter(edge.Key))
        .Select(edge => (Edge: edge, Distance: SegmentDistance(point, edge.A, edge.B)))
        .Where(hit => hit.Distance <= 6 * DeviceDpi / 96F)
        .OrderBy(hit => hit.Distance).Select(hit => hit.Edge).FirstOrDefault();

    internal PointF ContourHandle(string key, bool lower = false)
    {
        var edges = contourEdges.Where(edge => edge.Key == key).ToArray();
        var edge = lower ? edges.Last() : edges.First();
        return new((edge.A.X + edge.B.X) / 2, (edge.A.Y + edge.B.Y) / 2);
    }
    private void BuildContourEdges(float left, float right, float xb, float xh, float cy, float rd, float rb, float ri, float rc)
    {
        foreach (var side in new[] { -1, 1 })
        {
            void Add(string key, float x1, float y1, float x2, float y2) => contourEdges.Add(new(key, new(x1, cy + side * y1), new(x2, cy + side * y2)));
            Add("bodyDiameter", left, rb, xb, rb);
            Add("flangeDiameter", xb, rd, right, rd);
            Add("boreDiameter", left, ri, xh, ri);
            Add("counterboreDiameter", xh, rc, right, rc);
            Add("length", left, ri, left, rb);
            Add("flangeWidth", xb, rb, xb, rd);
            Add("counterboreDepth", xh, ri, xh, rc);
        }
    }
}
