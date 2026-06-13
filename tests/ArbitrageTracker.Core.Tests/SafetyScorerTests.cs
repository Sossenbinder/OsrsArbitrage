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
            avgPrices: new long[] { 1000, 1000, 1000 },
            profitableBuckets: 12, totalBuckets: 12,
            ageSeconds: 0);

        Assert.Equal(100.0, score, precision: 1);
        Assert.Equal(1.0, breakdown.Liquidity, precision: 2);
        Assert.Equal(1.0, breakdown.Volatility, precision: 2);
        Assert.Equal(1.0, breakdown.Persistence, precision: 2);
        Assert.Equal(1.0, breakdown.Freshness, precision: 2);
    }

    [Fact]
    public void Score_deadSecondSideCapsAtSixtyPercent()
    {
        // liquidity = 0 (one-sided), everything else perfect.
        // Max achievable = (0.25 + 0.20 + 0.15) * 100 = 60.
        var (score, _) = SafetyScorer.Score(
            lowVolume: 10_000, highVolume: 0,
            avgPrices: new long[] { 1000, 1000 },
            profitableBuckets: 12, totalBuckets: 12,
            ageSeconds: 0);

        Assert.Equal(60.0, score, precision: 1);
    }

    [Fact]
    public void Score_weightsSumToOne()
    {
        Assert.Equal(1.0,
            SafetyScorer.LiquidityWeight + SafetyScorer.VolatilityWeight
            + SafetyScorer.PersistenceWeight + SafetyScorer.FreshnessWeight,
            precision: 6);
    }
}
