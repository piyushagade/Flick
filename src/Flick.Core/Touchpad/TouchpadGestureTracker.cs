using Flick.Core.Models;
using Flick.Core.Recognition;

namespace Flick.Core.Touchpad;

public readonly record struct TouchContact(int Id, double NormalizedX, double NormalizedY);

public enum TouchpadGestureKind
{
    None,
    Swipe,
    Tap,
    PinchIn,
    PinchOut
}

public readonly record struct TouchpadGestureResult(
    TouchpadGestureKind Kind,
    int FingerCount,
    IReadOnlyList<GesturePoint> CentroidPath,
    IReadOnlyList<IReadOnlyList<GesturePoint>> FingerPaths);

/// <summary>
/// Stateful tracker that converts Precision Touchpad contact frames into
/// multi-finger swipe, tap, or pinch gesture candidates, preserving per-finger paths.
/// </summary>
public sealed class TouchpadGestureTracker
{
    private readonly List<GesturePoint> _centroidPath = [];
    private readonly Dictionary<int, List<GesturePoint>> _fingerPaths = [];
    private int _peakFingers;
    private double _initialPinchSpread;
    private double _lastPinchSpread;
    private DateTimeOffset _sessionStart;
    private bool _inSession;

    public double MinNormalizedSwipeDistance { get; set; } = 0.08;
    public double MaxTapMovement { get; set; } = 0.065;
    public TimeSpan MaxTapDuration { get; set; } = TimeSpan.FromMilliseconds(450);

    public event Action<IReadOnlyList<IReadOnlyList<GesturePoint>>, IReadOnlyList<GesturePoint>, int>? StrokeUpdated;

    public TouchpadGestureResult? ProcessFrame(IReadOnlyList<TouchContact> contacts, DateTimeOffset? timestamp = null)
    {
        var now = timestamp ?? DateTimeOffset.UtcNow;

        if (contacts.Count == 0)
        {
            if (!_inSession)
            {
                return null;
            }

            var result = FinalizeSession(now);
            Reset();
            return result;
        }

        var centroid = ComputeCentroid(contacts);

        if (!_inSession)
        {
            _inSession = true;
            _sessionStart = now;
            _peakFingers = contacts.Count;
            _centroidPath.Clear();
            _centroidPath.Add(centroid);
            ResetFingerPaths(contacts);

            if (contacts.Count == 2)
            {
                _initialPinchSpread = ComputeSpread(contacts, centroid);
                _lastPinchSpread = _initialPinchSpread;
            }
            return null;
        }

        if (contacts.Count > _peakFingers)
        {
            // Reset paths when additional fingers land so the gesture reflects the full multi-finger chord.
            _peakFingers = contacts.Count;
            _centroidPath.Clear();
            _centroidPath.Add(centroid);
            ResetFingerPaths(contacts);

            if (contacts.Count == 2)
            {
                _initialPinchSpread = ComputeSpread(contacts, centroid);
                _lastPinchSpread = _initialPinchSpread;
            }
            return null;
        }

        if (contacts.Count == _peakFingers)
        {
            bool moved = false;
            foreach (var c in contacts)
            {
                if (_fingerPaths.TryGetValue(c.Id, out var path))
                {
                    var pt = new GesturePoint(c.NormalizedX, c.NormalizedY);
                    if (path.Count == 0 || Distance(path[^1], pt) >= 0.004)
                    {
                        path.Add(pt);
                        moved = true;
                    }
                }
            }

            if (_centroidPath.Count == 0 ||
                Distance(_centroidPath[^1], centroid) >= 0.004)
            {
                _centroidPath.Add(centroid);
                moved = true;
            }

            if (moved)
            {
                double currentLen = _centroidPath.Count >= 2
                    ? GestureRecognizer.ComputePathLength(_centroidPath)
                    : 0;
                if (currentLen >= MinNormalizedSwipeDistance)
                {
                    StrokeUpdated?.Invoke(SnapshotFingerPaths(), _centroidPath, _peakFingers);
                }
            }

            if (contacts.Count == 2)
            {
                _lastPinchSpread = ComputeSpread(contacts, centroid);
            }
        }

        return null;
    }

    private TouchpadGestureResult? FinalizeSession(DateTimeOffset endTime)
    {
        if (_peakFingers < 2)
        {
            // Single-finger touchpad movement is normal cursor control; ignore.
            return null;
        }

        double pathLength = _centroidPath.Count >= 2
            ? GestureRecognizer.ComputePathLength(_centroidPath)
            : 0;

        var fingerPaths = SnapshotFingerPaths();

        if (_peakFingers == 2 && _initialPinchSpread > 0.02)
        {
            double delta = _lastPinchSpread - _initialPinchSpread;
            double minPinchDelta = Math.Max(0.06, MinNormalizedSwipeDistance * 0.8);
            if (Math.Abs(delta) >= minPinchDelta && pathLength < 0.14)
            {
                return new TouchpadGestureResult(
                    delta < 0 ? TouchpadGestureKind.PinchIn : TouchpadGestureKind.PinchOut,
                    2,
                    [.. _centroidPath],
                    fingerPaths);
            }
        }

        if (pathLength >= MinNormalizedSwipeDistance && _centroidPath.Count >= 2)
        {
            return new TouchpadGestureResult(
                TouchpadGestureKind.Swipe,
                _peakFingers,
                [.. _centroidPath],
                fingerPaths);
        }

        if (pathLength <= MaxTapMovement && (endTime - _sessionStart) <= MaxTapDuration)
        {
            return new TouchpadGestureResult(
                TouchpadGestureKind.Tap,
                _peakFingers,
                [.. _centroidPath],
                fingerPaths);
        }

        return null;
    }

    public void Reset()
    {
        _inSession = false;
        _peakFingers = 0;
        _centroidPath.Clear();
        _fingerPaths.Clear();
        _initialPinchSpread = 0;
        _lastPinchSpread = 0;
    }

    private void ResetFingerPaths(IReadOnlyList<TouchContact> contacts)
    {
        _fingerPaths.Clear();
        foreach (var c in contacts)
        {
            _fingerPaths[c.Id] = [new GesturePoint(c.NormalizedX, c.NormalizedY)];
        }
    }

    private IReadOnlyList<IReadOnlyList<GesturePoint>> SnapshotFingerPaths()
    {
        var list = new List<IReadOnlyList<GesturePoint>>(_fingerPaths.Count);
        foreach (var path in _fingerPaths.Values)
        {
            list.Add([.. path]);
        }
        return list;
    }

    private static GesturePoint ComputeCentroid(IReadOnlyList<TouchContact> contacts)
    {
        double sx = 0, sy = 0;
        foreach (var c in contacts)
        {
            sx += c.NormalizedX;
            sy += c.NormalizedY;
        }
        return new GesturePoint(sx / contacts.Count, sy / contacts.Count);
    }

    private static double ComputeSpread(IReadOnlyList<TouchContact> contacts, GesturePoint centroid)
    {
        double sum = 0;
        foreach (var c in contacts)
        {
            double dx = c.NormalizedX - centroid.X;
            double dy = c.NormalizedY - centroid.Y;
            sum += Math.Sqrt(dx * dx + dy * dy);
        }
        return sum / contacts.Count;
    }

    private static double Distance(GesturePoint a, GesturePoint b)
    {
        double dx = a.X - b.X;
        double dy = a.Y - b.Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }
}
