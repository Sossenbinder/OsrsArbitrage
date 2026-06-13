using ArbitrageTracker.Core.Domain;

namespace ArbitrageTracker.Web.Pipeline;

public sealed class OpportunityCache
{
    private volatile IReadOnlyList<Opportunity> _current = Array.Empty<Opportunity>();
    public IReadOnlyList<Opportunity> Current => _current;
    public void Set(IReadOnlyList<Opportunity> opportunities) => _current = opportunities;
}
