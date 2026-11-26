using System.Collections.ObjectModel;
using Flick.App.Services.Sneak;
using Flick.Core.Events;
using Flick.Core.Keyboard;
using Flick.Core.Knock;
using Flick.Core.Models;
using Flick.Core.Recognition;
using Flick.Core.Rules;
using Flick.Core.Storage;
using Flick.Core.TextExpansion;
using Flick.Core.Touchpad;
using Microsoft.UI.Dispatching;

namespace Flick.App.Services;

public sealed record GestureActivityItem(
    DateTimeOffset Timestamp,
    string GestureName,
    string RuleName,
    string ProcessName,
    double Confidence,
    bool Executed);

/// <summary>
/// Central coordinator connecting the global mouse hook, gesture recognizer,
/// rule matcher, trail overlay, and action executor.
/// </summary>
public sealed class GestureEngineService : IDisposable
{
    private readonly ProfileStore _store;
    private readonly GestureRecognizer _recognizer = new();
    private readonly RuleMatcher _ruleMatcher = new();
    private readonly RepetitionGate _repetitionGate = new();
    private readonly GestureSequenceTracker _sequenceTracker = new();
    private readonly ForegroundWindowService _foreground = new();
    private readonly ActionExecutor _executor = new();
    private readonly TextExpansionExecutor _textExpansionExecutor = new();
    private readonly MouseGestureHook _hook = new();
    private readonly PrecisionTouchpadHook _touchpadHook = new();
    private readonly KeyboardChordHook _keyboardHook = new();
    private readonly KnockAudioService _knockAudio = new();
    private readonly SystemEventService _systemEvents = new();
    private readonly GestureTrailOverlay _overlay = new();
    private readonly SneakOrbOverlay _sneakOverlay = new();
    private readonly HotCornerService _hotCorners;
    private DispatcherQueue? _uiDispatcher;
    private bool _isRecording;
    private CancellationTokenSource? _sequenceTimeoutCts;
    private string? _sequenceProcessName;
    private Action<IReadOnlyList<IReadOnlyList<GesturePoint>>, int>? _onRecordTouchpadLive;
    private Action<TouchpadGestureResult>? _onRecordTouchpadCompleted;

    public FlickProfile Profile { get; private set; }
    public ObservableCollection<GestureActivityItem> RecentActivity { get; } = [];
    public bool IsTouchpadDetected => _touchpadHook.IsTouchpadDetected;
    public bool IsRecording => _isRecording;

    public event Action? ProfileChanged;

    public GestureEngineService(ProfileStore? store = null)
    {
        _store = store ?? new ProfileStore();
        Profile = _store.Load();

        _executor.ToggleSneakHandler = showAll => _sneakOverlay.Toggle(showAll);

        _hotCorners = new HotCornerService(
            canTrigger: () => Profile.Settings.IsEnabled && !_isRecording && !ShouldIgnoreCurrent(),
            onCornerTriggered: OnHotCornerTriggered);

        _hook.ShouldIgnoreCurrentWindow = ShouldIgnoreCurrent;

        _hook.StrokeStarted += _ =>
        {
            if (Profile.Settings.ShowTrailOverlay)
            {
                ConfigureOverlayForCurrentApp();
            }
        };

        _hook.StrokeUpdated += pts =>
        {
            if (!Profile.Settings.ShowTrailOverlay)
            {
                return;
            }

            string? hudText = null;
            if (Profile.Settings.ShowActionHudPill && pts.Count >= 4)
            {
                hudText = ResolveLiveHudLabel(pts, GestureInputSource.Mouse, 0);
            }
            _overlay.UpdateTrail(pts, hudText);
        };

        _hook.StrokeCancelled += () => _overlay.Clear();

        _hook.StrokeCompleted += OnStrokeCompleted;

        _touchpadHook.ShouldIgnoreCurrentWindow = _hook.ShouldIgnoreCurrentWindow;
        _touchpadHook.TouchpadStrokeUpdated += OnTouchpadStrokeUpdated;
        _touchpadHook.TouchpadGestureCompleted += OnTouchpadGestureCompleted;

        _keyboardHook.ShouldIgnoreCurrentWindow = ShouldIgnoreCurrent;
        _keyboardHook.GetRules = () => Profile.Rules;
        _keyboardHook.GetTextExpansions = () => Profile.TextExpansions;
        _keyboardHook.ChordMatched += OnChordMatched;
        _keyboardHook.TextExpansionMatched += OnTextExpansionMatched;

        _knockAudio.ShouldIgnoreCurrentWindow = ShouldIgnoreCurrent;
        _knockAudio.GetRules = () => Profile.Rules;
        _knockAudio.GetSurfaces = () => Profile.Settings.KnockSurfaces;
        _knockAudio.KnockPatternMatched += OnKnockPatternMatched;

        _systemEvents.EventTriggered += OnSystemEventTriggered;
    }

    public void Start(DispatcherQueue uiDispatcher)
    {
        _uiDispatcher = uiDispatcher;
        ApplySettingsToRuntime();
        _overlay.Start();
        _sneakOverlay.Start();
        _hook.Start();
        _touchpadHook.Start();
        _keyboardHook.Start();
        _systemEvents.Start();
    }

    public void BeginRecording(
        Action<IReadOnlyList<IReadOnlyList<GesturePoint>>, int>? onTouchpadLive,
        Action<TouchpadGestureResult>? onTouchpadCompleted)
    {
        _onRecordTouchpadLive = onTouchpadLive;
        _onRecordTouchpadCompleted = onTouchpadCompleted;
        _isRecording = true;
        _overlay.Clear();
        ApplySettingsToRuntime();
    }

    public void BeginChordRecording(Func<int, bool, ChordModifiers, bool> onKey)
    {
        _isRecording = true;
        _keyboardHook.ResetState();
        _keyboardHook.RecordKeyInterceptor = onKey;
        ApplySettingsToRuntime();
    }

    public void EndChordRecording()
    {
        _keyboardHook.RecordKeyInterceptor = null;
        _isRecording = false;
        ApplySettingsToRuntime();
    }

    public void BeginKnockRecording(Action<KnockOnsetEvent> onKnock)
    {
        _isRecording = true;
        _knockAudio.RecordKnockCallback = onKnock;
        _knockAudio.Configure(true, Profile.Settings.KnockSensitivity, Profile.Settings.KnockPatternTimeoutMs, Profile.Settings.KnockMicrophoneName);
    }

    public void EndKnockRecording()
    {
        _knockAudio.RecordKnockCallback = null;
        _isRecording = false;
        ApplySettingsToRuntime();
    }

    public void EndRecording()
    {
        _isRecording = false;
        _onRecordTouchpadLive = null;
        _onRecordTouchpadCompleted = null;
        ApplySettingsToRuntime();
    }

    public void ApplySettingsToRuntime()
    {
        _hook.IsEnabled = Profile.Settings.IsEnabled && !_isRecording;
        _hook.TriggerButton = Profile.Settings.TriggerButton;
        _hook.MinimumStrokeLength = Profile.Settings.MinimumStrokeLength;
        // Keep touchpad hook active during recording so multi-finger gestures can be captured without clicking.
        _touchpadHook.IsEnabled = _isRecording || (Profile.Settings.IsEnabled && Profile.Settings.EnableTouchpadGestures);
        _touchpadHook.MinNormalizedSwipeDistance = Profile.Settings.TouchpadMinSwipeDistance;
        _keyboardHook.IsEnabled = Profile.Settings.IsEnabled && !_isRecording && Profile.Settings.EnableKeyboardChords;
        _keyboardHook.IsTextExpansionEnabled = Profile.Settings.IsEnabled && !_isRecording && Profile.Settings.EnableTextExpansion;
        _keyboardHook.SimultaneousWindowMs = Profile.Settings.ChordSimultaneousWindowMs;
        _keyboardHook.SequenceTimeoutMs = Profile.Settings.ChordSequenceTimeoutMs;
        _systemEvents.IsEnabled = Profile.Settings.IsEnabled && !_isRecording && Profile.Settings.EnableSystemEvents;
        _knockAudio.Configure(
            _knockAudio.RecordKnockCallback is not null || (Profile.Settings.IsEnabled && !_isRecording && Profile.Settings.EnableKnockDetection),
            Profile.Settings.KnockSensitivity,
            Profile.Settings.KnockPatternTimeoutMs,
            Profile.Settings.KnockMicrophoneName);
        _overlay.Configure(
            Profile.Settings.TrailColorHex,
            Profile.Settings.TrailThickness,
            Profile.Settings.RippleRadius,
            Profile.Settings.RippleThickness,
            Profile.Settings.RippleSpeed);
        _hotCorners.Configure(
            Profile.Settings.IsEnabled && !_isRecording && Profile.Settings.EnableHotCorners,
            Profile.Settings.HotCornerActions);
        StartupService.SetStartWithWindows(Profile.Settings.StartWithWindows);
    }

    private bool ShouldIgnoreCurrent()
    {
        var app = _foreground.GetForegroundApp();
        if (RuleMatcher.IsProcessIgnored(app.ProcessName, Profile.Settings.IgnoredProcesses))
        {
            return true;
        }

        if (Profile.Settings.AutoPauseOnFullscreen && _foreground.IsForegroundFullscreen())
        {
            return true;
        }

        return false;
    }

    private void ConfigureOverlayForCurrentApp(string? processName = null)
    {
        processName ??= _foreground.GetForegroundApp().ProcessName;
        string color = ResolveTrailColor(processName);
        _overlay.Configure(
            color,
            Profile.Settings.TrailThickness,
            Profile.Settings.RippleRadius,
            Profile.Settings.RippleThickness,
            Profile.Settings.RippleSpeed);
    }

    private string ResolveTrailColor(string? processName)
    {
        string norm = RuleMatcher.NormalizeProcessName(processName);
        if (!string.IsNullOrEmpty(norm))
        {
            foreach (var scope in Profile.Scopes)
            {
                if (RuleMatcher.MatchesFilter(norm, scope.ProcessFilter) &&
                    Profile.Settings.ScopeTrailColors.TryGetValue(scope.Id, out string? hex) &&
                    !string.IsNullOrWhiteSpace(hex))
                {
                    return hex;
                }
            }
        }
        return Profile.Settings.TrailColorHex;
    }

    private string? ResolveLiveHudLabel(
        IReadOnlyList<GesturePoint> points,
        GestureInputSource source,
        int fingerCount,
        IReadOnlyList<IReadOnlyList<GesturePoint>>? fingerPaths = null)
    {
        var foreground = _foreground.GetForegroundApp();
        var mods = ForegroundWindowService.GetActiveModifiers();
        var ranked = _recognizer.RecognizeRanked(points, Profile.Gestures, Profile.Settings.RecognitionThreshold, fingerPaths);

        foreach (var candidate in ranked)
        {
            var found = _ruleMatcher.FindBestMatch(
                candidate.Gesture!.Id,
                foreground.ProcessName,
                Profile.Rules,
                source,
                fingerCount,
                foreground.WindowTitle,
                mods);
            if (found is not null)
            {
                return found.Name;
            }
        }

        return ranked.Count > 0 ? ranked[0].Gesture!.Name : null;
    }

    private void OnSystemEventTriggered(SystemEventKind kind, ForegroundAppInfo appInfo)
    {
        if (!Profile.Settings.IsEnabled || _isRecording || !Profile.Settings.EnableSystemEvents)
        {
            return;
        }

        if (SystemEventHelper.IsAppScopedEvent(kind) &&
            RuleMatcher.IsProcessIgnored(appInfo.ProcessName, Profile.Settings.IgnoredProcesses))
        {
            return;
        }

        var mods = ForegroundWindowService.GetActiveModifiers();
        var matching = SystemEventHelper.FindMatchingEventRules(
            kind,
            appInfo.ProcessName,
            appInfo.WindowTitle,
            mods,
            Profile.Rules);

        foreach (var rule in matching)
        {
            bool executed = TryExecuteRule(rule, appInfo.WindowHandle);
            RecordActivity(new GestureActivityItem(
                DateTimeOffset.Now,
                $"Event: {SystemEventHelper.FormatEvent(kind)}",
                rule.Name,
                string.IsNullOrEmpty(appInfo.ProcessName) ? "System" : appInfo.ProcessName,
                1.0,
                executed));
        }
    }

    private void OnKnockPatternMatched(KnockSequenceMatch match, ForegroundAppInfo foreground)
    {
        bool executed = TryExecuteRule(match.Rule, foreground.WindowHandle);
        if (executed && Profile.Settings.ShowRippleEffect && NativeMethods.GetCursorPos(out var pt))
        {
            ConfigureOverlayForCurrentApp(foreground.ProcessName);
            _overlay.TriggerRipple(new GesturePoint(pt.X, pt.Y));
        }

        RecordActivity(new GestureActivityItem(
            DateTimeOffset.Now,
            $"Knock: {match.KnockCount}× ({match.SurfaceName})",
            match.Rule.Name,
            string.IsNullOrEmpty(foreground.ProcessName) ? "Desktop" : foreground.ProcessName,
            match.Confidence,
            executed));
    }

    private void OnChordMatched(GestureRule rule, ForegroundAppInfo foreground)
    {
        bool executed = TryExecuteRule(rule, foreground.WindowHandle);
        if (executed && Profile.Settings.ShowRippleEffect && NativeMethods.GetCursorPos(out var pt))
        {
            ConfigureOverlayForCurrentApp(foreground.ProcessName);
            _overlay.TriggerRipple(new GesturePoint(pt.X, pt.Y));
        }

        string chordLabel = ChordKeyHelper.FormatChord(rule.KeyboardChord);
        RecordActivity(new GestureActivityItem(
            DateTimeOffset.Now,
            $"Chord: {chordLabel}",
            rule.Name,
            string.IsNullOrEmpty(foreground.ProcessName) ? "Desktop" : foreground.ProcessName,
            1.0,
            executed));
    }

    private void OnHotCornerTriggered(string corner, string actionCommand, GesturePoint pt)
    {
        var foreground = _foreground.GetForegroundApp();
        _executor.ExecuteSingle(ActionType.WindowManagement, actionCommand, foreground.WindowHandle);
        if (Profile.Settings.ShowRippleEffect)
        {
            ConfigureOverlayForCurrentApp(foreground.ProcessName);
            _overlay.TriggerRipple(pt);
        }

        RecordActivity(new GestureActivityItem(
            DateTimeOffset.Now,
            $"Corner: {corner}",
            actionCommand,
            string.IsNullOrEmpty(foreground.ProcessName) ? "Desktop" : foreground.ProcessName,
            1.0,
            true));
    }

    public void SaveProfile()
    {
        _store.Save(Profile);
        ApplySettingsToRuntime();
        ProfileChanged?.Invoke();
    }

    public IReadOnlyList<string> GetRunningProcesses() => _foreground.GetRunningWindowedProcesses();

    public RecognitionResult TestRecognize(IReadOnlyList<GesturePoint> points)
    {
        return _recognizer.Recognize(points, Profile.Gestures, Profile.Settings.RecognitionThreshold);
    }

    private void OnStrokeCompleted(IReadOnlyList<GesturePoint> points)
    {
        var foreground = (Profile.Settings.WindowTarget != GlobalWindowTargetMode.ActiveWindow && points.Count > 0)
            ? _foreground.GetWindowAtPoint((int)points[0].X, (int)points[0].Y)
            : _foreground.GetForegroundApp();
        if (foreground.WindowHandle == 0)
        {
            foreground = _foreground.GetForegroundApp();
        }

        var mods = ForegroundWindowService.GetActiveModifiers();
        var ranked = _recognizer.RecognizeRanked(points, Profile.Gestures, Profile.Settings.RecognitionThreshold);

        if (ranked.Count == 0)
        {
            _overlay.Clear();
            RecordActivity(new GestureActivityItem(
                DateTimeOffset.Now,
                "Unrecognized",
                "No matching gesture",
                string.IsNullOrEmpty(foreground.ProcessName) ? "Desktop" : foreground.ProcessName,
                0,
                false));
            return;
        }

        GestureDefinition matchedGesture = ranked[0].Gesture!;
        double matchedScore = ranked[0].Score;
        var candidateIds = ranked.Select(r => r.Gesture!.Id).ToList();

        var (executed, activityRuleLabel, resolvedRule) = EvaluateAndExecute(
            candidateIds,
            foreground,
            GestureInputSource.Mouse,
            0,
            mods,
            out string? overriddenGestureId,
            cursorPoint: points.Count > 0 ? points[0] : null);

        if (overriddenGestureId is not null)
        {
            var found = ranked.FirstOrDefault(r => r.Gesture!.Id == overriddenGestureId);
            if (found.Gesture is not null)
            {
                matchedGesture = found.Gesture;
                matchedScore = found.Score;
            }
        }

        if (Profile.Settings.ShowRippleEffect && executed && points.Count > 0)
        {
            ConfigureOverlayForCurrentApp(foreground.ProcessName);
            _overlay.TriggerRipple(points[^1]);
        }
        else
        {
            _overlay.Clear();
        }

        RecordActivity(new GestureActivityItem(
            DateTimeOffset.Now,
            matchedGesture.Name,
            activityRuleLabel,
            string.IsNullOrEmpty(foreground.ProcessName) ? "Desktop" : foreground.ProcessName,
            matchedScore,
            executed));
    }

    private (bool Executed, string Label, GestureRule? Rule) EvaluateAndExecute(
        IReadOnlyList<string> candidateGestureIds,
        ForegroundAppInfo foreground,
        GestureInputSource source,
        int fingerCount,
        ModifierKeyFilter mods,
        out string? matchedGestureId,
        GesturePoint? cursorPoint = null)
    {
        matchedGestureId = null;
        bool isMouse = source == GestureInputSource.Mouse;

        // 1. Check if this gesture advances an in-progress multi-gesture sequence.
        var seqAdvance = _sequenceTracker.TryAdvance(candidateGestureIds, source, fingerCount);
        if (seqAdvance is { } adv)
        {
            if (adv.CompletedRule is not null)
            {
                CancelSequenceTimeout();
                return (TryExecuteRule(adv.CompletedRule, foreground.WindowHandle, isMouse, cursorPoint), adv.CompletedRule.Name, adv.CompletedRule);
            }

            if (adv.PendingRule is not null)
            {
                ResetSequenceTimeout(foreground.ProcessName);
                return (false, $"{adv.PendingRule.Name} (Step {adv.CurrentStep}/{adv.TotalSteps})", adv.PendingRule);
            }
        }

        // 2. Otherwise find the best matching rule for any recognized candidate.
        GestureRule? rule = null;
        foreach (string gid in candidateGestureIds)
        {
            var found = _ruleMatcher.FindBestMatch(gid, foreground.ProcessName, Profile.Rules, source, fingerCount, foreground.WindowTitle, mods);
            if (found is not null)
            {
                matchedGestureId = gid;
                rule = found;
                break;
            }
        }

        if (rule is null)
        {
            return (false, "No action bound", null);
        }

        // 3. If this rule requires follow-up gestures, start its sequence and wait.
        if (rule.FollowUpGestures is { Count: > 0 })
        {
            var started = _sequenceTracker.BeginSequence(rule);
            ResetSequenceTimeout(foreground.ProcessName);
            return (false, $"{rule.Name} (Step {started.CurrentStep}/{started.TotalSteps})", rule);
        }

        // 4. Otherwise apply RepetitionGate.
        var rep = _repetitionGate.RegisterMatch(rule);
        if (!rep.ShouldExecute)
        {
            return (false, $"{rule.Name} ({rep.CurrentCount}/{rep.RequiredCount})", rule);
        }

        return (TryExecuteRule(rule, foreground.WindowHandle, isMouse, cursorPoint), rule.Name, rule);
    }

    private bool TryExecuteRule(GestureRule rule, nint defaultWindowHandle, bool isMouseGesture = false, GesturePoint? cursorPoint = null)
    {
        try
        {
            nint targetHandle = defaultWindowHandle;
            bool useCursorWindow = rule.WindowTarget switch
            {
                WindowTargetMode.ActiveWindow => false,
                WindowTargetMode.WindowUnderCursor => true,
                _ => Profile.Settings.WindowTarget == GlobalWindowTargetMode.WindowUnderCursorAlways ||
                     (isMouseGesture && Profile.Settings.WindowTarget == GlobalWindowTargetMode.WindowUnderCursorForMouse)
            };

            if (useCursorWindow)
            {
                nint cursorHwnd = 0;
                if (cursorPoint is { } cp)
                {
                    cursorHwnd = _foreground.GetWindowAtPoint((int)cp.X, (int)cp.Y).WindowHandle;
                }
                else if (NativeMethods.GetCursorPos(out var pt))
                {
                    cursorHwnd = _foreground.GetWindowAtPoint(pt.X, pt.Y).WindowHandle;
                }

                if (cursorHwnd != 0)
                {
                    targetHandle = cursorHwnd;
                    if (rule.ActionType is ActionType.SendHotkey or ActionType.TextSnippet)
                    {
                        NativeMethods.SetForegroundWindow(targetHandle);
                    }
                }
            }

            if (rule.TriggerType != TriggerType.KeyboardChord && rule.ExecutionDelayMs > 0)
            {
                int delayMs = rule.ExecutionDelayMs;
                _ = Task.Run(async () =>
                {
                    await Task.Delay(delayMs).ConfigureAwait(false);
                    try { _executor.Execute(rule, targetHandle); } catch { }
                });
                return true;
            }

            _executor.Execute(rule, targetHandle);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private void OnTouchpadStrokeUpdated(
        IReadOnlyList<IReadOnlyList<GesturePoint>> fingerPaths,
        IReadOnlyList<GesturePoint> centroidPoints,
        int fingerCount)
    {
        _knockAudio.NotifyTouchpadActivity();
        if (_isRecording)
        {
            var copy = fingerPaths.Select(p => (IReadOnlyList<GesturePoint>)p.ToArray()).ToArray();
            var cb = _onRecordTouchpadLive;
            if (cb is not null)
            {
                _uiDispatcher?.TryEnqueue(() => cb(copy, fingerCount));
            }
            return;
        }

        if (!Profile.Settings.ShowTrailOverlay || fingerCount < 2 || fingerPaths.Count == 0)
        {
            return;
        }

        int screenW = NativeMethods.GetSystemMetrics(0); // SM_CXSCREEN
        int screenH = NativeMethods.GetSystemMetrics(1); // SM_CYSCREEN
        double boxSize = Math.Min(screenW, screenH) * 0.45;
        double offsetX = (screenW - boxSize) / 2.0;
        double offsetY = (screenH - boxSize) / 2.0;

        var screenPaths = new GesturePoint[fingerPaths.Count][];
        for (int f = 0; f < fingerPaths.Count; f++)
        {
            var src = fingerPaths[f];
            var dst = new GesturePoint[src.Count];
            for (int i = 0; i < src.Count; i++)
            {
                dst[i] = new GesturePoint(
                    offsetX + src[i].X * boxSize,
                    offsetY + src[i].Y * boxSize);
            }
            screenPaths[f] = dst;
        }

        ConfigureOverlayForCurrentApp();
        string? hudText = null;
        if (Profile.Settings.ShowActionHudPill && centroidPoints.Count >= 3)
        {
            hudText = ResolveLiveHudLabel(centroidPoints, GestureInputSource.Touchpad, fingerCount, fingerPaths);
        }
        var hudPos = NativeMethods.GetCursorPos(out var curPt)
            ? new GesturePoint(curPt.X, curPt.Y)
            : screenPaths[0][^1];
        _overlay.UpdateMultiTrail(screenPaths, hudText, hudPos);
    }

    private void OnTouchpadGestureCompleted(TouchpadGestureResult tpResult)
    {
        if (_isRecording)
        {
            var cb = _onRecordTouchpadCompleted;
            if (cb is not null)
            {
                _uiDispatcher?.TryEnqueue(() => cb(tpResult));
            }
            return;
        }

        var foreground = _foreground.GetForegroundApp();
        var mods = ForegroundWindowService.GetActiveModifiers();
        GestureDefinition? matchedGesture = null;
        double score = 1.0;
        var candidateIds = new List<string>();

        switch (tpResult.Kind)
        {
            case TouchpadGestureKind.Tap:
                matchedGesture = Profile.Gestures.FirstOrDefault(g => g.Id == "builtin-tap");
                if (matchedGesture is not null) candidateIds.Add(matchedGesture.Id);
                break;
            case TouchpadGestureKind.PinchIn:
                matchedGesture = Profile.Gestures.FirstOrDefault(g => g.Id == "builtin-pinch-in");
                if (matchedGesture is not null) candidateIds.Add(matchedGesture.Id);
                break;
            case TouchpadGestureKind.PinchOut:
                matchedGesture = Profile.Gestures.FirstOrDefault(g => g.Id == "builtin-pinch-out");
                if (matchedGesture is not null) candidateIds.Add(matchedGesture.Id);
                break;
            case TouchpadGestureKind.Swipe:
                var ranked = _recognizer.RecognizeRanked(tpResult.CentroidPath, Profile.Gestures, Profile.Settings.RecognitionThreshold, tpResult.FingerPaths);
                if (ranked.Count > 0)
                {
                    matchedGesture = ranked[0].Gesture;
                    score = ranked[0].Score;
                    foreach (var r in ranked) candidateIds.Add(r.Gesture!.Id);
                }
                break;
        }

        if (matchedGesture is null)
        {
            _overlay.Clear();
            RecordActivity(new GestureActivityItem(
                DateTimeOffset.Now,
                $"{tpResult.FingerCount}F Unrecognized",
                "No matching gesture",
                string.IsNullOrEmpty(foreground.ProcessName) ? "Desktop" : foreground.ProcessName,
                score,
                false));
            return;
        }

        var (executed, activityRuleLabel, _) = EvaluateAndExecute(
            candidateIds,
            foreground,
            GestureInputSource.Touchpad,
            tpResult.FingerCount,
            mods,
            out string? overriddenId);

        if (overriddenId is not null)
        {
            var g = Profile.Gestures.FirstOrDefault(x => x.Id == overriddenId);
            if (g is not null) matchedGesture = g;
        }

        if (Profile.Settings.ShowRippleEffect && executed && NativeMethods.GetCursorPos(out var cursorPt))
        {
            ConfigureOverlayForCurrentApp(foreground.ProcessName);
            _overlay.TriggerRipple(new GesturePoint(cursorPt.X, cursorPt.Y));
        }
        else
        {
            _overlay.Clear();
        }

        RecordActivity(new GestureActivityItem(
            DateTimeOffset.Now,
            $"{tpResult.FingerCount}F {matchedGesture.Name}",
            activityRuleLabel,
            string.IsNullOrEmpty(foreground.ProcessName) ? "Desktop" : foreground.ProcessName,
            score,
            executed));
    }

    private void OnTextExpansionMatched(
        TextExpansionRule rule,
        TextExpansionMatchResult match,
        ForegroundAppInfo foreground)
    {
        _textExpansionExecutor.Execute(rule, match, foreground, Profile.TextExpansions, _uiDispatcher);

        string label = string.IsNullOrWhiteSpace(rule.Name) ? rule.Abbreviation : rule.Name;
        RecordActivity(new GestureActivityItem(
            DateTimeOffset.Now,
            $"Snippet: {rule.Abbreviation}",
            label,
            string.IsNullOrEmpty(foreground.ProcessName) ? "Desktop" : foreground.ProcessName,
            1.0,
            true));
    }

    private void RecordActivity(GestureActivityItem item)
    {
        void Add()
        {
            RecentActivity.Insert(0, item);
            while (RecentActivity.Count > 25)
            {
                RecentActivity.RemoveAt(RecentActivity.Count - 1);
            }
        }

        if (_uiDispatcher is not null && !_uiDispatcher.HasThreadAccess)
        {
            _uiDispatcher.TryEnqueue(Add);
        }
        else
        {
            Add();
        }
    }

    private void CancelSequenceTimeout()
    {
        var old = Interlocked.Exchange(ref _sequenceTimeoutCts, null);
        if (old is not null)
        {
            old.Cancel();
            old.Dispose();
        }
    }

    private void ResetSequenceTimeout(string? processName)
    {
        CancelSequenceTimeout();
        _sequenceProcessName = processName;
        var cts = new CancellationTokenSource();
        _sequenceTimeoutCts = cts;
        _ = WaitAndExpireSequenceAsync(cts);
    }

    private async Task WaitAndExpireSequenceAsync(CancellationTokenSource cts)
    {
        try
        {
            await Task.Delay(_sequenceTracker.StepTimeout, cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        if (!ReferenceEquals(Volatile.Read(ref _sequenceTimeoutCts), cts) || cts.IsCancellationRequested)
        {
            return;
        }

        if (_sequenceTracker.HasActiveSequence)
        {
            _sequenceTracker.Reset();
            if (Profile.Settings.ShowRippleEffect && NativeMethods.GetCursorPos(out var cursorPt))
            {
                ConfigureOverlayForCurrentApp(_sequenceProcessName);
                _overlay.TriggerRipple(new GesturePoint(cursorPt.X, cursorPt.Y), reverse: true);
            }
        }
    }

    public void Dispose()
    {
        CancelSequenceTimeout();
        _hotCorners.Dispose();
        _hook.Dispose();
        _touchpadHook.Dispose();
        _keyboardHook.Dispose();
        _knockAudio.Dispose();
        _systemEvents.Dispose();
        _overlay.Dispose();
        _sneakOverlay.Dispose();
    }
}
