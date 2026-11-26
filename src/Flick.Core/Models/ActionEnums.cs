namespace Flick.Core.Models;

/// <summary>
/// Top-level trigger mechanism for a rule.
/// </summary>
public enum TriggerType
{
    Gesture = 0,
    KeyboardChord = 1,
    KnockPattern = 2
}

/// <summary>
/// Global default for which window receives triggered actions.
/// </summary>
public enum GlobalWindowTargetMode
{
    ActiveWindow = 0,
    WindowUnderCursorForMouse = 1,
    WindowUnderCursorAlways = 2
}

/// <summary>
/// Per-rule override for which window receives triggered actions.
/// </summary>
public enum WindowTargetMode
{
    Default = 0,
    ActiveWindow = 1,
    WindowUnderCursor = 2
}

/// <summary>
/// Supported action types executed when a gesture rule matches.
/// </summary>
public enum ActionType
{
    SendHotkey,
    MouseAction,
    WindowManagement,
    SystemAction,
    MediaControl,
    OpenUrl,
    WindowsIntent,
    HttpRequest,
    IftttWebhook,
    MqttPublish,
    RunProgram,
    TextSnippet,
    RunPowerShell
}

/// <summary>
/// Mouse button used to initiate a gesture stroke.
/// </summary>
public enum TriggerButton
{
    Right,
    Middle,
    XButton1,
    XButton2
}

/// <summary>
/// Input device source for a gesture rule.
/// </summary>
public enum GestureInputSource
{
    Any,
    Mouse,
    Touchpad
}

/// <summary>
/// Optional modifier keys required while performing a gesture.
/// </summary>
[Flags]
public enum ModifierKeyFilter
{
    None = 0,
    Ctrl = 1,
    Alt = 2,
    Shift = 4
}
