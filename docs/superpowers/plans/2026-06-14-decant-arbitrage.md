# Decant (dose) Arbitrage Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Detect "buy the cheapest-per-dose potion variant → decant up → sell the 4-dose" opportunities and surface them under a Flips|Decants mode toggle, with the same money-safety gates as flips.

**Architecture:** A pure Core layer parses potion families from item names and a `DecantDetector` computes per-family opportunities; the existing `DetectionPipeline` hydrates families from `MarketState` and pushes a second `Decants` list in `DashboardSnapshot`; the Blazor page gains a mode toggle and a decant table.

**Tech Stack:** .NET 10, xUnit + FakeTimeProvider, Blazor Server + SignalR (all already in the repo). Reuses `GrandExchangeTax`, `SafetyComponents`, `SafetyScorer`, `DetectionSettings`.

**Conventions:** Commit after each task. Run `dotnet test` from repo root. Verify the app with `dotnet run --project src/ArbitrageTracker.Web` and the playwright tab as in prior work.

---

## File Structure
```
src/ArbitrageTracker.Core/
  Domain/DecantModels.cs            # DecantOpportunity record
  Detection/PotionFamilies.cs       # name parsing + family grouping (pure)
  Detection/DecantDetector.cs       # DecantInput/Variant records + detector
  State/MarketState.cs              # +AllMappings accessor (modify)
src/ArbitrageTracker.Web/
  Pipeline/DashboardSnapshot.cs     # +Decants list (modify)
  Pipeline/OpportunityCache.cs      # default snapshot includes empty Decants (modify)
  Pipeline/DecantHydrator.cs        # FamilyDef + MarketState -> DecantInput
  Pipeline/DetectionPipeline.cs     # compute decants each cycle (modify)
  Components/Pages/Opportunities.razor   # mode toggle + decant table (modify)
  Pipeline/Rationale.cs             # +decant helpers (modify)
  wwwroot/dashboard.css             # decant grid columns (modify)
tests/ArbitrageTracker.Core.Tests/
  PotionFamiliesTests.cs
  DecantDetectorTests.cs
```

---

## Task 1: Domain + detector input records

**Files:**
- Create: `src/ArbitrageTracker.Core/Domain/DecantModels.cs`

- [ ] **Step 1: Create the records**

`src/ArbitrageTracker.Core/Domain/DecantModels.cs`:
```csharp
namespace ArbitrageTracker.Core.Domain;

/// <summary>A "buy cheapest dose, decant up, sell 4-dose" opportunity for one potion family.</summary>
public sealed record DecantOpportunity(
    string FamilyName,
    int TargetItemId,           // the (4) variant
    long TargetSell,            // 4-dose instant-buy (high)
    long Tax,                   // GE tax on the 4-dose sale
    long KeepAfterTax,          // TargetSell - Tax
    int SourceItemId,
    string SourceName,
    int SourceDose,             // 1..3
    long SourceBuy,             // source instant-sell (low) — your buy price
    double PerDoseProfit,       // (KeepAfterTax/4) - (SourceBuy/SourceDose)
    long ProfitPerSourceUnit,   // SourceDose-doses worth: round(SourceDose * PerDoseProfit)
    int BuyLimit,               // shared across doses
    long ExpectedCycleProfit,
    long SourceVolume5m,
    long TargetVolume5m,
    double SafetyScore,
    SafetyBreakdown SafetyBreakdown,
    long PriceAgeSeconds,
    double RankScore);
```

- [ ] **Step 2: Verify it compiles**

Run: `dotnet build src/ArbitrageTracker.Core`
Expected: Build succeeded.

- [ ] **Step 3: Commit**
```bash
git add src/ArbitrageTracker.Core/Domain/DecantModels.cs
git commit -m "feat(core): add DecantOpportunity model"
```

---

## Task 2: Potion family parsing & grouping (TDD)

**Files:**
- Create: `src/ArbitrageTracker.Core/Detection/PotionFamilies.cs`
- Test: `tests/ArbitrageTracker.Core.Tests/PotionFamiliesTests.cs`

- [ ] **Step 1: Write the failing test**

`tests/ArbitrageTracker.Core.Tests/PotionFamiliesTests.cs`:
```csharp
using ArbitrageTracker.Core.Detection;
using ArbitrageTracker.Core.Domain;
using Xunit;

namespace ArbitrageTracker.Core.Tests;

public class PotionFamiliesTests
{
    private static ItemMapping Map(int id, string name) =>
        new(id, name, "examine", true, 1, 2, 2000, 50, "icon.png");

    [Theory]
    [InlineData("Prayer potion(4)", "Prayer potion", 4)]
    [InlineData("Super combat potion(1)", "Super combat potion", 1)]
    public void ParseDose_extractsBaseAndDose(string name, string expBase, int expDose)
    {
        var p = PotionFamilies.ParseDose(name);
        Assert.NotNull(p);
        Assert.Equal(expBase, p!.Value.Base);
        Assert.Equal(expDose, p.Value.Dose);
    }

    [Theory]
    [InlineData("Dragon dagger")]      // no dose suffix
    [InlineData("Potion(5)")]          // dose out of range
    [InlineData("Cake(3)")]            // matches shape; harmless — filtered later by family rules
    public void ParseDose_returnsNullForNonDoseNames(string name)
    {
        // Only (1)-(4) parse; everything else is null.
        var p = PotionFamilies.ParseDose(name);
        if (name == "Cake(3)") Assert.NotNull(p);     // shape matches; grouping rules reject it
        else Assert.Null(p);
    }

    [Fact]
    public void Group_buildsFamilyWithFourDosePlusLower()
    {
        var fams = PotionFamilies.Group(new[]
        {
            Map(1, "Prayer potion(4)"),
            Map(2, "Prayer potion(3)"),
            Map(3, "Prayer potion(2)"),
            Map(4, "Prayer potion(1)"),
        });

        var fam = Assert.Single(fams);
        Assert.Equal("Prayer potion", fam.BaseName);
        Assert.Equal(2000, fam.BuyLimit);
        Assert.Equal(4, fam.Variants.Count);
        Assert.Contains(fam.Variants, v => v.Dose == 4 && v.ItemId == 1);
    }

    [Fact]
    public void Group_dropsFamiliesWithoutA4DoseOrWithOnlyOneVariant()
    {
        var fams = PotionFamilies.Group(new[]
        {
            Map(1, "Half potion(3)"),     // no (4)
            Map(2, "Half potion(2)"),
            Map(3, "Loner(4)"),           // only one variant
            Map(4, "Cake(3)"),            // single, no (4)
        });
        Assert.Empty(fams);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter PotionFamiliesTests`
Expected: FAIL — `PotionFamilies` does not exist.

- [ ] **Step 3: Implement**

`src/ArbitrageTracker.Core/Detection/PotionFamilies.cs`:
```csharp
using System.Text.RegularExpressions;
using ArbitrageTracker.Core.Domain;

namespace ArbitrageTracker.Core.Detection;

public readonly record struct DoseName(string Base, int Dose);

public sealed record FamilyVariant(int ItemId, string Name, int Dose);

public sealed record FamilyDef(string BaseName, int BuyLimit, IReadOnlyList<FamilyVariant> Variants);

public static partial class PotionFamilies
{
    [GeneratedRegex(@"^(?<base>.*)\((?<d>[1-4])\)$")]
    private static partial Regex DoseRegex();

    /// <summary>Parse "Prayer potion(3)" → ("Prayer potion", 3). Null if it doesn't match a 1-4 dose.</summary>
    public static DoseName? ParseDose(string name)
    {
        var m = DoseRegex().Match(name);
        if (!m.Success) return null;
        return new DoseName(m.Groups["base"].Value.TrimEnd(), int.Parse(m.Groups["d"].Value));
    }

    /// <summary>Group mappings into potion families that have a (4) variant plus at least one lower dose.</summary>
    public static IReadOnlyList<FamilyDef> Group(IEnumerable<ItemMapping> mappings)
    {
        var byBase = new Dictionary<string, List<FamilyVariant>>();
        var limitByBase = new Dictionary<string, int>();

        foreach (var m in mappings)
        {
            if (ParseDose(m.Name) is not { } dn) continue;
            if (!byBase.TryGetValue(dn.Base, out var list))
            {
                list = new List<FamilyVariant>();
                byBase[dn.Base] = list;
                limitByBase[dn.Base] = m.BuyLimit;   // doses share one limit
            }
            list.Add(new FamilyVariant(m.Id, m.Name, dn.Dose));
        }

        var families = new List<FamilyDef>();
        foreach (var (b, variants) in byBase)
        {
            if (variants.Count < 2) continue;                 // need a spread of doses
            if (variants.All(v => v.Dose != 4)) continue;     // must be able to sell as 4-dose
            families.Add(new FamilyDef(b, limitByBase[b], variants));
        }
        return families;
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test --filter PotionFamiliesTests`
Expected: PASS.

- [ ] **Step 5: Commit**
```bash
git add src/ArbitrageTracker.Core/Detection/PotionFamilies.cs tests/ArbitrageTracker.Core.Tests/PotionFamiliesTests.cs
git commit -m "feat(core): potion family parsing & grouping with TDD"
```

---

## Task 3: DecantDetector (TDD)

**Files:**
- Create: `src/ArbitrageTracker.Core/Detection/DecantDetector.cs`
- Test: `tests/ArbitrageTracker.Core.Tests/DecantDetectorTests.cs`

**Math recap:** `tax4 = GrandExchangeTax.Calculate(targetHigh, false)`, `keep = targetHigh - tax4`,
`perDoseProfit = keep/4 - sourceLow/sourceDose` (choose source = min `low/dose` among variants with
`low>0`; skip if that's the 4-dose). `profitPerUnit = round(sourceDose * perDoseProfit)`,
`cycleProfit = min(buyLimit, sourceBuyVol5m*48) * profitPerUnit`.

- [ ] **Step 1: Write the failing test**

`tests/ArbitrageTracker.Core.Tests/DecantDetectorTests.cs`:
```csharp
using ArbitrageTracker.Core.Detection;
using ArbitrageTracker.Core.Domain;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace ArbitrageTracker.Core.Tests;

public class DecantDetectorTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.FromUnixTimeSeconds(1_000_000);
    private static FakeTimeProvider Clock() => new(Now);

    private static DecantInputVariant V(int id, int dose, long? low, long? high,
        long buyVol = 500, long sellVol = 500, long t = 999_900) =>
        new(id, $"Prayer potion({dose})", dose, new LatestPrice(id, high, t, low, t), buyVol, sellVol);

    // 4-dose sells at 1000 (keep ~980 after 2% tax → 245/dose). 2-dose dumped at 400 (200/dose).
    // perDoseProfit ≈ 245 - 200 = 45. Source = the 2-dose.
    private static DecantInput Family(
        long target4High = 1000, long source2Low = 400, long source2BuyVol = 500,
        long target4SellVol = 500, long sourceTime = 999_900, long targetTime = 999_900,
        int buyLimit = 2000)
    {
        var v4 = new DecantInputVariant(1, "Prayer potion(4)", 4,
            new LatestPrice(1, target4High, targetTime, target4High, targetTime), 10, target4SellVol);
        var v2 = new DecantInputVariant(2, "Prayer potion(2)", 2,
            new LatestPrice(2, source2Low + 50, sourceTime, source2Low, sourceTime), source2BuyVol, 10);
        var buckets = new[] { new MarketBucket(1, 999_900, target4High, target4High - 5, target4SellVol, 10) };
        return new DecantInput("Prayer potion", buyLimit, new[] { v4, v2 }, buckets);
    }

    [Fact]
    public void Detect_findsDecantWhenLowerDoseIsCheaperPerDose()
    {
        var d = new DecantDetector(Clock());
        var opp = d.Detect(Family(), DetectionSettings.Default);

        Assert.NotNull(opp);
        Assert.Equal(2, opp!.SourceDose);
        Assert.Equal(2, opp.SourceItemId);
        Assert.Equal(1, opp.TargetItemId);
        Assert.True(opp.PerDoseProfit > 40 && opp.PerDoseProfit < 50);
        Assert.True(opp.ExpectedCycleProfit > 0);
        Assert.True(opp.SafetyScore > 0);
    }

    [Fact]
    public void Detect_skipsWhenFourDoseIsCheapestPerDose()
    {
        // 4-dose cheap per dose (200/dose), 2-dose expensive (300/dose) → no decant.
        var d = new DecantDetector(Clock());
        Assert.Null(d.Detect(Family(target4High: 800, source2Low: 600), DetectionSettings.Default));
        // (4-dose low ~800/4=200 per dose beats 2-dose 600/2=300; cheapest is the 4-dose → skip)
    }

    [Fact]
    public void Detect_rejectsNonProfitablePerDose()
    {
        // 2-dose at 480 → 240/dose; 4-dose keep/4 ≈ 245 → ~5/dose but tax/rounding can flip it.
        // Use a clearly unprofitable source: 2-dose at 520 → 260/dose > 245 → negative.
        var d = new DecantDetector(Clock());
        Assert.Null(d.Detect(Family(target4High: 1000, source2Low: 520), DetectionSettings.Default));
    }

    [Fact]
    public void Detect_rejectsStaleSide()
    {
        var d = new DecantDetector(Clock());
        Assert.Null(d.Detect(Family(sourceTime: 990_000), DetectionSettings.Default)); // source >30m old
    }

    [Fact]
    public void Detect_rejectsThinSourceVolumeWhenGated()
    {
        var d = new DecantDetector(Clock());
        var settings = DetectionSettings.Default with { MinTwoSidedVolume = 20 };
        Assert.Null(d.Detect(Family(source2BuyVol: 0), settings)); // no source buy-side volume
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter DecantDetectorTests`
Expected: FAIL — `DecantDetector` / `DecantInput` do not exist.

- [ ] **Step 3: Implement**

`src/ArbitrageTracker.Core/Detection/DecantDetector.cs`:
```csharp
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
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test --filter DecantDetectorTests`
Expected: PASS.

- [ ] **Step 5: Commit**
```bash
git add src/ArbitrageTracker.Core/Detection/DecantDetector.cs tests/ArbitrageTracker.Core.Tests/DecantDetectorTests.cs
git commit -m "feat(core): decant detector with TDD"
```

---

## Task 4: Server wiring — expose mappings, hydrate families, push decants

**Files:**
- Modify: `src/ArbitrageTracker.Core/State/MarketState.cs`
- Create: `src/ArbitrageTracker.Web/Pipeline/DecantHydrator.cs`
- Modify: `src/ArbitrageTracker.Web/Pipeline/DashboardSnapshot.cs`, `OpportunityCache.cs`, `DetectionPipeline.cs`
- Modify: `src/ArbitrageTracker.Web/Program.cs`

- [ ] **Step 1: Expose mappings from MarketState**

In `src/ArbitrageTracker.Core/State/MarketState.cs`, add inside the class:
```csharp
public IReadOnlyCollection<ItemMapping> AllMappings => _mappings.Values.ToArray();
```

- [ ] **Step 2: DashboardSnapshot carries decants**

`src/ArbitrageTracker.Web/Pipeline/DashboardSnapshot.cs` — add the field:
```csharp
public sealed record DashboardSnapshot(
    long GeneratedAtUnix,
    long FeedAgeSeconds,
    bool FeedHealthy,
    string? FeedError,
    IReadOnlyList<Opportunity> Opportunities,
    IReadOnlyList<DecantOpportunity> Decants);
```

- [ ] **Step 3: Fix the cache default**

`src/ArbitrageTracker.Web/Pipeline/OpportunityCache.cs`:
```csharp
using ArbitrageTracker.Core.Domain;

namespace ArbitrageTracker.Web.Pipeline;

public sealed class OpportunityCache
{
    private volatile DashboardSnapshot _current = new(0, 0, FeedHealthy: false, FeedError: null,
        Opportunities: Array.Empty<Opportunity>(), Decants: Array.Empty<DecantOpportunity>());

    public DashboardSnapshot Current => _current;
    public void Set(DashboardSnapshot snapshot) => _current = snapshot;
}
```

- [ ] **Step 4: Hydrator — FamilyDef + MarketState → DecantInput**

`src/ArbitrageTracker.Web/Pipeline/DecantHydrator.cs`:
```csharp
using ArbitrageTracker.Core.Detection;
using ArbitrageTracker.Core.State;

namespace ArbitrageTracker.Web.Pipeline;

public static class DecantHydrator
{
    /// <summary>Build a DecantInput for a family from current MarketState, or null if data is missing.</summary>
    public static DecantInput? Hydrate(FamilyDef def, MarketState state)
    {
        var variants = new List<DecantInputVariant>();
        IReadOnlyList<Core.Domain.MarketBucket> targetBuckets = Array.Empty<Core.Domain.MarketBucket>();

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
```

- [ ] **Step 5: Detector DI + pipeline computes decants**

In `src/ArbitrageTracker.Web/Program.cs`, register the detector (next to `OpportunityDetector`):
```csharp
builder.Services.AddSingleton<DecantDetector>();
```
(Add `using ArbitrageTracker.Core.Detection;` if not present.)

In `src/ArbitrageTracker.Web/Pipeline/DetectionPipeline.cs`:
1. Add constructor param `DecantDetector decantDetector,` (after `OpportunityDetector detector,`).
2. Add a cached family list field + build lazily, and compute decants each cycle. Replace the
   ranking/cache block with:
```csharp
                var ranked = opps.OrderByDescending(o => o.RankScore).ToList();

                // Decants: group families from the mapping (cache while the mapping is stable),
                // hydrate from live state, detect, rank.
                _families ??= PotionFamilies.Group(state.AllMappings);
                if (_families.Count == 0) _families = null;  // mapping not loaded yet; retry next cycle
                var decants = new List<DecantOpportunity>();
                foreach (var def in _families ?? Array.Empty<FamilyDef>())
                {
                    var input = DecantHydrator.Hydrate(def, state);
                    if (input is null) continue;
                    var d = decantDetector.Detect(input, DetectionSettings.Default);
                    if (d is not null) decants.Add(d);
                }
                var rankedDecants = decants.OrderByDescending(d => d.RankScore).ToList();

                long now = clock.GetUtcNow().ToUnixTimeSeconds();
                long feedAge = now - feedHealth.LastLatestSuccessUnix;
                bool healthy = feedHealth.ConsecutiveFailures == 0 && feedAge <= 180;
                cache.Set(new DashboardSnapshot(now, feedAge, healthy, feedHealth.LastError, ranked, rankedDecants));
```
3. Add the field near the other private state:
```csharp
    private IReadOnlyList<FamilyDef>? _families;
```
4. Add `using ArbitrageTracker.Core.Detection;` and `using ArbitrageTracker.Core.Domain;` if missing.
5. The existing `await hub.Clients.All.SendAsync("OpportunitiesUpdated", cache.Current, ct);` stays.

- [ ] **Step 6: Build + full test suite**

Run: `dotnet build ArbitrageTracker.slnx` then `dotnet test`
Expected: Build succeeded; all tests pass.

- [ ] **Step 7: Commit**
```bash
git add -A
git commit -m "feat(web): compute and push decant opportunities in the pipeline"
```

---

## Task 5: Client — Flips|Decants toggle + decant table

**Files:**
- Modify: `src/ArbitrageTracker.Web/Components/Pages/Opportunities.razor`
- Modify: `src/ArbitrageTracker.Web/Pipeline/Rationale.cs`
- Modify: `src/ArbitrageTracker.Web/wwwroot/dashboard.css`

- [ ] **Step 1: Rationale helper for source label**

In `src/ArbitrageTracker.Web/Pipeline/Rationale.cs`, add:
```csharp
public static string DosePerDose(long price, int dose) => $"{price / (double)dose:0.#}/dose";
```

- [ ] **Step 2: Component state — mode + persistence**

In the `@code` block of `Opportunities.razor`:
1. Add field: `private bool decantMode;`
2. Extend the `Prefs` record with `bool? Decant` (last field group): change its definition and the
   `SavePrefs` call and the load block to include `decantMode`:
```csharp
    private sealed record Prefs(string? Sort, bool? Desc, double? MinMargin, double? MinSafety,
        long? MinVolume, long? MinProfit, long? MaxPrice, bool? TaxFree, bool? Decant,
        string? Search, long? Bankroll);
```
   In `SavePrefs(...)` pass `taxFreeOnly, decantMode, searchText, bankroll`.
   In the load block add: `decantMode = p.Decant ?? false;`
3. Add accessor: `private IReadOnlyList<DecantOpportunity> decants => snap?.Decants ?? Array.Empty<DecantOpportunity>();`
4. Add a setter that re-saves:
```csharp
    private async Task SetMode(bool decant) { decantMode = decant; await SavePrefs(); }
```

- [ ] **Step 3: Mode toggle markup**

Immediately above the `<section class="stats">` block, add:
```razor
    <div class="mode-toggle">
        <button class="@(!decantMode ? "on" : "")" @onclick="() => SetMode(false)">Flips</button>
        <button class="@(decantMode ? "on" : "")" @onclick="() => SetMode(true)">Decants</button>
    </div>
```

- [ ] **Step 4: Render the decant table when in decant mode**

Wrap the existing flips grid (`@if (opportunities.Count == 0 ...) { ... } else { <div class="opp-grid"> ... }`)
so it only renders when `!decantMode`. Then add the decant branch. The simplest structure: at the top
of the results area,
```razor
@if (decantMode)
{
    @if (decants.Count == 0)
    {
        <div class="empty empty-none"><div>
            <strong>No decant opportunities right now.</strong>
            <p class="muted small">No potion family currently has a low dose dumped cheaply enough to decant up for profit. These spike at off-peak hours — check back.</p>
        </div></div>
    }
    else
    {
        <div class="opp-grid decant-grid">
            <div class="opp-head">
                <span>#</span>
                <span class="tt"><span class="tt-trigger">Potion</span><span class="tt-pop">Potion family. You'll sell the 4-dose; icon/link are the 4-dose item.</span></span>
                <span class="tt"><span class="tt-trigger">Buy (source)</span><span class="tt-pop">The cheapest-per-dose variant to buy and decant up. All doses share one 4-hour buy limit.</span></span>
                <span class="tt"><span class="tt-trigger">Per dose</span><span class="tt-pop">Source cost per dose vs the 4-dose's after-tax value per dose. The gap is your edge.</span></span>
                <span class="tt"><span class="tt-trigger">Sell 4-dose</span><span class="tt-pop">4-dose instant-buy price and what you keep after the 2% GE tax.</span></span>
                <span class="tt"><span class="tt-trigger">Profit/dose</span><span class="tt-pop">After-tax profit per dose once decanted. Position is sized to your bankroll and the shared limit.</span></span>
                <span class="tt"><span class="tt-trigger">Profit if filled</span><span class="tt-pop">Per-cycle profit at the suggested quantity (bankroll- and volume-bounded).</span></span>
                <span class="tt"><span class="tt-trigger">Safety</span><span class="tt-pop">Clean-fill probability: source buy liquidity + 4-dose sell liquidity, stability, freshness.</span></span>
                <span class="tt"><span class="tt-trigger">Age</span><span class="tt-pop">Older of the source/4-dose prices. Both must be ≤30 min old.</span></span>
            </div>
            @{ int drank = 0; }
            @foreach (var dop in decants)
            {
                drank++;
                var dfactors = Rationale.Factors(dop.SafetyBreakdown);
                long budget = (long)(bankroll * 0.125);
                int dqty = dop.SourceBuy > 0 ? (int)Math.Min(dop.BuyLimit, budget / dop.SourceBuy) : 0;
                long dcap = dqty * dop.SourceBuy;
                long dproj = dqty * dop.ProfitPerSourceUnit;
                <div class="opp-row" @key="dop.TargetItemId">
                    <span class="rank">@drank</span>
                    <span class="cell-item">
                        <img class="icon" src="https://static.runelite.net/cache/item/icon/@(dop.TargetItemId).png" alt="" loading="lazy" onerror="this.style.visibility='hidden'" />
                        <span class="item-text">
                            <span class="item-head">
                                <span class="item-name">@dop.FamilyName</span>
                                <a class="src-link" href="https://prices.runescape.wiki/osrs/item/@dop.TargetItemId" target="_blank" rel="noopener noreferrer"><svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M14 3h7v7"/><path d="M21 3l-9 9"/><path d="M21 14v5a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h5"/></svg></a>
                            </span>
                            <span class="item-sub">limit @dop.BuyLimit.ToString("N0") / 4h (shared)</span>
                        </span>
                    </span>
                    <span class="cell-spread"><span class="spread-row"><span class="sell">@dop.SourceDose-dose</span> <span class="buy">@dop.SourceBuy.ToString("N0")</span></span><span class="spread-tax">@dop.SourceName</span></span>
                    <span class="cell-margin"><span class="margin-pct">@Rationale.DosePerDose(dop.SourceBuy, dop.SourceDose)</span><span class="margin-pct">vs @Rationale.DosePerDose(dop.KeepAfterTax, 4)</span></span>
                    <span class="cell-spread"><span class="spread-row"><span class="sell">@dop.TargetSell.ToString("N0")</span></span><span class="spread-tax taxfree">keep @dop.KeepAfterTax.ToString("N0")</span></span>
                    <span class="cell-margin"><span class="margin-net">+@dop.PerDoseProfit.ToString("F1")/dose</span><span class="margin-pct">@dqty.ToString("N0") @@ @Rationale.Gp(dcap)</span></span>
                    <span class="cell-profit"><span class="p-proj">+@dproj.ToString("N0")</span><span class="p-cycle">4h max @Rationale.Gp(dop.ExpectedCycleProfit)</span></span>
                    <span class="cell-safety tt">
                        <span class="meter"><span class="meter-bar"><span class="meter-fill @Rationale.TierClass(dop.SafetyScore)" style="width:@dop.SafetyScore.ToString("F0")%"></span></span><span class="meter-num">@dop.SafetyScore.ToString("F0")</span></span>
                        <span class="seg">@foreach (var f in dfactors){<span class="seg-bit s-@f.Status" style="flex:@f.WeightPct"></span>}</span>
                        <span class="tt-pop card">
                            <span class="card-head"><span class="badge @Rationale.TierClass(dop.SafetyScore)">@Rationale.Tier(dop.SafetyScore)</span><span class="card-score">@dop.SafetyScore.ToString("F0")<span class="card-score-max">/100</span></span></span>
                            <span class="card-summary">@Rationale.Summary(new Opportunity(dop.TargetItemId, dop.FamilyName, dop.SourceBuy, dop.TargetSell, dop.Tax, 0, 0, dop.BuyLimit, dop.ExpectedCycleProfit, dop.SafetyScore, dop.SafetyBreakdown, dop.PriceAgeSeconds, dop.RankScore, dop.SourceVolume5m, dop.TargetVolume5m))</span>
                            @foreach (var f in dfactors){<span class="factor"><span class="factor-top"><span class="factor-label">@f.Label</span><span class="factor-meta">@f.Value.ToString("P0") · @f.WeightPct% weight</span></span><span class="factor-bar"><span class="factor-fill s-@f.Status" style="width:@((f.Value*100).ToString("F0"))%"></span></span><span class="factor-why">@f.Explanation</span></span>}
                            <span class="card-foot">Decant: buy the cheap low dose, decant up free at Bob Barter, sell the 4-dose.</span>
                        </span>
                    </span>
                    <span class="cell-age"><span class="age-pill @Rationale.FreshnessClass(dop.PriceAgeSeconds)">@Rationale.Age(dop.PriceAgeSeconds)</span></span>
                </div>
            }
        </div>
    }
}
else
{
    <!-- existing flips stats/filter/grid stay here, unchanged -->
}
```
> Note: `Rationale.Summary` takes an `Opportunity`; the adapter above reuses it for the decant
> breakdown text. If that reads awkwardly, add a `Summary(SafetyBreakdown, double)` overload in
> Task 5 Step 1 instead and call it directly — but the adapter keeps the change minimal.

- [ ] **Step 5: Decant grid CSS**

Append to `src/ArbitrageTracker.Web/wwwroot/dashboard.css`:
```css
/* mode toggle */
.mode-toggle { display: inline-flex; border: 1px solid var(--border); border-radius: 8px; overflow: hidden; margin-bottom: 16px; }
.mode-toggle button { background: var(--surface-2); color: var(--muted); border: none; padding: 8px 18px; font-size: 13px; font-weight: 650; cursor: pointer; border-right: 1px solid var(--border); }
.mode-toggle button:last-child { border-right: none; }
.mode-toggle button.on { background: var(--blue); color: #04223f; }

/* decant grid columns: #, potion, source, per-dose, sell4, profit/dose, profit, safety, age */
.decant-grid .opp-head, .decant-grid .opp-row {
  grid-template-columns: 30px minmax(160px,1.8fr) 1.2fr 1.2fr 1.2fr 1.3fr 1.1fr 184px 58px;
}
```

- [ ] **Step 6: Build, run, verify in browser**

Run: `dotnet build ArbitrageTracker.slnx` (expect 0 errors), then
`dotnet run --project src/ArbitrageTracker.Web`.
In the browser: toggle **Decants**; confirm rows show a potion family, a `{n}-dose` source, per-dose
comparison, after-tax keep, a positive profit/dose, a safety meter, and that toggling back to
**Flips** restores the normal table. Reload → the mode persists.

- [ ] **Step 7: Commit**
```bash
git add -A
git commit -m "feat(ui): Flips|Decants mode toggle and decant table"
```

---

## Self-Review Notes
- **Spec coverage:** family grouping (Task 2), per-dose metric + cheapest-source + cycle profit +
  gates + tax + safety reuse (Task 3), shared-buy-limit throughput (Task 3 `fillableUnits`), server
  second list (Task 4), toggle + table + localStorage persistence (Task 5). All spec sections map to a task.
- **Validity/money-safety:** stale, one-sided, source==4 skip, perDoseProfit≤0, glitch-margin cap —
  all in `DecantDetector` and tested.
- **Type consistency:** `DecantOpportunity`, `DecantInput`, `DecantInputVariant`, `FamilyDef`,
  `FamilyVariant`, `DecantDetector.Detect`, `DashboardSnapshot(..., Decants)` used identically across tasks.
- **Known follow-ups (out of scope):** decant safety reuses flip factor *explanations* (slightly
  flip-worded); down-decanting not detected; family heuristic may form a junk family but the gates
  make it harmless.
