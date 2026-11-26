namespace Flick.Core.Models;

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
