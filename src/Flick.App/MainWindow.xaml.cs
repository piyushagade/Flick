using Flick.App.Views;
using Flick.Core.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Flick.App;

public sealed record ScopeShortcutItem(string Tag, string Label, string Glyph, string Count, Brush BackgroundBrush);

public sealed partial class MainWindow : Window
{
    private string _activePageTag = "home";
    private string? _activeScopeTag;

    public MainWindow()
    {
        InitializeComponent();

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);

        string iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "flick.ico");
        if (File.Exists(iconPath))
        {
            AppWindow.SetIcon(iconPath);
        }

        AppWindow.Resize(new Windows.Graphics.SizeInt32(1140, 760));

        RefreshScopeCounts();
        App.Engine.ProfileChanged += RefreshScopeCounts;
        NavigateTo("home");
    }

    private void RefreshScopeCounts()
    {
        var profile = App.Engine.Profile;
        var activeBrush = (Brush)Application.Current.Resources["SubtleFillColorSecondaryBrush"];
        var transparent = new SolidColorBrush(Microsoft.UI.Colors.Transparent);

        Brush BrushFor(string tag) =>
            _activePageTag == "rules" && string.Equals(_activeScopeTag, tag, StringComparison.Ordinal)
                ? activeBrush
                : transparent;

        var items = new List<ScopeShortcutItem>
        {
            new("global", "Global", "\uE774",
                (profile.Rules.Count(r => r.IsGlobal) + profile.TextExpansions.Count(t => t.IsGlobal)).ToString(),
                BrushFor("global"))
        };

        foreach (var scope in profile.Scopes)
        {
            string filter = scope.ProcessFilter.Trim();
            int count =
                profile.Rules.Count(r =>
                    !r.IsGlobal &&
                    string.Equals(r.ApplicationFilter.Trim(), filter, StringComparison.OrdinalIgnoreCase)) +
                profile.TextExpansions.Count(t =>
                    !t.IsGlobal &&
                    string.Equals(t.ApplicationFilter.Trim(), filter, StringComparison.OrdinalIgnoreCase));
            string tag = $"appscope:{scope.Id}";
            items.Add(new ScopeShortcutItem(tag, scope.Name, "\uE737", count.ToString(), BrushFor(tag)));
        }

        ScopeNavList.ItemsSource = items;
    }

    public void NavigateTo(string tag, string? scopeFilter = null)
    {
        _activePageTag = tag;
        _activeScopeTag = tag == "rules" ? (scopeFilter ?? "global") : null;

        Type pageType = tag switch
        {
            "rules" => typeof(RulesPage),
            "corners" => typeof(CornersPage),
            "settings" => typeof(SettingsPage),
            "whatsnew" => typeof(WhatsNewPage),
            "about" => typeof(AboutPage),
            _ => typeof(HomePage)
        };

        ContentFrame.Navigate(pageType, scopeFilter);
        UpdateFooterHighlights();
        RefreshScopeCounts();
    }

    private void UpdateFooterHighlights()
    {
        var activeBrush = (Brush)Application.Current.Resources["SubtleFillColorSecondaryBrush"];
        var transparent = new SolidColorBrush(Microsoft.UI.Colors.Transparent);

        NavDashboardBtn.Background = _activePageTag == "home" ? activeBrush : transparent;
        NavCornersBtn.Background = _activePageTag == "corners" ? activeBrush : transparent;
        NavSettingsBtn.Background = _activePageTag == "settings" ? activeBrush : transparent;
        NavWhatsNewBtn.Background = _activePageTag == "whatsnew" ? activeBrush : transparent;
        NavAboutBtn.Background = _activePageTag == "about" ? activeBrush : transparent;
    }

    private void FooterNav_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string tag)
        {
            NavigateTo(tag);
        }
    }

    private void ScopeButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string tag)
        {
            NavigateTo("rules", tag);
        }
    }

    private async void SidebarNewRule_Click(object sender, RoutedEventArgs e)
    {
        var nameBox = new TextBox
        {
            Header = "Scope Name",
            PlaceholderText = "e.g. Browsers, VS Code, Photoshop"
        };

        var processBox = new TextBox
        {
            Header = "Target Process(es) (comma-separated)",
            PlaceholderText = "e.g. msedge, chrome or code"
        };

        var runningPicker = new ComboBox
        {
            Header = "Quick-add running app",
            HorizontalAlignment = HorizontalAlignment.Stretch,
            PlaceholderText = "Select an open app...",
            ItemsSource = App.Engine.GetRunningProcesses()
        };
        runningPicker.SelectionChanged += (_, _) =>
        {
            if (runningPicker.SelectedItem is string proc)
            {
                if (string.IsNullOrWhiteSpace(nameBox.Text))
                {
                    nameBox.Text = proc;
                }
                processBox.Text = string.IsNullOrWhiteSpace(processBox.Text)
                    ? proc
                    : $"{processBox.Text.Trim()}, {proc}";
            }
        };

        var colorCombo = new ComboBox
        {
            Header = "Scope Trail & Ripple Accent (optional)",
            HorizontalAlignment = HorizontalAlignment.Stretch,
            SelectedIndex = 0
        };
        colorCombo.Items.Add(new ComboBoxItem { Content = "Use Global Default", Tag = "" });
        colorCombo.Items.Add(new ComboBoxItem { Content = "Windows Blue", Tag = "#0078D4" });
        colorCombo.Items.Add(new ComboBoxItem { Content = "Emerald Green", Tag = "#107C10" });
        colorCombo.Items.Add(new ComboBoxItem { Content = "Violet", Tag = "#8764B8" });
        colorCombo.Items.Add(new ComboBoxItem { Content = "Coral Red", Tag = "#E74856" });
        colorCombo.Items.Add(new ComboBoxItem { Content = "Amber Gold", Tag = "#FFB900" });

        var panel = new StackPanel { Spacing = 12, MinWidth = 360 };
        panel.Children.Add(nameBox);
        panel.Children.Add(processBox);
        panel.Children.Add(runningPicker);
        panel.Children.Add(colorCombo);

        var dialog = new ContentDialog
        {
            XamlRoot = Content.XamlRoot,
            Title = "New Application Scope",
            Content = panel,
            PrimaryButtonText = "Create Scope",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        string filter = processBox.Text?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(filter))
        {
            return;
        }

        string name = string.IsNullOrWhiteSpace(nameBox.Text) ? filter : nameBox.Text.Trim();
        var scope = new AppScope
        {
            Name = name,
            ProcessFilter = filter
        };

        App.Engine.Profile.Scopes.Add(scope);
        string? scopeColor = (colorCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString();
        if (!string.IsNullOrEmpty(scopeColor))
        {
            App.Engine.Profile.Settings.ScopeTrailColors[scope.Id] = scopeColor;
        }
        App.Engine.SaveProfile();
        NavigateTo("rules", $"appscope:{scope.Id}");
    }
}
