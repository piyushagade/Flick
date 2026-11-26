using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Flick.App.Views;

public sealed record ReleaseNoteItem(
    string Version,
    string Title,
    Visibility LatestVisibility,
    IReadOnlyList<string> Notes);

public sealed partial class WhatsNewPage : Page
{
    public WhatsNewPage()
    {
        InitializeComponent();

        ReleasesList.ItemsSource = new ReleaseNoteItem[]
        {
            new(
                "v1.4.0",
                "System Event Triggers, Device Privacy & Touchpad Conflict Guard",
                Visibility.Visible,
                [
                    "Added System Event triggers (App Open/Close/Minimize/Focus, Session Lock/Unlock, Display On/Off, Start Menu, System Tray, and On Login) with optional process/title matching.",
                    "Added Device Privacy controls for global microphone mute/unmute and camera allow/deny toggling, plus quick-status toggles on the Dashboard.",
                    "Added Precision Touchpad Conflict detection in Settings and Home to identify and resolve Windows touchpad gestures that collide with active Flick rules."
                ]),
            new(
                "v1.3.0",
                "Sneak Window Portal & Modifier-Only Sequences",
                Visibility.Collapsed,
                [
                    "Added Sneak: a feathered, cursor-following circular portal to peek through the active window at windows underneath without Alt-Tabbing.",
                    "Supports both Single Window Lock (cycle with Middle-Click, Tab, or Left/Right Arrows) and All Behind Windows composite layout, plus live scroll-wheel resizing.",
                    "Added modifier-only sequential keyboard chords (such as Ctrl → Alt → Alt) with smooth async background capture and dirty-rect rendering."
                ]),
            new(
                "v1.2.0",
                "Acoustic Knock Detection & Surface Calibration",
                Visibility.Collapsed,
                [
                    "Added acoustic knock pattern detection using real-time FFT spectral bands, temporal decay ratios, and adaptive noise-floor gating.",
                    "Added Knock Surface Calibration (5× sample training and live detection tester) in Settings to distinguish Touchpad, Laptop Lid, and Desk knocks.",
                    "Added a live visual knock timeline in the rule editor showing each knock's peak amplitude and millisecond cadence."
                ]),
            new(
                "v1.1.0",
                "Keyboard Chords, Window Targeting & Dedicated Corners",
                Visibility.Collapsed,
                [
                    "Added simultaneous and sequential modifier-led keyboard chords via global low-level keyboard hook (WH_KEYBOARD_LL) with keystroke suppression.",
                    "Added global and per-rule Window Action Target options to act on either the Active Window or the Window Under Cursor.",
                    "Moved Screen Hot-Corners to its own dedicated Corners page and redesigned the Dashboard with vivid trigger engine controls."
                ]),
            new(
                "v1.0.0",
                "Mouse & Precision Touchpad Gestures",
                Visibility.Collapsed,
                [
                    "Initial release with global mouse stroke recognition and 2–5 finger Precision Touchpad swipes, taps, and pinches.",
                    "Includes glowing gesture trail overlays, liquid glass ripple feedback, app-scoped rules, and multi-step macro chains."
                ])
        };
    }
}
