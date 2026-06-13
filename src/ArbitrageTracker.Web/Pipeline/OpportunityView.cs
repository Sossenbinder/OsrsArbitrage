using ArbitrageTracker.Core.Domain;

namespace ArbitrageTracker.Web.Pipeline;

/// <summary>An opportunity enriched with bankroll-aware sizing and fill realism, pushed to the UI.</summary>
public sealed record OpportunityView(
    Opportunity Opp,
    int SuggestedQuantity,
    long CapitalNeeded,
    long ProjectedProfit,
    double EstFillHours,
    bool Affordable);

/// <summary>
/// One push to the dashboard: the ranked picks plus the health of the upstream price feed,
/// so the client can refuse to present stale/dead data as if it were live.
/// </summary>
public sealed record DashboardSnapshot(
    long GeneratedAtUnix,
    long FeedAgeSeconds,
    bool FeedHealthy,
    string? FeedError,
    IReadOnlyList<OpportunityView> Opportunities);
