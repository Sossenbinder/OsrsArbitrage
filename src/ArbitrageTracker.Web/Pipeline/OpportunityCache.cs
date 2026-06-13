namespace ArbitrageTracker.Web.Pipeline;

public sealed class OpportunityCache
{
    private volatile IReadOnlyList<OpportunityView> _current = Array.Empty<OpportunityView>();
    public IReadOnlyList<OpportunityView> Current => _current;
    public void Set(IReadOnlyList<OpportunityView> opportunities) => _current = opportunities;
}
