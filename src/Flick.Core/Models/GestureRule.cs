namespace Flick.Core.Models;

/// <summary>
/// A single step in a chained multi-action macro.
/// </summary>
public sealed class ActionStep
{
    public ActionType ActionType { get; set; } = ActionType.SendHotkey;
    public string ActionPayload { get; set; } = string.Empty;
    public int DelayBeforeMs { get; set; } = 0;
}

/// <summary>
/// A follow-up gesture step required after the primary gesture to trigger a chained-gesture rule.
/// </summary>
public sealed class GestureSequenceStep
{
    public string GestureId { get; set; } = string.Empty;
    public GestureInputSource InputSource { get; set; } = GestureInputSource.Any;
    public int FingerCount { get; set; } = 0;
}

/// <summary>
/// Maps a recognized gesture (optionally scoped to specific applications) to an action or macro chain.
/// </summary>
public sealed class GestureRule
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = string.Empty;
    public TriggerType TriggerType { get; set; } = TriggerType.Gesture;
    public string GestureId { get; set; } = string.Empty;
    public KeyboardChordDefinition? KeyboardChord { get; set; }
    public KnockPatternDefinition? KnockPattern { get; set; }
    public SystemEventKind? SystemEvent { get; set; }

    /// <summary>
    /// Empty or "*" means Global. Otherwise comma-separated process names (e.g. "msedge, chrome").
    /// </summary>
    public string ApplicationFilter { get; set; } = "*";

    /// <summary>
    /// Optional substring or "regex:..." filter matched against the foreground window title.
    /// </summary>
    public string? WindowTitleFilter { get; set; }

    public WindowTargetMode WindowTarget { get; set; } = WindowTargetMode.Default;

    public ModifierKeyFilter RequiredModifiers { get; set; } = ModifierKeyFilter.None;

    /// <summary>
    /// Number of consecutive times this gesture must be performed before the action triggers (default 1).
    /// </summary>
    public int RequiredRepetitions { get; set; } = 1;

    /// <summary>
    /// Optional execution delay in milliseconds (0 = immediate). For overlapping chord prefixes, waits this long for additional keys before firing.
    /// </summary>
    public int ExecutionDelayMs { get; set; } = 0;

    /// <summary>
    /// Optional ordered follow-up gestures that must be performed after the primary gesture.
    /// </summary>
    public List<GestureSequenceStep>? FollowUpGestures { get; set; }

    public ActionType ActionType { get; set; } = ActionType.SendHotkey;
    public string ActionPayload { get; set; } = string.Empty;

    /// <summary>
    /// Optional multi-step macro chain. When non-empty, executes steps in sequence.
    /// </summary>
    public List<ActionStep>? Steps { get; set; }

    public GestureInputSource InputSource { get; set; } = GestureInputSource.Any;

    /// <summary>
    /// Required finger count for Touchpad rules (0 = any, 2..5 = specific multi-finger count).
    /// </summary>
    public int FingerCount { get; set; } = 0;

    /// <summary>
    /// Optional normalized [0..1] finger coordinates for accurate tap previews.
    /// </summary>
    public List<GesturePoint>? PreviewTapPoints { get; set; }

    public bool IsEnabled { get; set; } = true;

    public bool IsGlobal =>
        string.IsNullOrWhiteSpace(ApplicationFilter) || ApplicationFilter.Trim() == "*";
}
