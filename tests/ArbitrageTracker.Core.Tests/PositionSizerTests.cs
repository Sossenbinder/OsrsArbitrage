using ArbitrageTracker.Core.Domain;
using ArbitrageTracker.Core.Sizing;
using Xunit;

namespace ArbitrageTracker.Core.Tests;

public class PositionSizerTests
{
    private static Opportunity Opp(int id, long buy, long netMargin, int buyLimit) =>
        new(id, $"Item{id}", buy, buy + netMargin, 0, netMargin, 1.0, buyLimit,
            buyLimit * netMargin, 50.0, new SafetyBreakdown(1, 1, 1, 1), 0, 1.0);

    [Fact]
    public void Size_limitedByPerSlotBudget()
    {
        var settings = new SizingSettings { Bankroll = 8_000_000 }; // per slot = 1,000,000
        var sizer = new PositionSizer();
        // buy 1000, limit 5000 → budget allows 1000 units, limit allows 5000 → 1000
        var sized = sizer.Size(Opp(1, buy: 1000, netMargin: 50, buyLimit: 5000), settings);

        Assert.Equal(1000, sized.SuggestedQuantity);
        Assert.Equal(1_000_000, sized.CapitalNeeded);
        Assert.Equal(50_000, sized.ProjectedProfit);
    }

    [Fact]
    public void Size_limitedByBuyLimit()
    {
        var settings = new SizingSettings { Bankroll = 8_000_000 };
        var sizer = new PositionSizer();
        // budget allows 1000 units but limit is 100 → 100
        var sized = sizer.Size(Opp(1, buy: 1000, netMargin: 50, buyLimit: 100), settings);
        Assert.Equal(100, sized.SuggestedQuantity);
        Assert.Equal(100_000, sized.CapitalNeeded);
    }

    [Fact]
    public void Size_zeroQuantityWhenBudgetBelowUnitPrice()
    {
        var settings = new SizingSettings { Bankroll = 4000 }; // per slot 500, buy 1000
        var sizer = new PositionSizer();
        var sized = sizer.Size(Opp(1, buy: 1000, netMargin: 50, buyLimit: 100), settings);
        Assert.Equal(0, sized.SuggestedQuantity);
    }

    [Fact]
    public void Overflows_trueWhenMorePicksThanSlots()
    {
        var settings = new SizingSettings { Bankroll = 1_000_000_000, Slots = 8 };
        var sizer = new PositionSizer();
        var opps = Enumerable.Range(1, 9).Select(i => Opp(i, 1000, 50, 100)).ToList();
        Assert.True(sizer.OverflowsSlots(opps, settings));
    }
}
