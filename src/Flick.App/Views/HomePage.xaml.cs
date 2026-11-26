using System.Diagnostics;
using Flick.App.Services;
using Flick.Core.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.System;
using Windows.UI;

namespace Flick.App.Views;

public sealed partial class HomePage : Page
{
    private bool _suppressToggle;
    private IReadOnlyList<TouchpadConflict> _detectedConflicts = [];

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
        EventQuickToggle.IsOn = s.EnableSystemEvents;
        _suppressToggle = false;

        StatusTitleText.Text = s.IsEnabled ? "Flick Engine Active" : "Flick Engine Paused";
        StatusSubtitleText.Text = s.IsEnabled
            ? "Listening for enabled triggers."
            : "All triggers are paused.";

        int tpRules = profile.Rules.Count(r => r.IsEnabled && r.TriggerType == TriggerType.Gesture && r.InputSource == GestureInputSource.Touchpad);
        int chordRules = profile.Rules.Count(r => r.IsEnabled && r.TriggerType == TriggerType.KeyboardChord);
        int knockRules = profile.Rules.Count(r => r.IsEnabled && r.TriggerType == TriggerType.KnockPattern);
        int eventRules = profile.Rules.Count(r => r.IsEnabled && r.TriggerType == TriggerType.SystemEvent);

        TouchpadSubText.Text = App.Engine.IsTouchpadDetected
            ? $"Precision Touchpad ready • {tpRules} active rule(s)"
            : $"2–5 finger gestures • {tpRules} active rule(s)";
        ChordSubText.Text = $"Simultaneous & sequential • {chordRules} active rule(s)";
        KnockSubText.Text = $"{s.KnockSurfaces.Count} calibrated surface(s) • {knockRules} active rule(s)";
        EventSubText.Text = $"App, lock, display, login & shell • {eventRules} active rule(s)";

        bool active = s.IsEnabled;
        TriggerEnginesCard.Opacity = active ? 1.0 : 0.45;
        TouchpadQuickToggle.IsEnabled = active;
        ChordQuickToggle.IsEnabled = active;
        KnockQuickToggle.IsEnabled = active;
        EventQuickToggle.IsEnabled = active;

        bool tpOn = active && s.EnableTouchpadGestures;
        bool chordOn = active && s.EnableKeyboardChords;
        bool knockOn = active && s.EnableKnockDetection;
        bool eventOn = active && s.EnableSystemEvents;

        TouchpadBadge.Background = tpOn ? ActiveTintBrush : MutedBadgeBrush;
        TouchpadIcon.Foreground = tpOn ? ActiveIconBrush : MutedIconBrush;
        ChordBadge.Background = chordOn ? ActiveTintBrush : MutedBadgeBrush;
        ChordIcon.Foreground = chordOn ? ActiveIconBrush : MutedIconBrush;
        KnockBadge.Background = knockOn ? ActiveTintBrush : MutedBadgeBrush;
        KnockIcon.Foreground = knockOn ? ActiveIconBrush : MutedIconBrush;
        EventBadge.Background = eventOn ? ActiveTintBrush : MutedBadgeBrush;
        EventIcon.Foreground = eventOn ? ActiveIconBrush : MutedIconBrush;

        RefreshConflicts(profile);
        RefreshMicWarning(profile);
    }

    private void RefreshMicWarning(FlickProfile profile)
    {
        if (!profile.Settings.IsEnabled || !profile.Settings.EnableKnockDetection)
        {
            MicWarningInfoBar.IsOpen = false;
            MicWarningInfoBar.Visibility = Visibility.Collapsed;
            return;
        }

        var status = DevicePrivacyService.GetMicrophoneStatus();
        if (!status.HasCaptureDevice)
        {
            MicWarningInfoBar.Severity = InfoBarSeverity.Warning;
            MicWarningInfoBar.Title = "No Microphone Detected";
            MicWarningInfoBar.Message = "Acoustic Knock Detection is enabled, but no capture microphone was found.";
            UnmuteMicButton.Visibility = Visibility.Collapsed;
            OpenMicPrivacyButton.Visibility = Visibility.Collapsed;
            MicWarningInfoBar.Visibility = Visibility.Visible;
            MicWarningInfoBar.IsOpen = true;
        }
        else if (status.IsPrivacyDenied)
        {
            MicWarningInfoBar.Severity = InfoBarSeverity.Warning;
            MicWarningInfoBar.Title = "Microphone Access Disabled";
            MicWarningInfoBar.Message = "Windows privacy settings are blocking microphone access, so Knock Detection cannot listen.";
            UnmuteMicButton.Visibility = Visibility.Collapsed;
            OpenMicPrivacyButton.Visibility = Visibility.Visible;
            MicWarningInfoBar.Visibility = Visibility.Visible;
            MicWarningInfoBar.IsOpen = true;
        }
        else if (status.IsMuted)
        {
            MicWarningInfoBar.Severity = InfoBarSeverity.Warning;
            MicWarningInfoBar.Title = "Microphone Is Muted";
            MicWarningInfoBar.Message = "Your microphone is muted, so Acoustic Knock Detection cannot hear knocks.";
            UnmuteMicButton.Visibility = Visibility.Visible;
            OpenMicPrivacyButton.Visibility = Visibility.Collapsed;
            MicWarningInfoBar.Visibility = Visibility.Visible;
            MicWarningInfoBar.IsOpen = true;
        }
        else
        {
            MicWarningInfoBar.IsOpen = false;
            MicWarningInfoBar.Visibility = Visibility.Collapsed;
        }
    }

    private void UnmuteMic_Click(object sender, RoutedEventArgs e)
    {
        _ = DevicePrivacyService.ToggleMicrophoneMute();
        RefreshMicWarning(App.Engine.Profile);
    }

    private async void OpenMicPrivacy_Click(object sender, RoutedEventArgs e)
    {
        _ = await Launcher.LaunchUriAsync(new Uri("ms-settings:privacy-microphone"));
    }

    private void RefreshConflicts(FlickProfile profile)
    {
        _detectedConflicts = TouchpadConflictService.DetectConflicts(profile);
        if (_detectedConflicts.Count > 0)
        {
            string items = string.Join(", ", _detectedConflicts.Select(c =>
                $"{c.FriendlyName} ({c.ValueName}={c.DisplayValue})"));
            ConflictInfoBar.Severity = InfoBarSeverity.Warning;
            ConflictInfoBar.Title = "Windows Touchpad Conflict";
            ConflictInfoBar.Message = $"{items} overlaps with your Flick touchpad rule(s). Disable in Registry or set to Nothing in Touchpad Settings.";
            FixConflictsButton.Visibility = Visibility.Visible;
            SignOutButton.Visibility = Visibility.Collapsed;
            ConflictInfoBar.Visibility = Visibility.Visible;
            ConflictInfoBar.IsOpen = true;
        }
        else
        {
            ConflictInfoBar.IsOpen = false;
            ConflictInfoBar.Visibility = Visibility.Collapsed;
        }
    }

    private async void FixConflicts_Click(object sender, RoutedEventArgs e)
    {
        if (_detectedConflicts.Count == 0) return;

        string listText = string.Join("\n", _detectedConflicts.Select(c =>
            $"• {c.FriendlyName} ({c.ValueName}: {c.DisplayValue} → 0)"));

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "Disable Conflicting Touchpad Entries?",
            Content = new TextBlock
            {
                Text = $"Set these HKCU\\{TouchpadConflictService.PrecisionTouchpadKeyPath} values to 0 (Nothing):\n\n{listText}",
                TextWrapping = TextWrapping.Wrap
            },
            PrimaryButtonText = "Disable Entries",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary
        };

        var result = await dialog.ShowAsync();
        if (result != ContentDialogResult.Primary) return;

        bool needsSignOut = _detectedConflicts.Any(c =>
            c.ValueName.StartsWith("ThreeFinger", StringComparison.Ordinal) ||
            c.ValueName.StartsWith("FourFinger", StringComparison.Ordinal));

        if (TouchpadConflictService.DisableConflicts(_detectedConflicts))
        {
            _detectedConflicts = [];
            FixConflictsButton.Visibility = Visibility.Collapsed;
            if (needsSignOut)
            {
                ConflictInfoBar.Severity = InfoBarSeverity.Warning;
                ConflictInfoBar.Title = "Sign Out Required to Apply 3F/4F Changes";
                ConflictInfoBar.Message = "Registry set to 0 (Nothing), but Windows caches 3/4-finger gestures until you sign out and back in (or set to Nothing in Touchpad Settings for instant effect).";
                SignOutButton.Visibility = Visibility.Visible;
            }
            else
            {
                ConflictInfoBar.Severity = InfoBarSeverity.Success;
                ConflictInfoBar.Title = "Touchpad Conflicts Disabled";
                ConflictInfoBar.Message = "Registry entries set to 0 (Nothing) and applied to Windows Touchpad.";
                SignOutButton.Visibility = Visibility.Collapsed;
            }
            ConflictInfoBar.Visibility = Visibility.Visible;
            ConflictInfoBar.IsOpen = true;
        }
        else
        {
            ConflictInfoBar.Severity = InfoBarSeverity.Error;
            ConflictInfoBar.Title = "Could Not Update Registry";
            ConflictInfoBar.Message = "Open Touchpad Settings and set the conflicting gestures to Nothing manually.";
            ConflictInfoBar.Visibility = Visibility.Visible;
            ConflictInfoBar.IsOpen = true;
        }
    }

    private async void SignOutNow_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "Sign Out of Windows Now?",
            Content = new TextBlock
            {
                Text = "Save any open work before signing out. Windows will apply the updated 3/4-finger touchpad settings when you sign back in.",
                TextWrapping = TextWrapping.Wrap
            },
            PrimaryButtonText = "Sign Out",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close
        };

        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            Process.Start(new ProcessStartInfo("shutdown", "/l") { CreateNoWindow = true, UseShellExecute = false });
        }
    }

    private async void OpenTouchpadSettings_Click(object sender, RoutedEventArgs e)
    {
        _ = await Launcher.LaunchUriAsync(new Uri("ms-settings:devices-touchpad"));
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
        s.EnableSystemEvents = EventQuickToggle.IsOn;
        App.Engine.SaveProfile();
    }
}
