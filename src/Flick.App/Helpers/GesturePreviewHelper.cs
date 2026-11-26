using Flick.Core.Models;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;

namespace Flick.App.Helpers;

/// <summary>
/// Converts a gesture's point sequence into normalized points for WinUI Polyline previews.
/// </summary>
public static class GesturePreviewHelper
{
    public static PointCollection ToPreviewPoints(IReadOnlyList<GesturePoint> points, double size = 48, double padding = 6)
    {
        var collection = new PointCollection();
        if (points.Count < 2)
        {
            return collection;
        }

        double minX = double.MaxValue, maxX = double.MinValue;
        double minY = double.MaxValue, maxY = double.MinValue;

        foreach (var p in points)
        {
            if (p.X < minX) minX = p.X;
            if (p.X > maxX) maxX = p.X;
            if (p.Y < minY) minY = p.Y;
            if (p.Y > maxY) maxY = p.Y;
        }

        double width = maxX - minX;
        double height = maxY - minY;
        double maxDim = Math.Max(width, height);
        double usable = Math.Max(1, size - padding * 2);

        if (maxDim < 1e-5)
        {
            return collection;
        }

        double scale = usable / maxDim;
        double offsetX = padding + (usable - width * scale) / 2.0;
        double offsetY = padding + (usable - height * scale) / 2.0;

        foreach (var p in points)
        {
            collection.Add(new Point(
                offsetX + (p.X - minX) * scale,
                offsetY + (p.Y - minY) * scale));
        }

        return collection;
    }

    public static List<PointCollection> ToMultiPreviewPoints(
        IReadOnlyList<List<GesturePoint>> strokes,
        double size = 48,
        double padding = 8)
    {
        var result = new List<PointCollection>(strokes.Count);
        if (strokes.Count == 0)
        {
            return result;
        }

        double minX = double.MaxValue, maxX = double.MinValue;
        double minY = double.MaxValue, maxY = double.MinValue;
        bool hasAny = false;

        foreach (var stroke in strokes)
        {
            foreach (var p in stroke)
            {
                hasAny = true;
                if (p.X < minX) minX = p.X;
                if (p.X > maxX) maxX = p.X;
                if (p.Y < minY) minY = p.Y;
                if (p.Y > maxY) maxY = p.Y;
            }
        }

        double width = maxX - minX;
        double height = maxY - minY;
        double maxDim = Math.Max(width, height);
        double usable = Math.Max(1, size - padding * 2);

        if (!hasAny || maxDim < 1e-5)
        {
            return result;
        }

        double scale = usable / maxDim;
        double offsetX = padding + (usable - width * scale) / 2.0;
        double offsetY = padding + (usable - height * scale) / 2.0;

        foreach (var stroke in strokes)
        {
            var col = new PointCollection();
            foreach (var p in stroke)
            {
                col.Add(new Point(
                    offsetX + (p.X - minX) * scale,
                    offsetY + (p.Y - minY) * scale));
            }
            result.Add(col);
        }

        return result;
    }

    /// <summary>
    /// Generates normalized 0..100 multi-finger strokes for a pinch-in or pinch-out gesture.
    /// </summary>
    public static List<List<GesturePoint>> BuildPinchStrokes(bool pinchIn, int fingerCount)
    {
        int count = Math.Clamp(fingerCount > 0 ? fingerCount : 2, 2, 5);
        if (count == 2)
        {
            return pinchIn
                ? [[new(0, 50), new(40, 50)], [new(100, 50), new(60, 50)]]
                : [[new(40, 50), new(0, 50)], [new(60, 50), new(100, 50)]];
        }

        var strokes = new List<List<GesturePoint>>(count);
        double startAngle = count == 4 ? -Math.PI * 0.75 : -Math.PI / 2.0;
        for (int i = 0; i < count; i++)
        {
            double angle = startAngle + i * (2.0 * Math.PI / count);
            double cos = Math.Cos(angle);
            double sin = Math.Sin(angle);
            var outer = new GesturePoint(50.0 + 50.0 * cos, 50.0 + 50.0 * sin);
            var inner = new GesturePoint(50.0 + 14.0 * cos, 50.0 + 14.0 * sin);
            strokes.Add(pinchIn ? [outer, inner] : [inner, outer]);
        }
        return strokes;
    }

    public static PointCollection CreateArrowhead(IList<Point> points, double headLength = 10, double halfAngleDeg = 28)
    {
        var arrow = new PointCollection();
        if (points.Count < 2)
        {
            return arrow;
        }

        Point tip = points[^1];
        Point anchor = points[0];
        for (int i = points.Count - 2; i >= 0; i--)
        {
            double dx = tip.X - points[i].X;
            double dy = tip.Y - points[i].Y;
            if (Math.Sqrt(dx * dx + dy * dy) >= 6.0)
            {
                anchor = points[i];
                break;
            }
        }

        double vx = tip.X - anchor.X;
        double vy = tip.Y - anchor.Y;
        double len = Math.Sqrt(vx * vx + vy * vy);
        if (len < 1e-3)
        {
            return arrow;
        }

        double angle = Math.Atan2(vy, vx);
        double rad = halfAngleDeg * Math.PI / 180.0;

        arrow.Add(new Point(
            tip.X - headLength * Math.Cos(angle - rad),
            tip.Y - headLength * Math.Sin(angle - rad)));
        arrow.Add(tip);
        arrow.Add(new Point(
            tip.X - headLength * Math.Cos(angle + rad),
            tip.Y - headLength * Math.Sin(angle + rad)));

        return arrow;
    }
}
