using Flick.Core.Keyboard;
using Flick.Core.Models;
using Flick.Core.Rules;
using Xunit;

namespace Flick.Core.Tests;

public sealed class KeyboardChordTests
{
    private static GestureRule MakeSimRule(string name, int[] vks, ChordModifiers mods = ChordModifiers.None, string app = "*") => new()
    {
        Name = name,
        TriggerType = TriggerType.KeyboardChord,
        ApplicationFilter = app,
        KeyboardChord = new KeyboardChordDefinition
        {
            Mode = KeyboardChordMode.Simultaneous,
            Steps = [new ChordStep { Modifiers = mods, KeyCodes = [.. vks] }],
            SuppressKeys = true
        }
    };

    private static GestureRule MakeSeqRule(string name, (ChordModifiers Mods, int Vk)[] steps, string app = "*") => new()
    {
        Name = name,
        TriggerType = TriggerType.KeyboardChord,
        ApplicationFilter = app,
        KeyboardChord = new KeyboardChordDefinition
        {
            Mode = KeyboardChordMode.Sequential,
            Steps = steps.Select(s => new ChordStep { Modifiers = s.Mods, KeyCodes = [s.Vk] }).ToList(),
            SuppressKeys = true
        }
    };

    [Fact]
    public void Simultaneous_TwoKeyChord_MatchesAndSuppressesUp()
    {
        var engine = new KeyboardChordEngine();
        var rules = new[] { MakeSimRule("CtrlJK", ['J', 'K'], ChordModifiers.Ctrl) };

        var r1 = engine.ProcessKeyEvent('J', true, ChordModifiers.Ctrl, 100, rules, "notepad", null);
        Assert.True(r1.SuppressCurrentEvent);
        Assert.Null(r1.MatchedRule);

        var r2 = engine.ProcessKeyEvent('K', true, ChordModifiers.Ctrl, 120, rules, "notepad", null);
        Assert.True(r2.SuppressCurrentEvent);
        Assert.Equal("CtrlJK", r2.MatchedRule?.Name);

        // KeyUp for both J and K should also be suppressed.
        Assert.True(engine.ProcessKeyEvent('J', false, ChordModifiers.Ctrl, 180, rules, "notepad", null).SuppressCurrentEvent);
        Assert.True(engine.ProcessKeyEvent('K', false, ChordModifiers.Ctrl, 190, rules, "notepad", null).SuppressCurrentEvent);
    }

    [Fact]
    public void Simultaneous_FastTypingRelease_FlushesJBeforeK()
    {
        var engine = new KeyboardChordEngine();
        var rules = new[] { MakeSimRule("CtrlJK", ['J', 'K'], ChordModifiers.Ctrl) };

        var rDownJ = engine.ProcessKeyEvent('J', true, ChordModifiers.Ctrl, 100, rules, "notepad", null);
        Assert.True(rDownJ.SuppressCurrentEvent);

        // User releases J before pressing K
        var rUpJ = engine.ProcessKeyEvent('J', false, ChordModifiers.Ctrl, 115, rules, "notepad", null);
        Assert.False(rUpJ.SuppressCurrentEvent);
        Assert.Equal(['J'], rUpJ.KeysToReplayDown);
        Assert.Null(rUpJ.MatchedRule);

        // Now K is pressed -> buffered as new potential Ctrl+J+K start, not matched
        var rDownK = engine.ProcessKeyEvent('K', true, ChordModifiers.Ctrl, 125, rules, "notepad", null);
        Assert.True(rDownK.SuppressCurrentEvent);
        Assert.Null(rDownK.MatchedRule);
    }

    [Fact]
    public void Simultaneous_NonMatchingSecondKey_FlushesBuffer()
    {
        var engine = new KeyboardChordEngine();
        var rules = new[] { MakeSimRule("CtrlJK", ['J', 'K'], ChordModifiers.Ctrl) };

        engine.ProcessKeyEvent('J', true, ChordModifiers.Ctrl, 100, rules, "notepad", null);
        var rL = engine.ProcessKeyEvent('L', true, ChordModifiers.Ctrl, 120, rules, "notepad", null);

        Assert.False(rL.SuppressCurrentEvent);
        Assert.Equal(['J'], rL.KeysToReplayDown);
        Assert.Null(rL.MatchedRule);
    }

    [Fact]
    public void Simultaneous_WindowExpires_FlushReturnsBufferedKey()
    {
        var engine = new KeyboardChordEngine();
        var rules = new[] { MakeSimRule("CtrlJK", ['J', 'K'], ChordModifiers.Ctrl) };

        engine.ProcessKeyEvent('J', true, ChordModifiers.Ctrl, 100, rules, "notepad", null, defaultSimWindowMs: 50);
        Assert.Empty(engine.FlushExpiredSimultaneous(140));
        Assert.Equal(['J'], engine.FlushExpiredSimultaneous(160));
    }

    [Fact]
    public void UnrelatedKey_PassesThroughWithZeroDelay()
    {
        var engine = new KeyboardChordEngine();
        var rules = new[] { MakeSimRule("CtrlJK", ['J', 'K'], ChordModifiers.Ctrl) };

        var r = engine.ProcessKeyEvent('A', true, ChordModifiers.None, 100, rules, "notepad", null);
        Assert.False(r.SuppressCurrentEvent);
        Assert.Empty(r.KeysToReplayDown);
        Assert.Null(r.MatchedRule);
        Assert.False(engine.HasPendingSimultaneous);
    }

    [Fact]
    public void ModifierlessFirstStep_IsIgnoredSoNormalTypingWorks()
    {
        var engine = new KeyboardChordEngine();
        var rules = new[]
        {
            MakeSeqRule("BareZKL", [(ChordModifiers.None, 'Z'), (ChordModifiers.None, 'K'), (ChordModifiers.None, 'L')]),
            MakeSimRule("BareJK", ['J', 'K'], ChordModifiers.None)
        };

        Assert.False(engine.ProcessKeyEvent('Z', true, ChordModifiers.None, 100, rules, "notepad", null).SuppressCurrentEvent);
        Assert.False(engine.ProcessKeyEvent('K', true, ChordModifiers.None, 120, rules, "notepad", null).SuppressCurrentEvent);
        Assert.False(engine.ProcessKeyEvent('L', true, ChordModifiers.None, 140, rules, "notepad", null).SuppressCurrentEvent);
        Assert.False(engine.ProcessKeyEvent('J', true, ChordModifiers.None, 160, rules, "notepad", null).SuppressCurrentEvent);
    }

    [Fact]
    public void Sequential_TwoStepChord_MatchesWithinTimeout()
    {
        var engine = new KeyboardChordEngine();
        var rules = new[]
        {
            MakeSeqRule("Comment", [(ChordModifiers.Ctrl, 'K'), (ChordModifiers.None, 'B')])
        };

        var r1 = engine.ProcessKeyEvent('K', true, ChordModifiers.Ctrl, 100, rules, "code", null);
        Assert.True(r1.SuppressCurrentEvent);
        Assert.Null(r1.MatchedRule);
        Assert.NotNull(r1.PendingSequenceLabel);

        var r2 = engine.ProcessKeyEvent('B', true, ChordModifiers.None, 400, rules, "code", null);
        Assert.True(r2.SuppressCurrentEvent);
        Assert.Equal("Comment", r2.MatchedRule?.Name);
    }

    [Fact]
    public void Sequential_TimeoutExpires_DoesNotMatch()
    {
        var engine = new KeyboardChordEngine();
        var rules = new[]
        {
            MakeSeqRule("Comment", [(ChordModifiers.Ctrl, 'K'), (ChordModifiers.None, 'B')])
        };

        engine.ProcessKeyEvent('K', true, ChordModifiers.Ctrl, 100, rules, "code", null, defaultSeqTimeoutMs: 500);
        var r2 = engine.ProcessKeyEvent('B', true, ChordModifiers.None, 700, rules, "code", null, defaultSeqTimeoutMs: 500);

        Assert.False(r2.SuppressCurrentEvent);
        Assert.Null(r2.MatchedRule);
    }

    [Fact]
    public void AppScopedRule_TakesPriorityOverGlobal()
    {
        var engine = new KeyboardChordEngine();
        var rules = new[]
        {
            MakeSimRule("GlobalJK", ['J', 'K'], ChordModifiers.Ctrl, app: "*"),
            MakeSimRule("CodeJK", ['J', 'K'], ChordModifiers.Ctrl, app: "code")
        };

        engine.ProcessKeyEvent('J', true, ChordModifiers.Ctrl, 100, rules, "code", null);
        var r2 = engine.ProcessKeyEvent('K', true, ChordModifiers.Ctrl, 110, rules, "code", null);
        Assert.Equal("CodeJK", r2.MatchedRule?.Name);
    }

    [Fact]
    public void GestureRuleMatcher_IgnoresKeyboardChordRules()
    {
        var matcher = new RuleMatcher();
        var rules = new[] { MakeSimRule("JK", ['J', 'K']) };
        Assert.Null(matcher.FindBestMatch(string.Empty, "notepad", rules));
    }

    [Fact]
    public void ChordKeyHelper_FormatsBothModes()
    {
        var sim = MakeSimRule("JK", ['J', 'K'], ChordModifiers.Ctrl).KeyboardChord;
        Assert.Equal("Ctrl+J+K", ChordKeyHelper.FormatChord(sim));

        var seq = MakeSeqRule("Seq", [(ChordModifiers.Ctrl, 'K'), (ChordModifiers.None, 'B')]).KeyboardChord;
        Assert.Equal("Ctrl+K → B", ChordKeyHelper.FormatChord(seq));
        Assert.Equal("Ctrl → Alt → Alt", ChordKeyHelper.FormatChord(new KeyboardChordDefinition
        {
            Mode = KeyboardChordMode.Sequential,
            Steps =
            [
                new ChordStep { Modifiers = ChordModifiers.Ctrl },
                new ChordStep { Modifiers = ChordModifiers.Alt },
                new ChordStep { Modifiers = ChordModifiers.Alt }
            ]
        }));
    }

    [Fact]
    public void Sequential_ModifierOnlySequence_CtrlAltAlt_Matches()
    {
        var engine = new KeyboardChordEngine();
        var rules = new[]
        {
            new GestureRule
            {
                Name = "Sneak",
                TriggerType = TriggerType.KeyboardChord,
                ApplicationFilter = "*",
                KeyboardChord = new KeyboardChordDefinition
                {
                    Mode = KeyboardChordMode.Sequential,
                    Steps =
                    [
                        new ChordStep { Modifiers = ChordModifiers.Ctrl, KeyCodes = [] },
                        new ChordStep { Modifiers = ChordModifiers.Alt, KeyCodes = [] },
                        new ChordStep { Modifiers = ChordModifiers.Alt, KeyCodes = [] }
                    ],
                    SuppressKeys = false
                }
            }
        };

        // Tap Ctrl
        var r1 = engine.ProcessKeyEvent(ChordKeyHelper.VkLControl, true, ChordModifiers.Ctrl, 100, rules, "notepad", null);
        Assert.False(r1.SuppressCurrentEvent);
        Assert.Null(r1.MatchedRule);
        Assert.Equal("Sneak (1/3)", r1.PendingSequenceLabel);
        engine.ProcessKeyEvent(ChordKeyHelper.VkLControl, false, ChordModifiers.None, 130, rules, "notepad", null);

        // Tap Alt
        var r2 = engine.ProcessKeyEvent(ChordKeyHelper.VkLMenu, true, ChordModifiers.Alt, 220, rules, "notepad", null);
        Assert.Null(r2.MatchedRule);
        Assert.Equal("Sneak (2/3)", r2.PendingSequenceLabel);
        engine.ProcessKeyEvent(ChordKeyHelper.VkLMenu, false, ChordModifiers.None, 250, rules, "notepad", null);

        // Tap Alt again -> completes!
        var r3 = engine.ProcessKeyEvent(ChordKeyHelper.VkLMenu, true, ChordModifiers.Alt, 340, rules, "notepad", null);
        Assert.Equal("Sneak", r3.MatchedRule?.Name);
        engine.ProcessKeyEvent(ChordKeyHelper.VkLMenu, false, ChordModifiers.None, 370, rules, "notepad", null);
    }

    private static GestureRule MakeModifierSeqRule(string name, int executionDelayMs, params ChordModifiers[] mods) => new()
    {
        Name = name,
        TriggerType = TriggerType.KeyboardChord,
        ApplicationFilter = "*",
        ExecutionDelayMs = executionDelayMs,
        KeyboardChord = new KeyboardChordDefinition
        {
            Mode = KeyboardChordMode.Sequential,
            Steps = mods.Select(m => new ChordStep { Modifiers = m, KeyCodes = [] }).ToList(),
            SuppressKeys = false
        }
    };

    [Fact]
    public void Sequential_ModifierPrefix_WithoutDelay_FiresImmediately()
    {
        var engine = new KeyboardChordEngine();
        var rules = new[]
        {
            MakeModifierSeqRule("ThreeStep", 0, ChordModifiers.Ctrl, ChordModifiers.Alt, ChordModifiers.Alt),
            MakeModifierSeqRule("FourStep", 0, ChordModifiers.Ctrl, ChordModifiers.Alt, ChordModifiers.Alt, ChordModifiers.Alt)
        };

        engine.ProcessKeyEvent(ChordKeyHelper.VkLControl, true, ChordModifiers.Ctrl, 100, rules, "notepad", null);
        engine.ProcessKeyEvent(ChordKeyHelper.VkLControl, false, ChordModifiers.None, 120, rules, "notepad", null);
        engine.ProcessKeyEvent(ChordKeyHelper.VkLMenu, true, ChordModifiers.Alt, 200, rules, "notepad", null);
        engine.ProcessKeyEvent(ChordKeyHelper.VkLMenu, false, ChordModifiers.None, 220, rules, "notepad", null);

        var r3 = engine.ProcessKeyEvent(ChordKeyHelper.VkLMenu, true, ChordModifiers.Alt, 300, rules, "notepad", null);
        Assert.Equal("ThreeStep", r3.MatchedRule?.Name);
        Assert.False(engine.HasPendingSequenceCommit);
    }

    [Fact]
    public void Sequential_ModifierPrefix_WaitsAndMatchesLongerRule()
    {
        var engine = new KeyboardChordEngine();
        var rules = new[]
        {
            MakeModifierSeqRule("ThreeStep", 400, ChordModifiers.Ctrl, ChordModifiers.Alt, ChordModifiers.Alt),
            MakeModifierSeqRule("FourStep", 0, ChordModifiers.Ctrl, ChordModifiers.Alt, ChordModifiers.Alt, ChordModifiers.Alt)
        };

        engine.ProcessKeyEvent(ChordKeyHelper.VkLControl, true, ChordModifiers.Ctrl, 100, rules, "notepad", null);
        engine.ProcessKeyEvent(ChordKeyHelper.VkLControl, false, ChordModifiers.None, 120, rules, "notepad", null);
        engine.ProcessKeyEvent(ChordKeyHelper.VkLMenu, true, ChordModifiers.Alt, 200, rules, "notepad", null);
        engine.ProcessKeyEvent(ChordKeyHelper.VkLMenu, false, ChordModifiers.None, 220, rules, "notepad", null);

        // 3rd step matches ThreeStep, but waits ExecutionDelayMs (400ms) because FourStep shares this prefix.
        var r3 = engine.ProcessKeyEvent(ChordKeyHelper.VkLMenu, true, ChordModifiers.Alt, 300, rules, "notepad", null);
        Assert.Null(r3.MatchedRule);
        Assert.True(engine.HasPendingSequenceCommit);
        engine.ProcessKeyEvent(ChordKeyHelper.VkLMenu, false, ChordModifiers.None, 320, rules, "notepad", null);

        // 4th step completes FourStep immediately.
        var r4 = engine.ProcessKeyEvent(ChordKeyHelper.VkLMenu, true, ChordModifiers.Alt, 400, rules, "notepad", null);
        Assert.Equal("FourStep", r4.MatchedRule?.Name);
        Assert.False(engine.HasPendingSequenceCommit);
    }

    [Fact]
    public void Sequential_ModifierPrefix_CommitsShorterRuleOnTimeout()
    {
        var engine = new KeyboardChordEngine();
        var rules = new[]
        {
            MakeModifierSeqRule("ThreeStep", 400, ChordModifiers.Ctrl, ChordModifiers.Alt, ChordModifiers.Alt),
            MakeModifierSeqRule("FourStep", 0, ChordModifiers.Ctrl, ChordModifiers.Alt, ChordModifiers.Alt, ChordModifiers.Alt)
        };

        engine.ProcessKeyEvent(ChordKeyHelper.VkLControl, true, ChordModifiers.Ctrl, 100, rules, "notepad", null);
        engine.ProcessKeyEvent(ChordKeyHelper.VkLControl, false, ChordModifiers.None, 120, rules, "notepad", null);
        engine.ProcessKeyEvent(ChordKeyHelper.VkLMenu, true, ChordModifiers.Alt, 200, rules, "notepad", null);
        engine.ProcessKeyEvent(ChordKeyHelper.VkLMenu, false, ChordModifiers.None, 220, rules, "notepad", null);

        var r3 = engine.ProcessKeyEvent(ChordKeyHelper.VkLMenu, true, ChordModifiers.Alt, 300, rules, "notepad", null);
        Assert.Null(r3.MatchedRule);
        Assert.True(engine.HasPendingSequenceCommit);

        // Before 400ms ExecutionDelayMs: does not flush.
        Assert.Null(engine.FlushExpiredSequenceCommit(650));
        // After 400ms ExecutionDelayMs: commits ThreeStep.
        var committed = engine.FlushExpiredSequenceCommit(710);
        Assert.Equal("ThreeStep", committed?.Name);
        Assert.False(engine.HasPendingSequenceCommit);
    }
}
