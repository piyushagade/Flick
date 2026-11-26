using Flick.Core.Models;
using Flick.Core.Rules;
using Flick.Core.Touchpad;
using Xunit;

namespace Flick.Core.Tests;

public sealed class TouchpadTrackerTests
{
    [Fact]
    public void Tracker_DetectsThreeFingerSwipeRight()
    {
        var tracker = new TouchpadGestureTracker();

        tracker.ProcessFrame([new(1, 0.20, 0.50), new(2, 0.25, 0.50), new(3, 0.30, 0.50)]);
        tracker.ProcessFrame([new(1, 0.40, 0.50), new(2, 0.45, 0.50), new(3, 0.50, 0.50)]);
        tracker.ProcessFrame([new(1, 0.65, 0.50), new(2, 0.70, 0.50), new(3, 0.75, 0.50)]);
        var result = tracker.ProcessFrame([]);

        Assert.NotNull(result);
        Assert.Equal(TouchpadGestureKind.Swipe, result.Value.Kind);
        Assert.Equal(3, result.Value.FingerCount);
        Assert.True(result.Value.CentroidPath.Count >= 3);
    }

    [Fact]
    public void Tracker_DetectsFourFingerTap()
    {
        var tracker = new TouchpadGestureTracker();
        var t0 = DateTimeOffset.UtcNow;

        tracker.ProcessFrame([new(1, 0.3, 0.4), new(2, 0.4, 0.4), new(3, 0.5, 0.4), new(4, 0.6, 0.4)], t0);
        var result = tracker.ProcessFrame([], t0.AddMilliseconds(90));

        Assert.NotNull(result);
        Assert.Equal(TouchpadGestureKind.Tap, result.Value.Kind);
        Assert.Equal(4, result.Value.FingerCount);
    }

    [Fact]
    public void Tracker_DetectsTwoFingerPinchInAndOut()
    {
        var tracker = new TouchpadGestureTracker();

        // Pinch In: two fingers start far apart and move toward center
        tracker.ProcessFrame([new(1, 0.25, 0.50), new(2, 0.75, 0.50)]);
        tracker.ProcessFrame([new(1, 0.45, 0.50), new(2, 0.55, 0.50)]);
        var pinchIn = tracker.ProcessFrame([]);

        Assert.NotNull(pinchIn);
        Assert.Equal(TouchpadGestureKind.PinchIn, pinchIn.Value.Kind);
        Assert.Equal(2, pinchIn.Value.FingerCount);

        // Pinch Out: two fingers start close and spread apart
        tracker.ProcessFrame([new(1, 0.45, 0.50), new(2, 0.55, 0.50)]);
        tracker.ProcessFrame([new(1, 0.20, 0.50), new(2, 0.80, 0.50)]);
        var pinchOut = tracker.ProcessFrame([]);

        Assert.NotNull(pinchOut);
        Assert.Equal(TouchpadGestureKind.PinchOut, pinchOut.Value.Kind);
        Assert.Equal(2, pinchOut.Value.FingerCount);
    }

    [Fact]
    public void Tracker_DetectsFourFingerPinchInAndOut()
    {
        var tracker = new TouchpadGestureTracker();

        // 4-finger Pinch In with slight downward centroid drift (like a real hand)
        tracker.ProcessFrame([new(1, 0.25, 0.30), new(2, 0.50, 0.20), new(3, 0.75, 0.30), new(4, 0.30, 0.75)]);
        tracker.ProcessFrame([new(1, 0.42, 0.48), new(2, 0.50, 0.45), new(3, 0.58, 0.48), new(4, 0.46, 0.56)]);
        var pinchIn = tracker.ProcessFrame([]);

        Assert.NotNull(pinchIn);
        Assert.Equal(TouchpadGestureKind.PinchIn, pinchIn.Value.Kind);
        Assert.Equal(4, pinchIn.Value.FingerCount);
    }

    [Fact]
    public void Tracker_IgnoresSingleFingerCursorMovement()
    {
        var tracker = new TouchpadGestureTracker();

        tracker.ProcessFrame([new(1, 0.1, 0.1)]);
        tracker.ProcessFrame([new(1, 0.5, 0.5)]);
        var result = tracker.ProcessFrame([]);

        Assert.Null(result);
    }

    [Fact]
    public void RuleMatcher_MatchesTouchpadByFingerCount()
    {
        var matcher = new RuleMatcher();
        var rules = new List<GestureRule>
        {
            new() { Id = "3f", GestureId = "builtin-left", InputSource = GestureInputSource.Touchpad, FingerCount = 3 },
            new() { Id = "4f", GestureId = "builtin-left", InputSource = GestureInputSource.Touchpad, FingerCount = 4 },
            new() { Id = "mouse", GestureId = "builtin-left", InputSource = GestureInputSource.Mouse }
        };

        Assert.Equal("3f", matcher.FindBestMatch("builtin-left", "explorer", rules, GestureInputSource.Touchpad, 3)?.Id);
        Assert.Equal("4f", matcher.FindBestMatch("builtin-left", "explorer", rules, GestureInputSource.Touchpad, 4)?.Id);
        Assert.Equal("mouse", matcher.FindBestMatch("builtin-left", "explorer", rules, GestureInputSource.Mouse, 0)?.Id);
    }

    [Fact]
    public void Tracker_RejectsShortMovementBelowMinDistance_UnlessTap()
    {
        var tracker = new TouchpadGestureTracker { MinNormalizedSwipeDistance = 0.20 };
        bool trailFired = false;
        tracker.StrokeUpdated += (_, _, _) => trailFired = true;

        var t0 = DateTimeOffset.UtcNow;
        // Move 0.08 (above MaxTapMovement 0.04, but below MinNormalizedSwipeDistance 0.20)
        tracker.ProcessFrame([new(1, 0.20, 0.50), new(2, 0.25, 0.50), new(3, 0.30, 0.50)], t0);
        tracker.ProcessFrame([new(1, 0.28, 0.50), new(2, 0.33, 0.50), new(3, 0.38, 0.50)], t0.AddMilliseconds(120));
        var shortResult = tracker.ProcessFrame([], t0.AddMilliseconds(200));

        Assert.Null(shortResult);
        Assert.False(trailFired);

        // Stationary tap still activates even when MinNormalizedSwipeDistance is high
        tracker.ProcessFrame([new(1, 0.20, 0.50), new(2, 0.25, 0.50), new(3, 0.30, 0.50)], t0);
        var tapResult = tracker.ProcessFrame([], t0.AddMilliseconds(80));

        Assert.NotNull(tapResult);
        Assert.Equal(TouchpadGestureKind.Tap, tapResult!.Value.Kind);
    }
}
