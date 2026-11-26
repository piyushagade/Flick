using Flick.Core.Keyboard;
using Flick.Core.Knock;
using Flick.Core.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Flick.App.Views;

public sealed record DashboardRuleItem(
    string Id,
    string Glyph,
    string Name,
    string ScopeLabel,
    string Summary,
    bool IsEnabled);

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

        var gestureMap = profile.Gestures.ToDictionary(g => g.Id, g => g);
        var items = new List<DashboardRuleItem>();
        foreach (var rule in profile.Rules)
        {
            string glyph;
            string triggerLabel;
            if (rule.TriggerType == TriggerType.KeyboardChord)
            {
                glyph = "\uE765";
                triggerLabel = ChordKeyHelper.FormatChord(rule.KeyboardChord);
            }
            else if (rule.TriggerType == TriggerType.KnockPattern)
            {
                glyph = "\uE720";
                triggerLabel = KnockPatternMatcher.FormatPattern(rule.KnockPattern);
            }
            else
            {
                glyph = rule.InputSource == GestureInputSource.Touchpad ? "\uE815" : "\uE962";
                triggerLabel = gestureMap.TryGetValue(rule.GestureId, out var g) ? g.Name : "Gesture";
            }

            string scope = rule.IsGlobal ? "Global" : rule.ApplicationFilter;
            items.Add(new DashboardRuleItem(
                rule.Id,
                glyph,
                string.IsNullOrWhiteSpace(rule.Name) ? "Untitled" : rule.Name,
                scope,
                $"{triggerLabel}  →  {rule.ActionType}: {rule.ActionPayload}",
                rule.IsEnabled));
        }

        int enabledCount = profile.Rules.Count(r => r.IsEnabled);
        RulesSummaryText.Text = $"{enabledCount} of {profile.Rules.Count} enabled";
        EmptyRulesText.Visibility = items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        _suppressToggle = true;
        ActiveRulesList.ItemsSource = items;
        _suppressToggle = false;

        bool active = s.IsEnabled;
        TriggerEnginesCard.Opacity = active ? 1.0 : 0.45;
        ActiveRulesCard.Opacity = active ? 1.0 : 0.45;
        TouchpadQuickToggle.IsEnabled = active;
        ChordQuickToggle.IsEnabled = active;
        KnockQuickToggle.IsEnabled = active;
        ActiveRulesList.IsHitTestVisible = active;
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

    private void RuleQuickToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_suppressToggle || sender is not ToggleSwitch ts || ts.Tag is not string id) return;
        var rule = App.Engine.Profile.Rules.FirstOrDefault(r => r.Id == id);
        if (rule is null) return;
        rule.IsEnabled = ts.IsOn;
        App.Engine.SaveProfile();
    }
}
