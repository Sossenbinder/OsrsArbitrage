namespace ArbitrageTracker.Core.Scoring;

public static class SafetyComponents
{
    public const double LiquiditySaturation = 10_000;
    public const double MaxCoefficientOfVariation = 0.10;
    public const double MaxFreshnessAgeSeconds = 1800;

    public static double Liquidity(long lowVolume, long highVolume)
    {
        if (lowVolume <= 0 || highVolume <= 0)
            return 0.0;

        double geo = Math.Sqrt((double)lowVolume * highVolume);
        double score = Math.Log10(geo + 1) / Math.Log10(LiquiditySaturation + 1);
        return Math.Clamp(score, 0.0, 1.0);
    }

    public static double Volatility(IReadOnlyList<long> avgPrices)
    {
        if (avgPrices.Count < 2)
            return 1.0;

        double mean = 0;
        foreach (long p in avgPrices) mean += p;
        mean /= avgPrices.Count;
        if (mean <= 0) return 1.0;

        double sumSq = 0;
        foreach (long p in avgPrices) sumSq += (p - mean) * (p - mean);
        double stdDev = Math.Sqrt(sumSq / avgPrices.Count);
        double cv = stdDev / mean;

        return Math.Clamp(1.0 - cv / MaxCoefficientOfVariation, 0.0, 1.0);
    }

    public static double Persistence(int profitableBuckets, int totalBuckets)
        => totalBuckets <= 0 ? 0.0 : (double)profitableBuckets / totalBuckets;

    public static double Freshness(long ageSeconds)
        => Math.Clamp(1.0 - ageSeconds / MaxFreshnessAgeSeconds, 0.0, 1.0);
}
