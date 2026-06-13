using ArbitrageTracker.Web.Pipeline;
using Microsoft.AspNetCore.SignalR;

namespace ArbitrageTracker.Web.Hubs;

public sealed class OpportunitiesHub(OpportunityCache cache) : Hub
{
    /// <summary>New clients pull the current snapshot immediately on connect.</summary>
    public DashboardSnapshot GetCurrent() => cache.Current;
}
