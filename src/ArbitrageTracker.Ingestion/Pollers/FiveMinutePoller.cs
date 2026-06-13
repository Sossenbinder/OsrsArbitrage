using ArbitrageTracker.Core.State;
using ArbitrageTracker.Data;
using ArbitrageTracker.Data.Entities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ArbitrageTracker.Ingestion.Pollers;

public sealed class FiveMinutePoller(
    IWikiPricesClient client, MarketState state,
    IServiceScopeFactory scopeFactory, ILogger<FiveMinutePoller> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var buckets = await client.Get5mAsync(ct);
                foreach (var b in buckets) state.AddBucket5m(b);

                using var scope = scopeFactory.CreateScope();
                var repo = scope.ServiceProvider.GetRequiredService<SnapshotRepository>();
                await repo.UpsertBucketsAsync(buckets.Select(b => new BucketSnapshot
                {
                    ItemId = b.ItemId, Interval = "5m", Timestamp = b.Timestamp,
                    AvgHighPrice = b.AvgHighPrice, AvgLowPrice = b.AvgLowPrice,
                    HighPriceVolume = b.HighPriceVolume, LowPriceVolume = b.LowPriceVolume
                }), ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                log.LogError(ex, "5m poll failed");
            }
            await Task.Delay(TimeSpan.FromMinutes(5), ct);
        }
    }
}
