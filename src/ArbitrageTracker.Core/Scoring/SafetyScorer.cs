using ArbitrageTracker.Core.Domain;

namespace ArbitrageTracker.Core.Scoring;

public static class SafetyScorer
{
    public const double LiquidityWeight   = 0.40;
    public const double VolatilityWeight  = 0.25;
    public const double PersistenceWeight = 0.20;
    public const double FreshnessWeight   = 0.15;

    public static (double Score, SafetyBreakdown Breakdown) Score(
        long lowVolume, long highVolume,
        IReadOnlyList<long> avgPrices,
        int profitableBuckets, int totalBuckets,
        long ageSeconds)
    {
        var breakdown = new SafetyBreakdown(
            Liquidity:   SafetyComponents.Liquidity(lowVolume, highVolume),
            Volatility:  SafetyComponents.Volatility(avgPrices),
            Persistence: SafetyComponents.Persistence(profitableBuckets, totalBuckets),
            Freshness:   SafetyComponents.Freshness(ageSeconds));

        double score = 100.0 * (
            breakdown.Liquidity   * LiquidityWeight +
            breakdown.Volatility  * VolatilityWeight +
            breakdown.Persistence * PersistenceWeight +
            breakdown.Freshness   * FreshnessWeight);

        return (score, breakdown);
    }
}
