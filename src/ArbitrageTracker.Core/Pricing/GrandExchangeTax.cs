namespace ArbitrageTracker.Core.Pricing;

/// <summary>
/// OSRS Grand Exchange sell tax: 2% of sell price, rounded down, capped at 5,000,000 gp/item.
/// Items below 50 gp and exempt items pay nothing. (Rate raised 1% → 2% on 2025-05-29.)
/// </summary>
public static class GrandExchangeTax
{
    public const long MaxTaxPerItem = 5_000_000;
    public const long MinTaxablePrice = 50;

    public static long Calculate(long sellPrice, bool exempt)
    {
        if (exempt || sellPrice < MinTaxablePrice)
            return 0;

        // 2% with integer floor: (sellPrice * 2) / 100.
        long tax = sellPrice * 2 / 100;
        return Math.Min(tax, MaxTaxPerItem);
    }
}
