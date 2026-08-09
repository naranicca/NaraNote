using NaraNote.Core.Models;

namespace NaraNote.Core.Drawing;

public sealed record ScribbleOptions(
    long MaximumDurationMs = 1800, double MinimumHorizontalDominance = 2.2,
    int MinimumDirectionChanges = 3, double MaximumCompactness = .65,
    double MinimumPathToChordRatio = 3.0, double HitTolerance = 8);

public sealed record ScribbleResult(bool IsScribble, double Score, IReadOnlyList<Guid> TargetStrokeIds);

public sealed class ScribbleRecognizer(ScribbleOptions? options = null)
{
    private readonly ScribbleOptions _options = options ?? new();

    public ScribbleResult Analyze(IReadOnlyList<InkPointData> points, IEnumerable<InkStrokeElement> existing)
    {
        if (points.Count < 6) return new(false, 0, []);
        double dx = 0, dy = 0, length = 0;
        var changes = 0; var previousSign = 0;
        for (var i = 1; i < points.Count; i++)
        {
            var sx = points[i].X - points[i - 1].X; var sy = points[i].Y - points[i - 1].Y;
            dx += Math.Abs(sx); dy += Math.Abs(sy); length += Math.Sqrt(sx * sx + sy * sy);
            var sign = Math.Abs(sx) < 1 ? 0 : Math.Sign(sx);
            if (sign != 0) { if (previousSign != 0 && sign != previousSign) changes++; previousSign = sign; }
        }
        var minX = points.Min(p => p.X); var maxX = points.Max(p => p.X);
        var minY = points.Min(p => p.Y); var maxY = points.Max(p => p.Y);
        var chord = Distance(points[0], points[^1]);
        var duration = Math.Max(0, points[^1].TimestampMs - points[0].TimestampMs);
        var dominance = dx / Math.Max(dy, .01);
        var compactness = (maxY - minY) / Math.Max(maxX - minX, .01);
        var pathRatio = length / Math.Max(chord, 1);
        var geometric = duration <= _options.MaximumDurationMs && dominance >= _options.MinimumHorizontalDominance &&
                        changes >= _options.MinimumDirectionChanges && compactness <= _options.MaximumCompactness &&
                        pathRatio >= _options.MinimumPathToChordRatio;
        if (!geometric) return new(false, 0, []);

        var region = ConvexHull(points);
        var targets = existing
            .Where(s => s.Points.Count > 1 && OverlapsScribbleRegion(region, minX, minY, maxX, maxY, s, _options.HitTolerance))
            .Select(s => s.Id)
            .ToArray();
        var score = Math.Min(1, .2 * dominance / _options.MinimumHorizontalDominance + .2 * changes / _options.MinimumDirectionChanges + .2 * _options.MaximumCompactness / Math.Max(compactness, .05) + .2 * pathRatio / _options.MinimumPathToChordRatio + (targets.Length > 0 ? .2 : 0));
        return new(true, score, targets);
    }

    private static bool OverlapsScribbleRegion(IReadOnlyList<InkPointData> region, double minX, double minY, double maxX, double maxY, InkStrokeElement stroke, double tolerance)
    {
        var strokeRadius = Math.Max(0, stroke.Thickness) / 2;
        var margin = tolerance + strokeRadius;
        var strokeMinX = stroke.Points.Min(p => p.X);
        var strokeMaxX = stroke.Points.Max(p => p.X);
        var strokeMinY = stroke.Points.Min(p => p.Y);
        var strokeMaxY = stroke.Points.Max(p => p.Y);
        if (strokeMaxX < minX - margin || strokeMinX > maxX + margin ||
            strokeMaxY < minY - margin || strokeMinY > maxY + margin) return false;

        if (region.Count >= 3 && stroke.Points.Any(point => IsInside(point, region))) return true;
        var marginSquared = margin * margin;
        for (var i = 1; i < stroke.Points.Count; i++)
        {
            var strokeStart = stroke.Points[i - 1];
            var strokeEnd = stroke.Points[i];
            var edgeCount = region.Count >= 3 ? region.Count : Math.Max(0, region.Count - 1);
            for (var j = 0; j < edgeCount; j++)
            {
                var regionStart = region[j];
                var regionEnd = region[(j + 1) % region.Count];
                if (SegmentsIntersect(strokeStart, strokeEnd, regionStart, regionEnd) ||
                    SegmentDistanceSquared(strokeStart, strokeEnd, regionStart, regionEnd) <= marginSquared) return true;
            }
        }
        return false;
    }

    private static List<InkPointData> ConvexHull(IReadOnlyList<InkPointData> points)
    {
        var sorted = points.DistinctBy(p => (p.X, p.Y)).OrderBy(p => p.X).ThenBy(p => p.Y).ToList();
        if (sorted.Count <= 2) return sorted;
        var lower = new List<InkPointData>();
        foreach (var point in sorted)
        {
            while (lower.Count >= 2 && Cross(lower[^2], lower[^1], point) <= 0) lower.RemoveAt(lower.Count - 1);
            lower.Add(point);
        }
        var upper = new List<InkPointData>();
        for (var i = sorted.Count - 1; i >= 0; i--)
        {
            var point = sorted[i];
            while (upper.Count >= 2 && Cross(upper[^2], upper[^1], point) <= 0) upper.RemoveAt(upper.Count - 1);
            upper.Add(point);
        }
        lower.RemoveAt(lower.Count - 1);
        upper.RemoveAt(upper.Count - 1);
        lower.AddRange(upper);
        return lower;
    }

    private static bool IsInside(InkPointData point, IReadOnlyList<InkPointData> polygon)
    {
        var inside = false;
        for (var i = 0; i < polygon.Count; i++)
        {
            var a = polygon[i]; var b = polygon[(i + 1) % polygon.Count];
            if ((a.Y > point.Y) != (b.Y > point.Y) &&
                point.X < (b.X - a.X) * (point.Y - a.Y) / (b.Y - a.Y) + a.X) inside = !inside;
        }
        return inside;
    }

    private static bool SegmentsIntersect(InkPointData a, InkPointData b, InkPointData c, InkPointData d)
    {
        const double epsilon = .0001;
        var abC = Cross(a, b, c); var abD = Cross(a, b, d);
        var cdA = Cross(c, d, a); var cdB = Cross(c, d, b);
        if (((abC > epsilon && abD < -epsilon) || (abC < -epsilon && abD > epsilon)) &&
            ((cdA > epsilon && cdB < -epsilon) || (cdA < -epsilon && cdB > epsilon))) return true;
        return Math.Abs(abC) <= epsilon && IsOnSegment(a, b, c) || Math.Abs(abD) <= epsilon && IsOnSegment(a, b, d) ||
               Math.Abs(cdA) <= epsilon && IsOnSegment(c, d, a) || Math.Abs(cdB) <= epsilon && IsOnSegment(c, d, b);
    }

    private static bool IsOnSegment(InkPointData a, InkPointData b, InkPointData point) =>
        point.X >= Math.Min(a.X, b.X) && point.X <= Math.Max(a.X, b.X) &&
        point.Y >= Math.Min(a.Y, b.Y) && point.Y <= Math.Max(a.Y, b.Y);

    private static double SegmentDistanceSquared(InkPointData a, InkPointData b, InkPointData c, InkPointData d) =>
        Math.Min(Math.Min(PointSegmentDistanceSquared(a, c, d), PointSegmentDistanceSquared(b, c, d)),
                 Math.Min(PointSegmentDistanceSquared(c, a, b), PointSegmentDistanceSquared(d, a, b)));

    private static double PointSegmentDistanceSquared(InkPointData point, InkPointData a, InkPointData b)
    {
        var dx = b.X - a.X; var dy = b.Y - a.Y;
        if (Math.Abs(dx) < .0001 && Math.Abs(dy) < .0001) return Squared(point, a);
        var t = Math.Clamp(((point.X - a.X) * dx + (point.Y - a.Y) * dy) / (dx * dx + dy * dy), 0, 1);
        var x = point.X - (a.X + t * dx); var y = point.Y - (a.Y + t * dy);
        return x * x + y * y;
    }

    private static double Cross(InkPointData a, InkPointData b, InkPointData c) =>
        (b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X);
    private static double Distance(InkPointData a, InkPointData b) => Math.Sqrt(Squared(a, b));
    private static double Squared(InkPointData a, InkPointData b) { var x = a.X - b.X; var y = a.Y - b.Y; return x * x + y * y; }
}
