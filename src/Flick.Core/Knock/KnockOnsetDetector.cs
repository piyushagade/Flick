namespace Flick.Core.Knock;

public sealed record KnockOnsetEvent(
    long TimestampMs,
    float[] Features,
    float PeakAmplitude);

/// <summary>
/// Real-time high-pass filtered transient detector with adaptive noise floor
/// and post-onset window capture for acoustic surface classification.
/// </summary>
public sealed class KnockOnsetDetector
{
    private readonly int _sampleRate;
    private readonly float _hpAlpha;
    private float _prevRaw;
    private float _prevHp;

    private readonly float[] _ringBuffer;
    private int _ringWriteIdx;
    private long _totalSamplesProcessed;

    private float _noiseFloorRms = 0.002f;
    private float _prevHopRms = 0.002f;
    private long _lastOnsetSample = -100_000;

    private bool _capturingWindow;
    private long _capturePeakSample;
    private float _capturePeakAmp;
    private long _captureEndSample;
    private long _captureTimestampMs;

    /// <summary>
    /// Detection sensitivity in [0.1 .. 1.0] (default 0.65). Higher = triggers on softer knocks.
    /// </summary>
    public double Sensitivity { get; set; } = 0.65;

    public int RefractoryMs { get; set; } = 75;

    public KnockOnsetDetector(int sampleRate = 48000)
    {
        _sampleRate = Math.Max(8000, sampleRate);
        // 1st-order high-pass filter at ~80 Hz
        double rc = 1.0 / (2.0 * Math.PI * 80.0);
        double dt = 1.0 / _sampleRate;
        _hpAlpha = (float)(rc / (rc + dt));

        // 120 ms ring buffer so we always have pre- and post-onset samples
        _ringBuffer = new float[Math.Max(2048, (int)(_sampleRate * 0.12))];
    }

    public List<KnockOnsetEvent> ProcessSamples(ReadOnlySpan<float> monoSamples, long chunkEndTimestampMs)
    {
        var events = new List<KnockOnsetEvent>();
        if (monoSamples.Length == 0) return events;

        double sens = Math.Clamp(Sensitivity, 0.1, 1.0);
        // Thresholds scale inversely with sensitivity
        float minPeakThreshold = (float)(0.015 + (1.0 - sens) * 0.085); // 0.015 .. 0.10
        float snrMultiplier = (float)(3.2 + (1.0 - sens) * 5.0);      // 3.2x .. 8.2x noise floor
        int refractorySamples = (int)(_sampleRate * (RefractoryMs / 1000.0));
        int postSamples = (int)(_sampleRate * 0.045);
        int preSamples = (int)(_sampleRate * 0.005);
        int windowLen = preSamples + postSamples;

        int hopSize = Math.Max(32, _sampleRate / 200); // 5 ms hop
        double hopSumSq = 0;
        float hopPeak = 0;
        int hopCount = 0;

        for (int i = 0; i < monoSamples.Length; i++)
        {
            float raw = monoSamples[i];
            float hp = _hpAlpha * (_prevHp + raw - _prevRaw);
            _prevRaw = raw;
            _prevHp = hp;

            _ringBuffer[_ringWriteIdx] = hp;
            _ringWriteIdx = (_ringWriteIdx + 1) % _ringBuffer.Length;
            _totalSamplesProcessed++;

            float absHp = Math.Abs(hp);
            hopSumSq += hp * hp;
            if (absHp > hopPeak) hopPeak = absHp;
            hopCount++;

            if (_capturingWindow)
            {
                if (absHp > _capturePeakAmp)
                {
                    _capturePeakAmp = absHp;
                }

                if (_totalSamplesProcessed >= _captureEndSample)
                {
                    var window = ExtractRingWindow(windowLen);
                    var features = KnockFeatureExtractor.Extract(window, _sampleRate);
                    events.Add(new KnockOnsetEvent(_captureTimestampMs, features, _capturePeakAmp));
                    _capturingWindow = false;
                }
            }

            if (hopCount >= hopSize)
            {
                float hopRms = (float)Math.Sqrt(hopSumSq / hopCount);
                float crest = hopRms > 1e-6f ? hopPeak / hopRms : 0f;

                bool pastRefractory = (_totalSamplesProcessed - _lastOnsetSample) >= refractorySamples;
                bool hasSharpAttack = crest >= 1.4f || hopRms >= _prevHopRms * 1.8f;
                bool isTransient = hopPeak >= minPeakThreshold &&
                                   hopRms >= _noiseFloorRms * snrMultiplier &&
                                   hasSharpAttack;

                if (!_capturingWindow && pastRefractory && isTransient)
                {
                    _capturingWindow = true;
                    _capturePeakSample = _totalSamplesProcessed;
                    _capturePeakAmp = hopPeak;
                    _captureEndSample = _totalSamplesProcessed + postSamples;
                    long offsetMs = (long)((monoSamples.Length - 1 - i) * 1000.0 / _sampleRate);
                    _captureTimestampMs = chunkEndTimestampMs - offsetMs;
                    _lastOnsetSample = _totalSamplesProcessed;
                }
                else if (!_capturingWindow && hopRms < _noiseFloorRms * 2.5f)
                {
                    // Slowly adapt noise floor only during quiet periods
                    _noiseFloorRms = 0.96f * _noiseFloorRms + 0.04f * Math.Max(0.0005f, hopRms);
                }

                _prevHopRms = hopRms;
                hopSumSq = 0;
                hopPeak = 0;
                hopCount = 0;
            }
        }

        return events;
    }

    private float[] ExtractRingWindow(int length)
    {
        int len = Math.Min(length, _ringBuffer.Length);
        var result = new float[len];
        int start = (_ringWriteIdx - len + _ringBuffer.Length) % _ringBuffer.Length;
        for (int i = 0; i < len; i++)
        {
            result[i] = _ringBuffer[(start + i) % _ringBuffer.Length];
        }
        return result;
    }
}
