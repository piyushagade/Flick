using Flick.Core.Events;
using Flick.Core.Models;
using Xunit;

namespace Flick.Core.Tests;

public sealed class SystemEventTests
{
    [Fact]
    public void FindMatchingEventRules_AppOpen_RespectsProcessScope()
    {
        var rules = new[]
        {
            new GestureRule
            {
                Name = "EdgeOpened",
                TriggerType = TriggerType.SystemEvent,
                SystemEvent = SystemEventKind.AppOpen,
                ApplicationFilter = "msedge",
                IsEnabled = true
            },
            new GestureRule
            {
                Name = "AnyAppOpened",
                TriggerType = TriggerType.SystemEvent,
                SystemEvent = SystemEventKind.AppOpen,
                ApplicationFilter = "*",
                IsEnabled = true
            }
        };

        var edgeMatches = SystemEventHelper.FindMatchingEventRules(
            SystemEventKind.AppOpen, "msedge", "New Tab", ModifierKeyFilter.None, rules);
        Assert.Equal(2, edgeMatches.Count);
        Assert.Equal("EdgeOpened", edgeMatches[0].Name);

        var notepadMatches = SystemEventHelper.FindMatchingEventRules(
            SystemEventKind.AppOpen, "notepad", "Untitled", ModifierKeyFilter.None, rules);
        Assert.Single(notepadMatches);
        Assert.Equal("AnyAppOpened", notepadMatches[0].Name);
    }

    [Fact]
    public void FindMatchingEventRules_SessionLock_MatchesGlobalAndRespectsModifiers()
    {
        var rules = new[]
        {
            new GestureRule
            {
                Name = "MuteOnLock",
                TriggerType = TriggerType.SystemEvent,
                SystemEvent = SystemEventKind.SessionLock,
                ApplicationFilter = "*",
                IsEnabled = true
            },
            new GestureRule
            {
                Name = "DisabledLock",
                TriggerType = TriggerType.SystemEvent,
                SystemEvent = SystemEventKind.SessionLock,
                ApplicationFilter = "*",
                IsEnabled = false
            },
            new GestureRule
            {
                Name = "CtrlDisplayOff",
                TriggerType = TriggerType.SystemEvent,
                SystemEvent = SystemEventKind.DisplayOff,
                RequiredModifiers = ModifierKeyFilter.Ctrl,
                ApplicationFilter = "*",
                IsEnabled = true
            }
        };

        var lockMatches = SystemEventHelper.FindMatchingEventRules(
            SystemEventKind.SessionLock, "explorer", null, ModifierKeyFilter.None, rules);
        Assert.Single(lockMatches);
        Assert.Equal("MuteOnLock", lockMatches[0].Name);

        Assert.Empty(SystemEventHelper.FindMatchingEventRules(
            SystemEventKind.DisplayOff, "explorer", null, ModifierKeyFilter.None, rules));
        Assert.Single(SystemEventHelper.FindMatchingEventRules(
            SystemEventKind.DisplayOff, "explorer", null, ModifierKeyFilter.Ctrl, rules));
    }
}
