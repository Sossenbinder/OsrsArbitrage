using ArbitrageTracker.Core.Pricing;
using Xunit;

namespace ArbitrageTracker.Core.Tests;

public class GrandExchangeTaxTests
{
    [Theory]
    [InlineData(49, 0)]        // below 50 → 0
    [InlineData(50, 1)]        // 2% of 50 = 1
    [InlineData(100, 2)]       // 2%
    [InlineData(1000, 20)]     // 2%
    [InlineData(1_000_000, 20_000)]
    [InlineData(250_000_000, 5_000_000)]   // exactly at cap
    [InlineData(500_000_000, 5_000_000)]   // capped
    public void Calculate_appliesTwoPercentFlooredAndCapped(long sellPrice, long expected)
    {
        Assert.Equal(expected, GrandExchangeTax.Calculate(sellPrice, exempt: false));
    }

    [Fact]
    public void Calculate_floorsFractionalTax()
    {
        // 2% of 99 = 1.98 → floor 1
        Assert.Equal(1, GrandExchangeTax.Calculate(99, exempt: false));
    }

    [Fact]
    public void Calculate_returnsZeroForExemptItems()
    {
        Assert.Equal(0, GrandExchangeTax.Calculate(10_000_000, exempt: true));
    }
}
