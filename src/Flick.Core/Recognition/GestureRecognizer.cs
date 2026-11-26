using Flick.Core.Models;

namespace Flick.Core.Recognition;

public readonly record struct RecognitionResult(GestureDefinition? Gesture, double Score);

/// <summary>
/// Orientation-preserving unistroke gesture recognizer using equidistant resampling,
/// aspect-preserving bounding-box normalization, and point-cloud Euclidean matching.
/// </summary>
public sealed class GestureRecognizer
{
    public const int SampleCount = 64;

    /// <summary>
    /// Recognizes a raw input stroke against the provided gesture library.
    /// </summary>
    public RecognitionResult Recognize(
        IReadOnlyList<GesturePoint> rawPoints,
        IEnumerable<GestureDefinition> templates,
        double threshold = 0.65)
    {
        var ranked = RecognizeRanked(rawPoints, templates, threshold);
        return ranked.Count > 0 ? ranked[0] : new RecognitionResult(null, 0);
    }

    /// <summary>
    /// Returns all templates scoring at or above <paramref name="threshold"/>, ordered by highest score first.
    /// Supports per-finger shape matching when both <paramref name="fingerPaths"/> and template <c>FingerStrokes</c>
    /// have matching finger counts.
    /// </summary>
    public List<RecognitionResult> RecognizeRanked(
        IReadOnlyList<GesturePoint> rawPoints,
        IEnumerable<GestureDefinition> templates,
        double threshold = 0.65,
        IReadOnlyList<IReadOnlyList<GesturePoint>>? fingerPaths = null)
    {
        var matches = new List<RecognitionResult>();
        if (rawPoints.Count < 2)
        {
            return matches;
        }

        var candidate = Normalize(rawPoints);
        if (candidate.Length == 0)
        {
            return matches;
        }

        foreach (var template in templates)
        {
            double score = 0;

            if (fingerPaths is { Count: > 1 } &&
                template.FingerStrokes is { Count: > 1 } &&
                fingerPaths.Count == template.FingerStrokes.Count)
            {
                score = ScoreMultiFinger(fingerPaths, template.FingerStrokes);
            }

            if (score <= 0 && template.Points.Count >= 2)
            {
                var normalizedTemplate = Normalize(template.Points);
                if (normalizedTemplate.Length > 0)
                {
                    double distance = AveragePointDistance(candidate, normalizedTemplate);
                    score = 1.0 / (1.0 + distance * 2.2);
                }
            }

            if (score >= threshold)
            {
                matches.Add(new RecognitionResult(template, score));
            }
        }

        matches.Sort((a, b) => b.Score.CompareTo(a.Score));
        return matches;
    }

    private static double ScoreMultiFinger(
        IReadOnlyList<IReadOnlyList<GesturePoint>> candidateFingers,
        IReadOnlyList<List<GesturePoint>> templateFingers)
    {
        double totalScore = 0;
        int valid = 0;
        for (int i = 0; i < candidateFingers.Count; i++)
        {
            var cNorm = Normalize(candidateFingers[i]);
            var tNorm = Normalize(templateFingers[i]);
            if (cNorm.Length == 0 || tNorm.Length == 0) continue;

            double dist = AveragePointDistance(cNorm, tNorm);
            totalScore += 1.0 / (1.0 + dist * 2.2);
            valid++;
        }

        return valid == candidateFingers.Count ? totalScore / valid : 0;
    }

    /// <summary>
    /// Computes the total Euclidean path length of a stroke.
    /// </summary>
    public static double ComputePathLength(IReadOnlyList<GesturePoint> points)
    {
        double length = 0;
        for (int i = 1; i < points.Count; i++)
        {
            length += Distance(points[i - 1], points[i]);
        }
        return length;
    }

    /// <summary>
    /// Resamples and normalizes a stroke to <see cref="SampleCount"/> points centered at origin
    /// within a [-0.5, 0.5] aspect-preserving bounding box.
    /// </summary>
    public static GesturePoint[] Normalize(IReadOnlyList<GesturePoint> points)
    {
        var resampled = Resample(points, SampleCount);
        if (resampled.Length == 0)
        {
            return [];
        }

        double minX = double.MaxValue, maxX = double.MinValue;
        double minY = double.MaxValue, maxY = double.MinValue;
        double sumX = 0, sumY = 0;

        foreach (var p in resampled)
        {
            if (p.X < minX) minX = p.X;
            if (p.X > maxX) maxX = p.X;
            if (p.Y < minY) minY = p.Y;
            if (p.Y > maxY) maxY = p.Y;
            sumX += p.X;
            sumY += p.Y;
        }

        double width = maxX - minX;
        double height = maxY - minY;
        double scale = Math.Max(width, height);
        if (scale < 1e-5)
        {
            return [];
        }

        double cx = sumX / resampled.Length;
        double cy = sumY / resampled.Length;

        var normalized = new GesturePoint[resampled.Length];
        for (int i = 0; i < resampled.Length; i++)
        {
            normalized[i] = new GesturePoint(
                (resampled[i].X - cx) / scale,
                (resampled[i].Y - cy) / scale);
        }

        return normalized;
    }

    /// <summary>
    /// Resamples a polyline into <paramref name="n"/> equidistant points.
    /// </summary>
    public static GesturePoint[] Resample(IReadOnlyList<GesturePoint> points, int n)
    {
        if (points.Count < 2 || n < 2)
        {
            return [];
        }

        double totalLength = ComputePathLength(points);
        if (totalLength < 1e-5)
        {
            return [];
        }

        double step = totalLength / (n - 1);
        double accumulated = 0;

        var src = new List<GesturePoint>(points);
        var dst = new List<GesturePoint>(n) { src[0] };

        int i = 1;
        while (i < src.Count)
        {
            double d = Distance(src[i - 1], src[i]);
            if (accumulated + d >= step)
            {
                double t = (step - accumulated) / d;
                var q = new GesturePoint(
                    src[i - 1].X + t * (src[i].X - src[i - 1].X),
                    src[i - 1].Y + t * (src[i].Y - src[i - 1].Y));
                dst.Add(q);
                src.Insert(i, q);
                accumulated = 0;
                i++;
            }
            else
            {
                accumulated += d;
                i++;
            }
        }

        while (dst.Count < n)
        {
            dst.Add(src[^1]);
        }

        if (dst.Count > n)
        {
            dst.RemoveRange(n, dst.Count - n);
        }

        return [.. dst];
    }

    private static double AveragePointDistance(GesturePoint[] a, GesturePoint[] b)
    {
        int len = Math.Min(a.Length, b.Length);
        double sum = 0;
        for (int i = 0; i < len; i++)
        {
            sum += Distance(a[i], b[i]);
        }
        return sum / len;
    }

    private static double Distance(GesturePoint a, GesturePoint b)
    {
        double dx = a.X - b.X;
        double dy = a.Y - b.Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }
}
