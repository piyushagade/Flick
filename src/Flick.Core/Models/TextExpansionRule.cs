namespace Flick.Core.Models;

/// <summary>
/// Controls when a typed abbreviation triggers expansion.
/// </summary>
public enum ExpansionTriggerMode
{
    /// <summary>Expands only when followed by a delimiter (Space, Tab, Enter, or punctuation).</summary>
    OnDelimiter = 0,

    /// <summary>Expands immediately as soon as the abbreviation's final character is typed.</summary>
    Immediate = 1
}

/// <summary>
/// Controls how typed casing affects abbreviation matching and output casing.
/// </summary>
public enum ExpansionCaseMode
{
    /// <summary>Abbreviation must match exact casing; output is emitted verbatim.</summary>
    Exact = 0,

    /// <summary>Abbreviation matches case-insensitively; output is emitted verbatim.</summary>
    CaseInsensitive = 1,

    /// <summary>
    /// Matches case-insensitively and adapts output casing:
    /// all-upper abbreviation -> ALL-UPPER output, Capitalized -> Capitalized first letter, lowercase -> verbatim.
    /// </summary>
    SmartCase = 2
}

/// <summary>
/// Controls how expanded text is injected into the target application.
/// </summary>
public enum ExpansionInjectionMode
{
    /// <summary>Uses Unicode SendInput for short single-line text and clipboard paste for long/multiline text.</summary>
    Auto = 0,

    /// <summary>Always types characters via SendInput (KEYEVENTF_UNICODE).</summary>
    SendInput = 1,

    /// <summary>Always pastes via clipboard (saving and restoring previous clipboard text).</summary>
    ClipboardPaste = 2
}

/// <summary>
/// A PhraseExpress-style text expansion rule (parallel to <see cref="GestureRule"/>).
/// </summary>
public sealed class TextExpansionRule
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = string.Empty;
    public string Folder { get; set; } = string.Empty;

    /// <summary>
    /// The typed trigger abbreviation (e.g. ";sig", "brb", "@@").
    /// </summary>
    public string Abbreviation { get; set; } = string.Empty;

    /// <summary>
    /// Replacement template supporting {cursor}, {date:...}, {time:...}, {clipboard}, {window.title},
    /// {window.process}, {env:...}, {random:...}, {key:...}, {delay:...}, {snippet:...}, {input:...}, {choice:...}.
    /// </summary>
    public string Template { get; set; } = string.Empty;

    /// <summary>
    /// Empty or "*" means Global. Otherwise comma-separated process names (e.g. "code, devenv").
    /// </summary>
    public string ApplicationFilter { get; set; } = "*";

    /// <summary>
    /// Optional substring or "regex:..." filter matched against the foreground window title.
    /// </summary>
    public string? WindowTitleFilter { get; set; }

    public ExpansionTriggerMode TriggerMode { get; set; } = ExpansionTriggerMode.OnDelimiter;
    public ExpansionCaseMode CaseMode { get; set; } = ExpansionCaseMode.SmartCase;
    public ExpansionInjectionMode InjectionMode { get; set; } = ExpansionInjectionMode.Auto;

    /// <summary>
    /// Require the character before the abbreviation to be whitespace/start-of-buffer (prevents firing mid-word).
    /// </summary>
    public bool RequireWordBoundary { get; set; } = true;

    /// <summary>
    /// When TriggerMode is OnDelimiter, keep the delimiter character (e.g. Space/Enter/punctuation) after the expanded text.
    /// </summary>
    public bool KeepDelimiter { get; set; } = true;

    public bool IsEnabled { get; set; } = true;

    public bool IsGlobal =>
        string.IsNullOrWhiteSpace(ApplicationFilter) || ApplicationFilter.Trim() == "*";
}
