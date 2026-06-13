using ArbitrageTracker.Core.Pricing;
using Xunit;

namespace ArbitrageTracker.Core.Tests;

public class FlipCalculatorTests
{
    [Fact]
    public void Compute_subtractsTaxFromGrossMargin()
    {
        // buy 1000, sell 1100, tax = 2% of 1100 = 22 → net = 100 - 22 = 78
        var result = FlipCalculator.Compute(buyPrice: 1000, sellPrice: 1100, exempt: false);

        Assert.Equal(1000, result.BuyPrice);
        Assert.Equal(1100, result.SellPrice);
        Assert.Equal(22, result.Tax);
        Assert.Equal(78, result.NetMargin);
    }

    [Fact]
    public void Compute_marginPercentIsRelativeToBuyPrice()
    {
        var result = FlipCalculator.Compute(buyPrice: 1000, sellPrice: 1100, exempt: false);
        // net 78 / buy 1000 = 7.8%
        Assert.Equal(7.8, result.MarginPercent, precision: 3);
    }

    [Fact]
    public void Compute_canBeNegativeAfterTax()
    {
        // buy 1000, sell 1010, tax = 20 → net = 10 - 20 = -10
        var result = FlipCalculator.Compute(buyPrice: 1000, sellPrice: 1010, exempt: false);
        Assert.Equal(-10, result.NetMargin);
    }

    [Fact]
    public void Compute_exemptItemPaysNoTax()
    {
        var result = FlipCalculator.Compute(buyPrice: 1000, sellPrice: 1100, exempt: true);
        Assert.Equal(0, result.Tax);
        Assert.Equal(100, result.NetMargin);
    }
}
