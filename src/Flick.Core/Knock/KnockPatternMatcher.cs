using Flick.Core.Models;
using Flick.Core.Rules;

namespace Flick.Core.Knock;

public sealed record KnockSequenceMatch(
    GestureRule Rule,
    int KnockCount,
    string SurfaceName,
    double Confidence);

/// <summary>
/// Accumulates knock events into rhythmic sequences and matches them against
/// enabled <see cref="TriggerType.KnockPattern"/> rules.
/// </summary>
public sealed class KnockPatternMatcher
{
    private readonly List<KnockOnsetEvent> _pending = [];
    private long _lastKnockMs;

    public bool HasPending => _pending.Count > 0;

    public void AddKnock(KnockOnsetEvent knock)
    {
        _pending.Add(knock);
        _lastKnockMs = knock.TimestampMs;
    }

    public void Reset()
    {
        _pending.Clear();
        _lastKnockMs = 0;
    }

    public KnockSequenceMatch? TryFinalizeIfExpired(
        long nowMs,
        int timeoutMs,
        IEnumerable<GestureRule> rules,
        IReadOnlyList<KnockSurfaceProfile> surfaces,
        string? foregroundProcessName,
        string? windowTitle,
        bool touchpadContactCorroborated = false)
    {
        if (_pending.Count == 0 || (nowMs - _lastKnockMs) < timeoutMs)
        {
            return null;
        }

        return FinalizeNow(rules, surfaces, foregroundProcessName, windowTitle, touchpadContactCorroborated);
    }

    public KnockSequenceMatch? FinalizeNow(
        IEnumerable<GestureRule> rules,
        IReadOnlyList<KnockSurfaceProfile> surfaces,
        string? foregroundProcessName,
        string? windowTitle,
        bool touchpadContactCorroborated = false)
    {
        if (_pending.Count == 0)
        {
            return null;
        }

        var snapshot = _pending.ToArray();
        _pending.Clear();

        int count = snapshot.Length;
        var ratios = ComputeRhythmRatios(snapshot);

        // Average feature vector across all knocks in the pattern for robust surface classification
        float[] avgFeatures = AverageFeatures(snapshot);
        var surfaceRes = KnockSurfaceClassifier.Classify(avgFeatures, surfaces);

        string? detectedSurfaceId = surfaceRes.Surface?.Id;
        string detectedSurfaceName = surfaceRes.Surface?.Name ?? "Any Surface";
        double confidence = surfaceRes.Surface is not null ? surfaceRes.Confidence : 1.0;

        if (touchpadContactCorroborated && surfaceRes.Surface is not null &&
            surfaceRes.Surface.Name.Contains("Touchpad", StringComparison.OrdinalIgnoreCase))
        {
            confidence = Math.Min(1.0, confidence + 0.15);
        }

        GestureRule? bestRule = null;
        int bestPriority = -1;

        foreach (var rule in rules)
        {
            if (!rule.IsEnabled ||
                rule.TriggerType != TriggerType.KnockPattern ||
                rule.KnockPattern is null ||
                rule.KnockPattern.KnockCount != count)
            {
                continue;
            }

            if (!string.IsNullOrEmpty(rule.KnockPattern.SurfaceProfileId))
            {
                if (!string.Equals(rule.KnockPattern.SurfaceProfileId, detectedSurfaceId, StringComparison.Ordinal))
                {
                    continue;
                }
            }

            if (rule.KnockPattern.RhythmRatios.Count > 0 &&
                !MatchesRhythm(ratios, rule.KnockPattern.RhythmRatios))
            {
                continue;
            }

            if (!RuleMatcher.MatchesScopeAndPriority(rule, foregroundProcessName, windowTitle, out int scopePriority))
            {
                continue;
            }

            int priority = scopePriority
                         + (!string.IsNullOrEmpty(rule.KnockPattern.SurfaceProfileId) ? 2 : 0)
                         + (rule.KnockPattern.RhythmRatios.Count > 0 ? 1 : 0);

            if (priority > bestPriority)
            {
                bestPriority = priority;
                bestRule = rule;
            }
        }

        return bestRule is not null
            ? new KnockSequenceMatch(bestRule, count, detectedSurfaceName, confidence)
            : null;
    }

    public static List<double> ComputeRhythmRatios(IReadOnlyList<KnockOnsetEvent> knocks)
    {
        if (knocks.Count < 3)
        {
            return [];
        }

        var intervals = new double[knocks.Count - 1];
        double minInterval = double.MaxValue;
        for (int i = 1; i < knocks.Count; i++)
        {
            double dt = Math.Max(40.0, knocks[i].TimestampMs - knocks[i - 1].TimestampMs);
            intervals[i - 1] = dt;
            if (dt < minInterval) minInterval = dt;
        }

        return intervals.Select(dt => Math.Round(dt / minInterval, 2)).ToList();
    }

    public static bool MatchesRhythm(IReadOnlyList<double> observed, IReadOnlyList<double> expected, double tolerance = 0.38)
    {
        if (observed.Count != expected.Count) return false;
        for (int i = 0; i < observed.Count; i++)
        {
            double diff = Math.Abs(observed[i] - expected[i]);
            if (diff > Math.Max(0.35, expected[i] * tolerance))
            {
                return false;
            }
        }
        return true;
    }

    public static string FormatPattern(KnockPatternDefinition? pattern)
    {
        if (pattern is null) return "Unassigned Knock";
        string countLabel = pattern.KnockCount switch
        {
            1 => "Single Knock",
            2 => "Double Knock",
            3 => "Triple Knock",
            _ => $"{pattern.KnockCount}× Knock"
        };
        string surface = string.IsNullOrWhiteSpace(pattern.SurfaceName) ? "Any Surface" : pattern.SurfaceName;
        return $"{countLabel} • {surface}";
    }

    private static float[] AverageFeatures(KnockOnsetEvent[] knocks)
    {
        int dims = knocks[0].Features.Length;
        var avg = new float[dims];
        foreach (var k in knocks)
        {
            for (int d = 0; d < dims && d < k.Features.Length; d++)
            {
                avg[d] += k.Features[d];
            }
        }
        for (int d = 0; d < dims; d++) avg[d] /= knocks.Length;
        return avg;
    }
}
