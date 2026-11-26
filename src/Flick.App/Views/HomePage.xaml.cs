using Flick.Core.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace Flick.App.Views;

public sealed partial class HomePage : Page
{
    private bool _suppressToggle;

    private static readonly SolidColorBrush EmeraldBrush = new(Color.FromArgb(255, 16, 185, 129));
    private static readonly SolidColorBrush VioletBrush = new(Color.FromArgb(255, 139, 92, 246));
    private static readonly SolidColorBrush AmberBrush = new(Color.FromArgb(255, 245, 158, 11));
    private static readonly SolidColorBrush MutedBadgeBrush = new(Color.FromArgb(255, 82, 82, 91));

    public HomePage()
    {
        InitializeComponent();
        Loaded += HomePage_Loaded;
        Unloaded += HomePage_Unloaded;
    }

    private void HomePage_Loaded(object sender, RoutedEventArgs e)
    {
        RefreshDashboard();
        App.Engine.ProfileChanged += RefreshDashboard;
    }

    private void HomePage_Unloaded(object sender, RoutedEventArgs e)
    {
        App.Engine.ProfileChanged -= RefreshDashboard;
    }

    private void RefreshDashboard()
    {
        var profile = App.Engine.Profile;
        var s = profile.Settings;

        _suppressToggle = true;
        EngineToggle.IsOn = s.IsEnabled;
        TouchpadQuickToggle.IsOn = s.EnableTouchpadGestures;
        ChordQuickToggle.IsOn = s.EnableKeyboardChords;
        KnockQuickToggle.IsOn = s.EnableKnockDetection;
        _suppressToggle = false;

        StatusTitleText.Text = s.IsEnabled ? "Flick Engine Active" : "Flick Engine Paused";
        StatusPillText.Text = s.IsEnabled ? "LIVE" : "PAUSED";
        StatusSubtitleText.Text = s.IsEnabled
            ? $"Mouse ({s.TriggerButton} drag, {s.MinimumStrokeLength:0} px) and enabled trigger engines are listening."
            : "Toggle the switch to resume global trigger recognition.";

        int tpRules = profile.Rules.Count(r => r.IsEnabled && r.TriggerType == TriggerType.Gesture && r.InputSource == GestureInputSource.Touchpad);
        int chordRules = profile.Rules.Count(r => r.IsEnabled && r.TriggerType == TriggerType.KeyboardChord);
        int knockRules = profile.Rules.Count(r => r.IsEnabled && r.TriggerType == TriggerType.KnockPattern);

        TouchpadSubText.Text = App.Engine.IsTouchpadDetected
            ? $"Precision Touchpad ready • {tpRules} active rule(s)"
            : $"2–5 finger gestures • {tpRules} active rule(s)";
        ChordSubText.Text = $"Simultaneous & sequential • {chordRules} active rule(s)";
        KnockSubText.Text = $"{s.KnockSurfaces.Count} calibrated surface(s) • {knockRules} active rule(s)";

        bool active = s.IsEnabled;
        TriggerEnginesCard.Opacity = active ? 1.0 : 0.45;
        TouchpadQuickToggle.IsEnabled = active;
        ChordQuickToggle.IsEnabled = active;
        KnockQuickToggle.IsEnabled = active;

        TouchpadBadge.Background = (active && s.EnableTouchpadGestures) ? EmeraldBrush : MutedBadgeBrush;
        ChordBadge.Background = (active && s.EnableKeyboardChords) ? VioletBrush : MutedBadgeBrush;
        KnockBadge.Background = (active && s.EnableKnockDetection) ? AmberBrush : MutedBadgeBrush;
    }

    private void EngineToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_suppressToggle) return;
        App.Engine.Profile.Settings.IsEnabled = EngineToggle.IsOn;
        App.Engine.SaveProfile();
    }

    private void TriggerQuickToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_suppressToggle) return;
        var s = App.Engine.Profile.Settings;
        s.EnableTouchpadGestures = TouchpadQuickToggle.IsOn;
        s.EnableKeyboardChords = ChordQuickToggle.IsOn;
        s.EnableKnockDetection = KnockQuickToggle.IsOn;
        App.Engine.SaveProfile();
    }
}
