using ArbitrageTracker.Core.Scoring;
using Xunit;

namespace ArbitrageTracker.Core.Tests;

public class SafetyScorerTests
{
    [Fact]
    public void Score_allPerfectComponentsIsHundred()
    {
        var (score, breakdown) = SafetyScorer.Score(
            lowVolume: 10_000, highVolume: 10_000,
            demandDepth: 1.0,
            avgPrices: new long[] { 1000, 1000, 1000 },
            profitableBuckets: 12, totalBuckets: 12,
            ageSeconds: 0);

        Assert.Equal(100.0, score, precision: 1);
        Assert.Equal(1.0, breakdown.Liquidity, precision: 2);
        Assert.Equal(1.0, breakdown.DemandDepth, precision: 2);
        Assert.Equal(1.0, breakdown.Volatility, precision: 2);
        Assert.Equal(1.0, breakdown.Persistence, precision: 2);
        Assert.Equal(1.0, breakdown.Freshness, precision: 2);
    }

    [Fact]
    public void Score_oneSidedMarketScoresLow()
    {
        // Dead sell side → no liquidity and no depth; everything else perfect.
        // Max achievable = (0.20 + 0.15 + 0.15) * 100 = 50.
        var (score, _) = SafetyScorer.Score(
            lowVolume: 10_000, highVolume: 0,
            demandDepth: 0.0,
            avgPrices: new long[] { 1000, 1000 },
            profitableBuckets: 12, totalBuckets: 12,
            ageSeconds: 0);

        Assert.Equal(50.0, score, precision: 1);
    }

    [Fact]
    public void Score_thinNicheMarketScoresBelowStaple()
    {
        // Identical instantaneous liquidity/volatility/persistence/freshness, but a niche item
        // with shallow structural demand must score lower than a deep staple.
        var common = (low: 5_000L, high: 5_000L, prices: (IReadOnlyList<long>)new long[] { 1000, 1000 }, prof: 12, total: 12, age: 0L);
        var (staple, _) = SafetyScorer.Score(common.low, common.high, 1.0, common.prices, common.prof, common.total, common.age);
        var (niche, _)  = SafetyScorer.Score(common.low, common.high, 0.1, common.prices, common.prof, common.total, common.age);

        Assert.True(staple > niche);
        Assert.True(staple - niche >= 20.0); // depth carries 25% weight → ~22.5 point gap
    }

    [Fact]
    public void Score_weightsSumToOne()
    {
        Assert.Equal(1.0,
            SafetyScorer.LiquidityWeight + SafetyScorer.DemandDepthWeight
            + SafetyScorer.VolatilityWeight + SafetyScorer.PersistenceWeight
            + SafetyScorer.FreshnessWeight,
            precision: 6);
    }
}
