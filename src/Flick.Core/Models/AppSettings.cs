namespace Flick.Core.Models;

/// <summary>
/// User-configurable application and recognition settings.
/// </summary>
public sealed class AppSettings
{
    public bool IsEnabled { get; set; } = true;
    public bool EnableKeyboardChords { get; set; } = true;
    public int ChordSimultaneousWindowMs { get; set; } = 50;
    public int ChordSequenceTimeoutMs { get; set; } = 1000;
    public bool EnableTouchpadGestures { get; set; } = true;
    public double TouchpadMinSwipeDistance { get; set; } = 0.08;
    public TriggerButton TriggerButton { get; set; } = TriggerButton.Right;
    public double MinimumStrokeLength { get; set; } = 28.0;
    public double RecognitionThreshold { get; set; } = 0.65;
    public bool ShowTrailOverlay { get; set; } = false;
    public string TrailColorHex { get; set; } = "#0078D4";
    public double TrailThickness { get; set; } = 4.5;
    public bool ShowRippleEffect { get; set; } = true;
    public double RippleRadius { get; set; } = 90.0;
    public double RippleThickness { get; set; } = 46.0;
    public double RippleSpeed { get; set; } = 0.8;
    public bool MinimizeToTray { get; set; } = true;
    public bool StartWithWindows { get; set; } = false;
    public bool AutoPauseOnFullscreen { get; set; } = true;
    public bool ShowActionHudPill { get; set; } = true;
    public bool EnableHotCorners { get; set; } = false;
    public Dictionary<string, string> HotCornerActions { get; set; } = [];
    public Dictionary<string, string> ScopeTrailColors { get; set; } = [];
    public string ThemePreference { get; set; } = "System";
    public List<string> IgnoredProcesses { get; set; } = ["mstsc", "vncviewer"];
}

/// <summary>
/// Root persisted profile containing gestures, rules, and settings.
/// </summary>
public sealed class FlickProfile
{
    public int Version { get; set; } = 1;
    public List<AppScope> Scopes { get; set; } = [];
    public List<GestureDefinition> Gestures { get; set; } = [];
    public List<GestureRule> Rules { get; set; } = [];
    public AppSettings Settings { get; set; } = new();
}
