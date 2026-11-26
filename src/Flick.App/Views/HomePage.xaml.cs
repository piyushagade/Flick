using System.Collections.Specialized;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Flick.App.Views;

public sealed record ActivityRowViewModel(
    string TimeText,
    string GestureName,
    string RuleName,
    string ProcessName,
    string ConfidenceText);

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
        RefreshActivity();
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
        RefreshActivity();
    }

    private void RefreshStats()
    {
        var profile = App.Engine.Profile;
        var s = profile.Settings;

        _suppressToggle = true;
        EngineToggle.IsOn = s.IsEnabled;
        _suppressToggle = false;

        StatusTitleText.Text = s.IsEnabled
            ? "Gesture Engine Active"
            : "Gesture Engine Paused";

        StatusPillText.Text = s.IsEnabled ? "LIVE" : "PAUSED";

        StatusSubtitleText.Text = s.IsEnabled
            ? $"Mouse ({s.TriggerButton} drag) and {(s.EnableTouchpadGestures ? "Multi-Finger Touchpad (2–5 fingers)" : "Touchpad disabled")} are listening."
            : "Toggle the switch to resume global gesture recognition.";

        int enabledRules = profile.Rules.Count(r => r.IsEnabled);
        ActiveRulesCountText.Text = enabledRules.ToString();
        TotalRulesSubText.Text = $"{profile.Rules.Count} total configured";

        TouchpadStatusText.Text = s.EnableTouchpadGestures ? "2–5 Fingers" : "Disabled";
        TouchpadSubText.Text = App.Engine.IsTouchpadDetected
            ? "Precision Touchpad ready"
            : "Waiting for touchpad input";

        TriggerButtonText.Text = s.TriggerButton.ToString();
        TriggerSubText.Text = $"{s.MinimumStrokeLength:0} px min drag";
    }

    private void RefreshActivity()
    {
        var items = App.Engine.RecentActivity;
        SessionCountText.Text = items.Count.ToString();
        SessionSubText.Text = $"{items.Count(a => a.Executed)} actions executed";
        EmptyActivityText.Visibility = items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        ActivityList.ItemsSource = items
            .Select(a => new ActivityRowViewModel(
                a.Timestamp.ToString("HH:mm:ss"),
                a.GestureName,
                a.RuleName,
                a.ProcessName,
                $"{a.Confidence:P0}"))
            .ToList();
    }

    private void EngineToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_suppressToggle)
        {
            return;
        }

        App.Engine.Profile.Settings.IsEnabled = EngineToggle.IsOn;
        App.Engine.SaveProfile();
    }

    private void ClearActivity_Click(object sender, RoutedEventArgs e)
    {
        App.Engine.RecentActivity.Clear();
    }
}
