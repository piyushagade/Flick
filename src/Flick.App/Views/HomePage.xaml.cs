using Flick.Core.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace Flick.App.Views;

public sealed partial class HomePage : Page
{
    private bool _suppressToggle;

    private static readonly SolidColorBrush ActiveTintBrush = new(Color.FromArgb(36, 56, 189, 248));
    private static readonly SolidColorBrush ActiveIconBrush = new(Color.FromArgb(255, 96, 205, 255));
    private static readonly SolidColorBrush MutedBadgeBrush = new(Color.FromArgb(28, 255, 255, 255));
    private static readonly SolidColorBrush MutedIconBrush = new(Color.FromArgb(140, 255, 255, 255));

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
            ? "Listening for enabled triggers."
            : "All triggers are paused.";

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

        bool tpOn = active && s.EnableTouchpadGestures;
        bool chordOn = active && s.EnableKeyboardChords;
        bool knockOn = active && s.EnableKnockDetection;

        TouchpadBadge.Background = tpOn ? ActiveTintBrush : MutedBadgeBrush;
        TouchpadIcon.Foreground = tpOn ? ActiveIconBrush : MutedIconBrush;
        ChordBadge.Background = chordOn ? ActiveTintBrush : MutedBadgeBrush;
        ChordIcon.Foreground = chordOn ? ActiveIconBrush : MutedIconBrush;
        KnockBadge.Background = knockOn ? ActiveTintBrush : MutedBadgeBrush;
        KnockIcon.Foreground = knockOn ? ActiveIconBrush : MutedIconBrush;
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
