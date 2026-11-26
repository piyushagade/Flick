using Flick.Core.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Flick.App.Views;

public sealed class SnippetRowViewModel
{
    public required string Id { get; init; }
    public required string Abbreviation { get; init; }
    public required string Title { get; init; }
    public required string ModeBadge { get; init; }
    public required string TemplatePreview { get; init; }
    public required bool IsEnabled { get; init; }
    public required bool IsEngineActive { get; init; }
    public required double RowOpacity { get; init; }
    public required string GroupByFolderKey { get; init; }
    public required string GroupByTriggerKey { get; init; }
    public required string GroupByCaseKey { get; init; }
}

public sealed class SnippetGroup : List<SnippetRowViewModel>
{
    public string Key { get; }
    public string GroupCount { get; }

    public SnippetGroup(string key, IEnumerable<SnippetRowViewModel> items) : base(items)
    {
        Key = key;
        GroupCount = Count.ToString();
    }
}

public sealed partial class RulesPage
{
    private void ModeSelectorBar_SelectionChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
    {
        if (!IsLoaded)
        {
            return;
        }

        bool isSnippets = IsTextExpansionTab;
        GroupByCombo.Visibility = isSnippets ? Visibility.Collapsed : Visibility.Visible;
        SnippetGroupByCombo.Visibility = isSnippets ? Visibility.Visible : Visibility.Collapsed;
        SearchBox.PlaceholderText = isSnippets
            ? "Search abbreviations, templates, or folders..."
            : "Search gestures or actions...";

        if (isSnippets)
        {
            ConflictBanner.IsOpen = false;
            ConflictBanner.Visibility = Visibility.Collapsed;
        }

        RefreshList();
    }

    private void RefreshSnippetsList()
    {
        RulesListView.Visibility = Visibility.Collapsed;
        ConflictBanner.IsOpen = false;
        ConflictBanner.Visibility = Visibility.Collapsed;

        var profile = App.Engine.Profile;
        var settings = profile.Settings;
        bool engineActive = settings.IsEnabled && settings.EnableTextExpansion;
        string query = SearchBox.Text?.Trim() ?? string.Empty;

        var items = new List<SnippetRowViewModel>();
        foreach (var snippet in profile.TextExpansions)
        {
            if (_activeAppScope is not null)
            {
                if (!string.Equals(snippet.ApplicationFilter.Trim(), _activeAppScope.ProcessFilter.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
            }
            else if (!snippet.IsGlobal)
            {
                continue;
            }

            if (!string.IsNullOrEmpty(query) &&
                !snippet.Abbreviation.Contains(query, StringComparison.OrdinalIgnoreCase) &&
                !snippet.Name.Contains(query, StringComparison.OrdinalIgnoreCase) &&
                !snippet.Folder.Contains(query, StringComparison.OrdinalIgnoreCase) &&
                !snippet.Template.Contains(query, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string triggerLabel = snippet.TriggerMode == ExpansionTriggerMode.Immediate ? "Immediate" : "On Delimiter";
            string caseLabel = snippet.CaseMode switch
            {
                ExpansionCaseMode.Exact => "Exact Case",
                ExpansionCaseMode.CaseInsensitive => "Case-Insensitive",
                _ => "Smart Case"
            };
            string folderLabel = string.IsNullOrWhiteSpace(snippet.Folder) ? "General" : snippet.Folder.Trim();
            string offBadge = !settings.IsEnabled ? " • Paused" : (!settings.EnableTextExpansion ? " • Off" : "");

            items.Add(new SnippetRowViewModel
            {
                Id = snippet.Id,
                Abbreviation = snippet.Abbreviation,
                Title = string.IsNullOrWhiteSpace(snippet.Name) ? snippet.Abbreviation : snippet.Name,
                ModeBadge = $"{triggerLabel} • {caseLabel}{offBadge}",
                TemplatePreview = snippet.Template.Replace("\r\n", " ↵ ").Replace("\n", " ↵ "),
                IsEnabled = snippet.IsEnabled,
                IsEngineActive = engineActive,
                RowOpacity = snippet.IsEnabled && engineActive ? 1.0 : 0.55,
                GroupByFolderKey = folderLabel,
                GroupByTriggerKey = triggerLabel,
                GroupByCaseKey = caseLabel
            });
        }

        string groupMode = (SnippetGroupByCombo.SelectedItem as ComboBoxItem)?.Tag as string ?? "folder";
        _suppressToggles = true;
        if (groupMode == "none")
        {
            SnippetsListView.ItemsSource = items;
        }
        else
        {
            Func<SnippetRowViewModel, string> keySelector = groupMode switch
            {
                "trigger" => s => s.GroupByTriggerKey,
                "case" => s => s.GroupByCaseKey,
                _ => s => s.GroupByFolderKey
            };

            var groups = items
                .GroupBy(keySelector)
                .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
                .Select(g => new SnippetGroup(g.Key, g))
                .ToList();

            GroupedSnippetsCvs.Source = groups;
            SnippetsListView.ItemsSource = GroupedSnippetsCvs.View;
        }
        _suppressToggles = false;

        bool isEmpty = items.Count == 0;
        EmptyStatePanel.Visibility = isEmpty ? Visibility.Visible : Visibility.Collapsed;
        SnippetsListView.Visibility = isEmpty ? Visibility.Collapsed : Visibility.Visible;

        if (isEmpty)
        {
            EmptyStateIcon.Glyph = "\uE8C1";
            bool hasQuery = !string.IsNullOrEmpty(query);
            EmptyTitleText.Text = hasQuery ? "No matching snippets found" : "No text expansions in this scope yet";
            EmptySubtitleText.Text = hasQuery
                ? $"No abbreviations or templates match \"{query}\". Try clearing the search box."
                : "Click + New above to create a PhraseExpress-style snippet with variables, {cursor} positioning, or interactive prompts.";
        }
        else
        {
            EmptyStateIcon.Glyph = "\uE815";
        }
    }

    private void SnippetToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_suppressToggles || sender is not ToggleSwitch ts || ts.Tag is not string id)
        {
            return;
        }

        var snippet = App.Engine.Profile.TextExpansions.FirstOrDefault(s => s.Id == id);
        if (snippet is not null)
        {
            snippet.IsEnabled = ts.IsOn;
            App.Engine.SaveProfile();
        }
    }

    private async Task AddSnippetAsync()
    {
        var snippet = new TextExpansionRule
        {
            ApplicationFilter = _activeAppScope?.ProcessFilter ?? "*",
            IsEnabled = true
        };

        if (await ShowTextExpansionDialogAsync(snippet, isNew: true))
        {
            App.Engine.Profile.TextExpansions.Add(snippet);
            App.Engine.SaveProfile();
            RefreshList();
        }
    }

    private async void EditSnippet_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not string id) return;
        var snippet = App.Engine.Profile.TextExpansions.FirstOrDefault(s => s.Id == id);
        if (snippet is null) return;

        if (await ShowTextExpansionDialogAsync(snippet, isNew: false))
        {
            App.Engine.SaveProfile();
            RefreshList();
        }
    }

    private async void DeleteSnippet_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not string id) return;
        var snippet = App.Engine.Profile.TextExpansions.FirstOrDefault(s => s.Id == id);
        if (snippet is null) return;

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "Delete Text Expansion?",
            Content = $"Remove abbreviation \"{snippet.Abbreviation}\"?",
            PrimaryButtonText = "Delete",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close
        };

        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            App.Engine.Profile.TextExpansions.Remove(snippet);
            App.Engine.SaveProfile();
            RefreshList();
        }
    }

    private void DuplicateSnippet_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not string id) return;
        var src = App.Engine.Profile.TextExpansions.FirstOrDefault(s => s.Id == id);
        if (src is null) return;

        var copy = new TextExpansionRule
        {
            Name = string.IsNullOrWhiteSpace(src.Name) ? $"{src.Abbreviation} (Copy)" : $"{src.Name} (Copy)",
            Folder = src.Folder,
            Abbreviation = src.Abbreviation + "2",
            Template = src.Template,
            ApplicationFilter = src.ApplicationFilter,
            WindowTitleFilter = src.WindowTitleFilter,
            TriggerMode = src.TriggerMode,
            CaseMode = src.CaseMode,
            InjectionMode = src.InjectionMode,
            RequireWordBoundary = src.RequireWordBoundary,
            KeepDelimiter = src.KeepDelimiter,
            IsEnabled = src.IsEnabled
        };

        App.Engine.Profile.TextExpansions.Add(copy);
        App.Engine.SaveProfile();
        RefreshList();
    }

    private async Task<bool> ShowTextExpansionDialogAsync(TextExpansionRule snippet, bool isNew)
    {
        static TextBox MakePlainBox(string header, string placeholder, string text, bool monospace = false)
        {
            var box = new TextBox
            {
                Header = header,
                PlaceholderText = placeholder,
                Text = text,
                IsSpellCheckEnabled = false,
                IsTextPredictionEnabled = false
            };
            if (monospace)
            {
                box.FontFamily = new FontFamily("Consolas");
            }
            return box;
        }

        var abbrBox = MakePlainBox("Abbreviation (Trigger)", "e.g. ;sig, brb, @@", snippet.Abbreviation, monospace: true);
        var nameBox = MakePlainBox("Name (Optional)", "e.g. Email Signature", snippet.Name);
        var folderBox = MakePlainBox("Folder / Category", "e.g. Email, Code, Dates", snippet.Folder);
        var titleFilterBox = MakePlainBox("Window Title Filter (Optional)", "Substring or regex:...", snippet.WindowTitleFilter ?? string.Empty);

        var triggerCombo = new ComboBox
        {
            Header = "Trigger Mode",
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Items =
            {
                new ComboBoxItem { Content = "On Delimiter (Space / Tab / Enter / Punctuation)", Tag = ExpansionTriggerMode.OnDelimiter },
                new ComboBoxItem { Content = "Immediate (As soon as last char is typed)", Tag = ExpansionTriggerMode.Immediate }
            }
        };
        triggerCombo.SelectedIndex = snippet.TriggerMode == ExpansionTriggerMode.Immediate ? 1 : 0;

        var caseCombo = new ComboBox
        {
            Header = "Case Handling",
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Items =
            {
                new ComboBoxItem { Content = "Smart Case (ty -> text, Ty -> Text, TY -> TEXT)", Tag = ExpansionCaseMode.SmartCase },
                new ComboBoxItem { Content = "Case-Insensitive", Tag = ExpansionCaseMode.CaseInsensitive },
                new ComboBoxItem { Content = "Exact Case", Tag = ExpansionCaseMode.Exact }
            }
        };
        caseCombo.SelectedIndex = snippet.CaseMode switch
        {
            ExpansionCaseMode.Exact => 2,
            ExpansionCaseMode.CaseInsensitive => 1,
            _ => 0
        };

        var injectionCombo = new ComboBox
        {
            Header = "Injection Method",
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Items =
            {
                new ComboBoxItem { Content = "Auto (SendInput for short, Clipboard for long/multiline)", Tag = ExpansionInjectionMode.Auto },
                new ComboBoxItem { Content = "SendInput (Unicode keystrokes)", Tag = ExpansionInjectionMode.SendInput },
                new ComboBoxItem { Content = "Clipboard Paste (Preserves clipboard)", Tag = ExpansionInjectionMode.ClipboardPaste }
            }
        };
        injectionCombo.SelectedIndex = (int)snippet.InjectionMode;

        var wordBoundaryCheck = new CheckBox
        {
            Content = "Require word boundary before abbreviation",
            IsChecked = snippet.RequireWordBoundary
        };

        var keepDelimiterCheck = new CheckBox
        {
            Content = "Keep typed delimiter after expansion",
            IsChecked = snippet.KeepDelimiter
        };

        var templateBox = new TextBox
        {
            Header = "Replacement Template",
            PlaceholderText = "Type replacement text. Click a macro chip below to insert {cursor}, {date}, {clipboard}, etc.",
            Text = snippet.Template,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            MinHeight = 130,
            MaxHeight = 220,
            FontFamily = new FontFamily("Consolas"),
            IsSpellCheckEnabled = false,
            IsTextPredictionEnabled = false
        };

        (string Label, string Token)[] macroChips =
        [
            ("{cursor}", "{cursor}"),
            ("{date}", "{date:yyyy-MM-dd}"),
            ("{time}", "{time:HH:mm}"),
            ("{clipboard}", "{clipboard}"),
            ("{input:...}", "{input:Value}"),
            ("{choice:...}", "{choice:Pick:Option A|Option B}"),
            ("{key:Tab}", "{key:Tab}"),
            ("{delay:100}", "{delay:100}"),
            ("{window.title}", "{window.title}"),
            ("{snippet:...}", "{snippet:abbr}")
        ];

        // Two clean, non-scrolling rows of macro chips so no horizontal scrollbar appears.
        var chipsContainer = new StackPanel { Spacing = 6 };
        var rowA = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        var rowB = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };

        for (int i = 0; i < macroChips.Length; i++)
        {
            var (label, token) = macroChips[i];
            var chipBtn = new Button
            {
                Content = label,
                Padding = new Thickness(9, 4, 9, 4),
                FontSize = 11.5,
                FontFamily = new FontFamily("Consolas")
            };
            chipBtn.Click += (_, _) =>
            {
                int selStart = templateBox.SelectionStart;
                string current = templateBox.Text ?? string.Empty;
                templateBox.Text = current.Insert(selStart, token);
                templateBox.SelectionStart = selStart + token.Length;
                templateBox.Focus(FocusState.Programmatic);
            };
            (i < 5 ? rowA : rowB).Children.Add(chipBtn);
        }

        chipsContainer.Children.Add(rowA);
        chipsContainer.Children.Add(rowB);

        var form = new StackPanel
        {
            Spacing = 12,
            Padding = new Thickness(0, 0, 14, 4)
        };

        static Grid TwoCol(FrameworkElement left, FrameworkElement right)
        {
            var g = new Grid { ColumnSpacing = 12 };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            Grid.SetColumn(left, 0);
            Grid.SetColumn(right, 1);
            g.Children.Add(left);
            g.Children.Add(right);
            return g;
        }

        form.Children.Add(TwoCol(abbrBox, nameBox));
        form.Children.Add(templateBox);
        form.Children.Add(chipsContainer);
        form.Children.Add(TwoCol(folderBox, titleFilterBox));
        form.Children.Add(TwoCol(triggerCombo, caseCombo));
        form.Children.Add(injectionCombo);
        form.Children.Add(TwoCol(wordBoundaryCheck, keepDelimiterCheck));

        var scroll = new ScrollViewer
        {
            Content = form,
            MaxHeight = 560,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = isNew ? "New Text Expansion" : "Edit Text Expansion",
            Content = scroll,
            PrimaryButtonText = "Save",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary
        };
        dialog.Resources["ContentDialogMaxWidth"] = 660.0;
        dialog.Resources["ContentDialogMinWidth"] = 620.0;

        dialog.PrimaryButtonClick += (_, args) =>
        {
            if (string.IsNullOrWhiteSpace(abbrBox.Text))
            {
                args.Cancel = true;
                abbrBox.Focus(FocusState.Programmatic);
            }
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return false;
        }

        snippet.Abbreviation = abbrBox.Text.Trim();
        snippet.Name = nameBox.Text.Trim();
        snippet.Folder = folderBox.Text.Trim();
        snippet.WindowTitleFilter = string.IsNullOrWhiteSpace(titleFilterBox.Text) ? null : titleFilterBox.Text.Trim();
        snippet.Template = templateBox.Text ?? string.Empty;
        snippet.TriggerMode = (ExpansionTriggerMode)((ComboBoxItem)triggerCombo.SelectedItem).Tag;
        snippet.CaseMode = (ExpansionCaseMode)((ComboBoxItem)caseCombo.SelectedItem).Tag;
        snippet.InjectionMode = (ExpansionInjectionMode)((ComboBoxItem)injectionCombo.SelectedItem).Tag;
        snippet.RequireWordBoundary = wordBoundaryCheck.IsChecked == true;
        snippet.KeepDelimiter = keepDelimiterCheck.IsChecked == true;

        return true;
    }
}
