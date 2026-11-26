using Flick.Core.Models;

namespace Flick.Core.Knock;

public sealed record SurfaceClassificationResult(
    KnockSurfaceProfile? Surface,
    double Confidence);

/// <summary>
/// Classifies a knock's acoustic feature vector against calibrated surface profiles
/// using cosine similarity + normalized Euclidean distance.
/// </summary>
public static class KnockSurfaceClassifier
{
    public static SurfaceClassificationResult Classify(
        float[] queryFeatures,
        IReadOnlyList<KnockSurfaceProfile> surfaces,
        double minConfidence = 0.55)
    {
        if (queryFeatures.Length == 0 || surfaces.Count == 0)
        {
            return new SurfaceClassificationResult(null, 0);
        }

        var calibrated = surfaces.Where(s => s.SampleFeatures.Count > 0).ToList();
        if (calibrated.Count == 0)
        {
            return new SurfaceClassificationResult(null, 0);
        }

        // Compute per-feature std across all samples for standardized distance
        int dims = queryFeatures.Length;
        double[] globalMean = new double[dims];
        int totalSamples = 0;
        foreach (var s in calibrated)
        {
            foreach (var vec in s.SampleFeatures)
            {
                if (vec.Length != dims) continue;
                for (int d = 0; d < dims; d++) globalMean[d] += vec[d];
                totalSamples++;
            }
        }

        if (totalSamples == 0)
        {
            return new SurfaceClassificationResult(null, 0);
        }

        for (int d = 0; d < dims; d++) globalMean[d] /= totalSamples;

        double[] globalStd = new double[dims];
        foreach (var s in calibrated)
        {
            foreach (var vec in s.SampleFeatures)
            {
                if (vec.Length != dims) continue;
                for (int d = 0; d < dims; d++)
                {
                    double diff = vec[d] - globalMean[d];
                    globalStd[d] += diff * diff;
                }
            }
        }

        for (int d = 0; d < dims; d++)
        {
            globalStd[d] = Math.Max(0.02, Math.Sqrt(globalStd[d] / totalSamples));
        }

        KnockSurfaceProfile? bestSurface = null;
        double bestScore = double.NegativeInfinity;
        double secondBestScore = double.NegativeInfinity;

        foreach (var surface in calibrated)
        {
            // Nearest-centroid + best-sample blend
            var centroid = ComputeCentroid(surface.SampleFeatures, dims);
            double centroidSim = CombinedSimilarity(queryFeatures, centroid, globalStd);
            double maxSampleSim = 0;
            foreach (var vec in surface.SampleFeatures)
            {
                if (vec.Length != dims) continue;
                double sim = CombinedSimilarity(queryFeatures, vec, globalStd);
                if (sim > maxSampleSim) maxSampleSim = sim;
            }

            double score = 0.6 * centroidSim + 0.4 * maxSampleSim;
            if (score > bestScore)
            {
                secondBestScore = bestScore;
                bestScore = score;
                bestSurface = surface;
            }
            else if (score > secondBestScore)
            {
                secondBestScore = score;
            }
        }

        double confidence = Math.Clamp(bestScore, 0.0, 1.0);
        if (bestSurface is null || confidence < minConfidence)
        {
            return new SurfaceClassificationResult(null, confidence);
        }

        return new SurfaceClassificationResult(bestSurface, confidence);
    }

    private static float[] ComputeCentroid(List<float[]> samples, int dims)
    {
        var c = new float[dims];
        int count = 0;
        foreach (var vec in samples)
        {
            if (vec.Length != dims) continue;
            for (int d = 0; d < dims; d++) c[d] += vec[d];
            count++;
        }
        if (count > 0)
        {
            for (int d = 0; d < dims; d++) c[d] /= count;
        }
        return c;
    }

    private static double CombinedSimilarity(float[] a, float[] b, double[] std)
    {
        double dot = 0, normA = 0, normB = 0, zDistSq = 0;
        for (int i = 0; i < a.Length; i++)
        {
            dot += a[i] * b[i];
            normA += a[i] * a[i];
            normB += b[i] * b[i];

            double zd = (a[i] - b[i]) / std[i];
            zDistSq += zd * zd;
        }

        double cosine = (normA > 1e-9 && normB > 1e-9) ? dot / Math.Sqrt(normA * normB) : 0;
        double zDist = Math.Sqrt(zDistSq / a.Length);
        double distSim = Math.Exp(-0.45 * zDist);
        return 0.55 * Math.Max(0, cosine) + 0.45 * distSim;
    }
}
