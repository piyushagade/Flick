using Flick.App.Services;
using Flick.Core.Knock;
using Flick.Core.Models;
using Flick.Core.Storage;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;

namespace Flick.App.Views;

public sealed record SurfaceRowViewModel(string Id, string Name, string SampleCountText);

public sealed partial class SettingsPage : Page
{
    private bool _loading;
    private bool _isCalibrating;
    private bool _isTestingSurface;
    private readonly List<float[]> _calSamples = [];

    public SettingsPage()
    {
        _loading = true;
        InitializeComponent();
        Loaded += (_, _) => LoadFromSettings();
        Unloaded += (_, _) => StopKnockCapture();
    }

    private void LoadFromSettings()
    {
        _loading = true;
        var s = App.Engine.Profile.Settings;

        SelectComboByTag(ThemeCombo, s.ThemePreference);
        StartupToggle.IsOn = s.StartWithWindows;
        MinimizeTrayToggle.IsOn = s.MinimizeToTray;
        FullscreenPauseToggle.IsOn = s.AutoPauseOnFullscreen;
        TextExpansionToggle.IsOn = s.EnableTextExpansion;

        SelectComboByTag(TriggerButtonCombo, s.TriggerButton.ToString());
        SelectComboByTag(WindowTargetCombo, s.WindowTarget.ToString());
        MinLengthSlider.Value = s.MinimumStrokeLength;
        MinLengthLabel.Text = $"{s.MinimumStrokeLength:0} px";

        ThresholdSlider.Value = s.RecognitionThreshold * 100.0;
        ThresholdLabel.Text = $"{s.RecognitionThreshold:P0}";

        TouchpadToggle.IsOn = s.EnableTouchpadGestures;
        TouchpadSwipeSlider.Value = s.TouchpadMinSwipeDistance * 100.0;
        TouchpadSwipeLabel.Text = $"{s.TouchpadMinSwipeDistance:P0}";

        KnockToggle.IsOn = s.EnableKnockDetection;
        KnockSensitivitySlider.Value = s.KnockSensitivity * 100.0;
        KnockSensitivityLabel.Text = $"{s.KnockSensitivity:P0}";

        KnockMicCombo.Items.Clear();
        KnockMicCombo.Items.Add(new ComboBoxItem { Content = "System Default Microphone", Tag = "" });
        foreach (string mic in KnockAudioService.GetAvailableMicrophones())
        {
            KnockMicCombo.Items.Add(new ComboBoxItem { Content = mic, Tag = mic });
        }
        SelectComboByTag(KnockMicCombo, s.KnockMicrophoneName ?? "");

        ShowTrailToggle.IsOn = s.ShowTrailOverlay;
        ShowHudPillToggle.IsOn = s.ShowActionHudPill;
        SelectComboByTag(TrailColorCombo, s.TrailColorHex);
        ThicknessSlider.Value = s.TrailThickness;
        ThicknessLabel.Text = $"{s.TrailThickness:0.0} px";

        ShowRippleToggle.IsOn = s.ShowRippleEffect;
        RippleRadiusSlider.Value = s.RippleRadius;
        RippleRadiusLabel.Text = $"{s.RippleRadius:0} px";
        RippleThicknessSlider.Value = s.RippleThickness;
        RippleThicknessLabel.Text = $"{s.RippleThickness:0} px";
        RippleSpeedSlider.Value = s.RippleSpeed;
        RippleSpeedLabel.Text = $"{s.RippleSpeed:0.0}×";

        IgnoredAppsBox.Text = string.Join(", ", s.IgnoredProcesses);
        RefreshSurfacesList();
        _loading = false;
    }

    private void RefreshSurfacesList()
    {
        var surfaces = App.Engine.Profile.Settings.KnockSurfaces;
        CalibratedSurfacesList.ItemsSource = surfaces
            .Select(s => new SurfaceRowViewModel(s.Id, s.Name, $"{s.SampleFeatures.Count} samples"))
            .ToList();
    }

    private void StopKnockCapture()
    {
        if (!_isCalibrating && !_isTestingSurface) return;
        _isCalibrating = false;
        _isTestingSurface = false;
        App.Engine.EndKnockRecording();
        CalibrateSurfaceBtn.Content = "Calibrate (5×)";
        TestSurfaceBtn.Content = "Test Detection";
    }

    private void CalibrateSurfaceBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_isCalibrating)
        {
            StopKnockCapture();
            CalStatusText.Text = "Calibration cancelled.";
            return;
        }

        string surfName = string.IsNullOrWhiteSpace(CalSurfaceNameCombo.Text)
            ? "Custom Surface"
            : CalSurfaceNameCombo.Text.Trim();

        StopKnockCapture();
        _isCalibrating = true;
        _calSamples.Clear();
        CalibrateSurfaceBtn.Content = "Cancel";
        CalStatusText.Text = $"● Knock 5 times on \"{surfName}\" (0/5)...";

        App.Engine.BeginKnockRecording(ev =>
        {
            _calSamples.Add(ev.Features);
            int count = _calSamples.Count;
            DispatcherQueue.TryEnqueue(() =>
            {
                if (!_isCalibrating) return;
                if (count < 5)
                {
                    CalStatusText.Text = $"● Knock 5 times on \"{surfName}\" ({count}/5)...";
                    return;
                }

                StopKnockCapture();
                var surfaces = App.Engine.Profile.Settings.KnockSurfaces;
                var existing = surfaces.FirstOrDefault(s => string.Equals(s.Name, surfName, StringComparison.OrdinalIgnoreCase));
                if (existing is null)
                {
                    existing = new KnockSurfaceProfile { Name = surfName };
                    surfaces.Add(existing);
                }
                existing.SampleFeatures = [.. _calSamples];
                App.Engine.SaveProfile();
                RefreshSurfacesList();
                CalStatusText.Text = $"Calibrated \"{surfName}\" with 5 samples.";
            });
        });
    }

    private void TestSurfaceBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_isTestingSurface)
        {
            StopKnockCapture();
            CalStatusText.Text = "Test stopped.";
            return;
        }

        if (App.Engine.Profile.Settings.KnockSurfaces.Count == 0)
        {
            CalStatusText.Text = "Calibrate at least one surface first before testing.";
            return;
        }

        StopKnockCapture();
        _isTestingSurface = true;
        TestSurfaceBtn.Content = "Stop Test";
        CalStatusText.Text = "● Listening — knock on any calibrated surface to test...";

        App.Engine.BeginKnockRecording(ev =>
        {
            var res = KnockSurfaceClassifier.Classify(ev.Features, App.Engine.Profile.Settings.KnockSurfaces);
            DispatcherQueue.TryEnqueue(() =>
            {
                if (!_isTestingSurface) return;
                CalStatusText.Text = res.Surface is not null
                    ? $"Detected: {res.Surface.Name} ({res.Confidence:P0} confidence)"
                    : $"Unrecognized surface (best score {res.Confidence:P0})";
            });
        });
    }

    private void DeleteSurface_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not string id) return;
        App.Engine.Profile.Settings.KnockSurfaces.RemoveAll(s => s.Id == id);
        App.Engine.SaveProfile();
        RefreshSurfacesList();
    }

    private static void SelectComboByTag(ComboBox combo, string tag)
    {
        foreach (var item in combo.Items.OfType<ComboBoxItem>())
        {
            if (string.Equals(item.Tag?.ToString(), tag, StringComparison.OrdinalIgnoreCase))
            {
                combo.SelectedItem = item;
                return;
            }
        }

        if (combo.Items.Count > 0)
        {
            combo.SelectedIndex = 0;
        }
    }

    private void KnockMicCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || !IsLoaded) return;
        string? selected = (KnockMicCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString();
        App.Engine.Profile.Settings.KnockMicrophoneName = string.IsNullOrWhiteSpace(selected) ? null : selected;
        App.Engine.SaveProfile();
    }

    private void SettingChanged(object sender, RoutedEventArgs e)
    {
        if (_loading || !IsLoaded) return;
        var s = App.Engine.Profile.Settings;
        s.StartWithWindows = StartupToggle.IsOn;
        s.MinimizeToTray = MinimizeTrayToggle.IsOn;
        s.AutoPauseOnFullscreen = FullscreenPauseToggle.IsOn;
        s.EnableTextExpansion = TextExpansionToggle.IsOn;
        s.EnableTouchpadGestures = TouchpadToggle.IsOn;
        s.EnableKnockDetection = KnockToggle.IsOn;
        s.ShowTrailOverlay = ShowTrailToggle.IsOn;
        s.ShowActionHudPill = ShowHudPillToggle.IsOn;
        s.ShowRippleEffect = ShowRippleToggle.IsOn;
        App.Engine.SaveProfile();
    }

    private async void ExportProfile_Click(object sender, RoutedEventArgs e)
    {
        var picker = new Windows.Storage.Pickers.FileSavePicker
        {
            SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.DocumentsLibrary,
            SuggestedFileName = "flick-profile"
        };
        picker.FileTypeChoices.Add("Flick Profile", [".flick.json", ".json"]);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, App.MainWindowHandle);

        var file = await picker.PickSaveFileAsync();
        if (file is null) return;

        var tempStore = new ProfileStore(file.Path);
        tempStore.Save(App.Engine.Profile);
    }

    private async void ImportProfile_Click(object sender, RoutedEventArgs e)
    {
        var picker = new Windows.Storage.Pickers.FileOpenPicker
        {
            SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.DocumentsLibrary
        };
        picker.FileTypeFilter.Add(".json");
        WinRT.Interop.InitializeWithWindow.Initialize(picker, App.MainWindowHandle);

        var file = await picker.PickSingleFileAsync();
        if (file is null) return;

        var loaded = new ProfileStore(file.Path).Load();
        App.Engine.Profile.Scopes = loaded.Scopes;
        App.Engine.Profile.Gestures = loaded.Gestures;
        App.Engine.Profile.Rules = loaded.Rules;
        App.Engine.Profile.TextExpansions = loaded.TextExpansions;
        App.Engine.Profile.Settings = loaded.Settings;
        App.Engine.SaveProfile();
        LoadFromSettings();
        ((App)Application.Current).ApplyTheme(loaded.Settings.ThemePreference);
    }

    private void SliderChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (_loading || !IsLoaded) return;
        var s = App.Engine.Profile.Settings;
        s.MinimumStrokeLength = MinLengthSlider.Value;
        s.RecognitionThreshold = ThresholdSlider.Value / 100.0;
        s.TouchpadMinSwipeDistance = TouchpadSwipeSlider.Value / 100.0;
        s.KnockSensitivity = KnockSensitivitySlider.Value / 100.0;
        s.TrailThickness = ThicknessSlider.Value;
        s.RippleRadius = RippleRadiusSlider.Value;
        s.RippleThickness = RippleThicknessSlider.Value;
        s.RippleSpeed = RippleSpeedSlider.Value;

        MinLengthLabel.Text = $"{s.MinimumStrokeLength:0} px";
        ThresholdLabel.Text = $"{s.RecognitionThreshold:P0}";
        TouchpadSwipeLabel.Text = $"{s.TouchpadMinSwipeDistance:P0}";
        KnockSensitivityLabel.Text = $"{s.KnockSensitivity:P0}";
        ThicknessLabel.Text = $"{s.TrailThickness:0.0} px";
        RippleRadiusLabel.Text = $"{s.RippleRadius:0} px";
        RippleThicknessLabel.Text = $"{s.RippleThickness:0} px";
        RippleSpeedLabel.Text = $"{s.RippleSpeed:0.0}×";

        App.Engine.SaveProfile();
    }

    private void ThemeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || ThemeCombo.SelectedItem is not ComboBoxItem item) return;
        string theme = item.Tag?.ToString() ?? "System";
        App.Engine.Profile.Settings.ThemePreference = theme;
        App.Engine.SaveProfile();
        ((App)Application.Current).ApplyTheme(theme);
    }

    private void TriggerButtonCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || TriggerButtonCombo.SelectedItem is not ComboBoxItem item) return;
        if (Enum.TryParse<TriggerButton>(item.Tag?.ToString(), out var btn))
        {
            App.Engine.Profile.Settings.TriggerButton = btn;
            App.Engine.SaveProfile();
        }
    }

    private void WindowTargetCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || WindowTargetCombo.SelectedItem is not ComboBoxItem item) return;
        if (Enum.TryParse<GlobalWindowTargetMode>(item.Tag?.ToString(), out var mode))
        {
            App.Engine.Profile.Settings.WindowTarget = mode;
            App.Engine.SaveProfile();
        }
    }

    private void TrailColorCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || TrailColorCombo.SelectedItem is not ComboBoxItem item) return;
        App.Engine.Profile.Settings.TrailColorHex = item.Tag?.ToString() ?? "#0078D4";
        App.Engine.SaveProfile();
    }

    private void IgnoredAppsBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        var list = (IgnoredAppsBox.Text ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        App.Engine.Profile.Settings.IgnoredProcesses = list;
        App.Engine.SaveProfile();
    }

    private async void ResetDefaults_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "Reset to Defaults",
            Content = "This will restore all built-in gestures, default actions, and settings. Custom gestures and actions will be removed.",
            PrimaryButtonText = "Reset",
            CloseButtonText = "Cancel"
        };

        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            var defaults = DefaultProfileFactory.CreateDefault();
            App.Engine.Profile.Gestures = defaults.Gestures;
            App.Engine.Profile.Rules = defaults.Rules;
            App.Engine.Profile.Settings = defaults.Settings;
            App.Engine.SaveProfile();
            LoadFromSettings();
            ((App)Application.Current).ApplyTheme(defaults.Settings.ThemePreference);
        }
    }
}
