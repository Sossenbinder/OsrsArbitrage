using ArbitrageTracker.Core.Domain;
using ArbitrageTracker.Core.Pricing;
using ArbitrageTracker.Core.Scoring;

namespace ArbitrageTracker.Core.Detection;

public sealed record DecantInputVariant(
    int ItemId, string Name, int Dose, LatestPrice Latest, long BuyVolume5m, long SellVolume5m);

public sealed record DecantInput(
    string FamilyName, int BuyLimit, IReadOnlyList<DecantInputVariant> Variants,
    IReadOnlyList<MarketBucket> TargetBuckets5m);

public sealed class DecantDetector(TimeProvider timeProvider)
{
    public DecantOpportunity? Detect(DecantInput fam, DetectionSettings settings)
    {
        // Target = the 4-dose; must have a sell (high) price.
        var target = fam.Variants.FirstOrDefault(v => v.Dose == 4);
        if (target is null || target.Latest.High is not > 0) return null;
        long targetHigh = target.Latest.High.Value;

        // Cheapest per-dose source among variants with a buy (low) price.
        DecantInputVariant? source = null;
        double bestPerDose = double.MaxValue;
        foreach (var v in fam.Variants)
        {
            if (v.Latest.Low is not > 0) continue;
            double perDose = (double)v.Latest.Low.Value / v.Dose;
            if (perDose < bestPerDose) { bestPerDose = perDose; source = v; }
        }
        if (source is null || source.Dose == 4) return null;   // no cheaper lower dose ⇒ not a decant

        long now = timeProvider.GetUtcNow().ToUnixTimeSeconds();
        long age = now - Math.Min(source.Latest.LowTime, target.Latest.HighTime);
        if (age > settings.MaxAgeSeconds) return null;

        long sourceLow = source.Latest.Low!.Value;
        long tax = GrandExchangeTax.Calculate(targetHigh, exempt: false);
        long keep = targetHigh - tax;

        double perDoseProfit = keep / 4.0 - (double)sourceLow / source.Dose;
        if (perDoseProfit <= 0) return null;

        // Glitch sanity: per-dose profit vs the per-dose cost.
        double perDoseCost = (double)sourceLow / source.Dose;
        if (settings.MaxMarginPercent > 0 && perDoseCost > 0
            && perDoseProfit / perDoseCost * 100.0 > settings.MaxMarginPercent) return null;

        // Two-sided liquidity: you buy the source, you sell the 4-dose.
        if (source.BuyVolume5m < settings.MinTwoSidedVolume
            || target.SellVolume5m < settings.MinTwoSidedVolume) return null;

        long profitPerUnit = (long)Math.Round(source.Dose * perDoseProfit);
        if (profitPerUnit <= 0) return null;

        long fillableUnits = Math.Min(fam.BuyLimit, Math.Max(0, source.BuyVolume5m * 48));
        long cycleProfit = fillableUnits * profitPerUnit;

        // Safety: liquidity across the decant (source buy + 4-dose sell), depth/volatility/persistence
        // from the 4-dose, freshness from the older side. Reuses the flip scorer.
        var avgPrices = new List<long>();
        int sellable = 0;
        foreach (var b in fam.TargetBuckets5m)
        {
            if (b.AvgHighPrice is { } h) avgPrices.Add(h);
            if (b.AvgHighPrice is not null && b.HighPriceVolume > 0) sellable++;
        }
        double depth = SafetyComponents.DemandDepth(fam.TargetBuckets5m);
        var (safety, breakdown) = SafetyScorer.Score(
            source.BuyVolume5m, target.SellVolume5m, depth, avgPrices,
            sellable, fam.TargetBuckets5m.Count, age);

        return new DecantOpportunity(
            FamilyName: fam.FamilyName,
            TargetItemId: target.ItemId,
            TargetSell: targetHigh,
            Tax: tax,
            KeepAfterTax: keep,
            SourceItemId: source.ItemId,
            SourceName: source.Name,
            SourceDose: source.Dose,
            SourceBuy: sourceLow,
            PerDoseProfit: perDoseProfit,
            ProfitPerSourceUnit: profitPerUnit,
            BuyLimit: fam.BuyLimit,
            ExpectedCycleProfit: cycleProfit,
            SourceVolume5m: source.BuyVolume5m,
            TargetVolume5m: target.SellVolume5m,
            SafetyScore: safety,
            SafetyBreakdown: breakdown,
            PriceAgeSeconds: age,
            RankScore: cycleProfit * (safety / 100.0));
    }
}
