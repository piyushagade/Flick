using System.Collections.Specialized;
using Flick.Core.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Flick.App.Views;

public sealed partial class HomePage : Page
{
    private bool _suppressToggle;

    public HomePage()
    {
        InitializeComponent();
        Loaded += HomePage_Loaded;
        Unloaded += HomePage_Unloaded;
    }

    private void HomePage_Loaded(object sender, RoutedEventArgs e)
    {
        RefreshStats();
        RefreshSessionStats();
        App.Engine.ProfileChanged += RefreshStats;
        App.Engine.RecentActivity.CollectionChanged += RecentActivity_CollectionChanged;
    }

    private void HomePage_Unloaded(object sender, RoutedEventArgs e)
    {
        App.Engine.ProfileChanged -= RefreshStats;
        App.Engine.RecentActivity.CollectionChanged -= RecentActivity_CollectionChanged;
    }

    private void RecentActivity_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        RefreshSessionStats();
    }

    private void RefreshStats()
    {
        var profile = App.Engine.Profile;
        var s = profile.Settings;

        _suppressToggle = true;
        EngineToggle.IsOn = s.IsEnabled;
        _suppressToggle = false;

        StatusTitleText.Text = s.IsEnabled ? "Flick Engine Active" : "Flick Engine Paused";
        StatusPillText.Text = s.IsEnabled ? "LIVE" : "PAUSED";

        if (s.IsEnabled)
        {
            var activeTriggers = new List<string> { "Mouse" };
            if (s.EnableTouchpadGestures) activeTriggers.Add("Touchpad");
            if (s.EnableKeyboardChords) activeTriggers.Add("Keyboard Chords");
            if (s.EnableKnockDetection) activeTriggers.Add("Acoustic Knock");
            StatusSubtitleText.Text = $"Listening: {string.Join(" • ", activeTriggers)}";
        }
        else
        {
            StatusSubtitleText.Text = "Toggle the switch to resume global trigger recognition.";
        }

        int enabledRules = profile.Rules.Count(r => r.IsEnabled);
        int scopeCount = profile.Scopes.Count;
        ActiveRulesCountText.Text = enabledRules.ToString();
        TotalRulesSubText.Text = $"{profile.Rules.Count} total • Global{(scopeCount > 0 ? $" + {scopeCount} app scope(s)" : "")}";

        int gestureRules = profile.Rules.Count(r => r.IsEnabled && r.TriggerType == TriggerType.Gesture && r.InputSource != GestureInputSource.Touchpad);
        int touchpadRules = profile.Rules.Count(r => r.IsEnabled && r.TriggerType == TriggerType.Gesture && r.InputSource == GestureInputSource.Touchpad);
        int chordRules = profile.Rules.Count(r => r.IsEnabled && r.TriggerType == TriggerType.KeyboardChord);
        int knockRules = profile.Rules.Count(r => r.IsEnabled && r.TriggerType == TriggerType.KnockPattern);

        TriggerButtonText.Text = $"{s.TriggerButton} Button";
        TriggerSubText.Text = $"{s.MinimumStrokeLength:0} px min drag • {gestureRules} rule(s)";

        TouchpadStatusText.Text = s.EnableTouchpadGestures ? "2–5 Fingers" : "Disabled";
        TouchpadSubText.Text = App.Engine.IsTouchpadDetected
            ? $"Precision Touchpad ready • {touchpadRules} rule(s)"
            : $"Waiting for touchpad • {touchpadRules} rule(s)";

        ChordStatusText.Text = s.EnableKeyboardChords ? "Active" : "Disabled";
        ChordSubText.Text = $"Simultaneous & Sequential • {chordRules} rule(s)";

        KnockStatusText.Text = s.EnableKnockDetection ? "Listening" : "Off";
        KnockSubText.Text = $"{s.KnockSurfaces.Count} surface(s) calibrated • {knockRules} rule(s)";
    }

    private void RefreshSessionStats()
    {
        var items = App.Engine.RecentActivity;
        SessionCountText.Text = items.Count.ToString();
        SessionSubText.Text = $"{items.Count(a => a.Executed)} actions executed this session";
    }

    private void EngineToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_suppressToggle) return;
        App.Engine.Profile.Settings.IsEnabled = EngineToggle.IsOn;
        App.Engine.SaveProfile();
    }
}
