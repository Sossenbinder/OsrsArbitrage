using ArbitrageTracker.Core.Detection;
using ArbitrageTracker.Core.Domain;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace ArbitrageTracker.Core.Tests;

public class OpportunityDetectorTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.FromUnixTimeSeconds(1_000_000);

    private static ItemMapping Map(int id, int buyLimit, bool members = true) =>
        new(id, $"Item{id}", "examine", members, 1, 2, buyLimit, 50, "icon.png");

    private static FakeTimeProvider Clock() => new(Now);

    private static ItemSnapshot Snapshot(
        long high, long low, int buyLimit = 1000,
        long highTime = 999_900, long lowTime = 999_900,
        long recentBuyVolume = 1000)
    {
        var map = Map(1, buyLimit);
        var latest = new LatestPrice(1, high, highTime, low, lowTime);
        var bucket = new MarketBucket(1, 999_900, high, low, 500, recentBuyVolume);
        return new ItemSnapshot(map, latest, new[] { bucket }, Array.Empty<MarketBucket>());
    }

    [Fact]
    public void Detect_returnsOpportunityForHealthyFlip()
    {
        var detector = new OpportunityDetector(Clock());
        // buy 1000, sell 1100, tax 22, net 78; limit 1000, demand 1000 → cycle profit 78,000
        var opp = detector.Detect(Snapshot(high: 1100, low: 1000), DetectionSettings.Default);

        Assert.NotNull(opp);
        Assert.Equal(78, opp!.NetMargin);
        Assert.Equal(78_000, opp.ExpectedCycleProfit);
        Assert.True(opp.SafetyScore > 0);
    }

    [Fact]
    public void Detect_rejectsWhenUnitPriceAboveCap()
    {
        var detector = new OpportunityDetector(Clock());
        var settings = DetectionSettings.Default with { MaxUnitPrice = 500 };
        Assert.Null(detector.Detect(Snapshot(high: 1100, low: 1000), settings));
    }

    [Fact]
    public void Detect_rejectsWhenCycleProfitBelowMinimum()
    {
        var detector = new OpportunityDetector(Clock());
        // limit 1 → cycle profit 78, below an explicit 50_000 floor (the gate mechanism still works
        // when a threshold is supplied; it just defaults to off now).
        var settings = DetectionSettings.Default with { MinCycleProfit = 50_000 };
        Assert.Null(detector.Detect(Snapshot(high: 1100, low: 1000, buyLimit: 1, recentBuyVolume: 30), settings));
    }

    [Fact]
    public void Detect_rejectsWhenOlderSideIsStale()
    {
        var detector = new OpportunityDetector(Clock());
        // Sell side fresh, buy side traded long ago. max() would pass; min() (older side) rejects.
        var snap = Snapshot(high: 1100, low: 1000, highTime: 999_950, lowTime: 990_000);
        Assert.Null(detector.Detect(snap, DetectionSettings.Default));
    }

    [Fact]
    public void Detect_rejectsOneSidedMarketByDefault()
    {
        var detector = new OpportunityDetector(Clock());
        // No buy-side volume at all → not genuinely two-sided; rejected by the validity gate even
        // with all preference floors off.
        Assert.Null(detector.Detect(Snapshot(high: 1100, low: 1000, recentBuyVolume: 0),
            DetectionSettings.Default));
    }

    [Fact]
    public void Detect_marginFloorIsOffByDefaultButEnforcedWhenSet()
    {
        var detector = new OpportunityDetector(Clock());
        // buy 1000, sell 1030 → 1.0% margin. Allowed by default (no floor)…
        Assert.NotNull(detector.Detect(Snapshot(high: 1030, low: 1000), DetectionSettings.Default));
        // …but rejected when the user applies a 2% floor.
        var settings = DetectionSettings.Default with { MinMarginPercent = 2.0 };
        Assert.Null(detector.Detect(Snapshot(high: 1030, low: 1000), settings));
    }

    [Fact]
    public void Detect_populatesRecentVolumes()
    {
        var detector = new OpportunityDetector(Clock());
        var opp = detector.Detect(Snapshot(high: 1100, low: 1000, recentBuyVolume: 1234), DetectionSettings.Default);
        Assert.Equal(1234, opp!.BuyVolume5m);   // last 5m low (buy-side) volume
        Assert.Equal(500, opp.SellVolume5m);    // bucket HighPriceVolume fixed at 500 in helper
    }

    [Fact]
    public void Detect_rejectsImplausiblyHighMargin()
    {
        var detector = new OpportunityDetector(Clock());
        // buy 100, sell 43_739 → ~427x margin: stale/illiquid bad data, not real arbitrage.
        var snap = Snapshot(high: 43_739, low: 100, buyLimit: 1000, recentBuyVolume: 1000);
        Assert.Null(detector.Detect(snap, DetectionSettings.Default));
    }

    [Fact]
    public void Detect_rejectsNegativeMarginAfterTax()
    {
        var detector = new OpportunityDetector(Clock());
        // buy 1000 sell 1010 tax 20 → net -10
        Assert.Null(detector.Detect(Snapshot(high: 1010, low: 1000), DetectionSettings.Default));
    }

    [Fact]
    public void Detect_rejectsStalePrices()
    {
        var detector = new OpportunityDetector(Clock());
        // lowTime far in the past → age > 1800s
        var snap = Snapshot(high: 1100, low: 1000, highTime: 990_000, lowTime: 990_000);
        Assert.Null(detector.Detect(snap, DetectionSettings.Default));
    }

    [Fact]
    public void Detect_rejectsMissingPriceSide()
    {
        var detector = new OpportunityDetector(Clock());
        var map = Map(1, 1000);
        var latest = new LatestPrice(1, High: 1100, HighTime: 999_900, Low: null, LowTime: 0);
        var snap = new ItemSnapshot(map, latest, Array.Empty<MarketBucket>(), Array.Empty<MarketBucket>());
        Assert.Null(detector.Detect(snap, DetectionSettings.Default));
    }

    [Fact]
    public void Detect_respectsDenyList()
    {
        var detector = new OpportunityDetector(Clock());
        var settings = DetectionSettings.Default with { DenyList = new HashSet<int> { 1 } };
        Assert.Null(detector.Detect(Snapshot(high: 1100, low: 1000), settings));
    }

    [Fact]
    public void Detect_cycleProfitCappedByBuyLimitNotVolume()
    {
        var detector = new OpportunityDetector(Clock());
        // limit 100, demand 5000 → capped at 100 units * 78 = 7800.
        var settings = DetectionSettings.Default with { MinCycleProfit = 0 };
        var opp = detector.Detect(Snapshot(high: 1100, low: 1000, buyLimit: 100, recentBuyVolume: 5000), settings);
        Assert.Equal(7800, opp!.ExpectedCycleProfit);
    }
}
