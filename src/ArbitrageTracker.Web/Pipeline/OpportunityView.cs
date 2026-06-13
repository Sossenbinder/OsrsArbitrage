using ArbitrageTracker.Core.Domain;

namespace ArbitrageTracker.Web.Pipeline;

/// <summary>An opportunity enriched with bankroll-aware position sizing, pushed to the UI.</summary>
public sealed record OpportunityView(
    Opportunity Opp,
    int SuggestedQuantity,
    long CapitalNeeded,
    long ProjectedProfit);
