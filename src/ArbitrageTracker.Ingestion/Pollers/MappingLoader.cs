using ArbitrageTracker.Core.State;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ArbitrageTracker.Ingestion.Pollers;

public sealed class MappingLoader(
    IWikiPricesClient client, MarketState state, ILogger<MappingLoader> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var mappings = await client.GetMappingAsync(ct);
                state.SetMapping(mappings);
                log.LogInformation("Loaded {Count} item mappings", mappings.Count);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                log.LogError(ex, "Mapping load failed");
            }
            await Task.Delay(TimeSpan.FromHours(24), ct);
        }
    }
}
