using ArbitrageTracker.Core.Scoring;
using Xunit;

namespace ArbitrageTracker.Core.Tests;

public class SafetyComponentsTests
{
    [Fact]
    public void Liquidity_isZeroWhenEitherSideIsZero()
    {
        Assert.Equal(0.0, SafetyComponents.Liquidity(0, 5000), precision: 6);
        Assert.Equal(0.0, SafetyComponents.Liquidity(5000, 0), precision: 6);
    }

    [Fact]
    public void Liquidity_reachesOneAtSaturation()
    {
        Assert.Equal(1.0, SafetyComponents.Liquidity(10_000, 10_000), precision: 3);
    }

    [Fact]
    public void Liquidity_isBetweenZeroAndOneForModerateVolume()
    {
        double s = SafetyComponents.Liquidity(100, 100);
        Assert.InRange(s, 0.4, 0.8);
    }

    [Fact]
    public void Volatility_isOneForFlatPrices()
    {
        Assert.Equal(1.0, SafetyComponents.Volatility(new long[] { 1000, 1000, 1000 }), precision: 6);
    }

    [Fact]
    public void Volatility_isOneForEmptyOrSingle()
    {
        Assert.Equal(1.0, SafetyComponents.Volatility(Array.Empty<long>()), precision: 6);
        Assert.Equal(1.0, SafetyComponents.Volatility(new long[] { 1000 }), precision: 6);
    }

    [Fact]
    public void Volatility_dropsAsPricesSwing()
    {
        // ~10% CV should floor to ~0
        double s = SafetyComponents.Volatility(new long[] { 900, 1100, 900, 1100 });
        Assert.InRange(s, 0.0, 0.2);
    }

    [Fact]
    public void Persistence_isRatioOfProfitableBuckets()
    {
        Assert.Equal(0.75, SafetyComponents.Persistence(profitableBuckets: 9, totalBuckets: 12), precision: 6);
        Assert.Equal(0.0, SafetyComponents.Persistence(0, 0), precision: 6);
    }

    [Fact]
    public void Freshness_decaysLinearlyToZeroAtMaxAge()
    {
        Assert.Equal(1.0, SafetyComponents.Freshness(0), precision: 6);
        Assert.Equal(0.5, SafetyComponents.Freshness(900), precision: 3);
        Assert.Equal(0.0, SafetyComponents.Freshness(1800), precision: 6);
        Assert.Equal(0.0, SafetyComponents.Freshness(5000), precision: 6); // clamped
    }
}
