using ArbitrageTracker.Core.Detection;
using ArbitrageTracker.Core.Domain;
using ArbitrageTracker.Core.Sizing;
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
    PositionSizer sizer,
    FeedHealth feedHealth,
    SettingsStore settings,
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
                var opps = new List<Opportunity>();
                foreach (int id in state.KnownItemIds)
                    if (state.TryGetSnapshot(id, out var snap) && snap is not null)
                    {
                        var opp = detector.Detect(snap, settings.Detection);
                        if (opp is not null) opps.Add(opp);
                    }

                var ranked = opps.OrderByDescending(o => o.RankScore).ToList();
                long now = clock.GetUtcNow().ToUnixTimeSeconds();

                // Enrich with bankroll-aware sizing + fill realism for the UI.
                var sizing = settings.Sizing;
                var views = ranked.Select(o =>
                {
                    var pos = sizer.Size(o, sizing);
                    // Est. hours to fill the suggested quantity at the recent buy-side throughput
                    // (5m volume × 12 ≈ per-hour). You compete with other buyers, so treat as a
                    // floor, not a promise.
                    double perHour = Math.Max(1, o.BuyVolume5m * 12.0);
                    double fillHours = pos.SuggestedQuantity / perHour;
                    return new OpportunityView(o, pos.SuggestedQuantity, pos.CapitalNeeded,
                        pos.ProjectedProfit, fillHours, pos.SuggestedQuantity > 0);
                }).ToList();

                long feedAge = now - feedHealth.LastLatestSuccessUnix;
                bool healthy = feedHealth.ConsecutiveFailures == 0 && feedAge <= 180;
                cache.Set(new DashboardSnapshot(now, feedAge, healthy, feedHealth.LastError, views));
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
