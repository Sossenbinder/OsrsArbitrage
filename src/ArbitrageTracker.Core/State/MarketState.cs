using System.Collections.Concurrent;
using ArbitrageTracker.Core.Domain;

namespace ArbitrageTracker.Core.State;

/// <summary>Thread-safe in-memory hot state for the detection pipeline.</summary>
public sealed class MarketState
{
    public const int MaxBuckets = 24;

    private readonly ConcurrentDictionary<int, ItemMapping> _mappings = new();
    private readonly ConcurrentDictionary<int, LatestPrice> _latest = new();
    private readonly ConcurrentDictionary<int, Queue<MarketBucket>> _buckets5m = new();
    private readonly ConcurrentDictionary<int, Queue<MarketBucket>> _buckets1h = new();

    public void SetMapping(IEnumerable<ItemMapping> mappings)
    {
        foreach (var m in mappings)
            _mappings[m.Id] = m;
    }

    public IReadOnlyCollection<int> KnownItemIds => _mappings.Keys.ToArray();

    public IReadOnlyCollection<ItemMapping> AllMappings => _mappings.Values.ToArray();

    public void UpdateLatest(LatestPrice price) => _latest[price.ItemId] = price;

    public void AddBucket5m(MarketBucket bucket) => AddBucket(_buckets5m, bucket);
    public void AddBucket1h(MarketBucket bucket) => AddBucket(_buckets1h, bucket);

    private static void AddBucket(ConcurrentDictionary<int, Queue<MarketBucket>> store, MarketBucket bucket)
    {
        var q = store.GetOrAdd(bucket.ItemId, _ => new Queue<MarketBucket>());
        lock (q)
        {
            q.Enqueue(bucket);
            while (q.Count > MaxBuckets) q.Dequeue();
        }
    }

    public bool TryGetSnapshot(int itemId, out ItemSnapshot? snapshot)
    {
        snapshot = null;
        if (!_mappings.TryGetValue(itemId, out var mapping)) return false;
        if (!_latest.TryGetValue(itemId, out var latest)) return false;

        snapshot = new ItemSnapshot(mapping, latest, Snapshot(_buckets5m, itemId), Snapshot(_buckets1h, itemId));
        return true;
    }

    private static IReadOnlyList<MarketBucket> Snapshot(
        ConcurrentDictionary<int, Queue<MarketBucket>> store, int itemId)
    {
        if (!store.TryGetValue(itemId, out var q)) return Array.Empty<MarketBucket>();
        lock (q) return q.ToArray();
    }
}
