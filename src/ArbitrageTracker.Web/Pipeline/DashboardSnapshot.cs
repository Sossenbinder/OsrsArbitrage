using ArbitrageTracker.Core.Domain;

namespace ArbitrageTracker.Web.Pipeline;

/// <summary>
/// One push to the dashboard: the raw ranked opportunities plus upstream feed health. Position
/// sizing is computed client-side from the per-browser bankroll, so it isn't part of the wire model.
/// </summary>
public sealed record DashboardSnapshot(
    long GeneratedAtUnix,
    long FeedAgeSeconds,
    bool FeedHealthy,
    string? FeedError,
    IReadOnlyList<Opportunity> Opportunities,
    IReadOnlyList<DecantOpportunity> Decants);
