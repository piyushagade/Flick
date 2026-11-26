using Flick.Core.Models;

namespace Flick.Core.Rules;

public readonly record struct SequenceAdvanceResult(
    GestureRule? CompletedRule,
    GestureRule? PendingRule,
    int CurrentStep,
    int TotalSteps);

/// <summary>
/// Tracks multi-gesture sequence triggers (e.g., 4-finger swipe down followed by a mouse wave).
/// </summary>
public sealed class GestureSequenceTracker
{
    private GestureRule? _activeRule;
    private int _nextFollowUpIndex;
    private DateTimeOffset _lastStepTime = DateTimeOffset.MinValue;

    public TimeSpan StepTimeout { get; set; } = TimeSpan.FromMilliseconds(2500);

    public bool HasActiveSequence => _activeRule is not null;

    /// <summary>
    /// Attempts to advance an active sequence with the newly recognized gesture candidates.
    /// Returns <see cref="SequenceAdvanceResult"/> if an active sequence matched (either advancing or completing).
    /// </summary>
    public SequenceAdvanceResult? TryAdvance(
        IEnumerable<string> candidateGestureIds,
        GestureInputSource inputSource,
        int fingerCount,
        DateTimeOffset? timestamp = null)
    {
        if (_activeRule is null || _activeRule.FollowUpGestures is null)
        {
            return null;
        }

        var now = timestamp ?? DateTimeOffset.UtcNow;
        if ((now - _lastStepTime) > StepTimeout)
        {
            Reset();
            return null;
        }

        var expected = _activeRule.FollowUpGestures[_nextFollowUpIndex];
        if (!MatchesStep(expected, candidateGestureIds, inputSource, fingerCount))
        {
            return null;
        }

        _nextFollowUpIndex++;
        _lastStepTime = now;
        int totalSteps = 1 + _activeRule.FollowUpGestures.Count;

        if (_nextFollowUpIndex >= _activeRule.FollowUpGestures.Count)
        {
            var completed = _activeRule;
            Reset();
            return new SequenceAdvanceResult(completed, null, totalSteps, totalSteps);
        }

        return new SequenceAdvanceResult(null, _activeRule, 1 + _nextFollowUpIndex, totalSteps);
    }

    public SequenceAdvanceResult BeginSequence(GestureRule rule, DateTimeOffset? timestamp = null)
    {
        _activeRule = rule;
        _nextFollowUpIndex = 0;
        _lastStepTime = timestamp ?? DateTimeOffset.UtcNow;
        int total = 1 + (rule.FollowUpGestures?.Count ?? 0);
        return new SequenceAdvanceResult(null, rule, 1, total);
    }

    public void Reset()
    {
        _activeRule = null;
        _nextFollowUpIndex = 0;
        _lastStepTime = DateTimeOffset.MinValue;
    }

    private static bool MatchesStep(
        GestureSequenceStep expected,
        IEnumerable<string> candidateGestureIds,
        GestureInputSource inputSource,
        int fingerCount)
    {
        if (expected.InputSource != GestureInputSource.Any && expected.InputSource != inputSource)
        {
            return false;
        }

        if (expected.InputSource == GestureInputSource.Touchpad &&
            expected.FingerCount > 0 &&
            expected.FingerCount != fingerCount)
        {
            return false;
        }

        foreach (string id in candidateGestureIds)
        {
            if (string.Equals(id, expected.GestureId, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}
