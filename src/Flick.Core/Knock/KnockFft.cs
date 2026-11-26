namespace Flick.Core.Knock;

/// <summary>
/// Radix-2 Cooley-Tukey FFT for real-valued audio frames.
/// </summary>
public static class KnockFft
{
    public static float[] ComputeMagnitudeSpectrum(ReadOnlySpan<float> input, int fftSize)
    {
        if ((fftSize & (fftSize - 1)) != 0 || fftSize < 16)
        {
            throw new ArgumentException("fftSize must be a power of two >= 16.", nameof(fftSize));
        }

        var re = new double[fftSize];
        var im = new double[fftSize];

        int copyLen = Math.Min(input.Length, fftSize);
        for (int i = 0; i < copyLen; i++)
        {
            double window = 0.5 * (1.0 - Math.Cos(2.0 * Math.PI * i / (copyLen - 1)));
            re[i] = input[i] * window;
        }

        // Bit-reversal permutation
        int j = 0;
        for (int i = 1; i < fftSize; i++)
        {
            int bit = fftSize >> 1;
            while ((j & bit) != 0)
            {
                j ^= bit;
                bit >>= 1;
            }
            j ^= bit;

            if (i < j)
            {
                (re[i], re[j]) = (re[j], re[i]);
                (im[i], im[j]) = (im[j], im[i]);
            }
        }

        // Butterfly stages
        for (int len = 2; len <= fftSize; len <<= 1)
        {
            double angle = -2.0 * Math.PI / len;
            double wLenRe = Math.Cos(angle);
            double wLenIm = Math.Sin(angle);

            for (int i = 0; i < fftSize; i += len)
            {
                double wRe = 1.0;
                double wIm = 0.0;
                int half = len >> 1;

                for (int k = 0; k < half; k++)
                {
                    int evenIdx = i + k;
                    int oddIdx = evenIdx + half;

                    double tRe = wRe * re[oddIdx] - wIm * im[oddIdx];
                    double tIm = wRe * im[oddIdx] + wIm * re[oddIdx];

                    re[oddIdx] = re[evenIdx] - tRe;
                    im[oddIdx] = im[evenIdx] - tIm;
                    re[evenIdx] += tRe;
                    im[evenIdx] += tIm;

                    double nextWRe = wRe * wLenRe - wIm * wLenIm;
                    wIm = wRe * wLenIm + wIm * wLenRe;
                    wRe = nextWRe;
                }
            }
        }

        int bins = fftSize / 2;
        var mag = new float[bins];
        for (int i = 0; i < bins; i++)
        {
            mag[i] = (float)Math.Sqrt(re[i] * re[i] + im[i] * im[i]);
        }
        return mag;
    }
}
