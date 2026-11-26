using Flick.Core.Models;
using Flick.Core.Rules;

namespace Flick.Core.Keyboard;

/// <summary>
/// Outcome of processing a single keyboard event through <see cref="KeyboardChordEngine"/>.
/// </summary>
public sealed record ChordProcessResult(
    bool SuppressCurrentEvent,
    IReadOnlyList<int> KeysToReplayDown,
    GestureRule? MatchedRule,
    string? PendingSequenceLabel = null);

/// <summary>
/// Pure state machine for simultaneous (buffered combo) and sequential (leader) keyboard chords.
/// </summary>
public sealed class KeyboardChordEngine
{
    private readonly List<int> _simBuffer = [];
    private readonly HashSet<int> _suppressedHeldKeys = [];
    private ChordModifiers _simModifiers = ChordModifiers.None;
    private long _simStartMs;
    private int _simWindowMs = 50;

    private List<GestureRule> _activeSeqRules = [];
    private int _seqMatchedSteps = 0;
    private long _seqDeadlineMs = 0;
    private GestureRule? _pendingSeqCompletedRule;
    private long _pendingSeqCommitDeadlineMs = 0;
    private ChordModifiers _heldModifierFlags = ChordModifiers.None;

    public bool HasPendingSimultaneous => _simBuffer.Count > 0;
    public bool HasPendingSequence => _seqMatchedSteps > 0 && _activeSeqRules.Count > 0;
    public bool HasPendingSequenceCommit => _pendingSeqCompletedRule is not null;

    public int GetRemainingSequenceCommitMs(long timestampMs) =>
        _pendingSeqCompletedRule is null
            ? 0
            : (int)Math.Max(1, _pendingSeqCommitDeadlineMs - timestampMs);

    public void Reset()
    {
        _simBuffer.Clear();
        _suppressedHeldKeys.Clear();
        _simModifiers = ChordModifiers.None;
        _simStartMs = 0;
        _activeSeqRules.Clear();
        _seqMatchedSteps = 0;
        _seqDeadlineMs = 0;
        _pendingSeqCompletedRule = null;
        _pendingSeqCommitDeadlineMs = 0;
        _heldModifierFlags = ChordModifiers.None;
    }

    public ChordProcessResult ProcessKeyEvent(
        int vkCode,
        bool isKeyDown,
        ChordModifiers activeModifiers,
        long timestampMs,
        IEnumerable<GestureRule> allRules,
        string? foregroundProcessName,
        string? windowTitle,
        int defaultSimWindowMs = 50,
        int defaultSeqTimeoutMs = 1000)
    {
        // 1. Modifier keys: track held state and evaluate modifier-only sequential steps (e.g. Ctrl -> Alt -> Alt).
        if (ChordKeyHelper.IsModifierKey(vkCode))
        {
            return ProcessModifierKeyEvent(
                vkCode,
                isKeyDown,
                activeModifiers,
                timestampMs,
                allRules,
                foregroundProcessName,
                windowTitle,
                defaultSeqTimeoutMs);
        }

        // 2. KeyUp handling: swallow if this key was part of a matched suppressed chord;
        //    flush buffered simultaneous keys if a buffered key is released early (fast typing).
        if (!isKeyDown)
        {
            if (_suppressedHeldKeys.Remove(vkCode))
            {
                return new ChordProcessResult(true, [], null);
            }

            if (_simBuffer.Contains(vkCode))
            {
                int[] flushed = [.. _simBuffer];
                _simBuffer.Clear();
                return new ChordProcessResult(false, flushed, null);
            }

            return new ChordProcessResult(false, [], null);
        }

        // 3. KeyDown auto-repeat for an already-suppressed key: keep suppressing.
        if (_suppressedHeldKeys.Contains(vkCode))
        {
            return new ChordProcessResult(true, [], null);
        }

        if (_simBuffer.Contains(vkCode))
        {
            return new ChordProcessResult(true, [], null);
        }

        // 4. Expire stale simultaneous buffer or stale sequence if their deadlines passed.
        List<int>? expiredReplay = null;
        if (_simBuffer.Count > 0 && (timestampMs - _simStartMs) > _simWindowMs)
        {
            expiredReplay = [.. _simBuffer];
            _simBuffer.Clear();
        }

        if (_seqMatchedSteps > 0 && timestampMs > _seqDeadlineMs)
        {
            _seqMatchedSteps = 0;
            _activeSeqRules.Clear();
            _pendingSeqCompletedRule = null;
            _pendingSeqCommitDeadlineMs = 0;
        }

        // 5. Gather matching keyboard chord rules for the active foreground window, ordered by specificity.
        var scopedRules = GetScopedChordRules(allRules, foregroundProcessName, windowTitle);

        // 6. Evaluate simultaneous buffering / matching.
        var simRules = scopedRules
            .Where(r => r.KeyboardChord!.Mode == KeyboardChordMode.Simultaneous &&
                        r.KeyboardChord.Steps.Count > 0 &&
                        r.KeyboardChord.Steps[0].KeyCodes.Count > 0)
            .ToList();

        if (_simBuffer.Count > 0)
        {
            // Check if adding vkCode keeps us as a subset of at least one simultaneous rule with matching modifiers.
            var nextSet = new HashSet<int>(_simBuffer) { vkCode };
            var matchingSim = FindMatchingSimultaneousRule(simRules, nextSet, _simModifiers);
            if (matchingSim is not null)
            {
                bool suppress = matchingSim.KeyboardChord!.SuppressKeys;
                if (suppress)
                {
                    foreach (int k in nextSet)
                    {
                        _suppressedHeldKeys.Add(k);
                    }
                }
                _simBuffer.Clear();
                return new ChordProcessResult(suppress, expiredReplay ?? [], matchingSim);
            }

            if (IsSubsetOfAnySimultaneousRule(simRules, nextSet, _simModifiers))
            {
                _simBuffer.Add(vkCode);
                return new ChordProcessResult(true, expiredReplay ?? [], null);
            }

            // Not a subset of any simultaneous rule: flush buffered keys and continue to sequential/pass-through.
            var flushed = new List<int>();
            if (expiredReplay is not null) flushed.AddRange(expiredReplay);
            flushed.AddRange(_simBuffer);
            _simBuffer.Clear();
            expiredReplay = flushed;
        }
        else
        {
            // Buffer is empty: check single-key + modifier simultaneous rule (e.g. Ctrl+Alt+Space) first.
            var singleSet = new HashSet<int> { vkCode };
            var immediateMatch = FindMatchingSimultaneousRule(simRules, singleSet, activeModifiers);
            if (immediateMatch is not null)
            {
                bool suppress = immediateMatch.KeyboardChord!.SuppressKeys;
                if (suppress)
                {
                    _suppressedHeldKeys.Add(vkCode);
                }
                return new ChordProcessResult(suppress, expiredReplay ?? [], immediateMatch);
            }

            // Otherwise, if vkCode starts/participates in any multi-key simultaneous rule with current modifiers, buffer it.
            var participating = simRules
                .Where(r => r.KeyboardChord!.Steps[0].Modifiers == activeModifiers &&
                            r.KeyboardChord.Steps[0].KeyCodes.Count >= 2 &&
                            r.KeyboardChord.Steps[0].KeyCodes.Contains(vkCode))
                .ToList();

            if (participating.Count > 0)
            {
                _simBuffer.Add(vkCode);
                _simModifiers = activeModifiers;
                _simStartMs = timestampMs;
                int customWin = participating.Max(r => r.KeyboardChord!.TimeoutMs);
                _simWindowMs = customWin > 0 ? customWin : Math.Max(15, defaultSimWindowMs);
                return new ChordProcessResult(true, expiredReplay ?? [], null);
            }
        }

        // 7. Sequential chord evaluation.
        var seqRules = scopedRules
            .Where(r => r.KeyboardChord!.Mode == KeyboardChordMode.Sequential &&
                        r.KeyboardChord.Steps.Count >= 2)
            .ToList();

        if (_seqMatchedSteps > 0 && _activeSeqRules.Count > 0)
        {
            var advancing = _activeSeqRules
                .Where(r => _seqMatchedSteps < r.KeyboardChord!.Steps.Count &&
                            StepMatchesSingleKey(r.KeyboardChord.Steps[_seqMatchedSteps], vkCode, activeModifiers))
                .ToList();

            if (advancing.Count > 0)
            {
                int nextStepCount = _seqMatchedSteps + 1;
                var completed = advancing.FirstOrDefault(r => r.KeyboardChord!.Steps.Count == nextStepCount);
                var longer = advancing.Where(r => r.KeyboardChord!.Steps.Count > nextStepCount).ToList();
                int execDelay = completed?.ExecutionDelayMs ?? 0;
                if (completed is not null && execDelay <= 0)
                {
                    _seqMatchedSteps = 0;
                    _activeSeqRules.Clear();
                    _pendingSeqCompletedRule = null;
                    _pendingSeqCommitDeadlineMs = 0;
                    bool suppress = completed.KeyboardChord!.SuppressKeys;
                    if (suppress)
                    {
                        _suppressedHeldKeys.Add(vkCode);
                    }
                    return new ChordProcessResult(suppress, expiredReplay ?? [], completed);
                }

                var nextPool = longer.Count > 0 ? longer : [];
                _activeSeqRules = nextPool;
                _seqMatchedSteps = nextPool.Count > 0 ? nextStepCount : 0;
                int timeout = advancing.Max(r => r.KeyboardChord!.TimeoutMs);
                int waitMs = timeout > 0 ? timeout : Math.Max(100, defaultSeqTimeoutMs);
                _seqDeadlineMs = nextPool.Count > 0 ? timestampMs + Math.Max(execDelay, waitMs) : 0;
                _pendingSeqCompletedRule = completed;
                _pendingSeqCommitDeadlineMs = completed is not null ? timestampMs + execDelay : 0;

                bool suppressStep = advancing.Any(r => r.KeyboardChord!.SuppressKeys);
                if (suppressStep)
                {
                    _suppressedHeldKeys.Add(vkCode);
                }
                var lead = completed ?? nextPool[0];
                return new ChordProcessResult(
                    suppressStep,
                    expiredReplay ?? [],
                    null,
                    $"{lead.Name} ({nextStepCount}/{lead.KeyboardChord!.Steps.Count})");
            }

            // Did not match next step; if a prefix rule was waiting to commit, fire it now.
            var pendingCommit = _pendingSeqCompletedRule;
            _seqMatchedSteps = 0;
            _activeSeqRules.Clear();
            _pendingSeqCompletedRule = null;
            _pendingSeqCommitDeadlineMs = 0;
            if (pendingCommit is not null)
            {
                return new ChordProcessResult(false, expiredReplay ?? [], pendingCommit);
            }
        }

        var starters = seqRules
            .Where(r => StepMatchesSingleKey(r.KeyboardChord!.Steps[0], vkCode, activeModifiers))
            .ToList();

        if (starters.Count > 0)
        {
            _activeSeqRules = starters;
            _seqMatchedSteps = 1;
            int timeout = starters.Max(r => r.KeyboardChord!.TimeoutMs);
            _seqDeadlineMs = timestampMs + (timeout > 0 ? timeout : Math.Max(100, defaultSeqTimeoutMs));
            bool suppressStart = starters.Any(r => r.KeyboardChord!.SuppressKeys);
            if (suppressStart)
            {
                _suppressedHeldKeys.Add(vkCode);
            }
            var lead = starters[0];
            return new ChordProcessResult(
                suppressStart,
                expiredReplay ?? [],
                null,
                $"{lead.Name} (1/{lead.KeyboardChord!.Steps.Count})");
        }

        return new ChordProcessResult(false, expiredReplay ?? [], null);
    }

    public GestureRule? FlushExpiredSequenceCommit(long timestampMs)
    {
        if (_pendingSeqCompletedRule is null || timestampMs < _pendingSeqCommitDeadlineMs)
        {
            return null;
        }

        var rule = _pendingSeqCompletedRule;
        _pendingSeqCompletedRule = null;
        _pendingSeqCommitDeadlineMs = 0;
        _seqMatchedSteps = 0;
        _activeSeqRules.Clear();
        return rule;
    }

    public IReadOnlyList<int> FlushExpiredSimultaneous(long timestampMs)
    {
        if (_simBuffer.Count == 0 || (timestampMs - _simStartMs) <= _simWindowMs)
        {
            return [];
        }

        int[] flushed = [.. _simBuffer];
        _simBuffer.Clear();
        return flushed;
    }

    private static List<GestureRule> GetScopedChordRules(
        IEnumerable<GestureRule> allRules,
        string? foregroundProcessName,
        string? windowTitle)
    {
        var matched = new List<(GestureRule Rule, int Priority)>();
        foreach (var rule in allRules)
        {
            if (!rule.IsEnabled ||
                rule.TriggerType != TriggerType.KeyboardChord ||
                rule.KeyboardChord is null ||
                rule.KeyboardChord.Steps.Count == 0 ||
                rule.KeyboardChord.Steps[0].Modifiers == ChordModifiers.None)
            {
                continue;
            }

            if (RuleMatcher.MatchesScopeAndPriority(rule, foregroundProcessName, windowTitle, out int priority))
            {
                matched.Add((rule, priority));
            }
        }

        return matched
            .OrderByDescending(x => x.Priority)
            .Select(x => x.Rule)
            .ToList();
    }

    private static GestureRule? FindMatchingSimultaneousRule(
        List<GestureRule> simRules,
        HashSet<int> pressedKeys,
        ChordModifiers modifiers)
    {
        foreach (var rule in simRules)
        {
            var step = rule.KeyboardChord!.Steps[0];
            if (step.Modifiers == modifiers &&
                step.KeyCodes.Count == pressedKeys.Count &&
                step.KeyCodes.All(pressedKeys.Contains))
            {
                return rule;
            }
        }

        return null;
    }

    private static bool IsSubsetOfAnySimultaneousRule(
        List<GestureRule> simRules,
        HashSet<int> pressedKeys,
        ChordModifiers modifiers)
    {
        foreach (var rule in simRules)
        {
            var step = rule.KeyboardChord!.Steps[0];
            if (step.Modifiers == modifiers &&
                pressedKeys.Count < step.KeyCodes.Count &&
                pressedKeys.All(step.KeyCodes.Contains))
            {
                return true;
            }
        }

        return false;
    }

    private static bool StepMatchesSingleKey(ChordStep step, int vkCode, ChordModifiers activeModifiers)
    {
        return step.Modifiers == activeModifiers &&
               step.KeyCodes.Count == 1 &&
               step.KeyCodes[0] == vkCode;
    }

    private static bool StepMatchesModifierOnly(ChordStep step, ChordModifiers modifierFlag)
    {
        return step.KeyCodes.Count == 0 && step.Modifiers == modifierFlag;
    }

    private ChordProcessResult ProcessModifierKeyEvent(
        int vkCode,
        bool isKeyDown,
        ChordModifiers activeModifiers,
        long timestampMs,
        IEnumerable<GestureRule> allRules,
        string? foregroundProcessName,
        string? windowTitle,
        int defaultSeqTimeoutMs)
    {
        var modFlag = ChordKeyHelper.ToModifierFlag(vkCode);
        if (!isKeyDown)
        {
            _heldModifierFlags &= ~modFlag;
            return new ChordProcessResult(false, [], null);
        }

        // Ignore OS key-repeat while the modifier is already held.
        if ((_heldModifierFlags & modFlag) != 0)
        {
            return new ChordProcessResult(false, [], null);
        }
        _heldModifierFlags |= modFlag;

        // Require a clean modifier tap (no other modifier currently held).
        if ((activeModifiers & ~modFlag) != ChordModifiers.None)
        {
            return new ChordProcessResult(false, [], null);
        }

        if (_seqMatchedSteps > 0 && timestampMs > _seqDeadlineMs)
        {
            _seqMatchedSteps = 0;
            _activeSeqRules.Clear();
            _pendingSeqCompletedRule = null;
            _pendingSeqCommitDeadlineMs = 0;
        }

        var seqRules = GetScopedChordRules(allRules, foregroundProcessName, windowTitle)
            .Where(r => r.KeyboardChord!.Mode == KeyboardChordMode.Sequential &&
                        r.KeyboardChord.Steps.Count >= 2)
            .ToList();

        if (_seqMatchedSteps > 0 && _activeSeqRules.Count > 0)
        {
            var advancing = _activeSeqRules
                .Where(r => _seqMatchedSteps < r.KeyboardChord!.Steps.Count &&
                            StepMatchesModifierOnly(r.KeyboardChord.Steps[_seqMatchedSteps], modFlag))
                .ToList();

            if (advancing.Count > 0)
            {
                int nextStepCount = _seqMatchedSteps + 1;
                var completed = advancing.FirstOrDefault(r => r.KeyboardChord!.Steps.Count == nextStepCount);
                var longer = advancing.Where(r => r.KeyboardChord!.Steps.Count > nextStepCount).ToList();
                int execDelay = completed?.ExecutionDelayMs ?? 0;
                if (completed is not null && execDelay <= 0)
                {
                    _seqMatchedSteps = 0;
                    _activeSeqRules.Clear();
                    _pendingSeqCompletedRule = null;
                    _pendingSeqCommitDeadlineMs = 0;
                    return new ChordProcessResult(false, [], completed);
                }

                var nextPool = longer.Count > 0 ? longer : [];
                _activeSeqRules = nextPool;
                _seqMatchedSteps = nextPool.Count > 0 ? nextStepCount : 0;
                int timeout = advancing.Max(r => r.KeyboardChord!.TimeoutMs);
                int waitMs = timeout > 0 ? timeout : Math.Max(100, defaultSeqTimeoutMs);
                _seqDeadlineMs = nextPool.Count > 0 ? timestampMs + Math.Max(execDelay, waitMs) : 0;
                _pendingSeqCompletedRule = completed;
                _pendingSeqCommitDeadlineMs = completed is not null ? timestampMs + execDelay : 0;

                var lead = completed ?? nextPool[0];
                return new ChordProcessResult(
                    false,
                    [],
                    null,
                    $"{lead.Name} ({nextStepCount}/{lead.KeyboardChord!.Steps.Count})");
            }

            // Preserve sequences expecting a Modifier+Key step (e.g. Ctrl+K -> Ctrl+B).
            bool expectingKeyStep = _activeSeqRules.Any(r =>
                _seqMatchedSteps < r.KeyboardChord!.Steps.Count &&
                r.KeyboardChord.Steps[_seqMatchedSteps].KeyCodes.Count > 0);
            if (expectingKeyStep)
            {
                return new ChordProcessResult(false, [], null);
            }

            var pendingCommit = _pendingSeqCompletedRule;
            _seqMatchedSteps = 0;
            _activeSeqRules.Clear();
            _pendingSeqCompletedRule = null;
            _pendingSeqCommitDeadlineMs = 0;
            if (pendingCommit is not null)
            {
                return new ChordProcessResult(false, [], pendingCommit);
            }
        }

        var starters = seqRules
            .Where(r => StepMatchesModifierOnly(r.KeyboardChord!.Steps[0], modFlag))
            .ToList();

        if (starters.Count > 0)
        {
            _activeSeqRules = starters;
            _seqMatchedSteps = 1;
            int timeout = starters.Max(r => r.KeyboardChord!.TimeoutMs);
            _seqDeadlineMs = timestampMs + (timeout > 0 ? timeout : Math.Max(100, defaultSeqTimeoutMs));
            var lead = starters[0];
            return new ChordProcessResult(
                false,
                [],
                null,
                $"{lead.Name} (1/{lead.KeyboardChord!.Steps.Count})");
        }

        return new ChordProcessResult(false, [], null);
    }
}
