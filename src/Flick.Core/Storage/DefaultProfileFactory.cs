using Flick.Core.Models;

namespace Flick.Core.Storage;

/// <summary>
/// Creates the default built-in gesture library and starter rules.
/// </summary>
public static class DefaultProfileFactory
{
    public static FlickProfile CreateDefault()
    {
        var gestures = CreateDefaultGestures();
        var rules = new List<GestureRule>
        {
            new()
            {
                Id = "rule-global-maximize",
                Name = "Maximize Window",
                GestureId = "builtin-up",
                ApplicationFilter = "*",
                ActionType = ActionType.WindowManagement,
                ActionPayload = "Maximize",
                IsEnabled = true
            },
            new()
            {
                Id = "rule-global-minimize",
                Name = "Minimize Window",
                GestureId = "builtin-down",
                ApplicationFilter = "*",
                ActionType = ActionType.WindowManagement,
                ActionPayload = "Minimize",
                IsEnabled = true
            },
            new()
            {
                Id = "rule-global-play-pause",
                Name = "Play / Pause Media",
                GestureId = "builtin-v",
                ApplicationFilter = "*",
                ActionType = ActionType.MediaControl,
                ActionPayload = "PlayPause",
                IsEnabled = true
            },
            new()
            {
                Id = "rule-global-volume-up",
                Name = "Volume Up",
                GestureId = "builtin-up-right",
                ApplicationFilter = "*",
                ActionType = ActionType.MediaControl,
                ActionPayload = "VolumeUp",
                IsEnabled = true
            },
            new()
            {
                Id = "rule-global-volume-down",
                Name = "Volume Down",
                GestureId = "builtin-down-left",
                ApplicationFilter = "*",
                ActionType = ActionType.MediaControl,
                ActionPayload = "VolumeDown",
                IsEnabled = true
            },
            new()
            {
                Id = "rule-tp-3f-desktop-left",
                Name = "3-Finger: Previous Desktop",
                GestureId = "builtin-left",
                ApplicationFilter = "*",
                InputSource = GestureInputSource.Touchpad,
                FingerCount = 3,
                ActionType = ActionType.WindowManagement,
                ActionPayload = "DesktopLeft",
                IsEnabled = true
            },
            new()
            {
                Id = "rule-tp-3f-desktop-right",
                Name = "3-Finger: Next Desktop",
                GestureId = "builtin-right",
                ApplicationFilter = "*",
                InputSource = GestureInputSource.Touchpad,
                FingerCount = 3,
                ActionType = ActionType.WindowManagement,
                ActionPayload = "DesktopRight",
                IsEnabled = true
            },
            new()
            {
                Id = "rule-tp-3f-task-view",
                Name = "3-Finger: Task View",
                GestureId = "builtin-up",
                ApplicationFilter = "*",
                InputSource = GestureInputSource.Touchpad,
                FingerCount = 3,
                ActionType = ActionType.WindowManagement,
                ActionPayload = "TaskView",
                IsEnabled = true
            },
            new()
            {
                Id = "rule-tp-3f-show-desktop",
                Name = "3-Finger: Show Desktop",
                GestureId = "builtin-down",
                ApplicationFilter = "*",
                InputSource = GestureInputSource.Touchpad,
                FingerCount = 3,
                ActionType = ActionType.WindowManagement,
                ActionPayload = "ShowDesktop",
                IsEnabled = true
            },
            new()
            {
                Id = "rule-tp-4f-tap-media",
                Name = "4-Finger Tap: Play / Pause",
                GestureId = "builtin-tap",
                ApplicationFilter = "*",
                InputSource = GestureInputSource.Touchpad,
                FingerCount = 4,
                ActionType = ActionType.MediaControl,
                ActionPayload = "PlayPause",
                IsEnabled = true
            },
            new()
            {
                Id = "rule-chord-sneak",
                Name = "Sneak (Peek Window Behind)",
                TriggerType = TriggerType.KeyboardChord,
                ApplicationFilter = "*",
                KeyboardChord = new KeyboardChordDefinition
                {
                    Mode = KeyboardChordMode.Sequential,
                    Steps =
                    [
                        new ChordStep { Modifiers = ChordModifiers.Ctrl, KeyCodes = [] },
                        new ChordStep { Modifiers = ChordModifiers.Alt, KeyCodes = [] },
                        new ChordStep { Modifiers = ChordModifiers.Alt, KeyCodes = [] }
                    ],
                    SuppressKeys = false
                },
                ActionType = ActionType.WindowManagement,
                ActionPayload = "Sneak",
                IsEnabled = true
            }
        };

        return new FlickProfile
        {
            Scopes = [],
            Gestures = gestures,
            Rules = rules,
            Settings = new AppSettings()
        };
    }

    public static List<GestureDefinition> CreateDefaultGestures()
    {
        var circlePoints = new List<GesturePoint>();
        for (int i = 0; i <= 32; i++)
        {
            double angle = (2 * Math.PI * i / 32) - Math.PI / 2;
            circlePoints.Add(new GesturePoint(Math.Cos(angle) * 50 + 50, Math.Sin(angle) * 50 + 50));
        }

        return
        [
            new GestureDefinition
            {
                Id = "builtin-left",
                Name = "Swipe Left",
                Description = "Horizontal stroke from right to left",
                IsBuiltIn = true,
                Points = [new(100, 0), new(50, 0), new(0, 0)]
            },
            new GestureDefinition
            {
                Id = "builtin-right",
                Name = "Swipe Right",
                Description = "Horizontal stroke from left to right",
                IsBuiltIn = true,
                Points = [new(0, 0), new(50, 0), new(100, 0)]
            },
            new GestureDefinition
            {
                Id = "builtin-up",
                Name = "Swipe Up",
                Description = "Vertical stroke upwards",
                IsBuiltIn = true,
                Points = [new(0, 100), new(0, 50), new(0, 0)]
            },
            new GestureDefinition
            {
                Id = "builtin-down",
                Name = "Swipe Down",
                Description = "Vertical stroke downwards",
                IsBuiltIn = true,
                Points = [new(0, 0), new(0, 50), new(0, 100)]
            },
            new GestureDefinition
            {
                Id = "builtin-down-right",
                Name = "Down then Right (L)",
                Description = "Vertical down then horizontal right",
                IsBuiltIn = true,
                Points = [new(0, 0), new(0, 100), new(100, 100)]
            },
            new GestureDefinition
            {
                Id = "builtin-up-right",
                Name = "Up then Right",
                Description = "Vertical up then horizontal right",
                IsBuiltIn = true,
                Points = [new(0, 100), new(0, 0), new(100, 0)]
            },
            new GestureDefinition
            {
                Id = "builtin-up-left",
                Name = "Up then Left",
                Description = "Vertical up then horizontal left",
                IsBuiltIn = true,
                Points = [new(100, 100), new(100, 0), new(0, 0)]
            },
            new GestureDefinition
            {
                Id = "builtin-down-left",
                Name = "Down then Left",
                Description = "Vertical down then horizontal left",
                IsBuiltIn = true,
                Points = [new(100, 0), new(100, 100), new(0, 100)]
            },
            new GestureDefinition
            {
                Id = "builtin-v",
                Name = "V-Shape",
                Description = "Diagonal down-right then up-right",
                IsBuiltIn = true,
                Points = [new(0, 0), new(50, 100), new(100, 0)]
            },
            new GestureDefinition
            {
                Id = "builtin-z",
                Name = "Z-Shape",
                Description = "Right, diagonal down-left, then right",
                IsBuiltIn = true,
                Points = [new(0, 0), new(100, 0), new(0, 100), new(100, 100)]
            },
            new GestureDefinition
            {
                Id = "builtin-circle",
                Name = "Circle",
                Description = "Clockwise loop",
                IsBuiltIn = true,
                Points = circlePoints
            },
            new GestureDefinition
            {
                Id = "builtin-tap",
                Name = "Multi-Finger Tap",
                Description = "Stationary multi-finger tap on touchpad",
                IsBuiltIn = true,
                Points = [new(50, 50), new(52, 50)]
            },
            new GestureDefinition
            {
                Id = "builtin-pinch-in",
                Name = "Pinch In",
                Description = "Two-finger pinch inward on touchpad",
                IsBuiltIn = true,
                Points = [new(0, 50), new(40, 50), new(100, 50), new(60, 50)],
                FingerStrokes =
                [
                    [new(0, 50), new(40, 50)],
                    [new(100, 50), new(60, 50)]
                ]
            },
            new GestureDefinition
            {
                Id = "builtin-pinch-out",
                Name = "Pinch Out",
                Description = "Two-finger spread outward on touchpad",
                IsBuiltIn = true,
                Points = [new(40, 50), new(0, 50), new(60, 50), new(100, 50)],
                FingerStrokes =
                [
                    [new(40, 50), new(0, 50)],
                    [new(60, 50), new(100, 50)]
                ]
            }
        ];
    }
}
