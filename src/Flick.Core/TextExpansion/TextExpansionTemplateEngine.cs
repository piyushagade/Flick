using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Flick.Core.Models;

namespace Flick.Core.TextExpansion;

public enum ExpansionStepKind
{
    Text = 0,
    Key = 1,
    Delay = 2,
    MoveCursorLeft = 3
}

public sealed record ExpansionStep(ExpansionStepKind Kind, string Value = "", int Count = 0);

public enum ExpansionPromptKind
{
    TextInput = 0,
    Choice = 1
}

public sealed record ExpansionPromptField(
    string TokenKey,
    ExpansionPromptKind Kind,
    string Label,
    string DefaultValue,
    IReadOnlyList<string> Choices);

public sealed record ExpansionContext(
    string ClipboardText,
    string WindowTitle,
    string ProcessName,
    IReadOnlyList<TextExpansionRule> AllRules,
    IReadOnlyDictionary<string, string>? PromptAnswers = null,
    DateTimeOffset? Now = null);

/// <summary>
/// Evaluates PhraseExpress-style templates with variables, smart-case, cursor positioning,
/// interactive prompt extraction, nested snippets, keystrokes, and delays.
/// </summary>
public static partial class TextExpansionTemplateEngine
{
    [GeneratedRegex(@"\{([^{}]+)\}", RegexOptions.Compiled)]
    private static partial Regex TokenRegex();

    /// <summary>
    /// Extracts any interactive {input:...} or {choice:...} fields from a template.
    /// </summary>
    public static IReadOnlyList<ExpansionPromptField> ExtractPromptFields(string template)
    {
        if (string.IsNullOrEmpty(template))
        {
            return [];
        }

        var fields = new List<ExpansionPromptField>();
        var seenKeys = new HashSet<string>(StringComparer.Ordinal);

        foreach (Match m in TokenRegex().Matches(template))
        {
            string raw = m.Groups[1].Value;
            if (!seenKeys.Add(raw))
            {
                continue;
            }

            if (raw.StartsWith("input:", StringComparison.OrdinalIgnoreCase))
            {
                string rest = raw["input:".Length..];
                int colonIdx = rest.IndexOf(':');
                string label = colonIdx >= 0 ? rest[..colonIdx].Trim() : rest.Trim();
                string defVal = colonIdx >= 0 ? rest[(colonIdx + 1)..] : string.Empty;
                fields.Add(new ExpansionPromptField(raw, ExpansionPromptKind.TextInput, label, defVal, []));
            }
            else if (raw.StartsWith("choice:", StringComparison.OrdinalIgnoreCase))
            {
                string rest = raw["choice:".Length..];
                int colonIdx = rest.IndexOf(':');
                string label;
                string optionsPart;
                if (colonIdx >= 0)
                {
                    label = rest[..colonIdx].Trim();
                    optionsPart = rest[(colonIdx + 1)..];
                }
                else
                {
                    label = "Select option";
                    optionsPart = rest;
                }

                var options = optionsPart.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                if (options.Length > 0)
                {
                    fields.Add(new ExpansionPromptField(raw, ExpansionPromptKind.Choice, label, options[0], options));
                }
            }
        }

        return fields;
    }

    /// <summary>
    /// Evaluates a rule's template into ordered execution steps (text, keys, delays, cursor-left).
    /// </summary>
    public static IReadOnlyList<ExpansionStep> Evaluate(
        TextExpansionRule rule,
        string typedAbbreviation,
        char? delimiterChar,
        ExpansionContext context)
    {
        string raw = ResolveTokensToString(
            rule.Template,
            context,
            depth: 0,
            out var inlineActions,
            out int? cursorMarkerIndex);

        // Apply SmartCase only to plain text when no explicit case-sensitive tokens override it.
        if (rule.CaseMode == ExpansionCaseMode.SmartCase && inlineActions.Count == 0)
        {
            raw = ApplySmartCase(raw, typedAbbreviation);
        }

        var steps = new List<ExpansionStep>();

        if (inlineActions.Count == 0)
        {
            string finalText = raw;
            if (rule.TriggerMode == ExpansionTriggerMode.OnDelimiter &&
                rule.KeepDelimiter &&
                delimiterChar.HasValue)
            {
                finalText += delimiterChar.Value;
            }

            if (!string.IsNullOrEmpty(finalText))
            {
                steps.Add(new ExpansionStep(ExpansionStepKind.Text, Value: finalText));
            }

            if (cursorMarkerIndex.HasValue)
            {
                int leftMoves = CountCursorLeftSteps(finalText, cursorMarkerIndex.Value);
                if (leftMoves > 0)
                {
                    steps.Add(new ExpansionStep(ExpansionStepKind.MoveCursorLeft, Count: leftMoves));
                }
            }

            return steps;
        }

        // Template contains {key:...} or {delay:...} steps: split around sentinel markers.
        // Note: {cursor} is applied relative to the final text segment if present.
        int cursorRemaining = -1;
        for (int i = 0; i < inlineActions.Count; i++)
        {
            var act = inlineActions[i];
            if (act.Kind == ExpansionStepKind.Text)
            {
                string txt = act.Value;
                if (rule.CaseMode == ExpansionCaseMode.SmartCase)
                {
                    txt = ApplySmartCase(txt, typedAbbreviation);
                }
                if (!string.IsNullOrEmpty(txt))
                {
                    steps.Add(new ExpansionStep(ExpansionStepKind.Text, Value: txt));
                }
            }
            else if (act.Kind == ExpansionStepKind.MoveCursorLeft)
            {
                cursorRemaining = i;
            }
            else
            {
                steps.Add(act);
            }
        }

        if (rule.TriggerMode == ExpansionTriggerMode.OnDelimiter &&
            rule.KeepDelimiter &&
            delimiterChar.HasValue)
        {
            steps.Add(new ExpansionStep(ExpansionStepKind.Text, Value: delimiterChar.Value.ToString()));
        }

        if (cursorRemaining >= 0)
        {
            int leftMoves = 0;
            bool onlyTextAfter = true;
            for (int i = cursorRemaining + 1; i < inlineActions.Count; i++)
            {
                if (inlineActions[i].Kind == ExpansionStepKind.Text)
                {
                    leftMoves += CountLogicalChars(inlineActions[i].Value);
                }
                else
                {
                    onlyTextAfter = false;
                }
            }

            if (rule.TriggerMode == ExpansionTriggerMode.OnDelimiter &&
                rule.KeepDelimiter &&
                delimiterChar.HasValue)
            {
                leftMoves += 1;
            }

            if (onlyTextAfter && leftMoves > 0)
            {
                steps.Add(new ExpansionStep(ExpansionStepKind.MoveCursorLeft, Count: leftMoves));
            }
        }

        return steps;
    }

    public static string ApplySmartCase(string output, string typedAbbreviation)
    {
        if (string.IsNullOrEmpty(output) || string.IsNullOrEmpty(typedAbbreviation))
        {
            return output;
        }

        bool hasLetter = false;
        bool allUpper = true;
        bool firstLetterSeen = false;
        bool firstUpper = false;
        bool restLower = true;

        foreach (char c in typedAbbreviation)
        {
            if (!char.IsLetter(c))
            {
                continue;
            }

            hasLetter = true;
            if (!char.IsUpper(c))
            {
                allUpper = false;
            }

            if (!firstLetterSeen)
            {
                firstLetterSeen = true;
                firstUpper = char.IsUpper(c);
            }
            else if (char.IsUpper(c))
            {
                restLower = false;
            }
        }

        if (!hasLetter)
        {
            return output;
        }

        if (allUpper)
        {
            return output.ToUpper(CultureInfo.CurrentCulture);
        }

        if (firstUpper && restLower)
        {
            var chars = output.ToCharArray();
            for (int i = 0; i < chars.Length; i++)
            {
                if (char.IsLetter(chars[i]))
                {
                    chars[i] = char.ToUpper(chars[i], CultureInfo.CurrentCulture);
                    break;
                }
            }
            return new string(chars);
        }

        return output;
    }

    private static string ResolveTokensToString(
        string template,
        ExpansionContext context,
        int depth,
        out List<ExpansionStep> segments,
        out int? cursorIndex)
    {
        segments = [];
        cursorIndex = null;

        if (string.IsNullOrEmpty(template))
        {
            return string.Empty;
        }

        var now = context.Now ?? DateTimeOffset.Now;
        var sb = new StringBuilder(template.Length);
        bool hasControlSteps = false;
        var rawSegments = new List<ExpansionStep>();

        int lastPos = 0;
        foreach (Match m in TokenRegex().Matches(template))
        {
            // Check for escaped "{{" before token
            if (m.Index > 0 && template[m.Index - 1] == '{')
            {
                continue;
            }

            if (m.Index > lastPos)
            {
                string literal = UnescapeBraces(template[lastPos..m.Index]);
                sb.Append(literal);
                rawSegments.Add(new ExpansionStep(ExpansionStepKind.Text, Value: literal));
            }

            string token = m.Groups[1].Value;
            if (TryHandleControlToken(token, out var controlStep))
            {
                if (controlStep.Kind == ExpansionStepKind.MoveCursorLeft)
                {
                    cursorIndex ??= sb.Length;
                    rawSegments.Add(controlStep);
                }
                else
                {
                    hasControlSteps = true;
                    rawSegments.Add(controlStep);
                }
            }
            else
            {
                string resolved = ResolveValueToken(token, context, now, depth);
                sb.Append(resolved);
                rawSegments.Add(new ExpansionStep(ExpansionStepKind.Text, Value: resolved));
            }

            lastPos = m.Index + m.Length;
        }

        if (lastPos < template.Length)
        {
            string tail = UnescapeBraces(template[lastPos..]);
            sb.Append(tail);
            rawSegments.Add(new ExpansionStep(ExpansionStepKind.Text, Value: tail));
        }

        if (hasControlSteps)
        {
            segments = MergeAdjacentTextSteps(rawSegments);
        }

        return sb.ToString();
    }

    private static List<ExpansionStep> MergeAdjacentTextSteps(List<ExpansionStep> raw)
    {
        var merged = new List<ExpansionStep>(raw.Count);
        var textAcc = new StringBuilder();

        void FlushText()
        {
            if (textAcc.Length > 0)
            {
                merged.Add(new ExpansionStep(ExpansionStepKind.Text, Value: textAcc.ToString()));
                textAcc.Clear();
            }
        }

        foreach (var step in raw)
        {
            if (step.Kind == ExpansionStepKind.Text)
            {
                textAcc.Append(step.Value);
            }
            else
            {
                FlushText();
                merged.Add(step);
            }
        }

        FlushText();
        return merged;
    }

    private static bool TryHandleControlToken(string token, out ExpansionStep step)
    {
        if (string.Equals(token, "cursor", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(token, "$|", StringComparison.Ordinal))
        {
            step = new ExpansionStep(ExpansionStepKind.MoveCursorLeft);
            return true;
        }

        if (token.StartsWith("key:", StringComparison.OrdinalIgnoreCase))
        {
            string key = token["key:".Length..].Trim();
            if (!string.IsNullOrEmpty(key))
            {
                step = new ExpansionStep(ExpansionStepKind.Key, Value: key);
                return true;
            }
        }

        if (token.StartsWith("delay:", StringComparison.OrdinalIgnoreCase))
        {
            if (int.TryParse(token["delay:".Length..].Trim(), out int ms))
            {
                step = new ExpansionStep(ExpansionStepKind.Delay, Count: Math.Clamp(ms, 0, 10_000));
                return true;
            }
        }

        step = default!;
        return false;
    }

    private static string ResolveValueToken(
        string token,
        ExpansionContext context,
        DateTimeOffset now,
        int depth)
    {
        if (context.PromptAnswers is not null &&
            context.PromptAnswers.TryGetValue(token, out string? answer))
        {
            return answer;
        }

        if (string.Equals(token, "date", StringComparison.OrdinalIgnoreCase))
        {
            return now.ToString("yyyy-MM-dd", CultureInfo.CurrentCulture);
        }

        if (token.StartsWith("date:", StringComparison.OrdinalIgnoreCase))
        {
            string fmt = token["date:".Length..];
            return SafeFormatDate(now, fmt, "yyyy-MM-dd");
        }

        if (string.Equals(token, "time", StringComparison.OrdinalIgnoreCase))
        {
            return now.ToString("HH:mm", CultureInfo.CurrentCulture);
        }

        if (token.StartsWith("time:", StringComparison.OrdinalIgnoreCase))
        {
            string fmt = token["time:".Length..];
            return SafeFormatDate(now, fmt, "HH:mm");
        }

        if (string.Equals(token, "datetime", StringComparison.OrdinalIgnoreCase))
        {
            return now.ToString("yyyy-MM-dd HH:mm", CultureInfo.CurrentCulture);
        }

        if (token.StartsWith("datetime:", StringComparison.OrdinalIgnoreCase))
        {
            string fmt = token["datetime:".Length..];
            return SafeFormatDate(now, fmt, "yyyy-MM-dd HH:mm");
        }

        if (string.Equals(token, "clipboard", StringComparison.OrdinalIgnoreCase))
        {
            return context.ClipboardText;
        }

        if (string.Equals(token, "clipboard:trim", StringComparison.OrdinalIgnoreCase))
        {
            return context.ClipboardText.Trim();
        }

        if (string.Equals(token, "window.title", StringComparison.OrdinalIgnoreCase))
        {
            return context.WindowTitle;
        }

        if (string.Equals(token, "window.process", StringComparison.OrdinalIgnoreCase))
        {
            return context.ProcessName;
        }

        if (token.StartsWith("env:", StringComparison.OrdinalIgnoreCase))
        {
            string varName = token["env:".Length..].Trim();
            return Environment.GetEnvironmentVariable(varName) ?? string.Empty;
        }

        if (token.StartsWith("random:", StringComparison.OrdinalIgnoreCase))
        {
            string spec = token["random:".Length..];
            if (spec.Contains('|'))
            {
                var parts = spec.Split('|');
                return parts[Random.Shared.Next(parts.Length)];
            }

            int dashIdx = spec.IndexOf('-');
            if (dashIdx > 0 &&
                int.TryParse(spec[..dashIdx], out int min) &&
                int.TryParse(spec[(dashIdx + 1)..], out int max) &&
                max >= min)
            {
                return Random.Shared.Next(min, max + 1).ToString(CultureInfo.InvariantCulture);
            }
        }

        if (token.StartsWith("snippet:", StringComparison.OrdinalIgnoreCase) && depth < 5)
        {
            string abbr = token["snippet:".Length..].Trim();
            var target = context.AllRules.FirstOrDefault(r =>
                r.IsEnabled && string.Equals(r.Abbreviation, abbr, StringComparison.OrdinalIgnoreCase));
            if (target is not null)
            {
                return ResolveTokensToString(target.Template, context, depth + 1, out _, out _);
            }
            return string.Empty;
        }

        if (token.StartsWith("input:", StringComparison.OrdinalIgnoreCase))
        {
            string rest = token["input:".Length..];
            int colonIdx = rest.IndexOf(':');
            return colonIdx >= 0 ? rest[(colonIdx + 1)..] : string.Empty;
        }

        if (token.StartsWith("choice:", StringComparison.OrdinalIgnoreCase))
        {
            string rest = token["choice:".Length..];
            int colonIdx = rest.IndexOf(':');
            string optionsPart = colonIdx >= 0 ? rest[(colonIdx + 1)..] : rest;
            var options = optionsPart.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            return options.Length > 0 ? options[0] : string.Empty;
        }

        // Unknown token: leave literal braces intact.
        return $"{{{token}}}";
    }

    private static string SafeFormatDate(DateTimeOffset now, string fmt, string fallback)
    {
        try
        {
            return string.IsNullOrWhiteSpace(fmt)
                ? now.ToString(fallback, CultureInfo.CurrentCulture)
                : now.ToString(fmt, CultureInfo.CurrentCulture);
        }
        catch (FormatException)
        {
            return now.ToString(fallback, CultureInfo.CurrentCulture);
        }
    }

    private static string UnescapeBraces(string s) =>
        s.Replace("{{", "{", StringComparison.Ordinal).Replace("}}", "}", StringComparison.Ordinal);

    private static int CountCursorLeftSteps(string fullText, int cursorIndex)
    {
        if (cursorIndex < 0 || cursorIndex >= fullText.Length)
        {
            return 0;
        }
        return CountLogicalChars(fullText[cursorIndex..]);
    }

    private static int CountLogicalChars(string tail)
    {
        int count = 0;
        for (int i = 0; i < tail.Length; i++)
        {
            if (tail[i] == '\r' && i + 1 < tail.Length && tail[i + 1] == '\n')
            {
                // Windows CRLF moves as 1 logical character on Left arrow.
                i++;
            }
            count++;
        }
        return count;
    }
}
