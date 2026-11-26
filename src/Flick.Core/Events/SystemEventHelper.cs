using Flick.Core.Models;
using Flick.Core.Rules;

namespace Flick.Core.Events;

/// <summary>
/// Formatting and rule-matching helpers for <see cref="TriggerType.SystemEvent"/> triggers.
/// </summary>
public static class SystemEventHelper
{
    public static string FormatEvent(SystemEventKind? kind) => kind switch
    {
        SystemEventKind.AppOpen => "App Opened",
        SystemEventKind.AppClose => "App Closed",
        SystemEventKind.AppMinimized => "App Minimized",
        SystemEventKind.AppFocused => "App Focused",
        SystemEventKind.OnLogin => "On Login / Startup",
        SystemEventKind.SessionLock => "Windows Locked",
        SystemEventKind.SessionUnlock => "Windows Unlocked",
        SystemEventKind.DisplayOff => "Display Turned Off",
        SystemEventKind.DisplayOn => "Display Turned On",
        SystemEventKind.StartMenuOpen => "Start Menu Opened",
        SystemEventKind.SystemTrayClick => "System Tray Clicked",
        _ => "System Event"
    };

    public static bool IsAppScopedEvent(SystemEventKind kind) => kind is
        SystemEventKind.AppOpen or
        SystemEventKind.AppClose or
        SystemEventKind.AppMinimized or
        SystemEventKind.AppFocused;

    /// <summary>
    /// Returns all enabled <see cref="TriggerType.SystemEvent"/> rules matching <paramref name="kind"/>,
    /// foreground/event process, window title, and modifiers, ordered by scope specificity.
    /// </summary>
    public static IReadOnlyList<GestureRule> FindMatchingEventRules(
        SystemEventKind kind,
        string? processName,
        string? windowTitle,
        ModifierKeyFilter activeModifiers,
        IEnumerable<GestureRule> rules)
    {
        var matched = new List<(GestureRule Rule, int Priority)>();
        foreach (var rule in rules)
        {
            if (!rule.IsEnabled ||
                rule.TriggerType != TriggerType.SystemEvent ||
                rule.SystemEvent != kind)
            {
                continue;
            }

            if (rule.RequiredModifiers != ModifierKeyFilter.None &&
                (activeModifiers & rule.RequiredModifiers) != rule.RequiredModifiers)
            {
                continue;
            }

            if (IsAppScopedEvent(kind))
            {
                if (RuleMatcher.MatchesScopeAndPriority(rule, processName, windowTitle, out int priority))
                {
                    matched.Add((rule, priority));
                }
            }
            else
            {
                // System-wide events (SessionLock, DisplayOff, OnLogin, StartMenuOpen, etc.)
                // always match Global rules, or match if the active app matches ApplicationFilter.
                if (rule.IsGlobal || RuleMatcher.MatchesScopeAndPriority(rule, processName, windowTitle, out _))
                {
                    matched.Add((rule, rule.IsGlobal ? 0 : 1));
                }
            }
        }

        return matched
            .OrderByDescending(x => x.Priority)
            .Select(x => x.Rule)
            .ToList();
    }
}
