using Flick.Core.Models;
using Flick.Core.TextExpansion;
using Xunit;

namespace Flick.Core.Tests;

public sealed class TextExpansionTests
{
    private static TextExpansionRule MakeRule(
        string abbr,
        string template,
        ExpansionTriggerMode trigger = ExpansionTriggerMode.OnDelimiter,
        ExpansionCaseMode caseMode = ExpansionCaseMode.SmartCase,
        string app = "*",
        bool wordBoundary = true,
        bool keepDelimiter = true) => new()
    {
        Abbreviation = abbr,
        Template = template,
        TriggerMode = trigger,
        CaseMode = caseMode,
        ApplicationFilter = app,
        RequireWordBoundary = wordBoundary,
        KeepDelimiter = keepDelimiter,
        IsEnabled = true
    };

    private static TextExpansionMatchResult TypeString(
        TextExpansionMatcher matcher,
        string text,
        IReadOnlyList<TextExpansionRule> rules,
        string? proc = "notepad",
        string? title = "Untitled")
    {
        TextExpansionMatchResult last = new(null, string.Empty, 0, null, false);
        foreach (char c in text)
        {
            last = matcher.ProcessCharacter(c, rules, proc, title);
            if (last.MatchedRule is not null)
            {
                return last;
            }
        }
        return last;
    }

    [Fact]
    public void OnDelimiter_MatchesOnSpace_AndReturnsExactBackspaceCount()
    {
        var matcher = new TextExpansionMatcher();
        var rules = new[] { MakeRule("brb", "be right back") };

        var res = TypeString(matcher, "brb ", rules);

        Assert.NotNull(res.MatchedRule);
        Assert.Equal("brb", res.TypedAbbreviation);
        Assert.Equal(3, res.BackspaceCount);
        Assert.Equal(' ', res.DelimiterChar);
        Assert.True(res.SuppressCurrentEvent);
    }

    [Fact]
    public void Immediate_MatchesOnLastChar_WithLengthMinusOneBackspaces()
    {
        var matcher = new TextExpansionMatcher();
        var rules = new[] { MakeRule(";sig", "Best regards,", ExpansionTriggerMode.Immediate) };

        var res = TypeString(matcher, ";sig", rules);

        Assert.NotNull(res.MatchedRule);
        Assert.Equal(";sig", res.TypedAbbreviation);
        Assert.Equal(3, res.BackspaceCount);
        Assert.Null(res.DelimiterChar);
        Assert.True(res.SuppressCurrentEvent);
    }

    [Fact]
    public void WordBoundary_PreventsMidWordExpansion()
    {
        var matcher = new TextExpansionMatcher();
        var rules = new[] { MakeRule("br", "Best regards") };

        var midWord = TypeString(matcher, "abr ", rules);
        Assert.Null(midWord.MatchedRule);

        matcher.Reset();
        var afterSpace = TypeString(matcher, "hi br ", rules);
        Assert.NotNull(afterSpace.MatchedRule);
    }

    [Fact]
    public void Backspace_UpdatesBufferCorrectly()
    {
        var matcher = new TextExpansionMatcher();
        var rules = new[] { MakeRule("ty", "thank you") };

        TypeString(matcher, "tx", rules);
        matcher.ProcessBackspace();
        var res = TypeString(matcher, "y ", rules);

        Assert.NotNull(res.MatchedRule);
        Assert.Equal("ty", res.TypedAbbreviation);
    }

    [Fact]
    public void AppScopedRule_OverridesGlobalRule()
    {
        var matcher = new TextExpansionMatcher();
        var global = MakeRule(";hi", "Hello!");
        var vscode = MakeRule(";hi", "// Hello from VS Code", app: "code");

        var inCode = TypeString(matcher, ";hi ", [global, vscode], proc: "code");
        Assert.Equal("// Hello from VS Code", inCode.MatchedRule?.Template);

        matcher.Reset();
        var inNotepad = TypeString(matcher, ";hi ", [global, vscode], proc: "notepad");
        Assert.Equal("Hello!", inNotepad.MatchedRule?.Template);
    }

    [Fact]
    public void SmartCase_AdaptsOutputCasing()
    {
        var rule = MakeRule("ty", "thank you", caseMode: ExpansionCaseMode.SmartCase);
        var ctx = new ExpansionContext("", "", "notepad", [rule]);

        var lower = TextExpansionTemplateEngine.Evaluate(rule, "ty", ' ', ctx);
        var title = TextExpansionTemplateEngine.Evaluate(rule, "Ty", ' ', ctx);
        var upper = TextExpansionTemplateEngine.Evaluate(rule, "TY", ' ', ctx);

        Assert.Equal("thank you ", lower[0].Value);
        Assert.Equal("Thank you ", title[0].Value);
        Assert.Equal("THANK YOU ", upper[0].Value);
    }

    [Fact]
    public void Template_CursorPosition_CountsLogicalCharsIncludingCrlf()
    {
        var rule = MakeRule("ifb", "if ({cursor}) {\r\n}", keepDelimiter: false);
        var ctx = new ExpansionContext("", "", "code", [rule]);

        var steps = TextExpansionTemplateEngine.Evaluate(rule, "ifb", ' ', ctx);

        Assert.Equal(2, steps.Count);
        Assert.Equal(ExpansionStepKind.Text, steps[0].Kind);
        Assert.Equal("if () {\r\n}", steps[0].Value);
        Assert.Equal(ExpansionStepKind.MoveCursorLeft, steps[1].Kind);
        // ") {\r\n}" -> ')', ' ', '{', '\r\n' (1), '}' = 5 logical chars
        Assert.Equal(5, steps[1].Count);
    }

    [Fact]
    public void Template_Variables_NestedSnippet_And_ControlSteps()
    {
        var sigRule = MakeRule(";sig", "Signed by {window.process}");
        var mainRule = MakeRule(
            ";rep",
            "Date: {date:yyyy-MM-dd} | Clip: {clipboard}{key:Tab}{delay:50}{snippet:;sig}",
            keepDelimiter: false);

        var ctx = new ExpansionContext(
            ClipboardText: "ABC",
            WindowTitle: "Doc",
            ProcessName: "winword",
            AllRules: [sigRule, mainRule],
            Now: new DateTimeOffset(2026, 11, 26, 10, 30, 0, TimeSpan.Zero));

        var steps = TextExpansionTemplateEngine.Evaluate(mainRule, ";rep", ' ', ctx);

        Assert.Equal(4, steps.Count);
        Assert.Equal("Date: 2026-11-26 | Clip: ABC", steps[0].Value);
        Assert.Equal(ExpansionStepKind.Key, steps[1].Kind);
        Assert.Equal("Tab", steps[1].Value);
        Assert.Equal(ExpansionStepKind.Delay, steps[2].Kind);
        Assert.Equal(50, steps[2].Count);
        Assert.Equal("Signed by winword", steps[3].Value);
    }

    [Fact]
    public void Template_InteractivePrompts_ExtractAndSubstitute()
    {
        var rule = MakeRule(
            ";greet",
            "Hi {input:Recipient:Friend}, status is {choice:Status:Open|Closed}.",
            keepDelimiter: false);

        var fields = TextExpansionTemplateEngine.ExtractPromptFields(rule.Template);
        Assert.Equal(2, fields.Count);
        Assert.Equal(ExpansionPromptKind.TextInput, fields[0].Kind);
        Assert.Equal("Recipient", fields[0].Label);
        Assert.Equal("Friend", fields[0].DefaultValue);
        Assert.Equal(ExpansionPromptKind.Choice, fields[1].Kind);
        Assert.Equal(new[] { "Open", "Closed" }, fields[1].Choices);

        var ctx = new ExpansionContext(
            ClipboardText: "",
            WindowTitle: "",
            ProcessName: "outlook",
            AllRules: [rule],
            PromptAnswers: new Dictionary<string, string>
            {
                [fields[0].TokenKey] = "Alice",
                [fields[1].TokenKey] = "Closed"
            });

        var steps = TextExpansionTemplateEngine.Evaluate(rule, ";greet", ' ', ctx);
        Assert.Single(steps);
        Assert.Equal("Hi Alice, status is Closed.", steps[0].Value);
    }
}
