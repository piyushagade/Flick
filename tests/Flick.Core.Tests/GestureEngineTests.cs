using Flick.Core.Models;
using Flick.Core.Recognition;
using Flick.Core.Rules;
using Flick.Core.Storage;
using Xunit;

namespace Flick.Core.Tests;

public sealed class GestureEngineTests
{
    private readonly GestureRecognizer _recognizer = new();
    private readonly List<GestureDefinition> _defaults = DefaultProfileFactory.CreateDefaultGestures();

    [Theory]
    [InlineData("builtin-left", 500, 200, 200, 200)]
    [InlineData("builtin-right", 100, 200, 450, 200)]
    [InlineData("builtin-up", 300, 500, 300, 150)]
    [InlineData("builtin-down", 300, 100, 300, 450)]
    public void Recognizer_DistinguishesDirectionalSwipes(
        string expectedId, double x1, double y1, double x2, double y2)
    {
        var stroke = new List<GesturePoint>
        {
            new(x1, y1),
            new((x1 + x2) / 2, (y1 + y2) / 2),
            new(x2, y2)
        };

        var result = _recognizer.Recognize(stroke, _defaults, threshold: 0.65);

        Assert.NotNull(result.Gesture);
        Assert.Equal(expectedId, result.Gesture!.Id);
        Assert.True(result.Score >= 0.85);
    }

    [Fact]
    public void Recognizer_MatchesScaledAndTranslatedLShape()
    {
        var stroke = new List<GesturePoint>
        {
            new(420, 310),
            new(422, 410),
            new(420, 520),
            new(530, 518),
            new(640, 520)
        };

        var result = _recognizer.Recognize(stroke, _defaults, threshold: 0.65);

        Assert.NotNull(result.Gesture);
        Assert.Equal("builtin-down-right", result.Gesture!.Id);
    }

    [Fact]
    public void Recognizer_RejectsUnrelatedScribbleBelowThreshold()
    {
        var scribble = new List<GesturePoint>
        {
            new(0, 0), new(50, 10), new(10, 40), new(80, 30), new(20, 90), new(90, 80)
        };

        var result = _recognizer.Recognize(scribble, _defaults, threshold: 0.80);

        Assert.Null(result.Gesture);
    }

    [Fact]
    public void RuleMatcher_PrefersAppSpecificRuleOverGlobal()
    {
        var matcher = new RuleMatcher();
        var rules = new List<GestureRule>
        {
            new() { Id = "global", GestureId = "builtin-left", ApplicationFilter = "*", ActionPayload = "Global" },
            new() { Id = "edge", GestureId = "builtin-left", ApplicationFilter = "msedge, chrome", ActionPayload = "Browser" }
        };

        var matchedEdge = matcher.FindBestMatch("builtin-left", "msedge.exe", rules);
        var matchedNotepad = matcher.FindBestMatch("builtin-left", "notepad", rules);

        Assert.Equal("edge", matchedEdge?.Id);
        Assert.Equal("global", matchedNotepad?.Id);
    }

    [Fact]
    public void RuleMatcher_SkipsDisabledRules()
    {
        var matcher = new RuleMatcher();
        var rules = new List<GestureRule>
        {
            new() { Id = "disabled", GestureId = "builtin-up", ApplicationFilter = "*", IsEnabled = false }
        };

        var matched = matcher.FindBestMatch("builtin-up", "explorer", rules);
        Assert.Null(matched);
    }

    [Fact]
    public void RuleMatcher_DetectsIgnoredProcesses()
    {
        Assert.True(RuleMatcher.IsProcessIgnored("MSTSC.exe", ["mstsc", "vncviewer"]));
        Assert.False(RuleMatcher.IsProcessIgnored("msedge", ["mstsc", "vncviewer"]));
    }

    [Fact]
    public void StrokeSmoother_PreservesEndpointsAndSmoothsJitter()
    {
        var jittery = new List<GesturePoint>
        {
            new(0, 0), new(20, 12), new(40, -10), new(60, 10), new(80, -8), new(100, 0)
        };

        var smoothed = StrokeSmoother.Smooth(jittery, denoisePasses: 3, chaikinIterations: 2);

        Assert.True(smoothed.Length > jittery.Count);
        Assert.Equal(jittery[0], smoothed[0]);
        Assert.Equal(jittery[^1], smoothed[^1]);

        double maxAbsY = smoothed.Max(p => Math.Abs(p.Y));
        Assert.True(maxAbsY < 8.0);
    }

    [Fact]
    public void Recognizer_RecognizeRanked_ReturnsAllPassingTemplatesSorted()
    {
        var customDown = new GestureDefinition
        {
            Id = "custom-down",
            Name = "Minimize Shape",
            Points = [new(0, 0), new(2, 50), new(0, 100)]
        };
        var library = new List<GestureDefinition>(_defaults) { customDown };
        var stroke = new List<GesturePoint> { new(300, 100), new(300, 250), new(300, 400) };

        var ranked = _recognizer.RecognizeRanked(stroke, library, threshold: 0.70);

        Assert.Contains(ranked, r => r.Gesture!.Id == "builtin-down");
        Assert.Contains(ranked, r => r.Gesture!.Id == "custom-down");
        Assert.True(ranked[0].Score >= ranked[1].Score);
    }

    [Fact]
    public void RuleMatcher_MatchesWindowTitleAndModifiers()
    {
        var matcher = new RuleMatcher();
        var rules = new List<GestureRule>
        {
            new() { Id = "base", GestureId = "builtin-left", ApplicationFilter = "msedge", ActionPayload = "Back" },
            new()
            {
                Id = "yt-ctrl",
                GestureId = "builtin-left",
                ApplicationFilter = "msedge",
                WindowTitleFilter = "YouTube",
                RequiredModifiers = ModifierKeyFilter.Ctrl,
                ActionPayload = "PrevVideo"
            },
            new()
            {
                Id = "regex-docs",
                GestureId = "builtin-left",
                ApplicationFilter = "msedge",
                WindowTitleFilter = "regex:.*Docs.*",
                ActionPayload = "DocsBack"
            }
        };

        // Without Ctrl or matching title -> base
        Assert.Equal("base", matcher.FindBestMatch("builtin-left", "msedge", rules, GestureInputSource.Mouse, 0, "GitHub", ModifierKeyFilter.None)?.Id);

        // With YouTube title + Ctrl -> yt-ctrl
        Assert.Equal("yt-ctrl", matcher.FindBestMatch("builtin-left", "msedge", rules, GestureInputSource.Mouse, 0, "Music - YouTube", ModifierKeyFilter.Ctrl)?.Id);

        // With Docs regex title -> regex-docs
        Assert.Equal("regex-docs", matcher.FindBestMatch("builtin-left", "msedge", rules, GestureInputSource.Mouse, 0, "API Docs - Edge", ModifierKeyFilter.None)?.Id);
    }

    [Fact]
    public void Recognizer_MatchesPerFingerDivergingStrokes()
    {
        var splitOut = new GestureDefinition
        {
            Id = "split-out",
            Name = "2F Split Out",
            Points = [new(0.5, 0.5), new(0.51, 0.5)],
            FingerStrokes =
            [
                [new(0.4, 0.5), new(0.1, 0.5)],
                [new(0.6, 0.5), new(0.9, 0.5)]
            ]
        };

        IReadOnlyList<IReadOnlyList<GesturePoint>> candidateFingers =
        [
            new List<GesturePoint> { new(0.42, 0.5), new(0.12, 0.5) },
            new List<GesturePoint> { new(0.58, 0.5), new(0.88, 0.5) }
        ];

        var ranked = _recognizer.RecognizeRanked(
            [new(0.5, 0.5), new(0.51, 0.5)],
            [splitOut],
            threshold: 0.70,
            fingerPaths: candidateFingers);

        Assert.Single(ranked);
        Assert.Equal("split-out", ranked[0].Gesture!.Id);
    }

    [Fact]
    public void RepetitionGate_RequiresNConsecutiveMatchesWithinWindow()
    {
        var gate = new RepetitionGate { RepetitionWindow = TimeSpan.FromSeconds(2) };
        var rule = new GestureRule { Id = "triple", RequiredRepetitions = 3 };
        var t0 = DateTimeOffset.UtcNow;

        var r1 = gate.RegisterMatch(rule, t0);
        Assert.False(r1.ShouldExecute);
        Assert.Equal(1, r1.CurrentCount);

        var r2 = gate.RegisterMatch(rule, t0.AddMilliseconds(500));
        Assert.False(r2.ShouldExecute);
        Assert.Equal(2, r2.CurrentCount);

        var r3 = gate.RegisterMatch(rule, t0.AddMilliseconds(1000));
        Assert.True(r3.ShouldExecute);
        Assert.Equal(3, r3.CurrentCount);

        // Next cycle resets back to 1/3
        var r4 = gate.RegisterMatch(rule, t0.AddMilliseconds(1300));
        Assert.False(r4.ShouldExecute);
        Assert.Equal(1, r4.CurrentCount);
    }

    [Fact]
    public void GestureSequenceTracker_MatchesFourFingerSwipeThenMouseWave()
    {
        var tracker = new GestureSequenceTracker { StepTimeout = TimeSpan.FromSeconds(2) };
        var rule = new GestureRule
        {
            Id = "seq-1",
            GestureId = "builtin-down",
            InputSource = GestureInputSource.Touchpad,
            FingerCount = 4,
            FollowUpGestures =
            [
                new GestureSequenceStep
                {
                    GestureId = "custom-wave",
                    InputSource = GestureInputSource.Mouse,
                    FingerCount = 0
                }
            ]
        };

        var t0 = DateTimeOffset.UtcNow;
        var started = tracker.BeginSequence(rule, t0);
        Assert.Null(started.CompletedRule);
        Assert.Equal(1, started.CurrentStep);
        Assert.Equal(2, started.TotalSteps);

        // Wrong follow-up does not complete sequence
        var wrong = tracker.TryAdvance(["builtin-left"], GestureInputSource.Mouse, 0, t0.AddMilliseconds(400));
        Assert.Null(wrong);

        // Matching mouse wave completes sequence
        var completed = tracker.TryAdvance(["custom-wave"], GestureInputSource.Mouse, 0, t0.AddMilliseconds(800));
        Assert.NotNull(completed);
        Assert.Equal("seq-1", completed!.Value.CompletedRule?.Id);
        Assert.False(tracker.HasActiveSequence);
    }

    [Fact]
    public void ProfileStore_RoundTripsProfileToDisk()
    {
        string tempPath = Path.Combine(Path.GetTempPath(), $"flick-test-{Guid.NewGuid():N}.json");
        try
        {
            var store = new ProfileStore(tempPath);
            var profile = DefaultProfileFactory.CreateDefault();
            profile.Settings.MinimumStrokeLength = 42;
            store.Save(profile);

            var reloaded = store.Load();
            Assert.Equal(42, reloaded.Settings.MinimumStrokeLength);
            Assert.Equal(profile.Gestures.Count, reloaded.Gestures.Count);
            Assert.Equal(profile.Rules.Count, reloaded.Rules.Count);
        }
        finally
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
        }
    }
}
