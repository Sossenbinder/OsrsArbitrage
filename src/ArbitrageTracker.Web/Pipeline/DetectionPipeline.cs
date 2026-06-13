using ArbitrageTracker.Core.Detection;
using ArbitrageTracker.Core.Domain;
using ArbitrageTracker.Core.State;
using ArbitrageTracker.Data;
using ArbitrageTracker.Data.Entities;
using ArbitrageTracker.Ingestion;
using ArbitrageTracker.Web.Hubs;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ArbitrageTracker.Web.Pipeline;

public sealed class DetectionPipeline(
    PriceUpdateChannel channel,
    MarketState state,
    OpportunityDetector detector,
    FeedHealth feedHealth,
    OpportunityCache cache,
    IHubContext<OpportunitiesHub> hub,
    IServiceScopeFactory scopeFactory,
    TimeProvider clock,
    ILogger<DetectionPipeline> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        await foreach (var _ in channel.Reader.ReadAllAsync(ct))
        {
            try
            {
                // Detection is permissive (validity gates only); the user filters in the UI.
                var opps = new List<Opportunity>();
                foreach (int id in state.KnownItemIds)
                    if (state.TryGetSnapshot(id, out var snap) && snap is not null)
                    {
                        var opp = detector.Detect(snap, DetectionSettings.Default);
                        if (opp is not null) opps.Add(opp);
                    }

                var ranked = opps.OrderByDescending(o => o.RankScore).ToList();
                long now = clock.GetUtcNow().ToUnixTimeSeconds();

                long feedAge = now - feedHealth.LastLatestSuccessUnix;
                bool healthy = feedHealth.ConsecutiveFailures == 0 && feedAge <= 180;
                cache.Set(new DashboardSnapshot(now, feedAge, healthy, feedHealth.LastError, ranked));

                using (var scope = scopeFactory.CreateScope())
                {
                    var repo = scope.ServiceProvider.GetRequiredService<SnapshotRepository>();
                    foreach (var o in ranked.Take(50))
                        await repo.SaveOpportunityAsync(new OpportunitySnapshot
                        {
                            ItemId = o.ItemId, DetectedAt = now,
                            BuyPrice = o.BuyPrice, SellPrice = o.SellPrice,
                            NetMargin = o.NetMargin, ExpectedCycleProfit = o.ExpectedCycleProfit,
                            SafetyScore = o.SafetyScore
                        }, ct);
                }

                await hub.Clients.All.SendAsync("OpportunitiesUpdated", cache.Current, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                log.LogError(ex, "Detection pipeline iteration failed");
            }
        }
    }
}
