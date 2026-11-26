namespace Flick.Core.Models;

/// <summary>
/// How keys in a keyboard chord are entered.
/// </summary>
public enum KeyboardChordMode
{
    /// <summary>
    /// Keys pressed together within a short simultaneity window (e.g. J+K, S+D+F).
    /// </summary>
    Simultaneous = 0,

    /// <summary>
    /// Leader/prefix key(s) followed by subsequent key(s) within a timeout (e.g. Ctrl+K, B).
    /// </summary>
    Sequential = 1
}

/// <summary>
/// Modifier keys for a keyboard chord step (includes Win key unlike gesture ModifierKeyFilter).
/// </summary>
[Flags]
public enum ChordModifiers
{
    None = 0,
    Ctrl = 1,
    Alt = 2,
    Shift = 4,
    Win = 8
}

/// <summary>
/// One step in a keyboard chord (a set of simultaneously pressed keys plus optional modifiers).
/// </summary>
public sealed class ChordStep
{
    public ChordModifiers Modifiers { get; set; } = ChordModifiers.None;

    /// <summary>
    /// Virtual-key codes (VK_*) required for this step.
    /// </summary>
    public List<int> KeyCodes { get; set; } = [];
}

/// <summary>
/// Definition of a simultaneous or sequential keyboard chord trigger.
/// </summary>
public sealed class KeyboardChordDefinition
{
    public KeyboardChordMode Mode { get; set; } = KeyboardChordMode.Simultaneous;

    /// <summary>
    /// Ordered steps. Simultaneous chords use 1 step; sequential chords use 2+ steps.
    /// </summary>
    public List<ChordStep> Steps { get; set; } = [];

    /// <summary>
    /// Optional per-rule override for simultaneous window or sequence timeout in ms (0 = use global setting).
    /// </summary>
    public int TimeoutMs { get; set; } = 0;

    /// <summary>
    /// When true, matched chord keystrokes are swallowed so they do not type into the active app.
    /// </summary>
    public bool SuppressKeys { get; set; } = true;
}
