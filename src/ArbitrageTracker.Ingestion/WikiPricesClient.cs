using System.Net.Http.Json;
using ArbitrageTracker.Core.Domain;
using ArbitrageTracker.Ingestion.Dto;

namespace ArbitrageTracker.Ingestion;

public sealed class WikiPricesClient(HttpClient http) : IWikiPricesClient
{
    public async Task<IReadOnlyList<ItemMapping>> GetMappingAsync(CancellationToken ct)
    {
        var items = await http.GetFromJsonAsync<List<WikiMappingItem>>("mapping", ct) ?? new();
        return items.Select(m => new ItemMapping(
            m.Id, m.Name, m.Examine, m.Members, m.LowAlch, m.HighAlch, m.Limit, m.Value, m.Icon)).ToList();
    }

    public async Task<IReadOnlyList<LatestPrice>> GetLatestAsync(CancellationToken ct)
    {
        var resp = await http.GetFromJsonAsync<WikiLatestResponse>("latest", ct) ?? new();
        var result = new List<LatestPrice>(resp.Data.Count);
        foreach (var (idStr, e) in resp.Data)
        {
            if (!int.TryParse(idStr, out int id)) continue;
            result.Add(new LatestPrice(id, e.High, e.HighTime ?? 0, e.Low, e.LowTime ?? 0));
        }
        return result;
    }

    public Task<IReadOnlyList<MarketBucket>> Get5mAsync(CancellationToken ct) => GetBucketsAsync("5m", ct);
    public Task<IReadOnlyList<MarketBucket>> Get1hAsync(CancellationToken ct) => GetBucketsAsync("1h", ct);

    private async Task<IReadOnlyList<MarketBucket>> GetBucketsAsync(string route, CancellationToken ct)
    {
        var resp = await http.GetFromJsonAsync<WikiBucketResponse>(route, ct) ?? new();
        long ts = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var result = new List<MarketBucket>(resp.Data.Count);
        foreach (var (idStr, e) in resp.Data)
        {
            if (!int.TryParse(idStr, out int id)) continue;
            result.Add(new MarketBucket(id, ts, e.AvgHighPrice, e.AvgLowPrice, e.HighPriceVolume, e.LowPriceVolume));
        }
        return result;
    }
}
