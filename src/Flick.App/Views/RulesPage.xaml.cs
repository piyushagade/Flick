using Flick.App.Helpers;
using Flick.Core.Keyboard;
using Flick.Core.Knock;
using Flick.Core.Models;
using Flick.Core.Recognition;
using Flick.Core.Touchpad;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;

namespace Flick.App.Views;

public sealed record TapDotViewModel(Thickness DotMargin, Brush Fill);
public sealed record PreviewStrokeViewModel(PointCollection Points, PointCollection ArrowPoints, Brush Stroke);

public sealed class RuleRowViewModel
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string ScopeBadge { get; init; }
    public required string ActionSummary { get; init; }
    public required bool IsEnabled { get; init; }
    public required IReadOnlyList<PreviewStrokeViewModel> PreviewStrokes { get; init; }
    public required IReadOnlyList<TapDotViewModel> TapDots { get; init; }
    public required Visibility StrokeVisibility { get; init; }
    public required Visibility TapIconVisibility { get; init; }
    public required Visibility ChordIconVisibility { get; init; }
    public required Visibility KnockIconVisibility { get; init; }
}

public sealed partial class RulesPage : Page
{
    private bool _suppressToggles;
    private AppScope? _activeAppScope;

    public RulesPage()
    {
        InitializeComponent();
        Loaded += (_, _) => RefreshList();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        _activeAppScope = null;

        if (e.Parameter is string param && param.StartsWith("appscope:", StringComparison.Ordinal))
        {
            string scopeId = param["appscope:".Length..];
            _activeAppScope = App.Engine.Profile.Scopes.FirstOrDefault(s => s.Id == scopeId);
        }

        UpdateScopeHeader();
    }

    private void UpdateScopeHeader()
    {
        if (_activeAppScope is not null)
        {
            ScopeTitleText.Text = _activeAppScope.Name;
            ScopeSubtitleText.Text = $"Target processes: {_activeAppScope.ProcessFilter}";
            DeleteScopeButton.Visibility = Visibility.Visible;
        }
        else
        {
            ScopeTitleText.Text = "Global";
            ScopeSubtitleText.Text = "Gestures that apply everywhere unless overridden by an app scope.";
            DeleteScopeButton.Visibility = Visibility.Collapsed;
        }
    }

    private void DeleteScope_Click(object sender, RoutedEventArgs e)
    {
        if (_activeAppScope is null) return;

        string targetFilter = _activeAppScope.ProcessFilter.Trim();
        App.Engine.Profile.Scopes.RemoveAll(s => s.Id == _activeAppScope.Id);
        App.Engine.Profile.Rules.RemoveAll(r =>
            !r.IsGlobal && string.Equals(r.ApplicationFilter.Trim(), targetFilter, StringComparison.OrdinalIgnoreCase));
        App.Engine.SaveProfile();

        _activeAppScope = null;
        UpdateScopeHeader();
        RefreshList();
    }

    private void RefreshList()
    {
        var profile = App.Engine.Profile;
        var gestureMap = profile.Gestures.ToDictionary(g => g.Id, g => g);

        string query = SearchBox.Text?.Trim() ?? string.Empty;

        var items = new List<RuleRowViewModel>();
        foreach (var rule in profile.Rules)
        {
            if (_activeAppScope is not null)
            {
                if (!string.Equals(rule.ApplicationFilter.Trim(), _activeAppScope.ProcessFilter.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
            }
            else if (!rule.IsGlobal)
            {
                continue;
            }

            bool isChord = rule.TriggerType == TriggerType.KeyboardChord;
            bool isKnock = rule.TriggerType == TriggerType.KnockPattern;
            gestureMap.TryGetValue(rule.GestureId, out var gesture);
            string gestureName = isChord
                ? ChordKeyHelper.FormatChord(rule.KeyboardChord)
                : isKnock
                    ? KnockPatternMatcher.FormatPattern(rule.KnockPattern)
                    : (gesture?.Name ?? "Unknown Gesture");

            if (!string.IsNullOrEmpty(query) &&
                !rule.Name.Contains(query, StringComparison.OrdinalIgnoreCase) &&
                !rule.ApplicationFilter.Contains(query, StringComparison.OrdinalIgnoreCase) &&
                !rule.ActionPayload.Contains(query, StringComparison.OrdinalIgnoreCase) &&
                !gestureName.Contains(query, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string sourceLabel = isChord
                ? (rule.KeyboardChord?.Mode == KeyboardChordMode.Sequential ? "Sequential Chord" : "Simultaneous Chord")
                : isKnock
                    ? "Acoustic Knock"
                    : rule.InputSource switch
                    {
                        GestureInputSource.Touchpad when rule.FingerCount > 0 => $"{rule.FingerCount}-Finger Touchpad",
                        GestureInputSource.Touchpad => "Touchpad",
                        GestureInputSource.Mouse => "Mouse",
                        _ => "Any Input"
                    };
            string scopeLabel = rule.IsGlobal ? "Global" : rule.ApplicationFilter;

            bool isTap = !isChord && !isKnock && string.Equals(rule.GestureId, "builtin-tap", StringComparison.Ordinal);
            string repBadge = !isChord && !isKnock && rule.RequiredRepetitions > 1 ? $" • {rule.RequiredRepetitions}×" : "";
            string seqBadge = !isChord && !isKnock && rule.FollowUpGestures is { Count: > 0 } ? $" • {1 + rule.FollowUpGestures.Count}-Step" : "";
            items.Add(new RuleRowViewModel
            {
                Id = rule.Id,
                Name = rule.Name,
                ScopeBadge = $"{scopeLabel} • {sourceLabel}{repBadge}{seqBadge}",
                ActionSummary = $"{gestureName}  →  {rule.ActionType}: {rule.ActionPayload}",
                IsEnabled = rule.IsEnabled,
                PreviewStrokes = !isChord && !isKnock && !isTap ? BuildPreviewStrokes(gesture) : [],
                TapDots = isTap ? BuildPreviewTapDots(rule) : [],
                StrokeVisibility = !isChord && !isKnock && !isTap ? Visibility.Visible : Visibility.Collapsed,
                TapIconVisibility = isTap ? Visibility.Visible : Visibility.Collapsed,
                ChordIconVisibility = isChord ? Visibility.Visible : Visibility.Collapsed,
                KnockIconVisibility = isKnock ? Visibility.Visible : Visibility.Collapsed
            });
        }

        _suppressToggles = true;
        RulesListView.ItemsSource = items;
        _suppressToggles = false;

        bool isEmpty = items.Count == 0;
        EmptyStatePanel.Visibility = isEmpty ? Visibility.Visible : Visibility.Collapsed;
        RulesListView.Visibility = isEmpty ? Visibility.Collapsed : Visibility.Visible;

        if (isEmpty)
        {
            bool hasQuery = !string.IsNullOrEmpty(query);
            EmptyTitleText.Text = hasQuery ? "No matching gestures found" : "No gestures in this scope yet";
            EmptySubtitleText.Text = hasQuery
                ? $"No gestures or actions match \"{query}\". Try clearing the search box."
                : "Click + New to record a mouse or multi-finger touchpad gesture and assign an action.";
        }
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e) => RefreshList();

    private void RuleToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_suppressToggles || sender is not ToggleSwitch ts || ts.Tag is not string id)
        {
            return;
        }

        var rule = App.Engine.Profile.Rules.FirstOrDefault(r => r.Id == id);
        if (rule is not null)
        {
            rule.IsEnabled = ts.IsOn;
            App.Engine.SaveProfile();
        }
    }

    private async void AddRule_Click(object sender, RoutedEventArgs e)
    {
        var newRule = new GestureRule
        {
            Name = string.Empty,
            GestureId = App.Engine.Profile.Gestures.FirstOrDefault()?.Id ?? string.Empty,
            ApplicationFilter = _activeAppScope?.ProcessFilter ?? "*",
            ActionType = ActionType.SendHotkey,
            ActionPayload = "Ctrl+W",
            IsEnabled = true
        };

        if (await ShowRuleDialogAsync(newRule, isNew: true))
        {
            App.Engine.Profile.Rules.Add(newRule);
            App.Engine.SaveProfile();
            RefreshList();
        }
    }

    private async void EditRule_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not string id)
        {
            return;
        }

        var rule = App.Engine.Profile.Rules.FirstOrDefault(r => r.Id == id);
        if (rule is null)
        {
            return;
        }

        if (await ShowRuleDialogAsync(rule, isNew: false))
        {
            App.Engine.SaveProfile();
            RefreshList();
        }
    }

    private void DeleteRule_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not string id)
        {
            return;
        }

        App.Engine.Profile.Rules.RemoveAll(r => r.Id == id);
        App.Engine.SaveProfile();
        RefreshList();
    }

    private void MoveRuleUp_Click(object sender, RoutedEventArgs e) => MoveRule(sender, -1);
    private void MoveRuleDown_Click(object sender, RoutedEventArgs e) => MoveRule(sender, +1);

    private void MoveRule(object sender, int delta)
    {
        if (sender is not Button btn || btn.Tag is not string id) return;
        var rules = App.Engine.Profile.Rules;
        int idx = rules.FindIndex(r => r.Id == id);
        if (idx < 0) return;

        int target = idx + delta;
        if (target < 0 || target >= rules.Count) return;

        (rules[idx], rules[target]) = (rules[target], rules[idx]);
        App.Engine.SaveProfile();
        RefreshList();
    }

    private void DuplicateRule_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not string id) return;
        var rules = App.Engine.Profile.Rules;
        int idx = rules.FindIndex(r => r.Id == id);
        if (idx < 0) return;

        var src = rules[idx];
        var copy = new GestureRule
        {
            Name = $"{src.Name} (Copy)",
            TriggerType = src.TriggerType,
            GestureId = src.GestureId,
            KeyboardChord = src.KeyboardChord is null ? null : new KeyboardChordDefinition
            {
                Mode = src.KeyboardChord.Mode,
                TimeoutMs = src.KeyboardChord.TimeoutMs,
                SuppressKeys = src.KeyboardChord.SuppressKeys,
                Steps = src.KeyboardChord.Steps.Select(s => new ChordStep
                {
                    Modifiers = s.Modifiers,
                    KeyCodes = [.. s.KeyCodes]
                }).ToList()
            },
            KnockPattern = src.KnockPattern is null ? null : new KnockPatternDefinition
            {
                KnockCount = src.KnockPattern.KnockCount,
                SurfaceProfileId = src.KnockPattern.SurfaceProfileId,
                SurfaceName = src.KnockPattern.SurfaceName,
                RhythmRatios = [.. src.KnockPattern.RhythmRatios]
            },
            ApplicationFilter = src.ApplicationFilter,
            WindowTitleFilter = src.WindowTitleFilter,
            WindowTarget = src.WindowTarget,
            RequiredModifiers = src.RequiredModifiers,
            RequiredRepetitions = src.RequiredRepetitions,
            FollowUpGestures = src.FollowUpGestures?.Select(f => new GestureSequenceStep
            {
                GestureId = f.GestureId,
                InputSource = f.InputSource,
                FingerCount = f.FingerCount
            }).ToList(),
            ActionType = src.ActionType,
            ActionPayload = src.ActionPayload,
            Steps = src.Steps?.Select(s => new ActionStep
            {
                ActionType = s.ActionType,
                ActionPayload = s.ActionPayload,
                DelayBeforeMs = s.DelayBeforeMs
            }).ToList(),
            InputSource = src.InputSource,
            FingerCount = src.FingerCount,
            PreviewTapPoints = src.PreviewTapPoints is not null ? [.. src.PreviewTapPoints] : null,
            IsEnabled = src.IsEnabled
        };

        rules.Insert(idx + 1, copy);
        App.Engine.SaveProfile();
        RefreshList();
    }

    private sealed record ActionPreset(string Label, string Payload, ActionType? OverrideType = null);

    private sealed record ActionCategoryInfo(
        ActionType PrimaryType,
        string Glyph,
        string Tooltip,
        ActionPreset[] Presets,
        bool AllowCustomPayload,
        string CustomPlaceholder);

    private static IReadOnlyList<PreviewStrokeViewModel> BuildPreviewStrokes(GestureDefinition? gesture)
    {
        if (gesture is null)
        {
            return [];
        }

        if (gesture.FingerStrokes is { Count: > 1 })
        {
            var multi = GesturePreviewHelper.ToMultiPreviewPoints(gesture.FingerStrokes, 48, 8);
            var list = new List<PreviewStrokeViewModel>(multi.Count);
            for (int f = 0; f < multi.Count; f++)
            {
                list.Add(new PreviewStrokeViewModel(
                    multi[f],
                    GesturePreviewHelper.CreateArrowhead(multi[f], headLength: 6.5, halfAngleDeg: 32),
                    new SolidColorBrush(FingerColors[f % FingerColors.Length])));
            }
            return list;
        }

        var singlePts = GesturePreviewHelper.ToPreviewPoints(gesture.Points, 48, 8);
        return
        [
            new PreviewStrokeViewModel(
                singlePts,
                GesturePreviewHelper.CreateArrowhead(singlePts, headLength: 6.5, halfAngleDeg: 32),
                new SolidColorBrush(FingerColors[0]))
        ];
    }

    private static IReadOnlyList<TapDotViewModel> BuildPreviewTapDots(GestureRule rule)
    {
        if (rule.PreviewTapPoints is { Count: > 0 } pts)
        {
            var dots = new List<TapDotViewModel>(pts.Count);
            double minX = pts.Min(p => p.X);
            double maxX = pts.Max(p => p.X);
            double minY = pts.Min(p => p.Y);
            double maxY = pts.Max(p => p.Y);
            double span = Math.Max(maxX - minX, maxY - minY);

            const double usable = 28.0;
            double scale = span > 1e-4 ? usable / span : 1.0;
            double offsetX = 10.0 + (usable - (maxX - minX) * scale) / 2.0;
            double offsetY = 10.0 + (usable - (maxY - minY) * scale) / 2.0;

            for (int i = 0; i < pts.Count; i++)
            {
                double x = (span > 1e-4 ? offsetX + (pts[i].X - minX) * scale : 24.0) - 3.5;
                double y = (span > 1e-4 ? offsetY + (pts[i].Y - minY) * scale : 24.0) - 3.5;
                dots.Add(new TapDotViewModel(new Thickness(x, y, 0, 0), new SolidColorBrush(FingerColors[i % FingerColors.Length])));
            }
            return dots;
        }

        int count = Math.Clamp(rule.FingerCount > 0 ? rule.FingerCount : 3, 2, 5);
        var fallback = new List<TapDotViewModel>(count);
        double step = 36.0 / (count + 1);

        for (int i = 0; i < count; i++)
        {
            double x = 6.0 + step * (i + 1) - 3.5;
            double norm = (count == 1) ? 0 : (i / (double)(count - 1)) * 2.0 - 1.0;
            double y = 18.0 + norm * norm * 5.0;
            fallback.Add(new TapDotViewModel(new Thickness(x, y, 0, 0), new SolidColorBrush(FingerColors[i % FingerColors.Length])));
        }

        return fallback;
    }

    private static readonly ActionCategoryInfo[] ActionCategories =
    [
        new(
            ActionType.SendHotkey,
            "\uE765",
            "Keyboard Shortcut",
            [
                new("Close Tab (Ctrl+W)", "Ctrl+W"),
                new("Reopen Closed Tab (Ctrl+Shift+T)", "Ctrl+Shift+T"),
                new("Back (Alt+Left)", "Alt+Left"),
                new("Forward (Alt+Right)", "Alt+Right"),
                new("Switch Window (Alt+Tab)", "Alt+Tab"),
                new("Copy (Ctrl+C)", "Ctrl+C"),
                new("Paste (Ctrl+V)", "Ctrl+V"),
                new("Custom Hotkey...", "")
            ],
            true,
            "e.g. Ctrl+Shift+N, Win+E, F5"),
        new(
            ActionType.MouseAction,
            "\uE962",
            "Mouse Actions",
            [
                new("Middle Click", "MiddleClick"),
                new("Left Click", "LeftClick"),
                new("Double Click", "DoubleClick"),
                new("Right Click", "RightClick"),
                new("Back Button (XButton1)", "BackClick"),
                new("Forward Button (XButton2)", "ForwardClick"),
                new("Scroll Up", "ScrollUp"),
                new("Scroll Down", "ScrollDown"),
                new("Scroll Left", "ScrollLeft"),
                new("Scroll Right", "ScrollRight")
            ],
            false,
            ""),
        new(
            ActionType.WindowManagement,
            "\uE737",
            "Window Management",
            [
                new("Maximize Window", "Maximize"),
                new("Minimize Window", "Minimize"),
                new("Restore Window", "Restore"),
                new("Close Window", "Close"),
                new("Snap Left", "SnapLeft"),
                new("Snap Right", "SnapRight"),
                new("Show Desktop", "ShowDesktop"),
                new("Task View", "TaskView"),
                new("Previous Virtual Desktop", "DesktopLeft"),
                new("Next Virtual Desktop", "DesktopRight")
            ],
            false,
            ""),
        new(
            ActionType.SystemAction,
            "\uE770",
            "System Actions",
            [
                new("Open Start Menu", "StartMenu"),
                new("Run Dialog (Win+R)", "RunDialog"),
                new("Task Manager", "TaskManager"),
                new("Ctrl + Alt + Del", "CtrlAltDel"),
                new("Lock Screen", "LockScreen"),
                new("Snipping Tool / Screenshot", "SnippingTool"),
                new("Clipboard History (Win+V)", "ClipboardHistory"),
                new("Emoji Picker (Win+.)", "EmojiPicker"),
                new("Quick Settings (Win+A)", "QuickSettings"),
                new("Empty Recycle Bin", "EmptyRecycleBin"),
                new("Sleep PC", "Sleep"),
                new("Restart PC", "Restart"),
                new("Shutdown PC", "Shutdown")
            ],
            false,
            ""),
        new(
            ActionType.MediaControl,
            "\uE8D6",
            "Media & Volume",
            [
                new("Play / Pause", "PlayPause"),
                new("Volume Up", "VolumeUp"),
                new("Volume Down", "VolumeDown"),
                new("Mute / Unmute", "Mute"),
                new("Next Track", "NextTrack"),
                new("Previous Track", "PreviousTrack")
            ],
            false,
            ""),
        new(
            ActionType.OpenUrl,
            "\uE774",
            "Open Website / URL",
            [
                new("Custom URL...", "")
            ],
            true,
            "https://example.com"),
        new(
            ActionType.WindowsIntent,
            "\uE8A7",
            "Windows Intents & Protocols",
            [
                new("Windows Settings", "ms-settings:"),
                new("Display Settings", "ms-settings:display"),
                new("Bluetooth & Devices", "ms-settings:bluetooth"),
                new("Sound Settings", "ms-settings:sound"),
                new("Calculator", "calculator:"),
                new("Mail Client", "mailto:"),
                new("Custom URI Intent...", "")
            ],
            true,
            "ms-settings:network-wifi or spotify:"),
        new(
            ActionType.HttpRequest,
            "\uE968",
            "HTTP / Webhook",
            [
                new("HTTP Request (GET / POST)...", "")
            ],
            true,
            "POST https://api.example.com/hook | {\"event\":\"flick\"}"),
        new(
            ActionType.IftttWebhook,
            "\uE945",
            "IFTTT Webhook",
            [
                new("IFTTT Trigger (event|key)...", "")
            ],
            true,
            "my_event|your_ifttt_maker_key"),
        new(
            ActionType.MqttPublish,
            "\uE704",
            "MQTT Publish",
            [
                new("Publish MQTT Message...", "")
            ],
            true,
            "broker.hivemq.com:1883|home/desk/light|TOGGLE"),
        new(
            ActionType.RunProgram,
            "\uE756",
            "Run Program / PowerShell",
            [
                new("Launch Application", "", ActionType.RunProgram),
                new("Run PowerShell Script", "", ActionType.RunPowerShell)
            ],
            true,
            "notepad.exe or Get-Date"),
        new(
            ActionType.TextSnippet,
            "\uE8C8",
            "Type Text Snippet",
            [
                new("Type Custom Text...", "")
            ],
            true,
            "Text to type...")
    ];

    private static readonly Windows.UI.Color[] FingerColors =
    [
        Windows.UI.Color.FromArgb(255, 56, 189, 248),  // Finger 1: Sky Blue
        Windows.UI.Color.FromArgb(255, 16, 185, 129),  // Finger 2: Emerald
        Windows.UI.Color.FromArgb(255, 167, 139, 250), // Finger 3: Violet
        Windows.UI.Color.FromArgb(255, 251, 191, 36),  // Finger 4: Amber
        Windows.UI.Color.FromArgb(255, 251, 113, 133)  // Finger 5: Coral
    ];

    private async Task<bool> ShowRuleDialogAsync(GestureRule rule, bool isNew)
    {
        var drawnPoints = new List<GesturePoint>();
        var drawnFingerStrokes = new List<List<GesturePoint>>();
        var recordedTapPoints = new List<GesturePoint>();
        var activeMousePoints = new List<GesturePoint>();
        var followUpSteps = rule.FollowUpGestures is { Count: > 0 }
            ? rule.FollowUpGestures.Select(f => new GestureSequenceStep { GestureId = f.GestureId, InputSource = f.InputSource, FingerCount = f.FingerCount }).ToList()
            : new List<GestureSequenceStep>();
        var pendingCustomFollowUpGestures = new List<GestureDefinition>();
        bool isDrawing = false;
        bool isPadArmed = false;
        bool isRecordingFollowUp = false;
        string? recordedBuiltInId = null;
        DateTimeOffset lastTouchpadEvent = DateTimeOffset.MinValue;

        // Auto-detected input source & finger count (updated automatically when drawing).
        GestureInputSource detectedSource = rule.InputSource;
        int detectedFingers = rule.FingerCount;

        var nameBox = new TextBox
        {
            Header = "Action Name",
            Text = rule.Name,
            PlaceholderText = "New Action"
        };

        var existingGesture = App.Engine.Profile.Gestures.FirstOrDefault(g => g.Id == rule.GestureId);

        const double padWidth = 380;
        const double padHeight = 145;

        var drawCanvas = new Canvas
        {
            Width = padWidth,
            Height = padHeight,
            Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent)
        };

        Polyline CreateFingerPolyline(int fingerIndex) => new()
        {
            Stroke = new SolidColorBrush(FingerColors[fingerIndex % FingerColors.Length]),
            StrokeThickness = 3.5,
            StrokeLineJoin = PenLineJoin.Round,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round
        };

        var mousePolyline = CreateFingerPolyline(0);
        drawCanvas.Children.Add(mousePolyline);

        void AddArrowheadToCanvas(IList<Point> pts, int fingerIndex)
        {
            var arrowPts = GesturePreviewHelper.CreateArrowhead(pts, headLength: 10, halfAngleDeg: 28);
            if (arrowPts.Count < 3) return;
            var arrowPoly = CreateFingerPolyline(fingerIndex);
            arrowPoly.StrokeThickness = 2.8;
            foreach (var ap in arrowPts) arrowPoly.Points.Add(ap);
            drawCanvas.Children.Add(arrowPoly);
        }

        void RenderMultiFingerPaths(IReadOnlyList<IReadOnlyList<GesturePoint>> normalizedPaths)
        {
            drawCanvas.Children.Clear();
            for (int f = 0; f < normalizedPaths.Count; f++)
            {
                var poly = CreateFingerPolyline(f);
                var scaled = normalizedPaths[f]
                    .Select(p => new GesturePoint(20 + p.X * (padWidth - 40), 20 + p.Y * (padHeight - 40)))
                    .ToArray();
                var smoothed = StrokeSmoother.Smooth(scaled, denoisePasses: 3, chaikinIterations: 2);
                var ptsList = new List<Point>(smoothed.Length);
                foreach (var p in smoothed)
                {
                    var pt = new Point(p.X, p.Y);
                    poly.Points.Add(pt);
                    ptsList.Add(pt);
                }
                drawCanvas.Children.Add(poly);
                AddArrowheadToCanvas(ptsList, f);
            }
        }

        var padHintText = new TextBlock
        {
            Text = "Click pad to record",
            Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
            Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
        };
        var padHintBadge = new Border
        {
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(10, 3, 10, 4),
            Background = (Brush)Application.Current.Resources["AcrylicBackgroundFillColorDefaultBrush"],
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 8, 0, 0),
            IsHitTestVisible = false,
            Child = padHintText
        };

        var drawPadBorder = new Border
        {
            Width = padWidth,
            Height = padHeight,
            CornerRadius = new CornerRadius(10),
            Background = (Brush)Application.Current.Resources["SubtleFillColorSecondaryBrush"],
            BorderBrush = (Brush)Application.Current.Resources["ControlStrokeColorDefaultBrush"],
            BorderThickness = new Thickness(1),
            Child = new Grid
            {
                Children = { drawCanvas, padHintBadge }
            }
        };

        var detectedBadgeText = new TextBlock
        {
            Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
        };
        var detectedBadge = new Border
        {
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(10, 4, 10, 5),
            Background = (Brush)Application.Current.Resources["SubtleFillColorTertiaryBrush"],
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
            Child = detectedBadgeText
        };

        void UpdateDetectedBadge()
        {
            detectedBadgeText.Text = detectedSource switch
            {
                GestureInputSource.Touchpad when detectedFingers > 0 => $"Touchpad • {detectedFingers} Fingers (Auto)",
                GestureInputSource.Touchpad => "Touchpad (Auto)",
                GestureInputSource.Mouse => "Mouse Stroke",
                _ => "Any Input"
            };
        }

        UpdateDetectedBadge();

        var drawStatusText = new TextBlock
        {
            Text = "Click the pad above to start recording a gesture.",
            Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
            Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
            TextWrapping = TextWrapping.Wrap
        };

        void ShowPresetPreview(GestureDefinition? g)
        {
            drawnPoints.Clear();
            drawnFingerStrokes.Clear();
            recordedBuiltInId = null;
            drawCanvas.Children.Clear();

            if (g is null) return;

            if (string.Equals(g.Id, "builtin-tap", StringComparison.Ordinal))
            {
                if (rule.PreviewTapPoints is { Count: > 0 } tapPts)
                {
                    var paths = tapPts.Select(p => (IReadOnlyList<GesturePoint>)[p]).ToArray();
                    RenderTapDots(paths, tapPts.Count);
                }
                else
                {
                    RenderTapDots([], rule.FingerCount > 0 ? rule.FingerCount : 3);
                }
                return;
            }

            if (g.FingerStrokes is { Count: > 1 })
            {
                for (int f = 0; f < g.FingerStrokes.Count; f++)
                {
                    var poly = CreateFingerPolyline(f);
                    var smoothed = StrokeSmoother.Smooth(g.FingerStrokes[f], denoisePasses: 2, chaikinIterations: 2);
                    var ptsList = new List<Point>(smoothed.Length);
                    foreach (var p in smoothed)
                    {
                        var pt = new Point(p.X, p.Y);
                        poly.Points.Add(pt);
                        ptsList.Add(pt);
                    }
                    drawCanvas.Children.Add(poly);
                    AddArrowheadToCanvas(ptsList, f);
                }
                return;
            }

            mousePolyline = CreateFingerPolyline(0);
            var singlePts = new List<Point>();
            const double prevSize = 125;
            foreach (var pt in GesturePreviewHelper.ToPreviewPoints(g.Points, prevSize, 14))
            {
                var shifted = new Point(pt.X + (padWidth - prevSize) / 2.0, pt.Y + (padHeight - prevSize) / 2.0);
                mousePolyline.Points.Add(shifted);
                singlePts.Add(shifted);
            }
            drawCanvas.Children.Add(mousePolyline);
            AddArrowheadToCanvas(singlePts, 0);
        }

        if (!isNew)
        {
            ShowPresetPreview(existingGesture);
        }

        void RenderTapDots(IReadOnlyList<IReadOnlyList<GesturePoint>> fingerPaths, int fingerCount)
        {
            drawCanvas.Children.Clear();
            int count = fingerPaths.Count > 0 ? fingerPaths.Count : fingerCount;
            for (int f = 0; f < count; f++)
            {
                double x = f < fingerPaths.Count && fingerPaths[f].Count > 0
                    ? 20 + fingerPaths[f][0].X * (padWidth - 40)
                    : (padWidth / (count + 1)) * (f + 1);
                double y = f < fingerPaths.Count && fingerPaths[f].Count > 0
                    ? 20 + fingerPaths[f][0].Y * (padHeight - 40)
                    : padHeight / 2.0;

                var dot = new Ellipse
                {
                    Width = 18,
                    Height = 18,
                    Fill = new SolidColorBrush(FingerColors[f % FingerColors.Length])
                };
                Canvas.SetLeft(dot, x - 9);
                Canvas.SetTop(dot, y - 9);
                drawCanvas.Children.Add(dot);
            }
        }

        void RestoreCurrentPrimaryOnCanvas()
        {
            drawCanvas.Children.Clear();
            if (!string.IsNullOrEmpty(recordedBuiltInId))
            {
                if (recordedBuiltInId == "builtin-tap")
                {
                    var paths = recordedTapPoints.Select(p => (IReadOnlyList<GesturePoint>)[p]).ToArray();
                    RenderTapDots(paths, detectedFingers > 0 ? detectedFingers : 3);
                }
                else
                {
                    var builtIn = App.Engine.Profile.Gestures.FirstOrDefault(g => g.Id == recordedBuiltInId);
                    if (builtIn is not null) ShowPresetPreview(builtIn);
                }
                return;
            }

            if (drawnFingerStrokes.Count > 1)
            {
                for (int f = 0; f < drawnFingerStrokes.Count; f++)
                {
                    var poly = CreateFingerPolyline(f);
                    var smoothed = StrokeSmoother.Smooth(drawnFingerStrokes[f], denoisePasses: 2, chaikinIterations: 2);
                    var ptsList = new List<Point>(smoothed.Length);
                    foreach (var p in smoothed)
                    {
                        var pt = new Point(p.X, p.Y);
                        poly.Points.Add(pt);
                        ptsList.Add(pt);
                    }
                    drawCanvas.Children.Add(poly);
                    AddArrowheadToCanvas(ptsList, f);
                }
                return;
            }

            if (drawnPoints.Count >= 2)
            {
                mousePolyline = CreateFingerPolyline(0);
                var smoothedMouse = StrokeSmoother.Smooth(drawnPoints, denoisePasses: 3, chaikinIterations: 2);
                var ptsList = new List<Point>(smoothedMouse.Length);
                foreach (var sp in smoothedMouse)
                {
                    var pt = new Point(sp.X, sp.Y);
                    mousePolyline.Points.Add(pt);
                    ptsList.Add(pt);
                }
                drawCanvas.Children.Add(mousePolyline);
                AddArrowheadToCanvas(ptsList, 0);
                return;
            }

            if (!isNew && existingGesture is not null)
            {
                ShowPresetPreview(existingGesture);
            }
        }

        Action? refreshSequenceList = null;
        Action<bool, bool>? setPadArmed = null;

        drawPadBorder.PointerPressed += (_, e) =>
        {
            if (!isPadArmed)
            {
                setPadArmed?.Invoke(true, false);
                drawStatusText.Text = "Pad active — swipe touchpad (2–5F) or drag with mouse.";
            }

            if ((DateTimeOffset.UtcNow - lastTouchpadEvent).TotalMilliseconds < 400)
            {
                return;
            }

            drawPadBorder.CapturePointer(e.Pointer);
            isDrawing = true;
            activeMousePoints.Clear();
            drawCanvas.Children.Clear();
            mousePolyline = CreateFingerPolyline(0);
            drawCanvas.Children.Add(mousePolyline);

            var pt = e.GetCurrentPoint(drawCanvas).Position;
            activeMousePoints.Add(new GesturePoint(pt.X, pt.Y));
            mousePolyline.Points.Add(pt);
        };

        drawPadBorder.PointerMoved += (_, e) =>
        {
            if (!isDrawing) return;
            var pt = e.GetCurrentPoint(drawCanvas).Position;
            if (activeMousePoints.Count == 0 ||
                Math.Abs(activeMousePoints[^1].X - pt.X) >= 2 ||
                Math.Abs(activeMousePoints[^1].Y - pt.Y) >= 2)
            {
                activeMousePoints.Add(new GesturePoint(pt.X, pt.Y));
                mousePolyline.Points.Add(pt);
            }
        };

        drawPadBorder.PointerReleased += (_, e) =>
        {
            if (!isDrawing) return;
            isDrawing = false;
            drawPadBorder.ReleasePointerCapture(e.Pointer);

            if ((DateTimeOffset.UtcNow - lastTouchpadEvent).TotalMilliseconds < 400)
            {
                return;
            }

            // A simple click without dragging arms the pad for touchpad or mouse input.
            if (activeMousePoints.Count < 4 || GestureRecognizer.ComputePathLength(activeMousePoints) < 16)
            {
                activeMousePoints.Clear();
                RestoreCurrentPrimaryOnCanvas();
                drawStatusText.Text = isRecordingFollowUp
                    ? "Pad active for Follow-Up — swipe touchpad (2–5F) or drag with mouse."
                    : "Pad active — swipe touchpad (2–5F) or drag with mouse.";
                return;
            }

            if (isRecordingFollowUp)
            {
                var custom = new GestureDefinition
                {
                    Name = $"Follow-Up #{followUpSteps.Count + 2}",
                    IsBuiltIn = false,
                    Points = [.. GestureRecognizer.Resample(activeMousePoints, 32)]
                };
                pendingCustomFollowUpGestures.Add(custom);
                followUpSteps.Add(new GestureSequenceStep
                {
                    GestureId = custom.Id,
                    InputSource = GestureInputSource.Mouse,
                    FingerCount = 0
                });
                activeMousePoints.Clear();
                setPadArmed?.Invoke(false, false);
                RestoreCurrentPrimaryOnCanvas();
                refreshSequenceList?.Invoke();
                drawStatusText.Text = $"Added follow-up mouse gesture (#{followUpSteps.Count + 1}).";
                return;
            }

            drawnPoints.Clear();
            drawnPoints.AddRange(activeMousePoints);
            activeMousePoints.Clear();
            drawnFingerStrokes.Clear();
            recordedBuiltInId = null;

            detectedSource = GestureInputSource.Mouse;
            detectedFingers = 0;
            UpdateDetectedBadge();

            var smoothedMouse = StrokeSmoother.Smooth(drawnPoints, denoisePasses: 3, chaikinIterations: 2);
            mousePolyline.Points.Clear();
            var mousePtsList = new List<Point>(smoothedMouse.Length);
            foreach (var sp in smoothedMouse)
            {
                var pt = new Point(sp.X, sp.Y);
                mousePolyline.Points.Add(pt);
                mousePtsList.Add(pt);
            }
            AddArrowheadToCanvas(mousePtsList, 0);

            var match = App.Engine.TestRecognize(drawnPoints);
            drawStatusText.Text = match.Gesture is not null
                ? $"Mouse stroke recorded (matches {match.Gesture.Name} at {match.Score:P0})."
                : $"Custom mouse stroke recorded ({drawnPoints.Count} points).";
            setPadArmed?.Invoke(false, false);
            refreshSequenceList?.Invoke();
        };

        var clearPadBtn = new Button { Content = "Clear Pad" };
        clearPadBtn.Click += (_, _) =>
        {
            drawnPoints.Clear();
            drawnFingerStrokes.Clear();
            recordedTapPoints.Clear();
            activeMousePoints.Clear();
            recordedBuiltInId = null;
            drawCanvas.Children.Clear();
            setPadArmed?.Invoke(false, false);
            refreshSequenceList?.Invoke();
            drawStatusText.Text = "Pad cleared. Click the pad above to record a gesture.";
        };

        var appBox = new TextBox
        {
            Header = "Target Apps (* = Global, or comma-separated)",
            Text = rule.ApplicationFilter,
            PlaceholderText = "* or msedge, chrome, code"
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
                appBox.Text = string.IsNullOrWhiteSpace(appBox.Text) || appBox.Text.Trim() == "*"
                    ? proc
                    : $"{appBox.Text.Trim()}, {proc}";
            }
        };

        // Action Category Dropdown + Subcategory Dropdown
        ActionCategoryInfo selectedCategory = ActionCategories.FirstOrDefault(c =>
            c.PrimaryType == rule.ActionType ||
            (c.PrimaryType == ActionType.RunProgram && rule.ActionType == ActionType.RunPowerShell))
            ?? ActionCategories[0];

        var categoryCombo = new ComboBox
        {
            Header = "Action Category",
            HorizontalAlignment = HorizontalAlignment.Stretch,
            ItemsSource = ActionCategories,
            DisplayMemberPath = "Tooltip"
        };

        var subcategoryCombo = new ComboBox
        {
            Header = "Action",
            HorizontalAlignment = HorizontalAlignment.Stretch,
            DisplayMemberPath = "Label"
        };

        var payloadBox = new TextBox
        {
            Header = "Custom Value",
            Text = rule.ActionPayload
        };

        // Interactive Hotkey Recorder
        bool isRecordingHotkey = false;
        var recordHotkeyBtn = new Button
        {
            Content = "Record Keys",
            Margin = new Thickness(0, 24, 0, 0),
            VerticalAlignment = VerticalAlignment.Top
        };
        recordHotkeyBtn.Click += (_, _) =>
        {
            isRecordingHotkey = !isRecordingHotkey;
            recordHotkeyBtn.Content = isRecordingHotkey ? "Press Keys..." : "Record Keys";
            if (isRecordingHotkey) payloadBox.Focus(FocusState.Programmatic);
        };
        payloadBox.PreviewKeyDown += (_, e) =>
        {
            if (!isRecordingHotkey) return;
            var key = e.Key;
            if (key is Windows.System.VirtualKey.Control or Windows.System.VirtualKey.LeftControl or Windows.System.VirtualKey.RightControl or
                Windows.System.VirtualKey.Menu or Windows.System.VirtualKey.LeftMenu or Windows.System.VirtualKey.RightMenu or
                Windows.System.VirtualKey.Shift or Windows.System.VirtualKey.LeftShift or Windows.System.VirtualKey.RightShift or
                Windows.System.VirtualKey.LeftWindows or Windows.System.VirtualKey.RightWindows)
            {
                e.Handled = true;
                return;
            }

            var mods = new List<string>();
            var state = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread;
            if (state(Windows.System.VirtualKey.Control).HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down)) mods.Add("Ctrl");
            if (state(Windows.System.VirtualKey.Menu).HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down)) mods.Add("Alt");
            if (state(Windows.System.VirtualKey.Shift).HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down)) mods.Add("Shift");
            if (state(Windows.System.VirtualKey.LeftWindows).HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down)) mods.Add("Win");
            mods.Add(key.ToString());

            payloadBox.Text = string.Join("+", mods);
            isRecordingHotkey = false;
            recordHotkeyBtn.Content = "Record Keys";
            e.Handled = true;
        };

        var payloadRow = new Grid { ColumnSpacing = 8 };
        payloadRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        payloadRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(payloadBox, 0);
        Grid.SetColumn(recordHotkeyBtn, 1);
        payloadRow.Children.Add(payloadBox);
        payloadRow.Children.Add(recordHotkeyBtn);

        void SelectCategory(ActionCategoryInfo cat, string? initialPayload = null)
        {
            selectedCategory = cat;
            subcategoryCombo.ItemsSource = cat.Presets;

            ActionPreset? matchedPreset = null;
            if (!string.IsNullOrEmpty(initialPayload))
            {
                matchedPreset = cat.Presets.FirstOrDefault(p =>
                    string.Equals(p.Payload, initialPayload, StringComparison.OrdinalIgnoreCase) &&
                    (p.OverrideType is null || p.OverrideType == rule.ActionType));
            }
            else if (cat.PrimaryType == ActionType.RunProgram && rule.ActionType == ActionType.RunPowerShell)
            {
                matchedPreset = cat.Presets.FirstOrDefault(p => p.OverrideType == ActionType.RunPowerShell);
            }

            subcategoryCombo.SelectedItem = matchedPreset
                ?? cat.Presets.FirstOrDefault(p => string.IsNullOrEmpty(p.Payload))
                ?? cat.Presets[0];

            payloadBox.PlaceholderText = cat.CustomPlaceholder;
            if (initialPayload is not null)
            {
                payloadBox.Text = initialPayload;
            }
        }

        subcategoryCombo.SelectionChanged += (_, _) =>
        {
            if (subcategoryCombo.SelectedItem is not ActionPreset preset) return;

            bool needsCustom = selectedCategory.AllowCustomPayload && string.IsNullOrEmpty(preset.Payload);
            payloadRow.Visibility = needsCustom ? Visibility.Visible : Visibility.Collapsed;
            recordHotkeyBtn.Visibility = selectedCategory.PrimaryType == ActionType.SendHotkey
                ? Visibility.Visible
                : Visibility.Collapsed;
            if (!needsCustom)
            {
                payloadBox.Text = preset.Payload;
            }
        };

        categoryCombo.SelectionChanged += (_, _) =>
        {
            if (categoryCombo.SelectedItem is ActionCategoryInfo cat)
            {
                SelectCategory(cat);
            }
        };

        categoryCombo.SelectedItem = selectedCategory;
        SelectCategory(selectedCategory, rule.ActionPayload);

        // Modifier Keys + Window Title Filter + Chained Macro Steps
        var ctrlCheck = new CheckBox { Content = "Ctrl", IsChecked = rule.RequiredModifiers.HasFlag(ModifierKeyFilter.Ctrl), MinWidth = 0 };
        var altCheck = new CheckBox { Content = "Alt", IsChecked = rule.RequiredModifiers.HasFlag(ModifierKeyFilter.Alt), MinWidth = 0 };
        var shiftCheck = new CheckBox { Content = "Shift", IsChecked = rule.RequiredModifiers.HasFlag(ModifierKeyFilter.Shift), MinWidth = 0 };
        var modRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 14 };
        modRow.Children.Add(new TextBlock { Text = "Require keys:", VerticalAlignment = VerticalAlignment.Center, Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"] });
        modRow.Children.Add(ctrlCheck);
        modRow.Children.Add(altCheck);
        modRow.Children.Add(shiftCheck);

        var titleFilterBox = new TextBox
        {
            Header = "Window Title Filter (optional)",
            Text = rule.WindowTitleFilter ?? string.Empty,
            PlaceholderText = "e.g. YouTube or regex:.*Docs.*"
        };

        var repeatBox = new NumberBox
        {
            Header = "Repeat (N×)",
            Value = Math.Clamp(rule.RequiredRepetitions, 1, 5),
            Minimum = 1,
            Maximum = 5,
            Width = 120,
            SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Inline
        };

        var addFollowUpBtn = new Button { Content = "+ Follow-Up Gesture" };

        setPadArmed = (armed, followUp) =>
        {
            isPadArmed = armed;
            isRecordingFollowUp = armed && followUp;
            drawPadBorder.BorderBrush = armed
                ? (Brush)Application.Current.Resources["AccentFillColorDefaultBrush"]
                : (Brush)Application.Current.Resources["ControlStrokeColorDefaultBrush"];
            drawPadBorder.BorderThickness = new Thickness(armed ? 2 : 1);
            padHintText.Text = armed
                ? (followUp
                    ? "● Recording Follow-Up — swipe touchpad or drag here"
                    : "● Recording Primary — swipe touchpad or drag here")
                : "Click pad to record";
            padHintText.Foreground = armed
                ? (Brush)Application.Current.Resources["AccentTextFillColorPrimaryBrush"]
                : (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"];
            addFollowUpBtn.Content = (armed && followUp) ? "Cancel Follow-Up" : "+ Follow-Up Gesture";
        };

        addFollowUpBtn.Click += (_, _) =>
        {
            if (isPadArmed && isRecordingFollowUp)
            {
                setPadArmed(false, false);
                RestoreCurrentPrimaryOnCanvas();
                drawStatusText.Text = "Follow-up recording cancelled.";
            }
            else
            {
                setPadArmed(true, true);
                drawCanvas.Children.Clear();
                drawStatusText.Text = "Pad active for Follow-Up — swipe touchpad (2–5F) or drag on the pad above.";
            }
        };

        Border CreateMiniGesturePreview(GestureDefinition? g, string? builtInId, int fingerCount)
        {
            var miniCanvas = new Canvas { Width = 36, Height = 36 };
            if (string.Equals(builtInId, "builtin-tap", StringComparison.Ordinal))
            {
                int dots = Math.Clamp(fingerCount > 0 ? fingerCount : 3, 1, 5);
                for (int f = 0; f < dots; f++)
                {
                    var dot = new Ellipse
                    {
                        Width = 6,
                        Height = 6,
                        Fill = new SolidColorBrush(FingerColors[f % FingerColors.Length])
                    };
                    Canvas.SetLeft(dot, 4 + (f + 1) * (28.0 / (dots + 1)) - 3);
                    Canvas.SetTop(dot, 15);
                    miniCanvas.Children.Add(dot);
                }
            }
            else if (g is not null)
            {
                var strokes = BuildPreviewStrokes(g);
                const double scale = 36.0 / 48.0;
                foreach (var s in strokes)
                {
                    var poly = new Polyline
                    {
                        Stroke = s.Stroke,
                        StrokeThickness = 2,
                        StrokeLineJoin = PenLineJoin.Round,
                        StrokeStartLineCap = PenLineCap.Round,
                        StrokeEndLineCap = PenLineCap.Round
                    };
                    foreach (var pt in s.Points) poly.Points.Add(new Point(pt.X * scale, pt.Y * scale));
                    miniCanvas.Children.Add(poly);
                    if (s.ArrowPoints.Count >= 3)
                    {
                        var arrow = new Polyline
                        {
                            Stroke = s.Stroke,
                            StrokeThickness = 1.6,
                            StrokeLineJoin = PenLineJoin.Round,
                            StrokeStartLineCap = PenLineCap.Round,
                            StrokeEndLineCap = PenLineCap.Round
                        };
                        foreach (var pt in s.ArrowPoints) arrow.Points.Add(new Point(pt.X * scale, pt.Y * scale));
                        miniCanvas.Children.Add(arrow);
                    }
                }
            }

            return new Border
            {
                Width = 38,
                Height = 38,
                CornerRadius = new CornerRadius(6),
                Background = (Brush)Application.Current.Resources["SubtleFillColorTertiaryBrush"],
                Child = miniCanvas
            };
        }

        var sequenceListPanel = new StackPanel { Spacing = 6 };
        refreshSequenceList = () =>
        {
            sequenceListPanel.Children.Clear();

            // #1: Primary Gesture preview item (if one is recorded or loaded)
            GestureDefinition? primaryPreviewDef = null;
            string? primaryBuiltIn = recordedBuiltInId;
            string primaryTitle = "Primary Gesture";

            if (!string.IsNullOrEmpty(recordedBuiltInId))
            {
                primaryPreviewDef = App.Engine.Profile.Gestures.FirstOrDefault(g => g.Id == recordedBuiltInId);
                primaryTitle = primaryPreviewDef?.Name ?? (recordedBuiltInId == "builtin-tap" ? "Tap" : "Built-in Gesture");
            }
            else if (drawnPoints.Count >= 2 || drawnFingerStrokes.Count > 1)
            {
                var match = drawnPoints.Count >= 2 ? App.Engine.TestRecognize(drawnPoints) : default;
                primaryTitle = match.Gesture?.Name ?? "Custom Gesture";
                primaryPreviewDef = new GestureDefinition
                {
                    Name = primaryTitle,
                    Points = [.. GestureRecognizer.Resample(drawnPoints, 32)],
                    FingerStrokes = drawnFingerStrokes.Count > 1 ? [.. drawnFingerStrokes] : null
                };
            }
            else if (!isNew && existingGesture is not null)
            {
                primaryPreviewDef = existingGesture;
                primaryBuiltIn = existingGesture.Id;
                primaryTitle = existingGesture.Name;
            }

            if (primaryPreviewDef is not null || !string.IsNullOrEmpty(primaryBuiltIn))
            {
                string srcDesc = detectedSource == GestureInputSource.Touchpad && detectedFingers > 0
                    ? $"{detectedFingers}F Touchpad"
                    : detectedSource == GestureInputSource.Mouse ? "Mouse Stroke" : detectedSource.ToString();

                var row = new Grid
                {
                    Padding = new Thickness(8, 6, 6, 6),
                    CornerRadius = new CornerRadius(6),
                    Background = (Brush)Application.Current.Resources["SubtleFillColorSecondaryBrush"],
                    ColumnSpacing = 10
                };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var preview = CreateMiniGesturePreview(primaryPreviewDef, primaryBuiltIn, detectedFingers);
                var infoStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Spacing = 1 };
                infoStack.Children.Add(new TextBlock
                {
                    Text = $"#1 Primary • {primaryTitle}",
                    Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                    TextTrimming = TextTrimming.CharacterEllipsis
                });
                infoStack.Children.Add(new TextBlock
                {
                    Text = srcDesc,
                    Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
                    Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"]
                });

                var rerecordBtn = new Button
                {
                    Content = new FontIcon { Glyph = "\uE72C", FontSize = 11 },
                    Padding = new Thickness(6, 4, 6, 4),
                    Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
                    BorderThickness = new Thickness(0)
                };
                ToolTipService.SetToolTip(rerecordBtn, "Re-record primary gesture");
                rerecordBtn.Click += (_, _) =>
                {
                    setPadArmed?.Invoke(true, false);
                    RestoreCurrentPrimaryOnCanvas();
                    drawStatusText.Text = "Pad active — swipe touchpad (2–5F) or drag with mouse.";
                };

                Grid.SetColumn(preview, 0);
                Grid.SetColumn(infoStack, 1);
                Grid.SetColumn(rerecordBtn, 2);
                row.Children.Add(preview);
                row.Children.Add(infoStack);
                row.Children.Add(rerecordBtn);
                sequenceListPanel.Children.Add(row);
            }

            // #2..N: Follow-up gestures
            for (int i = 0; i < followUpSteps.Count; i++)
            {
                int idx = i;
                var step = followUpSteps[i];
                var gDef = App.Engine.Profile.Gestures.FirstOrDefault(g => g.Id == step.GestureId)
                           ?? pendingCustomFollowUpGestures.FirstOrDefault(g => g.Id == step.GestureId);
                var gName = gDef?.Name ?? "Custom Gesture";
                string srcDesc = step.InputSource == GestureInputSource.Touchpad && step.FingerCount > 0
                    ? $"{step.FingerCount}F Touchpad"
                    : step.InputSource == GestureInputSource.Mouse ? "Mouse Stroke" : step.InputSource.ToString();

                var row = new Grid
                {
                    Padding = new Thickness(8, 6, 6, 6),
                    CornerRadius = new CornerRadius(6),
                    Background = (Brush)Application.Current.Resources["SubtleFillColorSecondaryBrush"],
                    ColumnSpacing = 10
                };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var preview = CreateMiniGesturePreview(gDef, step.GestureId, step.FingerCount);
                var infoStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Spacing = 1 };
                infoStack.Children.Add(new TextBlock
                {
                    Text = $"#{i + 2} Follow-Up • {gName}",
                    Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                    TextTrimming = TextTrimming.CharacterEllipsis
                });
                infoStack.Children.Add(new TextBlock
                {
                    Text = srcDesc,
                    Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
                    Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"]
                });

                var rmBtn = new Button
                {
                    Content = new FontIcon { Glyph = "\uE711", FontSize = 11 },
                    Padding = new Thickness(6, 4, 6, 4),
                    Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
                    BorderThickness = new Thickness(0)
                };
                rmBtn.Click += (_, _) =>
                {
                    followUpSteps.RemoveAt(idx);
                    refreshSequenceList?.Invoke();
                };

                Grid.SetColumn(preview, 0);
                Grid.SetColumn(infoStack, 1);
                Grid.SetColumn(rmBtn, 2);
                row.Children.Add(preview);
                row.Children.Add(infoStack);
                row.Children.Add(rmBtn);
                sequenceListPanel.Children.Add(row);
            }
        };
        refreshSequenceList();

        var macroSteps = rule.Steps is { Count: > 0 }
            ? rule.Steps.Select(s => new ActionStep { ActionType = s.ActionType, ActionPayload = s.ActionPayload, DelayBeforeMs = s.DelayBeforeMs }).ToList()
            : new List<ActionStep>();

        var chainCategoryCombo = new ComboBox
        {
            Header = "Add Macro Step",
            HorizontalAlignment = HorizontalAlignment.Stretch,
            PlaceholderText = "Select category to add a step...",
            ItemsSource = ActionCategories,
            DisplayMemberPath = "Tooltip",
            SelectedIndex = -1
        };

        var chainActionCombo = new ComboBox
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            PlaceholderText = "Select action...",
            DisplayMemberPath = "Label",
            IsEnabled = false
        };

        var chainCustomBox = new TextBox
        {
            PlaceholderText = "Custom value...",
            Visibility = Visibility.Collapsed
        };

        var chainDelayBox = new NumberBox
        {
            Header = "Delay (ms)",
            Value = 120,
            Minimum = 0,
            Maximum = 5000,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };

        var addStepBtn = new Button
        {
            Content = "+ Add Step",
            VerticalAlignment = VerticalAlignment.Bottom,
            Height = 32,
            IsEnabled = false
        };

        var clearChainBtn = new Button
        {
            Content = "Clear",
            VerticalAlignment = VerticalAlignment.Bottom,
            Height = 32,
            Visibility = Visibility.Collapsed
        };

        void RefreshChainActionCombo()
        {
            if (chainCategoryCombo.SelectedItem is not ActionCategoryInfo cat)
            {
                chainActionCombo.ItemsSource = null;
                chainActionCombo.IsEnabled = false;
                chainCustomBox.Visibility = Visibility.Collapsed;
                addStepBtn.IsEnabled = false;
                return;
            }
            chainActionCombo.ItemsSource = cat.Presets;
            chainActionCombo.IsEnabled = true;
            chainActionCombo.SelectedIndex = 0;
            chainCustomBox.PlaceholderText = cat.CustomPlaceholder;
            addStepBtn.IsEnabled = true;
        }

        chainCategoryCombo.SelectionChanged += (_, _) => RefreshChainActionCombo();
        chainActionCombo.SelectionChanged += (_, _) =>
        {
            if (chainCategoryCombo.SelectedItem is not ActionCategoryInfo cat ||
                chainActionCombo.SelectedItem is not ActionPreset preset) return;
            bool needsCustom = cat.AllowCustomPayload && string.IsNullOrEmpty(preset.Payload);
            chainCustomBox.Visibility = needsCustom ? Visibility.Visible : Visibility.Collapsed;
        };
        RefreshChainActionCombo();

        var macroStepsPanel = new StackPanel { Spacing = 4 };
        var macroEmptyText = new TextBlock
        {
            Text = "No chained steps — uses the single action from Step 2.",
            Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
            Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"]
        };

        void RefreshMacroList()
        {
            macroStepsPanel.Children.Clear();
            macroEmptyText.Visibility = macroSteps.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            clearChainBtn.Visibility = macroSteps.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

            for (int i = 0; i < macroSteps.Count; i++)
            {
                int idx = i;
                var step = macroSteps[i];
                var row = new Grid
                {
                    Padding = new Thickness(8, 4, 4, 4),
                    CornerRadius = new CornerRadius(6),
                    Background = (Brush)Application.Current.Resources["SubtleFillColorSecondaryBrush"],
                    ColumnSpacing = 8
                };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                string delayLabel = step.DelayBeforeMs > 0 ? $" (+{step.DelayBeforeMs}ms)" : "";
                var label = new TextBlock
                {
                    Text = $"{i + 1}. {step.ActionType}: {step.ActionPayload}{delayLabel}",
                    Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
                    VerticalAlignment = VerticalAlignment.Center,
                    TextTrimming = TextTrimming.CharacterEllipsis
                };
                var removeBtn = new Button
                {
                    Content = new FontIcon { Glyph = "\uE711", FontSize = 11 },
                    Padding = new Thickness(6, 4, 6, 4),
                    Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
                    BorderThickness = new Thickness(0)
                };
                removeBtn.Click += (_, _) =>
                {
                    macroSteps.RemoveAt(idx);
                    RefreshMacroList();
                };

                Grid.SetColumn(label, 0);
                Grid.SetColumn(removeBtn, 1);
                row.Children.Add(label);
                row.Children.Add(removeBtn);
                macroStepsPanel.Children.Add(row);
            }
        }
        RefreshMacroList();

        addStepBtn.Click += (_, _) =>
        {
            if (chainCategoryCombo.SelectedItem is not ActionCategoryInfo cat) return;
            var preset = chainActionCombo.SelectedItem as ActionPreset;
            var stepType = preset?.OverrideType ?? cat.PrimaryType;
            string stepPayload = !string.IsNullOrEmpty(preset?.Payload)
                ? preset.Payload
                : (chainCustomBox.Text?.Trim() ?? string.Empty);

            int delay = double.IsNaN(chainDelayBox.Value) ? 0 : (int)Math.Max(0, chainDelayBox.Value);
            macroSteps.Add(new ActionStep
            {
                ActionType = stepType,
                ActionPayload = stepPayload,
                DelayBeforeMs = macroSteps.Count == 0 ? 0 : delay
            });
            chainCustomBox.Text = string.Empty;
            chainCategoryCombo.SelectedIndex = -1;
            RefreshMacroList();
        };

        clearChainBtn.Click += (_, _) =>
        {
            macroSteps.Clear();
            chainCategoryCombo.SelectedIndex = -1;
            RefreshMacroList();
        };

        var addRow = new Grid { ColumnSpacing = 8 };
        addRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        addRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        addRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(chainDelayBox, 0);
        Grid.SetColumn(addStepBtn, 1);
        Grid.SetColumn(clearChainBtn, 2);
        addRow.Children.Add(chainDelayBox);
        addRow.Children.Add(addStepBtn);
        addRow.Children.Add(clearChainBtn);

        var macroStepsScroll = new ScrollViewer
        {
            MaxHeight = 110,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = macroStepsPanel
        };

        addFollowUpBtn.HorizontalAlignment = HorizontalAlignment.Stretch;
        addFollowUpBtn.VerticalAlignment = VerticalAlignment.Bottom;
        var triggerOptionsRow = new Grid { ColumnSpacing = 12 };
        triggerOptionsRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        triggerOptionsRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(addFollowUpBtn, 0);
        Grid.SetColumn(repeatBox, 1);
        triggerOptionsRow.Children.Add(addFollowUpBtn);
        triggerOptionsRow.Children.Add(repeatBox);

        // Step 1: Trigger Type Selector + Gesture Pad + Keyboard Chord Recorder
        var workingChord = rule.KeyboardChord is not null
            ? new KeyboardChordDefinition
            {
                Mode = rule.KeyboardChord.Mode,
                TimeoutMs = rule.KeyboardChord.TimeoutMs,
                SuppressKeys = rule.KeyboardChord.SuppressKeys,
                Steps = rule.KeyboardChord.Steps.Select(s => new ChordStep
                {
                    Modifiers = s.Modifiers,
                    KeyCodes = [.. s.KeyCodes]
                }).ToList()
            }
            : new KeyboardChordDefinition();

        var selectedTriggerType = rule.TriggerType;
        bool isRecordingChord = false;
        var simRecordedKeys = new List<int>();
        var simHeldKeys = new HashSet<int>();
        var simRecordedMods = ChordModifiers.None;

        var triggerTypeCombo = new ComboBox
        {
            Header = "Trigger Type",
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        var gestureTriggerItem = new ComboBoxItem { Content = "Mouse / Touchpad Gesture", Tag = TriggerType.Gesture };
        var chordTriggerItem = new ComboBoxItem { Content = "Keyboard Chord", Tag = TriggerType.KeyboardChord };
        var knockTriggerItem = new ComboBoxItem { Content = "Knock Pattern (Acoustic)", Tag = TriggerType.KnockPattern };
        triggerTypeCombo.Items.Add(gestureTriggerItem);
        triggerTypeCombo.Items.Add(chordTriggerItem);
        triggerTypeCombo.Items.Add(knockTriggerItem);
        triggerTypeCombo.SelectedItem = selectedTriggerType switch
        {
            TriggerType.KeyboardChord => chordTriggerItem,
            TriggerType.KnockPattern => knockTriggerItem,
            _ => gestureTriggerItem
        };

        var gestureTriggerPanel = new StackPanel { Spacing = 8 };
        gestureTriggerPanel.Children.Add(drawPadBorder);
        gestureTriggerPanel.Children.Add(new Grid
        {
            Children =
            {
                detectedBadge,
                new StackPanel { HorizontalAlignment = HorizontalAlignment.Right, Children = { clearPadBtn } }
            }
        });
        gestureTriggerPanel.Children.Add(drawStatusText);
        gestureTriggerPanel.Children.Add(sequenceListPanel);
        gestureTriggerPanel.Children.Add(triggerOptionsRow);

        var chordModeCombo = new ComboBox
        {
            Header = "Chord Mode",
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        var simModeItem = new ComboBoxItem { Content = "Simultaneous (e.g. Ctrl+J+K)", Tag = KeyboardChordMode.Simultaneous };
        var seqModeItem = new ComboBoxItem { Content = "Sequential (e.g. Ctrl+Z → K → L)", Tag = KeyboardChordMode.Sequential };
        chordModeCombo.Items.Add(simModeItem);
        chordModeCombo.Items.Add(seqModeItem);
        chordModeCombo.SelectedItem = workingChord.Mode == KeyboardChordMode.Sequential ? seqModeItem : simModeItem;

        var chordPreviewText = new TextBlock
        {
            Text = ChordKeyHelper.FormatChord(workingChord),
            Style = (Style)Application.Current.Resources["SubtitleTextBlockStyle"],
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.Wrap
        };
        var chordPreviewBorder = new Border
        {
            Height = 76,
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(12),
            Background = (Brush)Application.Current.Resources["SubtleFillColorSecondaryBrush"],
            BorderBrush = (Brush)Application.Current.Resources["ControlStrokeColorDefaultBrush"],
            BorderThickness = new Thickness(1),
            Child = chordPreviewText
        };

        var chordStatusText = new TextBlock
        {
            Text = workingChord.Mode == KeyboardChordMode.Sequential
                ? "Click Record Chord, start with Modifier+Key (e.g. Ctrl+Z), press follow-up keys, then click Done Recording."
                : "Click Record Chord and press Modifier + chord keys together (e.g. Ctrl+J+K).",
            Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
            Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
            TextWrapping = TextWrapping.Wrap
        };

        var recordChordBtn = new Button
        {
            Content = "Record Chord",
            Style = (Style)Application.Current.Resources["AccentButtonStyle"],
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        var clearChordBtn = new Button { Content = "Clear" };
        var suppressKeysCheck = new CheckBox
        {
            Content = "Suppress matched keystrokes from active app",
            IsChecked = workingChord.SuppressKeys
        };

        void RefreshChordPreview()
        {
            chordPreviewText.Text = ChordKeyHelper.FormatChord(workingChord);
        }

        void StopChordRecording()
        {
            if (!isRecordingChord) return;
            isRecordingChord = false;
            App.Engine.EndChordRecording();
            recordChordBtn.Content = "Record Chord";
            chordPreviewBorder.BorderBrush = (Brush)Application.Current.Resources["ControlStrokeColorDefaultBrush"];
            chordPreviewBorder.BorderThickness = new Thickness(1);
            chordStatusText.Text = workingChord.Steps.Count > 0
                ? $"Recorded: {ChordKeyHelper.FormatChord(workingChord)}"
                : "Click Record Chord to capture keys.";
        }

        recordChordBtn.Click += (_, _) =>
        {
            if (isRecordingChord)
            {
                StopChordRecording();
                return;
            }

            isRecordingChord = true;
            simRecordedKeys.Clear();
            simHeldKeys.Clear();
            simRecordedMods = ChordModifiers.None;
            workingChord.Steps.Clear();
            RefreshChordPreview();

            recordChordBtn.Content = "Done Recording";
            chordPreviewBorder.BorderBrush = (Brush)Application.Current.Resources["AccentFillColorDefaultBrush"];
            chordPreviewBorder.BorderThickness = new Thickness(2);
            chordStatusText.Text = workingChord.Mode == KeyboardChordMode.Sequential
                ? "● Recording Sequential — start with Modifier+Key (e.g. Ctrl+Z), then follow-up keys."
                : "● Recording Simultaneous — hold Modifier (Ctrl/Alt/Shift/Win) + chord keys together, then release.";

            var recordedMods = ChordModifiers.None;
            App.Engine.BeginChordRecording((vk, isDown, _) =>
            {
                var modFlag = ChordKeyHelper.ToModifierFlag(vk);
                if (modFlag != ChordModifiers.None)
                {
                    if (isDown) recordedMods |= modFlag;
                    else recordedMods &= ~modFlag;
                    return true;
                }

                var mods = recordedMods;

                if (workingChord.Mode == KeyboardChordMode.Simultaneous)
                {
                    if (isDown)
                    {
                        if (simRecordedKeys.Count == 0 && mods == ChordModifiers.None)
                        {
                            DispatcherQueue.TryEnqueue(() =>
                                chordStatusText.Text = "⚠ Start with a modifier key (Ctrl, Alt, Shift, or Win) + key.");
                            return true;
                        }

                        simHeldKeys.Add(vk);
                        if (!simRecordedKeys.Contains(vk))
                        {
                            simRecordedKeys.Add(vk);
                        }
                        simRecordedMods |= mods;
                        workingChord.Steps =
                        [
                            new ChordStep { Modifiers = simRecordedMods, KeyCodes = [.. simRecordedKeys] }
                        ];
                        DispatcherQueue.TryEnqueue(RefreshChordPreview);
                    }
                    else
                    {
                        simHeldKeys.Remove(vk);
                        if (simHeldKeys.Count == 0 && simRecordedKeys.Count > 0)
                        {
                            DispatcherQueue.TryEnqueue(StopChordRecording);
                        }
                    }
                }
                else
                {
                    if (isDown)
                    {
                        if (workingChord.Steps.Count == 0 && mods == ChordModifiers.None)
                        {
                            DispatcherQueue.TryEnqueue(() =>
                                chordStatusText.Text = "⚠ Step 1 must include a modifier key (Ctrl, Alt, Shift, or Win).");
                            return true;
                        }

                        workingChord.Steps.Add(new ChordStep { Modifiers = mods, KeyCodes = [vk] });
                        DispatcherQueue.TryEnqueue(() =>
                        {
                            RefreshChordPreview();
                            chordStatusText.Text = $"● Step {workingChord.Steps.Count} recorded — press next key or click Done Recording.";
                        });
                    }
                }

                return true;
            });
        };

        clearChordBtn.Click += (_, _) =>
        {
            StopChordRecording();
            workingChord.Steps.Clear();
            RefreshChordPreview();
        };

        chordModeCombo.SelectionChanged += (_, _) =>
        {
            StopChordRecording();
            if ((chordModeCombo.SelectedItem as ComboBoxItem)?.Tag is KeyboardChordMode mode)
            {
                workingChord.Mode = mode;
                workingChord.Steps.Clear();
                RefreshChordPreview();
                chordStatusText.Text = mode == KeyboardChordMode.Sequential
                    ? "Click Record Chord, press each step in order, then click Done Recording."
                    : "Click Record Chord and press the chord keys together.";
            }
        };

        var chordBtnRow = new Grid { ColumnSpacing = 8 };
        chordBtnRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        chordBtnRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(recordChordBtn, 0);
        Grid.SetColumn(clearChordBtn, 1);
        chordBtnRow.Children.Add(recordChordBtn);
        chordBtnRow.Children.Add(clearChordBtn);

        var chordTriggerPanel = new StackPanel { Spacing = 10 };
        chordTriggerPanel.Children.Add(chordModeCombo);
        chordTriggerPanel.Children.Add(chordPreviewBorder);
        chordTriggerPanel.Children.Add(chordBtnRow);
        chordTriggerPanel.Children.Add(chordStatusText);
        chordTriggerPanel.Children.Add(suppressKeysCheck);

        // Knock Pattern Panel
        var workingKnock = rule.KnockPattern is not null
            ? new KnockPatternDefinition
            {
                KnockCount = rule.KnockPattern.KnockCount,
                SurfaceProfileId = rule.KnockPattern.SurfaceProfileId,
                SurfaceName = rule.KnockPattern.SurfaceName,
                RhythmRatios = [.. rule.KnockPattern.RhythmRatios]
            }
            : new KnockPatternDefinition();

        bool isRecordingKnock = false;
        var recordedKnocks = new List<KnockOnsetEvent>();

        var knockSurfaceCombo = new ComboBox
        {
            Header = "Target Surface",
            HorizontalAlignment = HorizontalAlignment.Stretch
        };

        void RefreshKnockSurfaceCombo(string? selectId = null)
        {
            knockSurfaceCombo.Items.Clear();
            var anyItem = new ComboBoxItem { Content = "Any Surface", Tag = "" };
            knockSurfaceCombo.Items.Add(anyItem);
            ComboBoxItem toSelect = anyItem;
            string targetId = selectId ?? workingKnock.SurfaceProfileId ?? "";

            foreach (var surf in App.Engine.Profile.Settings.KnockSurfaces)
            {
                var item = new ComboBoxItem
                {
                    Content = $"{surf.Name} ({surf.SampleFeatures.Count} samples)",
                    Tag = surf.Id
                };
                knockSurfaceCombo.Items.Add(item);
                if (string.Equals(surf.Id, targetId, StringComparison.Ordinal))
                {
                    toSelect = item;
                }
            }
            knockSurfaceCombo.SelectedItem = toSelect;
        }
        RefreshKnockSurfaceCombo();

        knockSurfaceCombo.SelectionChanged += (_, _) =>
        {
            if (knockSurfaceCombo.SelectedItem is ComboBoxItem item)
            {
                string id = item.Tag?.ToString() ?? "";
                workingKnock.SurfaceProfileId = string.IsNullOrEmpty(id) ? null : id;
                var surf = App.Engine.Profile.Settings.KnockSurfaces.FirstOrDefault(s => s.Id == id);
                workingKnock.SurfaceName = surf?.Name ?? "Any Surface";
            }
        };

        var knockCountBox = new NumberBox
        {
            Header = "Knock Count",
            Minimum = 1,
            Maximum = 6,
            Value = workingKnock.KnockCount,
            SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        var knockTimelineCanvas = new Canvas
        {
            Width = padWidth - 24,
            Height = 76
        };
        var knockTimelineBorder = new Border
        {
            Height = 92,
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(12, 8, 12, 8),
            Background = (Brush)Application.Current.Resources["SubtleFillColorSecondaryBrush"],
            BorderBrush = (Brush)Application.Current.Resources["ControlStrokeColorDefaultBrush"],
            BorderThickness = new Thickness(1),
            Child = knockTimelineCanvas
        };

        void RenderKnockTimeline(IReadOnlyList<KnockOnsetEvent>? liveEvents = null, int? calProgress = null)
        {
            knockTimelineCanvas.Children.Clear();
            double w = padWidth - 24;
            double midY = 44;

            var accentBrush = (Brush)Application.Current.Resources["AccentFillColorDefaultBrush"];
            var secondaryBrush = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"];
            var dividerBrush = (Brush)Application.Current.Resources["DividerStrokeColorDefaultBrush"];

            // Baseline track
            var track = new Line
            {
                X1 = 16,
                Y1 = midY,
                X2 = w - 16,
                Y2 = midY,
                Stroke = dividerBrush,
                StrokeThickness = 2
            };
            knockTimelineCanvas.Children.Add(track);

            if (calProgress.HasValue)
            {
                int done = calProgress.Value;
                for (int i = 0; i < 5; i++)
                {
                    double x = 28 + i * ((w - 56) / 4.0);
                    bool filled = i < done;
                    var dot = new Ellipse
                    {
                        Width = filled ? 18 : 14,
                        Height = filled ? 18 : 14,
                        Fill = filled ? accentBrush : new SolidColorBrush(Microsoft.UI.Colors.Transparent),
                        Stroke = filled ? accentBrush : secondaryBrush,
                        StrokeThickness = 2
                    };
                    Canvas.SetLeft(dot, x - dot.Width / 2);
                    Canvas.SetTop(dot, midY - dot.Height / 2);
                    knockTimelineCanvas.Children.Add(dot);
                }
                return;
            }

            if (liveEvents is { Count: > 0 })
            {
                long t0 = liveEvents[0].TimestampMs;
                long span = Math.Max(1, liveEvents[^1].TimestampMs - t0);
                double usableW = w - 48;

                double prevX = 0;
                for (int i = 0; i < liveEvents.Count; i++)
                {
                    double normX = liveEvents.Count == 1 ? 0.5 : (double)(liveEvents[i].TimestampMs - t0) / span;
                    double x = 24 + normX * usableW;
                    double barH = Math.Clamp(liveEvents[i].PeakAmplitude * 75.0, 12, 34);

                    if (i > 0)
                    {
                        long dt = liveEvents[i].TimestampMs - liveEvents[i - 1].TimestampMs;
                        var dtLabel = new TextBlock
                        {
                            Text = $"{dt}ms",
                            FontSize = 10,
                            Foreground = secondaryBrush
                        };
                        Canvas.SetLeft(dtLabel, (prevX + x) / 2 - 14);
                        Canvas.SetTop(dtLabel, midY + 12);
                        knockTimelineCanvas.Children.Add(dtLabel);
                    }

                    var bar = new Border
                    {
                        Width = 6,
                        Height = barH,
                        CornerRadius = new CornerRadius(3),
                        Background = accentBrush
                    };
                    Canvas.SetLeft(bar, x - 3);
                    Canvas.SetTop(bar, midY - barH);
                    knockTimelineCanvas.Children.Add(bar);

                    var ring = new Ellipse
                    {
                        Width = 14,
                        Height = 14,
                        Fill = accentBrush
                    };
                    Canvas.SetLeft(ring, x - 7);
                    Canvas.SetTop(ring, midY - 7);
                    knockTimelineCanvas.Children.Add(ring);

                    prevX = x;
                }
                return;
            }

            // Static preview from workingKnock (KnockCount + optional RhythmRatios)
            int count = Math.Clamp(workingKnock.KnockCount, 1, 6);
            double[] positions = new double[count];
            if (count == 1)
            {
                positions[0] = 0.5;
            }
            else if (workingKnock.RhythmRatios.Count == count - 1)
            {
                double totalRatio = workingKnock.RhythmRatios.Sum();
                double acc = 0;
                positions[0] = 0;
                for (int i = 0; i < workingKnock.RhythmRatios.Count; i++)
                {
                    acc += workingKnock.RhythmRatios[i];
                    positions[i + 1] = acc / Math.Max(0.1, totalRatio);
                }
            }
            else
            {
                for (int i = 0; i < count; i++) positions[i] = (double)i / (count - 1);
            }

            for (int i = 0; i < count; i++)
            {
                double x = 28 + positions[i] * (w - 56);
                var bar = new Border
                {
                    Width = 6,
                    Height = 22,
                    CornerRadius = new CornerRadius(3),
                    Background = accentBrush,
                    Opacity = 0.75
                };
                Canvas.SetLeft(bar, x - 3);
                Canvas.SetTop(bar, midY - 22);
                knockTimelineCanvas.Children.Add(bar);

                var dot = new Ellipse
                {
                    Width = 14,
                    Height = 14,
                    Fill = accentBrush
                };
                Canvas.SetLeft(dot, x - 7);
                Canvas.SetTop(dot, midY - 7);
                knockTimelineCanvas.Children.Add(dot);

                var num = new TextBlock
                {
                    Text = $"#{i + 1}",
                    FontSize = 10,
                    Foreground = secondaryBrush
                };
                Canvas.SetLeft(num, x - 6);
                Canvas.SetTop(num, midY + 11);
                knockTimelineCanvas.Children.Add(num);
            }
        }
        RenderKnockTimeline();

        knockCountBox.ValueChanged += (_, args) =>
        {
            if (!double.IsNaN(args.NewValue))
            {
                workingKnock.KnockCount = Math.Clamp((int)args.NewValue, 1, 6);
                if (!isRecordingKnock) RenderKnockTimeline();
            }
        };

        var useRhythmCheck = new CheckBox
        {
            Content = "Match recorded rhythm cadence (3+ knocks)",
            IsChecked = workingKnock.RhythmRatios.Count > 0
        };

        var knockStatusText = new TextBlock
        {
            Text = "Calibrate surfaces (Touchpad, Lid, Desk) or record a knock pattern.",
            Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
            Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
            TextWrapping = TextWrapping.Wrap
        };

        var recordKnockBtn = new Button
        {
            Content = "Record Pattern",
            Style = (Style)Application.Current.Resources["AccentButtonStyle"],
            HorizontalAlignment = HorizontalAlignment.Stretch
        };

        var calSurfaceNameCombo = new ComboBox
        {
            Header = "Calibrate Surface",
            HorizontalAlignment = HorizontalAlignment.Stretch,
            IsEditable = true
        };
        calSurfaceNameCombo.Items.Add("Touchpad");
        calSurfaceNameCombo.Items.Add("Laptop Lid");
        calSurfaceNameCombo.Items.Add("Desk");
        calSurfaceNameCombo.SelectedIndex = 0;

        var calibrateSurfaceBtn = new Button
        {
            Content = "Calibrate (5×)",
            VerticalAlignment = VerticalAlignment.Bottom
        };

        void StopKnockRecording()
        {
            if (!isRecordingKnock) return;
            isRecordingKnock = false;
            App.Engine.EndKnockRecording();
            recordKnockBtn.Content = "Record Pattern";
            knockTimelineBorder.BorderBrush = (Brush)Application.Current.Resources["ControlStrokeColorDefaultBrush"];
            knockTimelineBorder.BorderThickness = new Thickness(1);
        }

        recordKnockBtn.Click += (_, _) =>
        {
            if (isRecordingKnock)
            {
                StopKnockRecording();
                if (recordedKnocks.Count > 0)
                {
                    workingKnock.KnockCount = Math.Clamp(recordedKnocks.Count, 1, 6);
                    knockCountBox.Value = workingKnock.KnockCount;
                    workingKnock.RhythmRatios = KnockPatternMatcher.ComputeRhythmRatios(recordedKnocks);
                    useRhythmCheck.IsChecked = workingKnock.RhythmRatios.Count > 0;
                    RenderKnockTimeline(recordedKnocks);
                    knockStatusText.Text = $"Recorded {workingKnock.KnockCount}× knock" +
                        (workingKnock.RhythmRatios.Count > 0 ? $" (cadence {string.Join(":", workingKnock.RhythmRatios)})" : "") + ".";
                }
                else
                {
                    RenderKnockTimeline();
                }
                return;
            }

            StopKnockRecording();
            isRecordingKnock = true;
            recordedKnocks.Clear();
            RenderKnockTimeline(recordedKnocks);
            knockTimelineBorder.BorderBrush = (Brush)Application.Current.Resources["AccentFillColorDefaultBrush"];
            knockTimelineBorder.BorderThickness = new Thickness(2);
            recordKnockBtn.Content = "Done Recording";
            knockStatusText.Text = "● Listening — knock your pattern now, then click Done Recording.";

            App.Engine.BeginKnockRecording(ev =>
            {
                recordedKnocks.Add(ev);
                var snapshot = recordedKnocks.ToArray();
                var surfRes = KnockSurfaceClassifier.Classify(ev.Features, App.Engine.Profile.Settings.KnockSurfaces);
                DispatcherQueue.TryEnqueue(() =>
                {
                    RenderKnockTimeline(snapshot);
                    string surfInfo = surfRes.Surface is not null
                        ? $" • {surfRes.Surface.Name} ({surfRes.Confidence:P0})"
                        : "";
                    knockStatusText.Text = $"● Captured {snapshot.Length} knock(s){surfInfo}";
                });
            });
        };

        var knockTopRow = new Grid { ColumnSpacing = 8 };
        knockTopRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        knockTopRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });
        Grid.SetColumn(knockSurfaceCombo, 0);
        Grid.SetColumn(knockCountBox, 1);
        knockTopRow.Children.Add(knockSurfaceCombo);
        knockTopRow.Children.Add(knockCountBox);

        var knockTriggerPanel = new StackPanel { Spacing = 8 };
        knockTriggerPanel.Children.Add(knockTimelineBorder);
        knockTriggerPanel.Children.Add(knockStatusText);
        knockTriggerPanel.Children.Add(knockTopRow);
        knockTriggerPanel.Children.Add(recordKnockBtn);
        knockTriggerPanel.Children.Add(useRhythmCheck);

        void UpdateTriggerTypeVisibility()
        {
            gestureTriggerPanel.Visibility = selectedTriggerType == TriggerType.Gesture ? Visibility.Visible : Visibility.Collapsed;
            chordTriggerPanel.Visibility = selectedTriggerType == TriggerType.KeyboardChord ? Visibility.Visible : Visibility.Collapsed;
            knockTriggerPanel.Visibility = selectedTriggerType == TriggerType.KnockPattern ? Visibility.Visible : Visibility.Collapsed;
            if (selectedTriggerType != TriggerType.KeyboardChord)
            {
                StopChordRecording();
            }
            if (selectedTriggerType != TriggerType.KnockPattern)
            {
                StopKnockRecording();
            }
        }

        triggerTypeCombo.SelectionChanged += (_, _) =>
        {
            if ((triggerTypeCombo.SelectedItem as ComboBoxItem)?.Tag is TriggerType tt)
            {
                selectedTriggerType = tt;
                UpdateTriggerTypeVisibility();
            }
        };
        UpdateTriggerTypeVisibility();

        var step1Panel = new StackPanel { Spacing = 10, Width = padWidth };
        step1Panel.Children.Add(triggerTypeCombo);
        step1Panel.Children.Add(gestureTriggerPanel);
        step1Panel.Children.Add(chordTriggerPanel);
        step1Panel.Children.Add(knockTriggerPanel);

        // Step 2: Action
        var step2Panel = new StackPanel { Spacing = 12, HorizontalAlignment = HorizontalAlignment.Stretch, Visibility = Visibility.Collapsed };
        step2Panel.Children.Add(nameBox);
        step2Panel.Children.Add(categoryCombo);
        step2Panel.Children.Add(subcategoryCombo);
        step2Panel.Children.Add(payloadRow);

        // Step 3: Conditions & Macro (Optional)
        var windowTargetCombo = new ComboBox
        {
            Header = "Target Window",
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        var wtDefault = new ComboBoxItem { Content = "Use Global Default", Tag = WindowTargetMode.Default };
        var wtActive = new ComboBoxItem { Content = "Active (Foreground) Window", Tag = WindowTargetMode.ActiveWindow };
        var wtCursor = new ComboBoxItem { Content = "Window Under Cursor", Tag = WindowTargetMode.WindowUnderCursor };
        windowTargetCombo.Items.Add(wtDefault);
        windowTargetCombo.Items.Add(wtActive);
        windowTargetCombo.Items.Add(wtCursor);
        windowTargetCombo.SelectedItem = rule.WindowTarget switch
        {
            WindowTargetMode.ActiveWindow => wtActive,
            WindowTargetMode.WindowUnderCursor => wtCursor,
            _ => wtDefault
        };

        var conditionsSection = new StackPanel { Spacing = 10 };
        conditionsSection.Children.Add(modRow);
        conditionsSection.Children.Add(titleFilterBox);
        conditionsSection.Children.Add(windowTargetCombo);

        var macroSection = new StackPanel { Spacing = 10 };
        macroSection.Children.Add(chainCategoryCombo);
        macroSection.Children.Add(chainActionCombo);
        macroSection.Children.Add(chainCustomBox);
        macroSection.Children.Add(addRow);
        macroSection.Children.Add(macroEmptyText);
        macroSection.Children.Add(macroStepsScroll);

        var step3Divider = new Border
        {
            Height = 1,
            Background = (Brush)Application.Current.Resources["DividerStrokeColorDefaultBrush"]
        };

        var step3Panel = new StackPanel { Spacing = 16, HorizontalAlignment = HorizontalAlignment.Stretch, Visibility = Visibility.Collapsed };
        step3Panel.Children.Add(conditionsSection);
        step3Panel.Children.Add(step3Divider);
        step3Panel.Children.Add(macroSection);
        if (_activeAppScope is null && !rule.IsGlobal)
        {
            step3Panel.Children.Add(appBox);
        }

        var stepIndicator = new TextBlock
        {
            Text = "Step 1 of 3  •  Draw or swipe a gesture",
            Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
            Foreground = (Brush)Application.Current.Resources["AccentTextFillColorPrimaryBrush"],
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
        };

        var wizardRoot = new StackPanel { Spacing = 14, Padding = new Thickness(0, 0, 14, 0) };
        wizardRoot.Children.Add(stepIndicator);
        wizardRoot.Children.Add(step1Panel);
        wizardRoot.Children.Add(step2Panel);
        wizardRoot.Children.Add(step3Panel);

        var wizardScroll = new ScrollViewer
        {
            Width = padWidth + 14,
            MaxHeight = Math.Max(300, XamlRoot.Size.Height - 220),
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = wizardRoot
        };

        int currentStep = 1;

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = isNew ? "New Gesture & Action" : "Edit Gesture & Action",
            Content = wizardScroll,
            PrimaryButtonText = "Next",
            SecondaryButtonText = "",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary
        };
        dialog.Resources["ContentDialogMaxWidth"] = 500.0;

        void GoToStep(int step)
        {
            currentStep = step;
            step1Panel.Visibility = step == 1 ? Visibility.Visible : Visibility.Collapsed;
            step2Panel.Visibility = step == 2 ? Visibility.Visible : Visibility.Collapsed;
            step3Panel.Visibility = step == 3 ? Visibility.Visible : Visibility.Collapsed;

            stepIndicator.Text = step switch
            {
                1 => selectedTriggerType switch
                {
                    TriggerType.KeyboardChord => "Step 1 of 3  •  Record a keyboard chord",
                    TriggerType.KnockPattern => "Step 1 of 3  •  Configure or record a knock pattern",
                    _ => "Step 1 of 3  •  Draw or swipe a gesture"
                },
                2 => "Step 2 of 3  •  Assign an action",
                _ => "Step 3 of 3  •  Conditions & macro (optional)"
            };

            dialog.PrimaryButtonText = step == 3 ? "Save" : "Next";
            dialog.SecondaryButtonText = step == 1 ? "" : "Back";
        }

        dialog.PrimaryButtonClick += (_, args) =>
        {
            if (currentStep == 1 && selectedTriggerType == TriggerType.KeyboardChord)
            {
                StopChordRecording();
                if (workingChord.Steps.Count == 0 || workingChord.Steps[0].Modifiers == ChordModifiers.None)
                {
                    args.Cancel = true;
                    chordStatusText.Text = "⚠ Record a chord starting with a modifier key (Ctrl, Alt, Shift, or Win).";
                    return;
                }
            }

            if (currentStep < 3)
            {
                args.Cancel = true;
                GoToStep(currentStep + 1);
            }
        };

        dialog.SecondaryButtonClick += (_, args) =>
        {
            if (currentStep > 1)
            {
                args.Cancel = true;
                GoToStep(currentStep - 1);
            }
        };

        App.Engine.BeginRecording(
            onTouchpadLive: (fingerPaths, fingers) =>
            {
                if (currentStep != 1 || !isPadArmed || fingers < 2 || fingerPaths.Count == 0) return;
                lastTouchpadEvent = DateTimeOffset.UtcNow;
                RenderMultiFingerPaths(fingerPaths);
                if (!isRecordingFollowUp)
                {
                    detectedSource = GestureInputSource.Touchpad;
                    detectedFingers = fingers;
                    UpdateDetectedBadge();
                }
                drawStatusText.Text = isRecordingFollowUp
                    ? $"Recording {fingers}-finger follow-up gesture..."
                    : $"Recording {fingers}-finger touchpad gesture...";
            },
            onTouchpadCompleted: tpResult =>
            {
                if (currentStep != 1 || !isPadArmed) return;

                if (isRecordingFollowUp)
                {
                    lastTouchpadEvent = DateTimeOffset.UtcNow;
                    string gid;
                    if (tpResult.Kind == TouchpadGestureKind.Tap) gid = "builtin-tap";
                    else if (tpResult.Kind == TouchpadGestureKind.PinchIn) gid = "builtin-pinch-in";
                    else if (tpResult.Kind == TouchpadGestureKind.PinchOut) gid = "builtin-pinch-out";
                    else
                    {
                        var scaledFingerStrokes = new List<List<GesturePoint>>();
                        foreach (var fPath in tpResult.FingerPaths)
                        {
                            var scaledPath = new List<GesturePoint>(fPath.Count);
                            foreach (var p in fPath)
                            {
                                scaledPath.Add(new GesturePoint(20 + p.X * (padWidth - 40), 20 + p.Y * (padHeight - 40)));
                            }
                            if (scaledPath.Count >= 2)
                            {
                                scaledFingerStrokes.Add([.. GestureRecognizer.Resample(scaledPath, 32)]);
                            }
                        }

                        var custom = new GestureDefinition
                        {
                            Name = $"Follow-Up {tpResult.FingerCount}F #{followUpSteps.Count + 2}",
                            IsBuiltIn = false,
                            Points = [.. GestureRecognizer.Resample(tpResult.CentroidPath, 32)],
                            FingerStrokes = scaledFingerStrokes.Count > 1 ? scaledFingerStrokes : null
                        };
                        pendingCustomFollowUpGestures.Add(custom);
                        gid = custom.Id;
                    }

                    followUpSteps.Add(new GestureSequenceStep
                    {
                        GestureId = gid,
                        InputSource = GestureInputSource.Touchpad,
                        FingerCount = tpResult.FingerCount
                    });
                    setPadArmed?.Invoke(false, false);
                    RestoreCurrentPrimaryOnCanvas();
                    refreshSequenceList?.Invoke();
                    drawStatusText.Text = $"Added follow-up {tpResult.FingerCount}F touchpad gesture (#{followUpSteps.Count + 1}).";
                    return;
                }

                lastTouchpadEvent = DateTimeOffset.UtcNow;
                detectedSource = GestureInputSource.Touchpad;
                detectedFingers = tpResult.FingerCount;
                UpdateDetectedBadge();

                if (tpResult.Kind == TouchpadGestureKind.Tap)
                {
                    drawnPoints.Clear();
                    drawnFingerStrokes.Clear();
                    recordedTapPoints.Clear();
                    foreach (var fp in tpResult.FingerPaths)
                    {
                        if (fp.Count > 0) recordedTapPoints.Add(fp[0]);
                    }
                    recordedBuiltInId = "builtin-tap";
                    RenderTapDots(tpResult.FingerPaths, tpResult.FingerCount);
                    drawStatusText.Text = $"Recorded {tpResult.FingerCount}-Finger Tap!";
                    setPadArmed?.Invoke(false, false);
                    refreshSequenceList?.Invoke();
                    return;
                }

                if (tpResult.Kind == TouchpadGestureKind.PinchIn || tpResult.Kind == TouchpadGestureKind.PinchOut)
                {
                    drawnPoints.Clear();
                    drawnFingerStrokes.Clear();
                    recordedBuiltInId = tpResult.Kind == TouchpadGestureKind.PinchIn ? "builtin-pinch-in" : "builtin-pinch-out";
                    RenderMultiFingerPaths(tpResult.FingerPaths);
                    drawStatusText.Text = $"Recorded {tpResult.FingerCount}-Finger {(tpResult.Kind == TouchpadGestureKind.PinchIn ? "Pinch In" : "Pinch Out")}!";
                    setPadArmed?.Invoke(false, false);
                    refreshSequenceList?.Invoke();
                    return;
                }

                recordedBuiltInId = null;
                drawnPoints.Clear();
                drawnFingerStrokes.Clear();

                foreach (var p in tpResult.CentroidPath)
                {
                    drawnPoints.Add(new GesturePoint(20 + p.X * (padWidth - 40), 20 + p.Y * (padHeight - 40)));
                }

                foreach (var fPath in tpResult.FingerPaths)
                {
                    var scaledPath = new List<GesturePoint>(fPath.Count);
                    foreach (var p in fPath)
                    {
                        scaledPath.Add(new GesturePoint(20 + p.X * (padWidth - 40), 20 + p.Y * (padHeight - 40)));
                    }
                    if (scaledPath.Count >= 2)
                    {
                        drawnFingerStrokes.Add([.. GestureRecognizer.Resample(scaledPath, 32)]);
                    }
                }

                RenderMultiFingerPaths(tpResult.FingerPaths);

                var match = App.Engine.TestRecognize(drawnPoints);
                drawStatusText.Text = match.Gesture is not null
                    ? $"Recorded {tpResult.FingerCount}-Finger Swipe (matches {match.Gesture.Name} at {match.Score:P0})."
                    : $"Recorded {tpResult.FingerCount}-Finger Swipe ({drawnFingerStrokes.Count} colored shapes).";
                setPadArmed?.Invoke(false, false);
                refreshSequenceList?.Invoke();
            });

        try
        {
            var result = await dialog.ShowAsync();
            if (result != ContentDialogResult.Primary)
            {
                return false;
            }

            var chosenPreset = subcategoryCombo.SelectedItem as ActionPreset;
            rule.Name = string.IsNullOrWhiteSpace(nameBox.Text)
                ? (chosenPreset?.Label ?? "Untitled Action")
                : nameBox.Text.Trim();

            rule.TriggerType = selectedTriggerType;
            if (selectedTriggerType == TriggerType.KeyboardChord)
            {
                workingChord.SuppressKeys = suppressKeysCheck.IsChecked == true;
                rule.KeyboardChord = workingChord;
                rule.KnockPattern = null;
                rule.FollowUpGestures = null;
            }
            else if (selectedTriggerType == TriggerType.KnockPattern)
            {
                if (useRhythmCheck.IsChecked != true)
                {
                    workingKnock.RhythmRatios.Clear();
                }
                rule.KnockPattern = workingKnock;
                rule.KeyboardChord = null;
                rule.FollowUpGestures = null;
                App.Engine.Profile.Settings.EnableKnockDetection = true;
            }
            else
            {
                rule.KeyboardChord = null;
                rule.KnockPattern = null;
                if (!string.IsNullOrEmpty(recordedBuiltInId))
                {
                    rule.GestureId = recordedBuiltInId;
                    rule.PreviewTapPoints = recordedBuiltInId == "builtin-tap" && recordedTapPoints.Count > 0
                        ? [.. recordedTapPoints]
                        : null;
                }
                else if (drawnPoints.Count >= 2)
                {
                    var customGesture = new GestureDefinition
                    {
                        Name = $"{rule.Name} Shape",
                        Description = drawnFingerStrokes.Count > 1
                            ? $"{drawnFingerStrokes.Count}-finger custom gesture"
                            : "Custom recorded gesture",
                        IsBuiltIn = false,
                        Points = [.. GestureRecognizer.Resample(drawnPoints, 32)],
                        FingerStrokes = drawnFingerStrokes.Count > 1 ? [.. drawnFingerStrokes] : null
                    };
                    App.Engine.Profile.Gestures.Add(customGesture);
                    rule.GestureId = customGesture.Id;
                }
                else if (existingGesture is not null)
                {
                    rule.GestureId = existingGesture.Id;
                }

                rule.InputSource = detectedSource;
                rule.FingerCount = detectedFingers;
                foreach (var fg in pendingCustomFollowUpGestures)
                {
                    App.Engine.Profile.Gestures.Add(fg);
                }
                rule.FollowUpGestures = followUpSteps.Count > 0 ? [.. followUpSteps] : null;
            }

            rule.ApplicationFilter = string.IsNullOrWhiteSpace(appBox.Text) ? "*" : appBox.Text.Trim();
            rule.WindowTitleFilter = string.IsNullOrWhiteSpace(titleFilterBox.Text) ? null : titleFilterBox.Text.Trim();
            rule.WindowTarget = (windowTargetCombo.SelectedItem as ComboBoxItem)?.Tag is WindowTargetMode wtm
                ? wtm
                : WindowTargetMode.Default;

            var mods = ModifierKeyFilter.None;
            if (ctrlCheck.IsChecked == true) mods |= ModifierKeyFilter.Ctrl;
            if (altCheck.IsChecked == true) mods |= ModifierKeyFilter.Alt;
            if (shiftCheck.IsChecked == true) mods |= ModifierKeyFilter.Shift;
            rule.RequiredModifiers = mods;
            rule.RequiredRepetitions = double.IsNaN(repeatBox.Value) ? 1 : Math.Clamp((int)repeatBox.Value, 1, 5);

            rule.Steps = macroSteps.Count > 0 ? [.. macroSteps] : null;
            rule.ActionType = chosenPreset?.OverrideType ?? selectedCategory.PrimaryType;
            rule.ActionPayload = !string.IsNullOrEmpty(chosenPreset?.Payload)
                ? chosenPreset.Payload
                : (payloadBox.Text?.Trim() ?? string.Empty);

            return true;
        }
        finally
        {
            StopChordRecording();
            StopKnockRecording();
            App.Engine.EndRecording();
        }
    }
}
