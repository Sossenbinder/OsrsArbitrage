using ArbitrageTracker.Core.Domain;

namespace ArbitrageTracker.Web.Pipeline;

public sealed class OpportunityCache
{
    private volatile DashboardSnapshot _current =
        new(0, 0, FeedHealthy: false, FeedError: null, Opportunities: Array.Empty<Opportunity>());

    public DashboardSnapshot Current => _current;
    public void Set(DashboardSnapshot snapshot) => _current = snapshot;
}
