using System.Text.RegularExpressions;
using Flick.Core.Models;

namespace Flick.Core.Rules;

/// <summary>
/// Resolves the highest-priority enabled rule for a recognized gesture, foreground window, and modifiers.
/// </summary>
public sealed class RuleMatcher
{
    public GestureRule? FindBestMatch(
        string gestureId,
        string? foregroundProcessName,
        IEnumerable<GestureRule> rules,
        GestureInputSource inputSource = GestureInputSource.Mouse,
        int fingerCount = 0,
        string? windowTitle = null,
        ModifierKeyFilter activeModifiers = ModifierKeyFilter.None)
    {
        string normalizedProc = NormalizeProcessName(foregroundProcessName);

        GestureRule? bestRule = null;
        int bestPriority = -1;

        foreach (var rule in rules)
        {
            if (!rule.IsEnabled ||
                rule.TriggerType != TriggerType.Gesture ||
                !string.Equals(rule.GestureId, gestureId, StringComparison.Ordinal))
            {
                continue;
            }

            if (rule.InputSource != GestureInputSource.Any && rule.InputSource != inputSource)
            {
                continue;
            }

            if (inputSource == GestureInputSource.Touchpad &&
                rule.FingerCount > 0 &&
                rule.FingerCount != fingerCount)
            {
                continue;
            }

            if (rule.RequiredModifiers != ModifierKeyFilter.None &&
                (activeModifiers & rule.RequiredModifiers) != rule.RequiredModifiers)
            {
                continue;
            }

            if (!MatchesScopeAndPriority(rule, normalizedProc, windowTitle, out int scopePriority))
            {
                continue;
            }

            int priority = scopePriority
                         + (rule.RequiredModifiers != ModifierKeyFilter.None ? 2 : 0)
                         + (rule.InputSource == inputSource ? 2 : 0)
                         + (inputSource == GestureInputSource.Touchpad && rule.FingerCount == fingerCount ? 1 : 0);

            if (priority > bestPriority)
            {
                bestPriority = priority;
                bestRule = rule;
            }
        }

        return bestRule;
    }

    /// <summary>
    /// Checks whether a rule's application and window-title filters match the foreground window
    /// and computes a base specificity priority (app match = 8, title match = 4).
    /// </summary>
    public static bool MatchesScopeAndPriority(
        GestureRule rule,
        string? foregroundProcessName,
        string? windowTitle,
        out int scopePriority)
    {
        scopePriority = 0;
        string normalizedProc = NormalizeProcessName(foregroundProcessName);

        bool titleMatched = false;
        if (!string.IsNullOrWhiteSpace(rule.WindowTitleFilter))
        {
            if (!MatchesWindowTitle(windowTitle, rule.WindowTitleFilter))
            {
                return false;
            }
            titleMatched = true;
        }

        bool appMatched = !rule.IsGlobal &&
                          !string.IsNullOrEmpty(normalizedProc) &&
                          MatchesFilter(normalizedProc, rule.ApplicationFilter);

        if (!rule.IsGlobal && !appMatched)
        {
            return false;
        }

        scopePriority = (appMatched ? 8 : 0) + (titleMatched ? 4 : 0);
        return true;
    }

    public static bool MatchesWindowTitle(string? windowTitle, string filter)
    {
        if (string.IsNullOrWhiteSpace(windowTitle))
        {
            return false;
        }

        string trimmed = filter.Trim();
        if (trimmed.StartsWith("regex:", StringComparison.OrdinalIgnoreCase))
        {
            string pattern = trimmed[6..].Trim();
            try
            {
                return Regex.IsMatch(windowTitle, pattern, RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(50));
            }
            catch
            {
                return false;
            }
        }

        return windowTitle.Contains(trimmed, StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsProcessIgnored(string? processName, IEnumerable<string> ignoredProcesses)
    {
        string normalized = NormalizeProcessName(processName);
        if (string.IsNullOrEmpty(normalized))
        {
            return false;
        }

        foreach (var ignored in ignoredProcesses)
        {
            if (string.Equals(normalized, NormalizeProcessName(ignored), StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    public static bool MatchesFilter(string normalizedProcessName, string filter)
    {
        var tokens = filter.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var token in tokens)
        {
            string candidate = NormalizeProcessName(token);
            if (candidate == "*" || string.Equals(normalizedProcessName, candidate, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    public static string NormalizeProcessName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return string.Empty;
        }

        string trimmed = name.Trim();
        return trimmed.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            ? trimmed[..^4]
            : trimmed;
    }
}
