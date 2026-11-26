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
    }
}
