using ArbitrageTracker.Core.Pricing;
using ArbitrageTracker.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ArbitrageTracker.Web.Validation;

public sealed class ProxyOutcomeJob(
    IServiceScopeFactory scopeFactory, TimeProvider clock, ILogger<ProxyOutcomeJob> log)
    : BackgroundService
{
    private const long EvaluationDelaySeconds = 3600; // validate 1h after detection

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                long now = clock.GetUtcNow().ToUnixTimeSeconds();
                long cutoff = now - EvaluationDelaySeconds;

                using var scope = scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<ArbitrageDbContext>();

                var pending = await db.OpportunitySnapshots
                    .Where(o => o.ProxyValidated == null && o.DetectedAt <= cutoff)
                    .Take(500)
                    .ToListAsync(ct);

                foreach (var o in pending)
                {
                    // Buckets recorded for this item after detection.
                    var laterBuckets = await db.BucketSnapshots
                        .Where(b => b.ItemId == o.ItemId && b.Interval == "5m" && b.Timestamp >= o.DetectedAt)
                        .ToListAsync(ct);

                    int profitable = laterBuckets.Count(b =>
                        b.AvgHighPrice is { } h && b.AvgLowPrice is { } l
                        && h - l - GrandExchangeTax.Calculate(h, exempt: false) > 0
                        && b.HighPriceVolume > 0 && b.LowPriceVolume > 0);

                    // Spread held in a majority of the following buckets → validated.
                    o.ProxyValidated = laterBuckets.Count > 0 && profitable * 2 >= laterBuckets.Count;
                    o.ProxyEvaluatedAt = now;
                }

                await db.SaveChangesAsync(ct);
                if (pending.Count > 0) log.LogInformation("Validated {Count} opportunities", pending.Count);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                log.LogError(ex, "Proxy outcome job failed");
            }
            await Task.Delay(TimeSpan.FromMinutes(5), ct);
        }
    }
}
