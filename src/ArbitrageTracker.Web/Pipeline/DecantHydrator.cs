using ArbitrageTracker.Core.Detection;
using ArbitrageTracker.Core.Domain;
using ArbitrageTracker.Core.State;

namespace ArbitrageTracker.Web.Pipeline;

public static class DecantHydrator
{
    /// <summary>Build a DecantInput for a family from current MarketState, or null if data is missing.</summary>
    public static DecantInput? Hydrate(FamilyDef def, MarketState state)
    {
        var variants = new List<DecantInputVariant>();
        IReadOnlyList<MarketBucket> targetBuckets = Array.Empty<MarketBucket>();

        foreach (var fv in def.Variants)
        {
            if (!state.TryGetSnapshot(fv.ItemId, out var snap) || snap is null) continue;
            long buyVol = snap.Buckets5m.Count > 0 ? snap.Buckets5m[^1].LowPriceVolume : 0;
            long sellVol = snap.Buckets5m.Count > 0 ? snap.Buckets5m[^1].HighPriceVolume : 0;
            variants.Add(new DecantInputVariant(fv.ItemId, fv.Name, fv.Dose, snap.Latest, buyVol, sellVol));
            if (fv.Dose == 4) targetBuckets = snap.Buckets5m;
        }

        if (variants.All(v => v.Dose != 4)) return null;
        return new DecantInput(def.BaseName, def.BuyLimit, variants, targetBuckets);
    }
}
