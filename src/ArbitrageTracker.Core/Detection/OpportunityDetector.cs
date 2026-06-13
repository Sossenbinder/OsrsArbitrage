using ArbitrageTracker.Core.Domain;
using ArbitrageTracker.Core.Pricing;
using ArbitrageTracker.Core.Scoring;

namespace ArbitrageTracker.Core.Detection;

public sealed class OpportunityDetector(TimeProvider timeProvider)
{
    public Opportunity? Detect(ItemSnapshot snap, DetectionSettings settings)
    {
        int id = snap.Mapping.Id;

        // Allow/deny gating.
        if (settings.DenyList.Contains(id)) return null;
        if (settings.AllowList.Count > 0 && !settings.AllowList.Contains(id)) return null;

        // Both sides must be present and positive.
        if (snap.Latest.High is not > 0 || snap.Latest.Low is not > 0) return null;
        long sell = snap.Latest.High.Value;
        long buy = snap.Latest.Low.Value;

        // Unit price cap (capital exposure).
        if (buy > settings.MaxUnitPrice) return null;

        // Freshness.
        long now = timeProvider.GetUtcNow().ToUnixTimeSeconds();
        long age = now - Math.Max(snap.Latest.HighTime, snap.Latest.LowTime);
        if (age > settings.MaxAgeSeconds) return null;

        bool exempt = settings.TaxExemptItemIds.Contains(id);
        var flip = FlipCalculator.Compute(buy, sell, exempt);

        // Margin gating.
        if (flip.NetMargin <= 0) return null;
        if (flip.MarginPercent < settings.MinMarginPercent) return null;

        // Expected per-cycle profit: limited by buy limit and recent buy-side demand.
        long recentDemand = snap.Buckets5m.Count > 0
            ? snap.Buckets5m[^1].LowPriceVolume
            : snap.Mapping.BuyLimit;
        long fillableUnits = Math.Min(snap.Mapping.BuyLimit, Math.Max(0, recentDemand));
        long cycleProfit = fillableUnits * flip.NetMargin;
        if (cycleProfit < settings.MinCycleProfit) return null;

        // Safety score.
        long lowVol = snap.Buckets5m.Count > 0 ? snap.Buckets5m[^1].LowPriceVolume : 0;
        long highVol = snap.Buckets5m.Count > 0 ? snap.Buckets5m[^1].HighPriceVolume : 0;
        var avgPrices = ExtractAvgPrices(snap.Buckets5m);
        int profitable = CountProfitableBuckets(snap.Buckets5m, exempt);
        var (safety, breakdown) = SafetyScorer.Score(
            lowVol, highVol, avgPrices, profitable, snap.Buckets5m.Count, age);

        if (safety < settings.MinSafetyScore) return null;

        double rank = cycleProfit * (safety / 100.0);

        return new Opportunity(
            ItemId: id,
            Name: snap.Mapping.Name,
            BuyPrice: buy,
            SellPrice: sell,
            Tax: flip.Tax,
            NetMargin: flip.NetMargin,
            MarginPercent: flip.MarginPercent,
            BuyLimit: snap.Mapping.BuyLimit,
            ExpectedCycleProfit: cycleProfit,
            SafetyScore: safety,
            SafetyBreakdown: breakdown,
            PriceAgeSeconds: age,
            RankScore: rank);
    }

    private static List<long> ExtractAvgPrices(IReadOnlyList<MarketBucket> buckets)
    {
        var prices = new List<long>(buckets.Count);
        foreach (var b in buckets)
            if (b.AvgHighPrice is { } h) prices.Add(h);
        return prices;
    }

    private static int CountProfitableBuckets(IReadOnlyList<MarketBucket> buckets, bool exempt)
    {
        int count = 0;
        foreach (var b in buckets)
        {
            if (b.AvgHighPrice is not { } h || b.AvgLowPrice is not { } l) continue;
            long tax = GrandExchangeTax.Calculate(h, exempt);
            if (h - l - tax > 0) count++;
        }
        return count;
    }
}
