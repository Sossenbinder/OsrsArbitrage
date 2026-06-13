using ArbitrageTracker.Core.Domain;

namespace ArbitrageTracker.Core.Sizing;

public sealed class PositionSizer
{
    public SizedPosition Size(Opportunity opp, SizingSettings settings)
    {
        long budget = settings.PerSlotBudget;
        long byBudget = opp.BuyPrice > 0 ? budget / opp.BuyPrice : 0;
        int qty = (int)Math.Min(opp.BuyLimit, Math.Max(0, byBudget));

        return new SizedPosition(
            ItemId: opp.ItemId,
            SuggestedQuantity: qty,
            CapitalNeeded: qty * opp.BuyPrice,
            ProjectedProfit: qty * opp.NetMargin);
    }

    /// <summary>True if the number of actionable picks exceeds available GE slots.</summary>
    public bool OverflowsSlots(IReadOnlyCollection<Opportunity> opportunities, SizingSettings settings)
        => opportunities.Count > settings.Slots;
}
