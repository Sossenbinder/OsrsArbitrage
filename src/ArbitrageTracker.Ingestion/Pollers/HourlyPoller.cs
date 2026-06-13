using ArbitrageTracker.Core.State;
using ArbitrageTracker.Data;
using ArbitrageTracker.Data.Entities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ArbitrageTracker.Ingestion.Pollers;

public sealed class HourlyPoller(
    IWikiPricesClient client, MarketState state,
    IServiceScopeFactory scopeFactory, ILogger<HourlyPoller> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var buckets = await client.Get1hAsync(ct);
                foreach (var b in buckets) state.AddBucket1h(b);

                using var scope = scopeFactory.CreateScope();
                var repo = scope.ServiceProvider.GetRequiredService<SnapshotRepository>();
                await repo.UpsertBucketsAsync(buckets.Select(b => new BucketSnapshot
                {
                    ItemId = b.ItemId, Interval = "1h", Timestamp = b.Timestamp,
                    AvgHighPrice = b.AvgHighPrice, AvgLowPrice = b.AvgLowPrice,
                    HighPriceVolume = b.HighPriceVolume, LowPriceVolume = b.LowPriceVolume
                }), ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                log.LogError(ex, "1h poll failed");
            }
            await Task.Delay(TimeSpan.FromHours(1), ct);
        }
    }
}
