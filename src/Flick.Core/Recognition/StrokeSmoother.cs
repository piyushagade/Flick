using Flick.Core.Models;

namespace Flick.Core.Recognition;

/// <summary>
/// Applies multi-pass weighted denoising and Chaikin corner-cutting subdivision
/// to produce hyper-smooth, ink-like gesture trails while preserving endpoints.
/// </summary>
public static class StrokeSmoother
{
    public static GesturePoint[] Smooth(
        IReadOnlyList<GesturePoint> points,
        int denoisePasses = 2,
        int chaikinIterations = 2)
    {
        if (points.Count < 3)
        {
            return [.. points];
        }

        var current = new List<GesturePoint>(points);

        // Pass 1: 1-2-1 weighted moving average to remove sensor/HID micro-jitter.
        for (int pass = 0; pass < denoisePasses; pass++)
        {
            var next = new List<GesturePoint>(current.Count) { current[0] };
            for (int i = 1; i < current.Count - 1; i++)
            {
                double x = 0.25 * current[i - 1].X + 0.50 * current[i].X + 0.25 * current[i + 1].X;
                double y = 0.25 * current[i - 1].Y + 0.50 * current[i].Y + 0.25 * current[i + 1].Y;
                next.Add(new GesturePoint(x, y));
            }
            next.Add(current[^1]);
            current = next;
        }

        // Pass 2: Chaikin's corner-cutting subdivision for C1-smooth curves.
        for (int iter = 0; iter < chaikinIterations; iter++)
        {
            if (current.Count < 3) break;
            var next = new List<GesturePoint>(current.Count * 2) { current[0] };

            for (int i = 0; i < current.Count - 1; i++)
            {
                var p0 = current[i];
                var p1 = current[i + 1];

                var q = new GesturePoint(0.75 * p0.X + 0.25 * p1.X, 0.75 * p0.Y + 0.25 * p1.Y);
                var r = new GesturePoint(0.25 * p0.X + 0.75 * p1.X, 0.25 * p0.Y + 0.75 * p1.Y);
                next.Add(q);
                next.Add(r);
            }

            next.Add(current[^1]);
            current = next;
        }

        return [.. current];
    }
}
