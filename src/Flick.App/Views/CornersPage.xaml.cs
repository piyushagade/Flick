using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Flick.App.Views;

public sealed partial class CornersPage : Page
{
    private bool _loading;

    private static readonly (string Label, string Command)[] CornerOptions =
    [
        ("Disabled", ""),
        ("Task View", "TaskView"),
        ("Show Desktop", "ShowDesktop"),
        ("Minimize Window", "Minimize"),
        ("Maximize / Restore Window", "Maximize"),
        ("Previous Desktop", "DesktopLeft"),
        ("Next Desktop", "DesktopRight"),
        ("Sneak (Peek Window Behind)", "Sneak")
    ];

    public CornersPage()
    {
        _loading = true;
        InitializeComponent();
        Loaded += (_, _) => LoadFromSettings();
    }

    private void LoadFromSettings()
    {
        _loading = true;
        var s = App.Engine.Profile.Settings;

        HotCornersToggle.IsOn = s.EnableHotCorners;
        InitHotCornerCombo(CornerTopLeftCombo, s.HotCornerActions.GetValueOrDefault("TopLeft", ""));
        InitHotCornerCombo(CornerTopRightCombo, s.HotCornerActions.GetValueOrDefault("TopRight", ""));
        InitHotCornerCombo(CornerBottomLeftCombo, s.HotCornerActions.GetValueOrDefault("BottomLeft", ""));
        InitHotCornerCombo(CornerBottomRightCombo, s.HotCornerActions.GetValueOrDefault("BottomRight", ""));
        UpdateEnabledState(s.EnableHotCorners);
        _loading = false;
    }

    private void UpdateEnabledState(bool enabled)
    {
        CornersConfigCard.Opacity = enabled ? 1.0 : 0.45;
        CornerTopLeftCombo.IsEnabled = enabled;
        CornerTopRightCombo.IsEnabled = enabled;
        CornerBottomLeftCombo.IsEnabled = enabled;
        CornerBottomRightCombo.IsEnabled = enabled;
    }

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

    private void HotCornersToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_loading || !IsLoaded) return;
        bool enabled = HotCornersToggle.IsOn;
        App.Engine.Profile.Settings.EnableHotCorners = enabled;
        UpdateEnabledState(enabled);
        App.Engine.SaveProfile();
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
}
