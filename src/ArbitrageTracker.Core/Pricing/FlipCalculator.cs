namespace ArbitrageTracker.Core.Pricing;

public readonly record struct FlipResult(
    long BuyPrice,
    long SellPrice,
    long Tax,
    long NetMargin,
    double MarginPercent);

public static class FlipCalculator
{
    /// <summary>buyPrice = instant-sell (low); sellPrice = instant-buy (high).</summary>
    public static FlipResult Compute(long buyPrice, long sellPrice, bool exempt)
    {
        long tax = GrandExchangeTax.Calculate(sellPrice, exempt);
        long net = sellPrice - buyPrice - tax;
        double pct = buyPrice > 0 ? net * 100.0 / buyPrice : 0.0;
        return new FlipResult(buyPrice, sellPrice, tax, net, pct);
    }
}
