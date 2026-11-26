using System.Text;
using Flick.Core.Models;
using Flick.Core.Rules;

namespace Flick.Core.TextExpansion;

public sealed record TextExpansionMatchResult(
    TextExpansionRule? MatchedRule,
    string TypedAbbreviation,
    int BackspaceCount,
    char? DelimiterChar,
    bool SuppressCurrentEvent);

/// <summary>
/// Rolling-buffer abbreviation matcher for <see cref="TextExpansionRule"/>.
/// Supports immediate and delimiter-triggered expansions, word-boundary checks,
/// smart-case matching, and app/window-title scoping.
/// </summary>
public sealed class TextExpansionMatcher
{
    public const int MaxBufferLength = 128;

    private readonly StringBuilder _buffer = new(MaxBufferLength);

    public string CurrentBuffer => _buffer.ToString();

    public void Reset() => _buffer.Clear();

    public void ProcessBackspace()
    {
        if (_buffer.Length > 0)
        {
            _buffer.Remove(_buffer.Length - 1, 1);
        }
    }

    public TextExpansionMatchResult ProcessCharacter(
        char ch,
        IReadOnlyList<TextExpansionRule> rules,
        string? processName,
        string? windowTitle)
    {
        if (rules.Count == 0)
        {
            AppendChar(ch);
            return new TextExpansionMatchResult(null, string.Empty, 0, null, false);
        }

        string normProc = RuleMatcher.NormalizeProcessName(processName);

        // 1. If this character is a delimiter, check OnDelimiter rules against the current buffer first.
        if (IsDelimiter(ch) && _buffer.Length > 0)
        {
            var delimMatch = FindBestMatch(
                rules,
                ExpansionTriggerMode.OnDelimiter,
                normProc,
                windowTitle,
                out string typedAbbr);

            if (delimMatch is not null)
            {
                _buffer.Clear();
                return new TextExpansionMatchResult(
                    delimMatch,
                    typedAbbr,
                    BackspaceCount: typedAbbr.Length,
                    DelimiterChar: ch,
                    SuppressCurrentEvent: true);
            }
        }

        // 2. Append the character to the rolling buffer and check Immediate rules.
        AppendChar(ch);

        var immediateMatch = FindBestMatch(
            rules,
            ExpansionTriggerMode.Immediate,
            normProc,
            windowTitle,
            out string immTyped);

        if (immediateMatch is not null)
        {
            _buffer.Clear();
            return new TextExpansionMatchResult(
                immediateMatch,
                immTyped,
                BackspaceCount: Math.Max(0, immTyped.Length - 1),
                DelimiterChar: null,
                SuppressCurrentEvent: true);
        }

        return new TextExpansionMatchResult(null, string.Empty, 0, null, false);
    }

    public static bool IsDelimiter(char ch) =>
        char.IsWhiteSpace(ch) ||
        ch is '.' or ',' or ';' or ':' or '!' or '?' or '(' or ')' or '[' or ']' or '{' or '}' or '/' or '\\' or '-' or '+' or '=' or '<' or '>' or '"' or '\'';

    private void AppendChar(char ch)
    {
        if (_buffer.Length >= MaxBufferLength)
        {
            _buffer.Remove(0, _buffer.Length - MaxBufferLength + 1);
        }
        _buffer.Append(ch);
    }

    private TextExpansionRule? FindBestMatch(
        IReadOnlyList<TextExpansionRule> rules,
        ExpansionTriggerMode mode,
        string normProc,
        string? windowTitle,
        out string typedAbbreviation)
    {
        TextExpansionRule? bestRule = null;
        string bestTyped = string.Empty;
        int bestScore = -1;

        foreach (var rule in rules)
        {
            if (!rule.IsEnabled || rule.TriggerMode != mode || string.IsNullOrEmpty(rule.Abbreviation))
            {
                continue;
            }

            string abbr = rule.Abbreviation;
            if (abbr.Length > _buffer.Length)
            {
                continue;
            }

            // Word-boundary check: character before abbreviation must not be a word char.
            int startIdx = _buffer.Length - abbr.Length;
            if (rule.RequireWordBoundary && startIdx > 0)
            {
                char prev = _buffer[startIdx - 1];
                if (char.IsLetterOrDigit(prev) || prev == '_')
                {
                    continue;
                }
            }

            if (!MatchesBufferSuffix(startIdx, abbr, rule.CaseMode, out string typed))
            {
                continue;
            }

            if (!MatchesScope(rule, normProc, windowTitle, out int scopeScore))
            {
                continue;
            }

            // Prefer app/title-specific rules, then longer abbreviations, then exact case.
            int score = (scopeScore * 1000)
                      + (abbr.Length * 10)
                      + (rule.CaseMode == ExpansionCaseMode.Exact ? 1 : 0);

            if (score > bestScore)
            {
                bestScore = score;
                bestRule = rule;
                bestTyped = typed;
            }
        }

        typedAbbreviation = bestTyped;
        return bestRule;
    }

    private bool MatchesBufferSuffix(
        int startIdx,
        string abbr,
        ExpansionCaseMode caseMode,
        out string typed)
    {
        bool ignoreCase = caseMode != ExpansionCaseMode.Exact;
        for (int i = 0; i < abbr.Length; i++)
        {
            char bufChar = _buffer[startIdx + i];
            char abbrChar = abbr[i];
            if (ignoreCase
                ? char.ToUpperInvariant(bufChar) != char.ToUpperInvariant(abbrChar)
                : bufChar != abbrChar)
            {
                typed = string.Empty;
                return false;
            }
        }

        typed = _buffer.ToString(startIdx, abbr.Length);
        return true;
    }

    private static bool MatchesScope(
        TextExpansionRule rule,
        string normProc,
        string? windowTitle,
        out int scopeScore)
    {
        scopeScore = 0;

        if (!string.IsNullOrWhiteSpace(rule.WindowTitleFilter))
        {
            if (!RuleMatcher.MatchesWindowTitle(windowTitle, rule.WindowTitleFilter))
            {
                return false;
            }
            scopeScore += 4;
        }

        if (!rule.IsGlobal)
        {
            if (string.IsNullOrEmpty(normProc) || !RuleMatcher.MatchesFilter(normProc, rule.ApplicationFilter))
            {
                return false;
            }
            scopeScore += 8;
        }

        return true;
    }
}
