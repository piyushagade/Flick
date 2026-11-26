using Flick.Core.Knock;
using Flick.Core.Models;
using Xunit;

namespace Flick.Core.Tests;

public sealed class KnockDetectionTests
{
    private static float[] SynthesizeKnock(int sampleRate, double freq1, double freq2, double decayRate, float amplitude = 0.6f)
    {
        int len = (int)(sampleRate * 0.050);
        var buf = new float[len];
        for (int i = 0; i < len; i++)
        {
            double t = (double)i / sampleRate;
            double env = Math.Exp(-decayRate * t);
            buf[i] = (float)(amplitude * env * (0.65 * Math.Sin(2 * Math.PI * freq1 * t) + 0.35 * Math.Sin(2 * Math.PI * freq2 * t)));
        }
        return buf;
    }

    [Fact]
    public void Fft_FindsDominantFrequencyBin()
    {
        const int sr = 48000;
        const int fftSize = 2048;
        var signal = new float[fftSize];
        for (int i = 0; i < fftSize; i++)
        {
            signal[i] = (float)Math.Sin(2 * Math.PI * 1000.0 * i / sr);
        }

        var mag = KnockFft.ComputeMagnitudeSpectrum(signal, fftSize);
        int peakBin = 0;
        for (int i = 1; i < mag.Length; i++)
        {
            if (mag[i] > mag[peakBin]) peakBin = i;
        }

        double peakHz = peakBin * (double)sr / fftSize;
        Assert.InRange(peakHz, 970, 1030);
    }

    [Fact]
    public void SurfaceClassifier_DistinguishesDeskLidAndTouchpad()
    {
        const int sr = 48000;
        var desk = new KnockSurfaceProfile
        {
            Id = "desk",
            Name = "Desk",
            SampleFeatures =
            [
                KnockFeatureExtractor.Extract(SynthesizeKnock(sr, 150, 260, 55, 0.5f), sr),
                KnockFeatureExtractor.Extract(SynthesizeKnock(sr, 165, 290, 60, 0.7f), sr)
            ]
        };
        var lid = new KnockSurfaceProfile
        {
            Id = "lid",
            Name = "Laptop Lid",
            SampleFeatures =
            [
                KnockFeatureExtractor.Extract(SynthesizeKnock(sr, 650, 1250, 35, 0.5f), sr),
                KnockFeatureExtractor.Extract(SynthesizeKnock(sr, 700, 1320, 40, 0.6f), sr)
            ]
        };
        var touchpad = new KnockSurfaceProfile
        {
            Id = "touchpad",
            Name = "Touchpad",
            SampleFeatures =
            [
                KnockFeatureExtractor.Extract(SynthesizeKnock(sr, 2800, 4600, 140, 0.4f), sr),
                KnockFeatureExtractor.Extract(SynthesizeKnock(sr, 3000, 4900, 150, 0.5f), sr)
            ]
        };

        var surfaces = new[] { desk, lid, touchpad };

        var qDesk = KnockFeatureExtractor.Extract(SynthesizeKnock(sr, 160, 275, 58, 0.4f), sr);
        var qLid = KnockFeatureExtractor.Extract(SynthesizeKnock(sr, 680, 1280, 38, 0.5f), sr);
        var qPad = KnockFeatureExtractor.Extract(SynthesizeKnock(sr, 2900, 4700, 145, 0.35f), sr);

        Assert.Equal("desk", KnockSurfaceClassifier.Classify(qDesk, surfaces).Surface?.Id);
        Assert.Equal("lid", KnockSurfaceClassifier.Classify(qLid, surfaces).Surface?.Id);
        Assert.Equal("touchpad", KnockSurfaceClassifier.Classify(qPad, surfaces).Surface?.Id);
    }

    [Fact]
    public void OnsetDetector_DetectsDoubleKnockAndIgnoresHum()
    {
        const int sr = 48000;
        var detector = new KnockOnsetDetector(sr, warmupMs: 200) { Sensitivity = 0.7 };

        // 800 ms buffer with 50 Hz hum + two knocks after warmup at 350 ms and 550 ms
        int totalSamples = (int)(sr * 0.8);
        var audio = new float[totalSamples];
        for (int i = 0; i < totalSamples; i++)
        {
            audio[i] = 0.02f * (float)Math.Sin(2 * Math.PI * 50.0 * i / sr);
        }

        var k1 = SynthesizeKnock(sr, 900, 1800, 90, 0.5f);
        var k2 = SynthesizeKnock(sr, 900, 1800, 90, 0.5f);
        Array.Copy(k1, 0, audio, (int)(sr * 0.35), k1.Length);
        Array.Copy(k2, 0, audio, (int)(sr * 0.55), k2.Length);

        var events = detector.ProcessSamples(audio, 800);
        Assert.Equal(2, events.Count);
        Assert.InRange(events[1].TimestampMs - events[0].TimestampMs, 160, 240);
    }

    [Fact]
    public void PatternMatcher_MatchesSurfaceAndRhythm()
    {
        const int sr = 48000;
        var touchpad = new KnockSurfaceProfile
        {
            Id = "pad",
            Name = "Touchpad",
            SampleFeatures = [KnockFeatureExtractor.Extract(SynthesizeKnock(sr, 2900, 4700, 145), sr)]
        };
        var desk = new KnockSurfaceProfile
        {
            Id = "desk",
            Name = "Desk",
            SampleFeatures = [KnockFeatureExtractor.Extract(SynthesizeKnock(sr, 160, 275, 58), sr)]
        };

        var rule = new GestureRule
        {
            Name = "Touchpad Rhythm",
            TriggerType = TriggerType.KnockPattern,
            KnockPattern = new KnockPatternDefinition
            {
                KnockCount = 3,
                SurfaceProfileId = "pad",
                SurfaceName = "Touchpad",
                RhythmRatios = [1.0, 2.2]
            }
        };

        var matcher = new KnockPatternMatcher();
        var padFeat = KnockFeatureExtractor.Extract(SynthesizeKnock(sr, 2850, 4650, 140), sr);
        matcher.AddKnock(new KnockOnsetEvent(100, padFeat, 0.4f));
        matcher.AddKnock(new KnockOnsetEvent(250, padFeat, 0.4f)); // +150ms
        matcher.AddKnock(new KnockOnsetEvent(580, padFeat, 0.4f)); // +330ms (ratio 1.0 : 2.2)

        var match = matcher.TryFinalizeIfExpired(1100, 450, [rule], [touchpad, desk], "notepad", null);
        Assert.NotNull(match);
        Assert.Equal("Touchpad Rhythm", match.Rule.Name);
        Assert.Equal("Touchpad", match.SurfaceName);
    }
}
