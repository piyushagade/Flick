using Flick.Core.Models;
using Flick.Core.Storage;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;

namespace Flick.App.Views;

public sealed partial class SettingsPage : Page
{
    private bool _loading;

    public SettingsPage()
    {
        _loading = true;
        InitializeComponent();
        Loaded += (_, _) => LoadFromSettings();
    }

    private void LoadFromSettings()
    {
        _loading = true;
        var s = App.Engine.Profile.Settings;

        SelectComboByTag(ThemeCombo, s.ThemePreference);
        StartupToggle.IsOn = s.StartWithWindows;
        MinimizeTrayToggle.IsOn = s.MinimizeToTray;
        FullscreenPauseToggle.IsOn = s.AutoPauseOnFullscreen;

        SelectComboByTag(TriggerButtonCombo, s.TriggerButton.ToString());
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

        ShowTrailToggle.IsOn = s.ShowTrailOverlay;
        ShowHudPillToggle.IsOn = s.ShowActionHudPill;
        SelectComboByTag(TrailColorCombo, s.TrailColorHex);
        ThicknessSlider.Value = s.TrailThickness;
        ThicknessLabel.Text = $"{s.TrailThickness:0.0} px";

        HotCornersToggle.IsOn = s.EnableHotCorners;
        InitHotCornerCombo(CornerTopLeftCombo, s.HotCornerActions.GetValueOrDefault("TopLeft", ""));
        InitHotCornerCombo(CornerTopRightCombo, s.HotCornerActions.GetValueOrDefault("TopRight", ""));
        InitHotCornerCombo(CornerBottomLeftCombo, s.HotCornerActions.GetValueOrDefault("BottomLeft", ""));
        InitHotCornerCombo(CornerBottomRightCombo, s.HotCornerActions.GetValueOrDefault("BottomRight", ""));

        ShowRippleToggle.IsOn = s.ShowRippleEffect;
        RippleRadiusSlider.Value = s.RippleRadius;
        RippleRadiusLabel.Text = $"{s.RippleRadius:0} px";
        RippleThicknessSlider.Value = s.RippleThickness;
        RippleThicknessLabel.Text = $"{s.RippleThickness:0} px";
        RippleSpeedSlider.Value = s.RippleSpeed;
        RippleSpeedLabel.Text = $"{s.RippleSpeed:0.0}×";

        IgnoredAppsBox.Text = string.Join(", ", s.IgnoredProcesses);
        _loading = false;
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

    private void SettingChanged(object sender, RoutedEventArgs e)
    {
        if (_loading || !IsLoaded) return;
        var s = App.Engine.Profile.Settings;
        s.StartWithWindows = StartupToggle.IsOn;
        s.MinimizeToTray = MinimizeTrayToggle.IsOn;
        s.AutoPauseOnFullscreen = FullscreenPauseToggle.IsOn;
        s.EnableTouchpadGestures = TouchpadToggle.IsOn;
        s.EnableKnockDetection = KnockToggle.IsOn;
        s.ShowTrailOverlay = ShowTrailToggle.IsOn;
        s.ShowActionHudPill = ShowHudPillToggle.IsOn;
        s.ShowRippleEffect = ShowRippleToggle.IsOn;
        s.EnableHotCorners = HotCornersToggle.IsOn;
        App.Engine.SaveProfile();
    }

    private static readonly (string Label, string Command)[] CornerOptions =
    [
        ("Disabled", ""),
        ("Task View", "TaskView"),
        ("Show Desktop", "ShowDesktop"),
        ("Minimize Window", "Minimize"),
        ("Maximize Window", "Maximize"),
        ("Previous Desktop", "DesktopLeft"),
        ("Next Desktop", "DesktopRight")
    ];

    private static void InitHotCornerCombo(ComboBox combo, string currentCommand)
    {
        combo.Items.Clear();
        int selectedIdx = 0;
        for (int i = 0; i < CornerOptions.Length; i++)
        {
            combo.Items.Add(new ComboBoxItem { Content = CornerOptions[i].Label, Tag = CornerOptions[i].Command });
            if (string.Equals(CornerOptions[i].Command, currentCommand, StringComparison.OrdinalIgnoreCase))
            {
                selectedIdx = i;
            }
        }
        combo.SelectedIndex = selectedIdx;
    }

    private void HotCornerCombo_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || !IsLoaded) return;
        var map = App.Engine.Profile.Settings.HotCornerActions;
        map["TopLeft"] = (CornerTopLeftCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "";
        map["TopRight"] = (CornerTopRightCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "";
        map["BottomLeft"] = (CornerBottomLeftCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "";
        map["BottomRight"] = (CornerBottomRightCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "";
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
