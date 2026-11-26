using Flick.Core.Models;

namespace Flick.Core.Rules;

public readonly record struct RepetitionCheckResult(bool ShouldExecute, int CurrentCount, int RequiredCount);

/// <summary>
/// Tracks consecutive gesture matches so rules with <see cref="GestureRule.RequiredRepetitions"/> &gt; 1
/// only execute after the gesture is performed N times within the timeout window.
/// </summary>
public sealed class RepetitionGate
{
    private string? _lastRuleId;
    private int _count;
    private DateTimeOffset _lastMatchTime = DateTimeOffset.MinValue;

    public TimeSpan RepetitionWindow { get; set; } = TimeSpan.FromMilliseconds(1800);

    public RepetitionCheckResult RegisterMatch(GestureRule rule, DateTimeOffset? timestamp = null)
    {
        int required = Math.Max(1, rule.RequiredRepetitions);
        if (required == 1)
        {
            Reset();
            return new RepetitionCheckResult(true, 1, 1);
        }

        var now = timestamp ?? DateTimeOffset.UtcNow;
        if (string.Equals(_lastRuleId, rule.Id, StringComparison.Ordinal) &&
            (now - _lastMatchTime) <= RepetitionWindow)
        {
            _count++;
        }
        else
        {
            _lastRuleId = rule.Id;
            _count = 1;
        }

        _lastMatchTime = now;

        if (_count >= required)
        {
            Reset();
            return new RepetitionCheckResult(true, required, required);
        }

        return new RepetitionCheckResult(false, _count, required);
    }

    public void Reset()
    {
        _lastRuleId = null;
        _count = 0;
        _lastMatchTime = DateTimeOffset.MinValue;
    }
}
