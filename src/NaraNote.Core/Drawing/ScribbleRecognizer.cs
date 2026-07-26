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

        var targets = existing.Where(s => s.Points.Count > 1 && IsNear(points, s.Points, _options.HitTolerance)).Select(s => s.Id).ToArray();
        var score = Math.Min(1, .2 * dominance / _options.MinimumHorizontalDominance + .2 * changes / _options.MinimumDirectionChanges + .2 * _options.MaximumCompactness / Math.Max(compactness, .05) + .2 * pathRatio / _options.MinimumPathToChordRatio + (targets.Length > 0 ? .2 : 0));
        return new(true, score, targets);
    }

    private static bool IsNear(IReadOnlyList<InkPointData> a, IReadOnlyList<InkPointData> b, double tolerance)
    {
        var toleranceSquared = tolerance * tolerance;
        for (var i = 0; i < a.Count; i += Math.Max(1, a.Count / 30))
            for (var j = 0; j < b.Count; j += Math.Max(1, b.Count / 30))
                if (Squared(a[i], b[j]) <= toleranceSquared) return true;
        return false;
    }
    private static double Distance(InkPointData a, InkPointData b) => Math.Sqrt(Squared(a, b));
    private static double Squared(InkPointData a, InkPointData b) { var x = a.X - b.X; var y = a.Y - b.Y; return x * x + y * y; }
}
