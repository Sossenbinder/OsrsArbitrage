using ArbitrageTracker.Core.Detection;
using ArbitrageTracker.Core.Domain;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace ArbitrageTracker.Core.Tests;

public class DecantDetectorTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.FromUnixTimeSeconds(1_000_000);
    private static FakeTimeProvider Clock() => new(Now);

    // 4-dose sells at 1000 (keep ~980 after 2% tax → 245/dose). 2-dose dumped at 400 (200/dose).
    // perDoseProfit ≈ 245 - 200 = 45. Source = the 2-dose.
    private static DecantInput Family(
        long target4High = 1000, long source2Low = 400, long source2BuyVol = 500,
        long target4SellVol = 500, long sourceTime = 999_900, long targetTime = 999_900,
        int buyLimit = 2000)
    {
        var v4 = new DecantInputVariant(1, "Prayer potion(4)", 4,
            new LatestPrice(1, target4High, targetTime, target4High, targetTime), 10, target4SellVol);
        var v2 = new DecantInputVariant(2, "Prayer potion(2)", 2,
            new LatestPrice(2, source2Low + 50, sourceTime, source2Low, sourceTime), source2BuyVol, 10);
        var buckets = new[] { new MarketBucket(1, 999_900, target4High, target4High - 5, target4SellVol, 10) };
        return new DecantInput("Prayer potion", buyLimit, new[] { v4, v2 }, buckets);
    }

    [Fact]
    public void Detect_findsDecantWhenLowerDoseIsCheaperPerDose()
    {
        var d = new DecantDetector(Clock());
        var opp = d.Detect(Family(), DetectionSettings.Default);

        Assert.NotNull(opp);
        Assert.Equal(2, opp!.SourceDose);
        Assert.Equal(2, opp.SourceItemId);
        Assert.Equal(1, opp.TargetItemId);
        Assert.True(opp.PerDoseProfit > 40 && opp.PerDoseProfit < 50);
        Assert.True(opp.ExpectedCycleProfit > 0);
        Assert.True(opp.SafetyScore > 0);
    }

    [Fact]
    public void Detect_skipsWhenFourDoseIsCheapestPerDose()
    {
        // 4-dose low 800 → 200/dose beats 2-dose 600 → 300/dose; cheapest is the 4-dose → no decant.
        var d = new DecantDetector(Clock());
        Assert.Null(d.Detect(Family(target4High: 800, source2Low: 600), DetectionSettings.Default));
    }

    [Fact]
    public void Detect_rejectsNonProfitablePerDose()
    {
        // 2-dose at 520 → 260/dose > 4-dose keep/4 ≈ 245 → negative per-dose profit.
        var d = new DecantDetector(Clock());
        Assert.Null(d.Detect(Family(target4High: 1000, source2Low: 520), DetectionSettings.Default));
    }

    [Fact]
    public void Detect_rejectsStaleSide()
    {
        var d = new DecantDetector(Clock());
        Assert.Null(d.Detect(Family(sourceTime: 990_000), DetectionSettings.Default)); // source >30m old
    }

    [Fact]
    public void Detect_rejectsThinSourceVolumeWhenGated()
    {
        var d = new DecantDetector(Clock());
        var settings = DetectionSettings.Default with { MinTwoSidedVolume = 20 };
        Assert.Null(d.Detect(Family(source2BuyVol: 0), settings)); // no source buy-side volume
    }
}
