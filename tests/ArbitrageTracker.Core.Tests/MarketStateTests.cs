using ArbitrageTracker.Core.Domain;
using ArbitrageTracker.Core.State;
using Xunit;

namespace ArbitrageTracker.Core.Tests;

public class MarketStateTests
{
    private static ItemMapping Map(int id) =>
        new(id, $"Item{id}", "examine", true, 1, 2, 100, 50, "icon.png");

    [Fact]
    public void TryGetSnapshot_returnsNullForUnknownItem()
    {
        var state = new MarketState();
        Assert.False(state.TryGetSnapshot(999, out _));
    }

    [Fact]
    public void TryGetSnapshot_returnsLatestAndBucketsAfterUpdates()
    {
        var state = new MarketState();
        state.SetMapping(new[] { Map(1) });
        state.UpdateLatest(new LatestPrice(1, High: 110, HighTime: 500, Low: 100, LowTime: 490));
        state.AddBucket5m(new MarketBucket(1, 100, 108, 101, 50, 60));

        Assert.True(state.TryGetSnapshot(1, out var snap));
        Assert.Equal(110, snap!.Latest.High);
        Assert.Single(snap.Buckets5m);
        Assert.Equal(108, snap.Buckets5m[0].AvgHighPrice);
    }

    [Fact]
    public void AddBucket5m_keepsAtMostMaxBucketsMostRecent()
    {
        var state = new MarketState();
        state.SetMapping(new[] { Map(1) });
        state.UpdateLatest(new LatestPrice(1, 110, 0, 100, 0));

        for (int t = 0; t < MarketState.MaxBuckets + 5; t++)
            state.AddBucket5m(new MarketBucket(1, t, 100 + t, 99 + t, 10, 10));

        state.TryGetSnapshot(1, out var snap);
        Assert.Equal(MarketState.MaxBuckets, snap!.Buckets5m.Count);
        // Oldest dropped: first retained timestamp is 5.
        Assert.Equal(5, snap.Buckets5m[0].Timestamp);
    }

    [Fact]
    public void TryGetSnapshot_returnsFalseWhenNoLatestYet()
    {
        var state = new MarketState();
        state.SetMapping(new[] { Map(1) });
        // Only a bucket, no latest price.
        state.AddBucket5m(new MarketBucket(1, 0, 100, 99, 10, 10));
        Assert.False(state.TryGetSnapshot(1, out _));
    }
}
