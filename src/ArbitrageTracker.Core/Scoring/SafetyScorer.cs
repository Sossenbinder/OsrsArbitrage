using ArbitrageTracker.Core.Domain;

namespace ArbitrageTracker.Core.Scoring;

public static class SafetyScorer
{
    // Liquidity = trading right now; DemandDepth = is this a structurally deep, staple-like market
    // (the "traditional" safety of runes/ammo vs niche PvP items). Together they weight "how real
    // and durable is this market" at 50%.
    public const double LiquidityWeight   = 0.25;
    public const double DemandDepthWeight = 0.25;
    public const double VolatilityWeight  = 0.20;
    public const double PersistenceWeight = 0.15;
    public const double FreshnessWeight   = 0.15;

    public static (double Score, SafetyBreakdown Breakdown) Score(
        long lowVolume, long highVolume,
        double demandDepth,
        IReadOnlyList<long> avgPrices,
        int profitableBuckets, int totalBuckets,
        long ageSeconds)
    {
        var breakdown = new SafetyBreakdown(
            Liquidity:   SafetyComponents.Liquidity(lowVolume, highVolume),
            DemandDepth: Math.Clamp(demandDepth, 0.0, 1.0),
            Volatility:  SafetyComponents.Volatility(avgPrices),
            Persistence: SafetyComponents.Persistence(profitableBuckets, totalBuckets),
            Freshness:   SafetyComponents.Freshness(ageSeconds));

        double score = 100.0 * (
            breakdown.Liquidity   * LiquidityWeight +
            breakdown.DemandDepth * DemandDepthWeight +
            breakdown.Volatility  * VolatilityWeight +
            breakdown.Persistence * PersistenceWeight +
            breakdown.Freshness   * FreshnessWeight);

        return (score, breakdown);
    }
}
