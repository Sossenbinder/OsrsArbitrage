using ArbitrageTracker.Core.Domain;

namespace ArbitrageTracker.Core.Scoring;

public static class SafetyComponents
{
    public const double LiquiditySaturation = 10_000;
    public const double MaxCoefficientOfVariation = 0.10;
    public const double MaxFreshnessAgeSeconds = 1800;
    public const double DepthSaturationPer5m = 2000;

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

    /// <summary>
    /// "Traditional" / structural safety: how deep and continuous the item's market is across the
    /// whole observed window, rather than in one instant. Staples (runes, ammo, food) trade in
    /// every window in large size → high; niche items (PvP/wilderness consumables) trade
    /// sporadically in small size → low, even if one window momentarily looks fine.
    /// Blends breadth (fraction of buckets with two-sided trading) and magnitude (log-scaled
    /// average two-sided volume per bucket).
    /// </summary>
    public static double DemandDepth(IReadOnlyList<MarketBucket> buckets)
    {
        if (buckets.Count == 0) return 0.0;

        int active = 0;
        double sumTwoSided = 0;
        foreach (var b in buckets)
        {
            long twoSided = Math.Min(b.LowPriceVolume, b.HighPriceVolume);
            if (twoSided > 0) active++;
            sumTwoSided += twoSided;
        }

        double breadth = (double)active / buckets.Count;
        double avgPerBucket = sumTwoSided / buckets.Count;
        double magnitude = Math.Clamp(
            Math.Log10(avgPerBucket + 1) / Math.Log10(DepthSaturationPer5m + 1), 0.0, 1.0);

        // Magnitude-dominant: absolute two-sided throughput is the feed-robust signal of a deep
        // staple market. Breadth is a lighter modifier (the /5m feed omits windows with no trade,
        // so it can read high even for sporadic items — it can't be trusted to carry the weight).
        return Math.Clamp(0.75 * magnitude + 0.25 * breadth, 0.0, 1.0);
    }

    public static double Freshness(long ageSeconds)
        => Math.Clamp(1.0 - ageSeconds / MaxFreshnessAgeSeconds, 0.0, 1.0);
}
