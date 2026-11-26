namespace Flick.Core.Knock;

/// <summary>
/// Extracts a 16-dimensional acoustic signature from a ~50 ms knock impulse window
/// to distinguish surfaces (touchpad, laptop lid, desk, etc.) independently of volume.
/// </summary>
public static class KnockFeatureExtractor
{
    public const int FeatureCount = 16;

    private static readonly (double LowHz, double HighHz)[] Bands =
    [
        (80, 200),
        (200, 450),
        (450, 900),
        (900, 1800),
        (1800, 3500),
        (3500, 6500),
        (6500, 11000),
        (11000, 20000)
    ];

    public static float[] Extract(ReadOnlySpan<float> window, int sampleRate)
    {
        var features = new float[FeatureCount];
        if (window.Length < 32 || sampleRate <= 0)
        {
            return features;
        }

        const int fftSize = 2048;
        var mag = KnockFft.ComputeMagnitudeSpectrum(window, fftSize);
        double binHz = (double)sampleRate / fftSize;
        double nyquist = sampleRate * 0.5;

        // 0..7: 8-band spectral energy distribution (normalized)
        double[] bandEnergy = new double[Bands.Length];
        double totalEnergy = 1e-12;
        double weightedFreqSum = 0;
        double totalMag = 1e-12;
        double logMagSum = 0;
        int validBins = 0;

        for (int b = 1; b < mag.Length; b++)
        {
            double freq = b * binHz;
            if (freq < 80) continue;

            double m = mag[b];
            double e = m * m;
            totalEnergy += e;
            totalMag += m;
            weightedFreqSum += freq * m;
            logMagSum += Math.Log(m + 1e-9);
            validBins++;

            for (int band = 0; band < Bands.Length; band++)
            {
                if (freq >= Bands[band].LowHz && freq < Bands[band].HighHz)
                {
                    bandEnergy[band] += e;
                    break;
                }
            }
        }

        for (int i = 0; i < Bands.Length; i++)
        {
            features[i] = (float)(bandEnergy[i] / totalEnergy);
        }

        // 8: Spectral Centroid (normalized 0..1)
        double centroidHz = weightedFreqSum / totalMag;
        features[8] = (float)Math.Clamp(centroidHz / nyquist, 0.0, 1.0);

        // 9: Spectral Rolloff (85% energy frequency, normalized 0..1)
        double rolloffTarget = totalEnergy * 0.85;
        double accum = 0;
        double rolloffHz = nyquist;
        for (int b = 1; b < mag.Length; b++)
        {
            double freq = b * binHz;
            if (freq < 80) continue;
            accum += mag[b] * mag[b];
            if (accum >= rolloffTarget)
            {
                rolloffHz = freq;
                break;
            }
        }
        features[9] = (float)Math.Clamp(rolloffHz / nyquist, 0.0, 1.0);

        // 10: Spectral Flatness (geometric mean / arithmetic mean)
        double flatness = validBins > 0
            ? Math.Exp(logMagSum / validBins) / (totalMag / validBins)
            : 0;
        features[10] = (float)Math.Clamp(flatness, 0.0, 1.0);

        // 11: Zero-Crossing Rate over first 15 ms
        int zcrLen = Math.Min(window.Length, Math.Max(16, (int)(sampleRate * 0.015)));
        int crossings = 0;
        for (int i = 1; i < zcrLen; i++)
        {
            if ((window[i] >= 0 && window[i - 1] < 0) || (window[i] < 0 && window[i - 1] >= 0))
            {
                crossings++;
            }
        }
        features[11] = (float)crossings / zcrLen;

        // 12..14: Temporal decay / ring-out ratios relative to [0..5ms] attack
        double attackRms = ComputeSliceRms(window, 0, (int)(sampleRate * 0.008)) + 1e-6;
        double decay1 = ComputeSliceRms(window, (int)(sampleRate * 0.008), (int)(sampleRate * 0.018));
        double decay2 = ComputeSliceRms(window, (int)(sampleRate * 0.018), (int)(sampleRate * 0.032));
        double decay3 = ComputeSliceRms(window, (int)(sampleRate * 0.032), window.Length);

        features[12] = (float)Math.Clamp(decay1 / attackRms, 0.0, 2.0);
        features[13] = (float)Math.Clamp(decay2 / attackRms, 0.0, 2.0);
        features[14] = (float)Math.Clamp(decay3 / attackRms, 0.0, 2.0);

        // 15: Low-to-High energy ratio (desk thud vs crisp touchpad tap)
        double lowE = bandEnergy[0] + bandEnergy[1];
        double highE = bandEnergy[4] + bandEnergy[5] + 1e-9;
        features[15] = (float)Math.Clamp(Math.Log10(1.0 + lowE / highE) / 3.0, 0.0, 1.0);

        return features;
    }

    private static double ComputeSliceRms(ReadOnlySpan<float> data, int start, int end)
    {
        int s = Math.Clamp(start, 0, data.Length);
        int e = Math.Clamp(end, s, data.Length);
        int count = e - s;
        if (count == 0) return 0;

        double sum = 0;
        for (int i = s; i < e; i++)
        {
            sum += data[i] * data[i];
        }
        return Math.Sqrt(sum / count);
    }
}
