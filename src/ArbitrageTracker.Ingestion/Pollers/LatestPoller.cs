using ArbitrageTracker.Core.State;
using ArbitrageTracker.Data;
using ArbitrageTracker.Data.Entities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ArbitrageTracker.Ingestion.Pollers;

public sealed class LatestPoller(
    IWikiPricesClient client,
    MarketState state,
    PriceUpdateChannel channel,
    IServiceScopeFactory scopeFactory,
    ILogger<LatestPoller> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var prices = await client.GetLatestAsync(ct);
                long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

                foreach (var p in prices) state.UpdateLatest(p);

                using (var scope = scopeFactory.CreateScope())
                {
                    var repo = scope.ServiceProvider.GetRequiredService<SnapshotRepository>();
                    await repo.SaveLatestAsync(prices.Select(p => new PriceSnapshot
                    {
                        ItemId = p.ItemId, CapturedAt = now,
                        High = p.High, HighTime = p.HighTime, Low = p.Low, LowTime = p.LowTime
                    }), ct);
                    await repo.PruneAsync(now - 7 * 24 * 3600, ct);
                }

                await channel.Writer.WriteAsync(new PriceUpdate(now, prices.Count), ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                log.LogError(ex, "Latest poll failed");
            }
            await Task.Delay(TimeSpan.FromSeconds(60), ct);
        }
    }
}
