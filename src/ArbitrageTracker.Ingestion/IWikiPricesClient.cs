using ArbitrageTracker.Core.Domain;

namespace ArbitrageTracker.Ingestion;

public interface IWikiPricesClient
{
    Task<IReadOnlyList<ItemMapping>> GetMappingAsync(CancellationToken ct);
    Task<IReadOnlyList<LatestPrice>> GetLatestAsync(CancellationToken ct);
    Task<IReadOnlyList<MarketBucket>> Get5mAsync(CancellationToken ct);
    Task<IReadOnlyList<MarketBucket>> Get1hAsync(CancellationToken ct);
}
