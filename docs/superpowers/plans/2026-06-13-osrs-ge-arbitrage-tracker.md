# OSRS GE Arbitrage Tracker Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build an ASP.NET Core + Blazor Server app that ingests the OSRS Wiki real-time prices API, detects low-risk same-day flipping opportunities (tax-adjusted, buy-limit- and liquidity-aware), scores their clean-fill probability, sizes positions against a bankroll, and surfaces them in a live-updating, alerting UI.

**Architecture:** Layered solution. `Core` holds pure domain + math + scoring + detection + sizing (no I/O, fully unit-tested). `Data` is EF Core/SQLite persistence. `Ingestion` polls the Wiki API on background services and publishes price updates onto an in-process channel. `Web` hosts Blazor Server + a SignalR hub, runs the detection pipeline as a hosted service, and pushes ranked opportunities to the UI. A forward-looking proxy job validates the safety score against later market behaviour (we never observe our own fills).

**Tech Stack:** .NET 10 (`net10.0`), ASP.NET Core + Blazor Server, SignalR, EF Core 10 + SQLite, `System.Threading.Channels`, `TimeProvider`, xUnit + `FakeTimeProvider`.

**Conventions for every task:** Use the existing git repo. Commit after each task completes (the final step of each task). Run `dotnet test` from the repo root. Target framework is `net10.0` everywhere. Tests use xUnit with the built-in `Assert` API (no FluentAssertions). All time-dependent code takes a `TimeProvider` so tests can inject `FakeTimeProvider`.

---

## File Structure

```
ArbitrageTracker.sln
├── src/
│   ├── ArbitrageTracker.Core/
│   │   ├── Domain/            ItemMapping, LatestPrice, MarketBucket, ItemSnapshot, Opportunity, SizedPosition
│   │   ├── Pricing/           GrandExchangeTax, FlipCalculator
│   │   ├── Scoring/           SafetyComponents, SafetyScorer, SafetyBreakdown
│   │   ├── Detection/         DetectionSettings, OpportunityDetector
│   │   ├── Sizing/            SizingSettings, PositionSizer
│   │   └── State/             MarketState
│   ├── ArbitrageTracker.Data/
│   │   ├── Entities/          PriceSnapshot, BucketSnapshot, OpportunitySnapshot, ItemMappingEntity, AppSetting
│   │   ├── ArbitrageDbContext.cs
│   │   └── SnapshotRepository.cs
│   ├── ArbitrageTracker.Ingestion/
│   │   ├── Dto/               WikiLatestResponse, WikiMappingItem, WikiBucketResponse, WikiTimeseriesResponse
│   │   ├── IWikiPricesClient.cs, WikiPricesClient.cs
│   │   ├── PriceUpdate.cs, PriceUpdateChannel.cs
│   │   └── Pollers/           MappingLoader, LatestPoller, FiveMinutePoller, HourlyPoller
│   └── ArbitrageTracker.Web/
│       ├── Program.cs
│       ├── Pipeline/          DetectionPipeline (hosted service), OpportunityCache
│       ├── Validation/        ProxyOutcomeJob
│       ├── Hubs/              OpportunitiesHub
│       └── Components/        App.razor, Routes.razor, Pages/Opportunities.razor, ItemDetail.razor, Settings.razor, _Imports.razor
└── tests/
    └── ArbitrageTracker.Core.Tests/
        ├── GrandExchangeTaxTests.cs
        ├── FlipCalculatorTests.cs
        ├── SafetyComponentsTests.cs
        ├── SafetyScorerTests.cs
        ├── MarketStateTests.cs
        ├── OpportunityDetectorTests.cs
        └── PositionSizerTests.cs
```

---

## Task 1: Solution & project scaffolding

**Files:**
- Create: `ArbitrageTracker.sln`, the four `src/*` project files, the test project.

- [ ] **Step 1: Create solution and projects**

Run from repo root:

```bash
dotnet new sln -n ArbitrageTracker
dotnet new classlib   -n ArbitrageTracker.Core      -o src/ArbitrageTracker.Core      -f net10.0
dotnet new classlib   -n ArbitrageTracker.Data      -o src/ArbitrageTracker.Data      -f net10.0
dotnet new classlib   -n ArbitrageTracker.Ingestion -o src/ArbitrageTracker.Ingestion -f net10.0
dotnet new blazor     -n ArbitrageTracker.Web       -o src/ArbitrageTracker.Web       -f net10.0 --interactivity Server --empty
dotnet new xunit      -n ArbitrageTracker.Core.Tests -o tests/ArbitrageTracker.Core.Tests -f net10.0

# Remove the default Class1.cs stubs
rm -f src/ArbitrageTracker.Core/Class1.cs src/ArbitrageTracker.Data/Class1.cs src/ArbitrageTracker.Ingestion/Class1.cs
```

- [ ] **Step 2: Add projects to solution and wire references**

```bash
dotnet sln add src/ArbitrageTracker.Core src/ArbitrageTracker.Data src/ArbitrageTracker.Ingestion src/ArbitrageTracker.Web tests/ArbitrageTracker.Core.Tests

dotnet add src/ArbitrageTracker.Data      reference src/ArbitrageTracker.Core
dotnet add src/ArbitrageTracker.Ingestion reference src/ArbitrageTracker.Core
dotnet add src/ArbitrageTracker.Web       reference src/ArbitrageTracker.Core src/ArbitrageTracker.Data src/ArbitrageTracker.Ingestion
dotnet add tests/ArbitrageTracker.Core.Tests reference src/ArbitrageTracker.Core
```

- [ ] **Step 3: Add test-only packages**

```bash
dotnet add tests/ArbitrageTracker.Core.Tests package Microsoft.Extensions.TimeProvider.Testing
```

- [ ] **Step 4: Enable nullable + implicit usings across src**

Confirm each `src/*/*.csproj` and the test `.csproj` contain (the templates set these by default for net10.0; add if missing):

```xml
<PropertyGroup>
  <TargetFramework>net10.0</TargetFramework>
  <Nullable>enable</Nullable>
  <ImplicitUsings>enable</ImplicitUsings>
</PropertyGroup>
```

- [ ] **Step 5: Verify the solution builds**

Run: `dotnet build`
Expected: Build succeeded, 0 errors.

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "chore: scaffold solution, projects, and references"
```

---

## Task 2: Core domain models

**Files:**
- Create: `src/ArbitrageTracker.Core/Domain/Models.cs`

- [ ] **Step 1: Create the domain records**

`src/ArbitrageTracker.Core/Domain/Models.cs`:

```csharp
namespace ArbitrageTracker.Core.Domain;

/// <summary>Static item metadata from the Wiki /mapping endpoint.</summary>
public sealed record ItemMapping(
    int Id,
    string Name,
    string Examine,
    bool Members,
    int LowAlch,
    int HighAlch,
    int BuyLimit,
    long Value,
    string Icon);

/// <summary>
/// Latest instant-buy / instant-sell prices from /latest.
/// High = instant-buy price (what we SELL at). Low = instant-sell price (what we BUY at).
/// Times are unix seconds. Null means no recent trade of that type.
/// </summary>
public sealed record LatestPrice(
    int ItemId,
    long? High,
    long HighTime,
    long? Low,
    long LowTime);

/// <summary>One 5m or 1h aggregate bucket from /5m or /1h.</summary>
public sealed record MarketBucket(
    int ItemId,
    long Timestamp,
    long? AvgHighPrice,
    long? AvgLowPrice,
    long HighPriceVolume,
    long LowPriceVolume);

/// <summary>Everything the detector needs about one item at a point in time.</summary>
public sealed record ItemSnapshot(
    ItemMapping Mapping,
    LatestPrice Latest,
    IReadOnlyList<MarketBucket> Buckets5m,
    IReadOnlyList<MarketBucket> Buckets1h);

/// <summary>A detected, scored flipping opportunity.</summary>
public sealed record Opportunity(
    int ItemId,
    string Name,
    long BuyPrice,
    long SellPrice,
    long Tax,
    long NetMargin,
    double MarginPercent,
    int BuyLimit,
    long ExpectedCycleProfit,
    double SafetyScore,
    SafetyBreakdown SafetyBreakdown,
    long PriceAgeSeconds,
    double RankScore);

/// <summary>Per-component contributions to the safety score (each 0..1).</summary>
public sealed record SafetyBreakdown(
    double Liquidity,
    double Volatility,
    double Persistence,
    double Freshness);

/// <summary>Bankroll-aware sizing suggestion for an opportunity.</summary>
public sealed record SizedPosition(
    int ItemId,
    int SuggestedQuantity,
    long CapitalNeeded,
    long ProjectedProfit);
```

- [ ] **Step 2: Verify it compiles**

Run: `dotnet build src/ArbitrageTracker.Core`
Expected: Build succeeded.

- [ ] **Step 3: Commit**

```bash
git add -A
git commit -m "feat(core): add domain models"
```

---

## Task 3: Grand Exchange tax calculator (TDD)

**Files:**
- Create: `src/ArbitrageTracker.Core/Pricing/GrandExchangeTax.cs`
- Test: `tests/ArbitrageTracker.Core.Tests/GrandExchangeTaxTests.cs`

Rules: tax = 2% of sell price, rounded **down**, capped at **5,000,000 gp**. Items selling below 50 gp pay 0 (falls out of the floor naturally, but we guard explicitly). Exempt items pay 0 regardless.

- [ ] **Step 1: Write the failing test**

`tests/ArbitrageTracker.Core.Tests/GrandExchangeTaxTests.cs`:

```csharp
using ArbitrageTracker.Core.Pricing;
using Xunit;

namespace ArbitrageTracker.Core.Tests;

public class GrandExchangeTaxTests
{
    [Theory]
    [InlineData(49, 0)]        // below 50 → 0
    [InlineData(50, 1)]        // 2% of 50 = 1
    [InlineData(100, 2)]       // 2%
    [InlineData(1000, 20)]     // 2%
    [InlineData(1_000_000, 20_000)]
    [InlineData(250_000_000, 5_000_000)]   // exactly at cap
    [InlineData(500_000_000, 5_000_000)]   // capped
    public void Calculate_appliesTwoPercentFlooredAndCapped(long sellPrice, long expected)
    {
        Assert.Equal(expected, GrandExchangeTax.Calculate(sellPrice, exempt: false));
    }

    [Fact]
    public void Calculate_floorsFractionalTax()
    {
        // 2% of 99 = 1.98 → floor 1
        Assert.Equal(1, GrandExchangeTax.Calculate(99, exempt: false));
    }

    [Fact]
    public void Calculate_returnsZeroForExemptItems()
    {
        Assert.Equal(0, GrandExchangeTax.Calculate(10_000_000, exempt: true));
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter GrandExchangeTaxTests`
Expected: FAIL — `GrandExchangeTax` does not exist (compile error).

- [ ] **Step 3: Write minimal implementation**

`src/ArbitrageTracker.Core/Pricing/GrandExchangeTax.cs`:

```csharp
namespace ArbitrageTracker.Core.Pricing;

/// <summary>
/// OSRS Grand Exchange sell tax: 2% of sell price, rounded down, capped at 5,000,000 gp/item.
/// Items below 50 gp and exempt items pay nothing. (Rate raised 1% → 2% on 2025-05-29.)
/// </summary>
public static class GrandExchangeTax
{
    public const long MaxTaxPerItem = 5_000_000;
    public const long MinTaxablePrice = 50;

    public static long Calculate(long sellPrice, bool exempt)
    {
        if (exempt || sellPrice < MinTaxablePrice)
            return 0;

        // 2% with integer floor: (sellPrice * 2) / 100.
        long tax = sellPrice * 2 / 100;
        return Math.Min(tax, MaxTaxPerItem);
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test --filter GrandExchangeTaxTests`
Expected: PASS (all theory cases + facts).

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat(core): add GE tax calculator with TDD"
```

---

## Task 4: Flip calculator (TDD)

**Files:**
- Create: `src/ArbitrageTracker.Core/Pricing/FlipCalculator.cs`
- Test: `tests/ArbitrageTracker.Core.Tests/FlipCalculatorTests.cs`

A flip buys at `low` (instant-sell price) and sells at `high` (instant-buy price). Net margin = sell − buy − tax. Margin percent is relative to buy price.

- [ ] **Step 1: Write the failing test**

`tests/ArbitrageTracker.Core.Tests/FlipCalculatorTests.cs`:

```csharp
using ArbitrageTracker.Core.Pricing;
using Xunit;

namespace ArbitrageTracker.Core.Tests;

public class FlipCalculatorTests
{
    [Fact]
    public void Compute_subtractsTaxFromGrossMargin()
    {
        // buy 1000, sell 1100, tax = 2% of 1100 = 22 → net = 100 - 22 = 78
        var result = FlipCalculator.Compute(buyPrice: 1000, sellPrice: 1100, exempt: false);

        Assert.Equal(1000, result.BuyPrice);
        Assert.Equal(1100, result.SellPrice);
        Assert.Equal(22, result.Tax);
        Assert.Equal(78, result.NetMargin);
    }

    [Fact]
    public void Compute_marginPercentIsRelativeToBuyPrice()
    {
        var result = FlipCalculator.Compute(buyPrice: 1000, sellPrice: 1100, exempt: false);
        // net 78 / buy 1000 = 7.8%
        Assert.Equal(7.8, result.MarginPercent, precision: 3);
    }

    [Fact]
    public void Compute_canBeNegativeAfterTax()
    {
        // buy 1000, sell 1010, tax = 20 → net = 10 - 20 = -10
        var result = FlipCalculator.Compute(buyPrice: 1000, sellPrice: 1010, exempt: false);
        Assert.Equal(-10, result.NetMargin);
    }

    [Fact]
    public void Compute_exemptItemPaysNoTax()
    {
        var result = FlipCalculator.Compute(buyPrice: 1000, sellPrice: 1100, exempt: true);
        Assert.Equal(0, result.Tax);
        Assert.Equal(100, result.NetMargin);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter FlipCalculatorTests`
Expected: FAIL — `FlipCalculator` does not exist.

- [ ] **Step 3: Write minimal implementation**

`src/ArbitrageTracker.Core/Pricing/FlipCalculator.cs`:

```csharp
namespace ArbitrageTracker.Core.Pricing;

public readonly record struct FlipResult(
    long BuyPrice,
    long SellPrice,
    long Tax,
    long NetMargin,
    double MarginPercent);

public static class FlipCalculator
{
    /// <summary>buyPrice = instant-sell (low); sellPrice = instant-buy (high).</summary>
    public static FlipResult Compute(long buyPrice, long sellPrice, bool exempt)
    {
        long tax = GrandExchangeTax.Calculate(sellPrice, exempt);
        long net = sellPrice - buyPrice - tax;
        double pct = buyPrice > 0 ? net * 100.0 / buyPrice : 0.0;
        return new FlipResult(buyPrice, sellPrice, tax, net, pct);
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test --filter FlipCalculatorTests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat(core): add flip calculator with TDD"
```

---

## Task 5: Safety score components (TDD)

**Files:**
- Create: `src/ArbitrageTracker.Core/Scoring/SafetyComponents.cs`
- Test: `tests/ArbitrageTracker.Core.Tests/SafetyComponentsTests.cs`

Each component returns 0..1. Definitions:
- **Liquidity** = log-scaled geometric mean of the two volumes. `geo = sqrt(low*high)`; `score = clamp(log10(geo+1) / log10(Saturation+1), 0, 1)` with `Saturation = 10_000`. Geometric mean so a dead side tanks the score.
- **Volatility** = `clamp(1 - cv / MaxCv, 0, 1)` where `cv` = stddev/mean of the supplied average prices, `MaxCv = 0.10`. Empty or single-element input → 1 (no observed volatility).
- **Persistence** = `profitableBuckets / totalBuckets`; `totalBuckets == 0` → 0.
- **Freshness** = `clamp(1 - ageSeconds / MaxAge, 0, 1)`, `MaxAge = 1800` (30 min).

- [ ] **Step 1: Write the failing test**

`tests/ArbitrageTracker.Core.Tests/SafetyComponentsTests.cs`:

```csharp
using ArbitrageTracker.Core.Scoring;
using Xunit;

namespace ArbitrageTracker.Core.Tests;

public class SafetyComponentsTests
{
    [Fact]
    public void Liquidity_isZeroWhenEitherSideIsZero()
    {
        Assert.Equal(0.0, SafetyComponents.Liquidity(0, 5000), precision: 6);
        Assert.Equal(0.0, SafetyComponents.Liquidity(5000, 0), precision: 6);
    }

    [Fact]
    public void Liquidity_reachesOneAtSaturation()
    {
        Assert.Equal(1.0, SafetyComponents.Liquidity(10_000, 10_000), precision: 3);
    }

    [Fact]
    public void Liquidity_isBetweenZeroAndOneForModerateVolume()
    {
        double s = SafetyComponents.Liquidity(100, 100);
        Assert.InRange(s, 0.4, 0.8);
    }

    [Fact]
    public void Volatility_isOneForFlatPrices()
    {
        Assert.Equal(1.0, SafetyComponents.Volatility(new long[] { 1000, 1000, 1000 }), precision: 6);
    }

    [Fact]
    public void Volatility_isOneForEmptyOrSingle()
    {
        Assert.Equal(1.0, SafetyComponents.Volatility(Array.Empty<long>()), precision: 6);
        Assert.Equal(1.0, SafetyComponents.Volatility(new long[] { 1000 }), precision: 6);
    }

    [Fact]
    public void Volatility_dropsAsPricesSwing()
    {
        // ~10% CV should floor to ~0
        double s = SafetyComponents.Volatility(new long[] { 900, 1100, 900, 1100 });
        Assert.InRange(s, 0.0, 0.2);
    }

    [Fact]
    public void Persistence_isRatioOfProfitableBuckets()
    {
        Assert.Equal(0.75, SafetyComponents.Persistence(profitableBuckets: 9, totalBuckets: 12), precision: 6);
        Assert.Equal(0.0, SafetyComponents.Persistence(0, 0), precision: 6);
    }

    [Fact]
    public void Freshness_decaysLinearlyToZeroAtMaxAge()
    {
        Assert.Equal(1.0, SafetyComponents.Freshness(0), precision: 6);
        Assert.Equal(0.5, SafetyComponents.Freshness(900), precision: 3);
        Assert.Equal(0.0, SafetyComponents.Freshness(1800), precision: 6);
        Assert.Equal(0.0, SafetyComponents.Freshness(5000), precision: 6); // clamped
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter SafetyComponentsTests`
Expected: FAIL — `SafetyComponents` does not exist.

- [ ] **Step 3: Write minimal implementation**

`src/ArbitrageTracker.Core/Scoring/SafetyComponents.cs`:

```csharp
namespace ArbitrageTracker.Core.Scoring;

public static class SafetyComponents
{
    public const double LiquiditySaturation = 10_000;
    public const double MaxCoefficientOfVariation = 0.10;
    public const double MaxFreshnessAgeSeconds = 1800;

    public static double Liquidity(long lowVolume, long highVolume)
    {
        if (lowVolume <= 0 || highVolume <= 0)
            return 0.0;

        double geo = Math.Sqrt((double)lowVolume * highVolume);
        double score = Math.Log10(geo + 1) / Math.Log10(LiquiditySaturation + 1);
        return Math.Clamp(score, 0.0, 1.0);
    }

    public static double Volatility(IReadOnlyList<long> avgPrices)
    {
        if (avgPrices.Count < 2)
            return 1.0;

        double mean = 0;
        foreach (long p in avgPrices) mean += p;
        mean /= avgPrices.Count;
        if (mean <= 0) return 1.0;

        double sumSq = 0;
        foreach (long p in avgPrices) sumSq += (p - mean) * (p - mean);
        double stdDev = Math.Sqrt(sumSq / avgPrices.Count);
        double cv = stdDev / mean;

        return Math.Clamp(1.0 - cv / MaxCoefficientOfVariation, 0.0, 1.0);
    }

    public static double Persistence(int profitableBuckets, int totalBuckets)
        => totalBuckets <= 0 ? 0.0 : (double)profitableBuckets / totalBuckets;

    public static double Freshness(long ageSeconds)
        => Math.Clamp(1.0 - ageSeconds / MaxFreshnessAgeSeconds, 0.0, 1.0);
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test --filter SafetyComponentsTests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat(core): add safety score components with TDD"
```

---

## Task 6: Composite safety scorer (TDD)

**Files:**
- Create: `src/ArbitrageTracker.Core/Scoring/SafetyScorer.cs`
- Test: `tests/ArbitrageTracker.Core.Tests/SafetyScorerTests.cs`

Weighted blend → 0..100. Weights: liquidity 0.40, volatility 0.25, persistence 0.20, freshness 0.15. Returns both the 0..100 score and the per-component `SafetyBreakdown` (raw 0..1 components, for UI display).

- [ ] **Step 1: Write the failing test**

`tests/ArbitrageTracker.Core.Tests/SafetyScorerTests.cs`:

```csharp
using ArbitrageTracker.Core.Scoring;
using Xunit;

namespace ArbitrageTracker.Core.Tests;

public class SafetyScorerTests
{
    [Fact]
    public void Score_allPerfectComponentsIsHundred()
    {
        var (score, breakdown) = SafetyScorer.Score(
            lowVolume: 10_000, highVolume: 10_000,
            avgPrices: new long[] { 1000, 1000, 1000 },
            profitableBuckets: 12, totalBuckets: 12,
            ageSeconds: 0);

        Assert.Equal(100.0, score, precision: 1);
        Assert.Equal(1.0, breakdown.Liquidity, precision: 2);
        Assert.Equal(1.0, breakdown.Volatility, precision: 2);
        Assert.Equal(1.0, breakdown.Persistence, precision: 2);
        Assert.Equal(1.0, breakdown.Freshness, precision: 2);
    }

    [Fact]
    public void Score_deadSecondSideCapsAtSixtyPercent()
    {
        // liquidity = 0 (one-sided), everything else perfect.
        // Max achievable = (0.25 + 0.20 + 0.15) * 100 = 60.
        var (score, _) = SafetyScorer.Score(
            lowVolume: 10_000, highVolume: 0,
            avgPrices: new long[] { 1000, 1000 },
            profitableBuckets: 12, totalBuckets: 12,
            ageSeconds: 0);

        Assert.Equal(60.0, score, precision: 1);
    }

    [Fact]
    public void Score_weightsSumToOne()
    {
        Assert.Equal(1.0,
            SafetyScorer.LiquidityWeight + SafetyScorer.VolatilityWeight
            + SafetyScorer.PersistenceWeight + SafetyScorer.FreshnessWeight,
            precision: 6);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter SafetyScorerTests`
Expected: FAIL — `SafetyScorer` does not exist.

- [ ] **Step 3: Write minimal implementation**

`src/ArbitrageTracker.Core/Scoring/SafetyScorer.cs`:

```csharp
using ArbitrageTracker.Core.Domain;

namespace ArbitrageTracker.Core.Scoring;

public static class SafetyScorer
{
    public const double LiquidityWeight   = 0.40;
    public const double VolatilityWeight  = 0.25;
    public const double PersistenceWeight = 0.20;
    public const double FreshnessWeight   = 0.15;

    public static (double Score, SafetyBreakdown Breakdown) Score(
        long lowVolume, long highVolume,
        IReadOnlyList<long> avgPrices,
        int profitableBuckets, int totalBuckets,
        long ageSeconds)
    {
        var breakdown = new SafetyBreakdown(
            Liquidity:   SafetyComponents.Liquidity(lowVolume, highVolume),
            Volatility:  SafetyComponents.Volatility(avgPrices),
            Persistence: SafetyComponents.Persistence(profitableBuckets, totalBuckets),
            Freshness:   SafetyComponents.Freshness(ageSeconds));

        double score = 100.0 * (
            breakdown.Liquidity   * LiquidityWeight +
            breakdown.Volatility  * VolatilityWeight +
            breakdown.Persistence * PersistenceWeight +
            breakdown.Freshness   * FreshnessWeight);

        return (score, breakdown);
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test --filter SafetyScorerTests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat(core): add composite safety scorer with TDD"
```

---

## Task 7: Market state (TDD)

**Files:**
- Create: `src/ArbitrageTracker.Core/State/MarketState.cs`
- Test: `tests/ArbitrageTracker.Core.Tests/MarketStateTests.cs`

In-memory hot state. Holds the mapping table, the latest price per item, and a bounded history of 5m and 1h buckets per item (keep last `MaxBuckets = 24` of each). Thread-safe (pollers write, detection reads). Exposes `TryGetSnapshot(itemId)` → `ItemSnapshot?`.

- [ ] **Step 1: Write the failing test**

`tests/ArbitrageTracker.Core.Tests/MarketStateTests.cs`:

```csharp
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
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter MarketStateTests`
Expected: FAIL — `MarketState` does not exist.

- [ ] **Step 3: Write minimal implementation**

`src/ArbitrageTracker.Core/State/MarketState.cs`:

```csharp
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
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test --filter MarketStateTests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat(core): add thread-safe market state with TDD"
```

---

## Task 8: Opportunity detector (TDD)

**Files:**
- Create: `src/ArbitrageTracker.Core/Detection/DetectionSettings.cs`, `src/ArbitrageTracker.Core/Detection/OpportunityDetector.cs`
- Test: `tests/ArbitrageTracker.Core.Tests/OpportunityDetectorTests.cs`

The detector turns an `ItemSnapshot` into an `Opportunity?`. It uses `TimeProvider` for "now" (freshness). Gating filters (return null if any fails):
1. `Latest.High` and `Latest.Low` both present and > 0.
2. Buy price (`Low`) ≤ `MaxUnitPrice`.
3. Price age (now − max(highTime, lowTime)) ≤ `MaxAgeSeconds`.
4. Net margin > 0 and margin% ≥ `MinMarginPercent`.
5. Expected cycle profit ≥ `MinCycleProfit`.
6. Item passes allow/deny lists (deny wins; if allow-list non-empty, item must be in it).

Expected cycle profit = `min(buyLimit, recentDemand) × netMargin`, where `recentDemand` = the most recent 5m bucket's `LowPriceVolume` (units traded on the buy side), defaulting to `buyLimit` if no bucket. Profitable-bucket count for persistence = count of 5m buckets where `AvgHighPrice − AvgLowPrice − tax(AvgHighPrice) > 0`.

- [ ] **Step 1: Write the failing test**

`tests/ArbitrageTracker.Core.Tests/OpportunityDetectorTests.cs`:

```csharp
using ArbitrageTracker.Core.Detection;
using ArbitrageTracker.Core.Domain;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace ArbitrageTracker.Core.Tests;

public class OpportunityDetectorTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.FromUnixTimeSeconds(1_000_000);

    private static ItemMapping Map(int id, int buyLimit, bool members = true) =>
        new(id, $"Item{id}", "examine", members, 1, 2, buyLimit, 50, "icon.png");

    private static FakeTimeProvider Clock() => new(Now);

    private static ItemSnapshot Snapshot(
        long high, long low, int buyLimit = 1000,
        long highTime = 999_900, long lowTime = 999_900,
        long recentBuyVolume = 1000)
    {
        var map = Map(1, buyLimit);
        var latest = new LatestPrice(1, high, highTime, low, lowTime);
        var bucket = new MarketBucket(1, 999_900, high, low, 500, recentBuyVolume);
        return new ItemSnapshot(map, latest, new[] { bucket }, Array.Empty<MarketBucket>());
    }

    [Fact]
    public void Detect_returnsOpportunityForHealthyFlip()
    {
        var detector = new OpportunityDetector(Clock());
        // buy 1000, sell 1100, tax 22, net 78; limit 1000, demand 1000 → cycle profit 78,000
        var opp = detector.Detect(Snapshot(high: 1100, low: 1000), DetectionSettings.Default);

        Assert.NotNull(opp);
        Assert.Equal(78, opp!.NetMargin);
        Assert.Equal(78_000, opp.ExpectedCycleProfit);
        Assert.True(opp.SafetyScore > 0);
    }

    [Fact]
    public void Detect_rejectsWhenUnitPriceAboveCap()
    {
        var detector = new OpportunityDetector(Clock());
        var settings = DetectionSettings.Default with { MaxUnitPrice = 500 };
        Assert.Null(detector.Detect(Snapshot(high: 1100, low: 1000), settings));
    }

    [Fact]
    public void Detect_rejectsWhenCycleProfitBelowMinimum()
    {
        var detector = new OpportunityDetector(Clock());
        // limit 1 → cycle profit 78, below 50_000 floor
        Assert.Null(detector.Detect(Snapshot(high: 1100, low: 1000, buyLimit: 1, recentBuyVolume: 1),
            DetectionSettings.Default));
    }

    [Fact]
    public void Detect_rejectsNegativeMarginAfterTax()
    {
        var detector = new OpportunityDetector(Clock());
        // buy 1000 sell 1010 tax 20 → net -10
        Assert.Null(detector.Detect(Snapshot(high: 1010, low: 1000), DetectionSettings.Default));
    }

    [Fact]
    public void Detect_rejectsStalePrices()
    {
        var detector = new OpportunityDetector(Clock());
        // lowTime far in the past → age > 1800s
        var snap = Snapshot(high: 1100, low: 1000, highTime: 990_000, lowTime: 990_000);
        Assert.Null(detector.Detect(snap, DetectionSettings.Default));
    }

    [Fact]
    public void Detect_rejectsMissingPriceSide()
    {
        var detector = new OpportunityDetector(Clock());
        var map = Map(1, 1000);
        var latest = new LatestPrice(1, High: 1100, HighTime: 999_900, Low: null, LowTime: 0);
        var snap = new ItemSnapshot(map, latest, Array.Empty<MarketBucket>(), Array.Empty<MarketBucket>());
        Assert.Null(detector.Detect(snap, DetectionSettings.Default));
    }

    [Fact]
    public void Detect_respectsDenyList()
    {
        var detector = new OpportunityDetector(Clock());
        var settings = DetectionSettings.Default with { DenyList = new HashSet<int> { 1 } };
        Assert.Null(detector.Detect(Snapshot(high: 1100, low: 1000), settings));
    }

    [Fact]
    public void Detect_cycleProfitCappedByBuyLimitNotVolume()
    {
        var detector = new OpportunityDetector(Clock());
        // limit 100, demand 5000 → capped at 100 units * 78 = 7800 (below 50k → rejected),
        // so raise min margin scenario: use big margin to keep it.
        var settings = DetectionSettings.Default with { MinCycleProfit = 0 };
        var opp = detector.Detect(Snapshot(high: 1100, low: 1000, buyLimit: 100, recentBuyVolume: 5000), settings);
        Assert.Equal(7800, opp!.ExpectedCycleProfit);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter OpportunityDetectorTests`
Expected: FAIL — `OpportunityDetector` / `DetectionSettings` do not exist.

- [ ] **Step 3: Write `DetectionSettings`**

`src/ArbitrageTracker.Core/Detection/DetectionSettings.cs`:

```csharp
namespace ArbitrageTracker.Core.Detection;

public sealed record DetectionSettings
{
    public long MaxUnitPrice { get; init; } = 1_000_000;
    public long MinCycleProfit { get; init; } = 50_000;
    public double MinMarginPercent { get; init; } = 0.0;
    public double MinSafetyScore { get; init; } = 0.0;
    public long MaxAgeSeconds { get; init; } = 1800;
    public IReadOnlySet<int> AllowList { get; init; } = new HashSet<int>();
    public IReadOnlySet<int> DenyList { get; init; } = new HashSet<int>();
    public IReadOnlySet<int> TaxExemptItemIds { get; init; } = new HashSet<int>();

    public static DetectionSettings Default => new();
}
```

- [ ] **Step 4: Write `OpportunityDetector`**

`src/ArbitrageTracker.Core/Detection/OpportunityDetector.cs`:

```csharp
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
```

- [ ] **Step 5: Run test to verify it passes**

Run: `dotnet test --filter OpportunityDetectorTests`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "feat(core): add opportunity detector with TDD"
```

---

## Task 9: Position sizer (TDD)

**Files:**
- Create: `src/ArbitrageTracker.Core/Sizing/SizingSettings.cs`, `src/ArbitrageTracker.Core/Sizing/PositionSizer.cs`
- Test: `tests/ArbitrageTracker.Core.Tests/PositionSizerTests.cs`

Per-slot budget = `Bankroll × PerSlotFraction` (default `1/8 = 0.125`). Suggested quantity = `min(buyLimit, floor(perSlotBudget / buyPrice))`. Capital needed = `qty × buyPrice`. Projected profit = `qty × netMargin`. Also a method to flag when a set of opportunities overflows the slot count or bankroll.

- [ ] **Step 1: Write the failing test**

`tests/ArbitrageTracker.Core.Tests/PositionSizerTests.cs`:

```csharp
using ArbitrageTracker.Core.Domain;
using ArbitrageTracker.Core.Scoring;
using ArbitrageTracker.Core.Sizing;
using Xunit;

namespace ArbitrageTracker.Core.Tests;

public class PositionSizerTests
{
    private static Opportunity Opp(int id, long buy, long netMargin, int buyLimit) =>
        new(id, $"Item{id}", buy, buy + netMargin, 0, netMargin, 1.0, buyLimit,
            buyLimit * netMargin, 50.0, new SafetyBreakdown(1, 1, 1, 1), 0, 1.0);

    [Fact]
    public void Size_limitedByPerSlotBudget()
    {
        var settings = new SizingSettings { Bankroll = 8_000_000 }; // per slot = 1,000,000
        var sizer = new PositionSizer();
        // buy 1000, limit 5000 → budget allows 1000 units, limit allows 5000 → 1000
        var sized = sizer.Size(Opp(1, buy: 1000, netMargin: 50, buyLimit: 5000), settings);

        Assert.Equal(1000, sized.SuggestedQuantity);
        Assert.Equal(1_000_000, sized.CapitalNeeded);
        Assert.Equal(50_000, sized.ProjectedProfit);
    }

    [Fact]
    public void Size_limitedByBuyLimit()
    {
        var settings = new SizingSettings { Bankroll = 8_000_000 };
        var sizer = new PositionSizer();
        // budget allows 1000 units but limit is 100 → 100
        var sized = sizer.Size(Opp(1, buy: 1000, netMargin: 50, buyLimit: 100), settings);
        Assert.Equal(100, sized.SuggestedQuantity);
        Assert.Equal(100_000, sized.CapitalNeeded);
    }

    [Fact]
    public void Size_zeroQuantityWhenBudgetBelowUnitPrice()
    {
        var settings = new SizingSettings { Bankroll = 4000 }; // per slot 500, buy 1000
        var sizer = new PositionSizer();
        var sized = sizer.Size(Opp(1, buy: 1000, netMargin: 50, buyLimit: 100), settings);
        Assert.Equal(0, sized.SuggestedQuantity);
    }

    [Fact]
    public void Overflows_trueWhenMorePicksThanSlots()
    {
        var settings = new SizingSettings { Bankroll = 1_000_000_000, Slots = 8 };
        var sizer = new PositionSizer();
        var opps = Enumerable.Range(1, 9).Select(i => Opp(i, 1000, 50, 100)).ToList();
        Assert.True(sizer.OverflowsSlots(opps, settings));
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter PositionSizerTests`
Expected: FAIL — `PositionSizer` / `SizingSettings` do not exist.

- [ ] **Step 3: Write `SizingSettings`**

`src/ArbitrageTracker.Core/Sizing/SizingSettings.cs`:

```csharp
namespace ArbitrageTracker.Core.Sizing;

public sealed record SizingSettings
{
    public long Bankroll { get; init; } = 0;
    public int Slots { get; init; } = 8;
    public double PerSlotFraction { get; init; } = 0.125;

    public long PerSlotBudget => (long)(Bankroll * PerSlotFraction);
}
```

- [ ] **Step 4: Write `PositionSizer`**

`src/ArbitrageTracker.Core/Sizing/PositionSizer.cs`:

```csharp
using ArbitrageTracker.Core.Domain;

namespace ArbitrageTracker.Core.Sizing;

public sealed class PositionSizer
{
    public SizedPosition Size(Opportunity opp, SizingSettings settings)
    {
        long budget = settings.PerSlotBudget;
        long byBudget = opp.BuyPrice > 0 ? budget / opp.BuyPrice : 0;
        int qty = (int)Math.Min(opp.BuyLimit, Math.Max(0, byBudget));

        return new SizedPosition(
            ItemId: opp.ItemId,
            SuggestedQuantity: qty,
            CapitalNeeded: qty * opp.BuyPrice,
            ProjectedProfit: qty * opp.NetMargin);
    }

    /// <summary>True if the number of actionable picks exceeds available GE slots.</summary>
    public bool OverflowsSlots(IReadOnlyCollection<Opportunity> opportunities, SizingSettings settings)
        => opportunities.Count > settings.Slots;
}
```

- [ ] **Step 5: Run test to verify it passes**

Run: `dotnet test --filter PositionSizerTests`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "feat(core): add position sizer with TDD"
```

---

## Task 10: Persistence — DbContext, entities, SQLite

**Files:**
- Create: `src/ArbitrageTracker.Data/Entities/Entities.cs`, `src/ArbitrageTracker.Data/ArbitrageDbContext.cs`
- Modify: `src/ArbitrageTracker.Data/ArbitrageTracker.Data.csproj` (packages)

- [ ] **Step 1: Add EF Core + SQLite packages**

```bash
dotnet add src/ArbitrageTracker.Data package Microsoft.EntityFrameworkCore.Sqlite
dotnet add src/ArbitrageTracker.Data package Microsoft.EntityFrameworkCore.Design
```

- [ ] **Step 2: Create entities**

`src/ArbitrageTracker.Data/Entities/Entities.cs`:

```csharp
namespace ArbitrageTracker.Data.Entities;

public class PriceSnapshot
{
    public long Id { get; set; }
    public int ItemId { get; set; }
    public long CapturedAt { get; set; } // unix seconds
    public long? High { get; set; }
    public long HighTime { get; set; }
    public long? Low { get; set; }
    public long LowTime { get; set; }
}

public class BucketSnapshot
{
    public long Id { get; set; }
    public int ItemId { get; set; }
    public string Interval { get; set; } = "5m"; // "5m" | "1h"
    public long Timestamp { get; set; }
    public long? AvgHighPrice { get; set; }
    public long? AvgLowPrice { get; set; }
    public long HighPriceVolume { get; set; }
    public long LowPriceVolume { get; set; }
}

public class OpportunitySnapshot
{
    public long Id { get; set; }
    public int ItemId { get; set; }
    public long DetectedAt { get; set; }
    public long BuyPrice { get; set; }
    public long SellPrice { get; set; }
    public long NetMargin { get; set; }
    public long ExpectedCycleProfit { get; set; }
    public double SafetyScore { get; set; }
    // Forward proxy validation (filled in later by ProxyOutcomeJob).
    public bool? ProxyValidated { get; set; }
    public long? ProxyEvaluatedAt { get; set; }
}

public class ItemMappingEntity
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Examine { get; set; } = "";
    public bool Members { get; set; }
    public int LowAlch { get; set; }
    public int HighAlch { get; set; }
    public int BuyLimit { get; set; }
    public long Value { get; set; }
    public string Icon { get; set; } = "";
}

public class AppSetting
{
    public string Key { get; set; } = "";
    public string Value { get; set; } = "";
}
```

- [ ] **Step 3: Create the DbContext**

`src/ArbitrageTracker.Data/ArbitrageDbContext.cs`:

```csharp
using ArbitrageTracker.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace ArbitrageTracker.Data;

public class ArbitrageDbContext(DbContextOptions<ArbitrageDbContext> options) : DbContext(options)
{
    public DbSet<PriceSnapshot> PriceSnapshots => Set<PriceSnapshot>();
    public DbSet<BucketSnapshot> BucketSnapshots => Set<BucketSnapshot>();
    public DbSet<OpportunitySnapshot> OpportunitySnapshots => Set<OpportunitySnapshot>();
    public DbSet<ItemMappingEntity> ItemMappings => Set<ItemMappingEntity>();
    public DbSet<AppSetting> AppSettings => Set<AppSetting>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<PriceSnapshot>().HasIndex(p => new { p.ItemId, p.CapturedAt });
        b.Entity<BucketSnapshot>().HasIndex(x => new { x.ItemId, x.Interval, x.Timestamp }).IsUnique();
        b.Entity<OpportunitySnapshot>().HasIndex(o => new { o.ItemId, o.DetectedAt });
        b.Entity<AppSetting>().HasKey(s => s.Key);
    }
}
```

- [ ] **Step 4: Create the initial migration**

```bash
dotnet tool install --global dotnet-ef --version 10.* || dotnet tool update --global dotnet-ef --version 10.*
dotnet ef migrations add InitialCreate \
  --project src/ArbitrageTracker.Data \
  --startup-project src/ArbitrageTracker.Web
```

> If the Web startup project isn't wired for EF design-time yet (Task 14 adds the DbContext registration), temporarily add `dotnet add src/ArbitrageTracker.Data package Microsoft.EntityFrameworkCore.Sqlite` design-time factory: create `src/ArbitrageTracker.Data/DesignTimeDbContextFactory.cs` implementing `IDesignTimeDbContextFactory<ArbitrageDbContext>` returning `new ArbitrageDbContext(new DbContextOptionsBuilder<ArbitrageDbContext>().UseSqlite("Data Source=data/arbitrage.db").Options)`. This lets `dotnet ef` run without the Web host.

`src/ArbitrageTracker.Data/DesignTimeDbContextFactory.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace ArbitrageTracker.Data;

public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<ArbitrageDbContext>
{
    public ArbitrageDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<ArbitrageDbContext>()
            .UseSqlite("Data Source=data/arbitrage.db")
            .Options;
        return new ArbitrageDbContext(options);
    }
}
```

Then run the migration command again (against `--project src/ArbitrageTracker.Data` only, no startup project needed):

```bash
dotnet ef migrations add InitialCreate --project src/ArbitrageTracker.Data
```

- [ ] **Step 5: Build to verify migration compiles**

Run: `dotnet build src/ArbitrageTracker.Data`
Expected: Build succeeded; a `Migrations/` folder exists.

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "feat(data): add EF Core DbContext, entities, initial migration"
```

---

## Task 11: Snapshot repository

**Files:**
- Create: `src/ArbitrageTracker.Data/SnapshotRepository.cs`

Thin write/read helper used by pollers, the pipeline, and the proxy job. Includes retention pruning.

- [ ] **Step 1: Write the repository**

`src/ArbitrageTracker.Data/SnapshotRepository.cs`:

```csharp
using ArbitrageTracker.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace ArbitrageTracker.Data;

public sealed class SnapshotRepository(ArbitrageDbContext db)
{
    public async Task SaveLatestAsync(IEnumerable<PriceSnapshot> snapshots, CancellationToken ct)
    {
        db.PriceSnapshots.AddRange(snapshots);
        await db.SaveChangesAsync(ct);
    }

    public async Task UpsertBucketsAsync(IEnumerable<BucketSnapshot> buckets, CancellationToken ct)
    {
        foreach (var bucket in buckets)
        {
            bool exists = await db.BucketSnapshots.AnyAsync(
                x => x.ItemId == bucket.ItemId && x.Interval == bucket.Interval && x.Timestamp == bucket.Timestamp, ct);
            if (!exists) db.BucketSnapshots.Add(bucket);
        }
        await db.SaveChangesAsync(ct);
    }

    public async Task ReplaceMappingsAsync(IEnumerable<ItemMappingEntity> mappings, CancellationToken ct)
    {
        await db.ItemMappings.ExecuteDeleteAsync(ct);
        db.ItemMappings.AddRange(mappings);
        await db.SaveChangesAsync(ct);
    }

    public async Task<long> SaveOpportunityAsync(OpportunitySnapshot opp, CancellationToken ct)
    {
        db.OpportunitySnapshots.Add(opp);
        await db.SaveChangesAsync(ct);
        return opp.Id;
    }

    public async Task PruneAsync(long olderThanUnixSeconds, CancellationToken ct)
    {
        await db.PriceSnapshots.Where(p => p.CapturedAt < olderThanUnixSeconds).ExecuteDeleteAsync(ct);
    }
}
```

- [ ] **Step 2: Build to verify**

Run: `dotnet build src/ArbitrageTracker.Data`
Expected: Build succeeded.

- [ ] **Step 3: Commit**

```bash
git add -A
git commit -m "feat(data): add snapshot repository with retention pruning"
```

---

## Task 12: Wiki prices API client

**Files:**
- Create: `src/ArbitrageTracker.Ingestion/Dto/WikiDtos.cs`, `src/ArbitrageTracker.Ingestion/IWikiPricesClient.cs`, `src/ArbitrageTracker.Ingestion/WikiPricesClient.cs`

Typed `HttpClient` against `https://prices.runescape.wiki/api/v1/osrs`. **Mandatory descriptive `User-Agent`** with contact info (Wiki blocks default agents). Maps JSON DTOs → Core domain records.

- [ ] **Step 1: Add JSON package (if not present)**

`System.Text.Json` ships in the framework; no package needed. Skip.

- [ ] **Step 2: Create DTOs**

`src/ArbitrageTracker.Ingestion/Dto/WikiDtos.cs`:

```csharp
using System.Text.Json.Serialization;

namespace ArbitrageTracker.Ingestion.Dto;

public sealed class WikiLatestResponse
{
    [JsonPropertyName("data")]
    public Dictionary<string, WikiLatestEntry> Data { get; set; } = new();
}

public sealed class WikiLatestEntry
{
    [JsonPropertyName("high")] public long? High { get; set; }
    [JsonPropertyName("highTime")] public long? HighTime { get; set; }
    [JsonPropertyName("low")] public long? Low { get; set; }
    [JsonPropertyName("lowTime")] public long? LowTime { get; set; }
}

public sealed class WikiMappingItem
{
    [JsonPropertyName("id")] public int Id { get; set; }
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("examine")] public string Examine { get; set; } = "";
    [JsonPropertyName("members")] public bool Members { get; set; }
    [JsonPropertyName("lowalch")] public int LowAlch { get; set; }
    [JsonPropertyName("highalch")] public int HighAlch { get; set; }
    [JsonPropertyName("limit")] public int Limit { get; set; }
    [JsonPropertyName("value")] public long Value { get; set; }
    [JsonPropertyName("icon")] public string Icon { get; set; } = "";
}

public sealed class WikiBucketResponse
{
    [JsonPropertyName("data")]
    public Dictionary<string, WikiBucketEntry> Data { get; set; } = new();
}

public sealed class WikiBucketEntry
{
    [JsonPropertyName("avgHighPrice")] public long? AvgHighPrice { get; set; }
    [JsonPropertyName("avgLowPrice")] public long? AvgLowPrice { get; set; }
    [JsonPropertyName("highPriceVolume")] public long HighPriceVolume { get; set; }
    [JsonPropertyName("lowPriceVolume")] public long LowPriceVolume { get; set; }
}
```

- [ ] **Step 3: Create the client interface**

`src/ArbitrageTracker.Ingestion/IWikiPricesClient.cs`:

```csharp
using ArbitrageTracker.Core.Domain;

namespace ArbitrageTracker.Ingestion;

public interface IWikiPricesClient
{
    Task<IReadOnlyList<ItemMapping>> GetMappingAsync(CancellationToken ct);
    Task<IReadOnlyList<LatestPrice>> GetLatestAsync(CancellationToken ct);
    Task<IReadOnlyList<MarketBucket>> Get5mAsync(CancellationToken ct);
    Task<IReadOnlyList<MarketBucket>> Get1hAsync(CancellationToken ct);
}
```

- [ ] **Step 4: Implement the client**

`src/ArbitrageTracker.Ingestion/WikiPricesClient.cs`:

```csharp
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
```

- [ ] **Step 5: Build to verify**

Run: `dotnet build src/ArbitrageTracker.Ingestion`
Expected: Build succeeded.

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "feat(ingestion): add typed Wiki prices API client"
```

---

## Task 13: Price update channel + pollers

**Files:**
- Create: `src/ArbitrageTracker.Ingestion/PriceUpdate.cs`, `src/ArbitrageTracker.Ingestion/PriceUpdateChannel.cs`, `src/ArbitrageTracker.Ingestion/Pollers/MappingLoader.cs`, `LatestPoller.cs`, `FiveMinutePoller.cs`, `HourlyPoller.cs`

- [ ] **Step 1: Create the channel + signal type**

`src/ArbitrageTracker.Ingestion/PriceUpdate.cs`:

```csharp
namespace ArbitrageTracker.Ingestion;

/// <summary>Signal that fresh /latest data landed and detection should run.</summary>
public sealed record PriceUpdate(long ReceivedAt, int ItemCount);
```

`src/ArbitrageTracker.Ingestion/PriceUpdateChannel.cs`:

```csharp
using System.Threading.Channels;

namespace ArbitrageTracker.Ingestion;

/// <summary>Single-reader in-process channel coupling pollers to the detection pipeline.</summary>
public sealed class PriceUpdateChannel
{
    private readonly Channel<PriceUpdate> _channel =
        Channel.CreateBounded<PriceUpdate>(new BoundedChannelOptions(8)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false,
        });

    public ChannelWriter<PriceUpdate> Writer => _channel.Writer;
    public ChannelReader<PriceUpdate> Reader => _channel.Reader;
}
```

- [ ] **Step 2: Create the mapping loader (startup + daily refresh)**

`src/ArbitrageTracker.Ingestion/Pollers/MappingLoader.cs`:

```csharp
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
```

- [ ] **Step 3: Create the latest poller (60s) — writes state, persists, signals channel**

`src/ArbitrageTracker.Ingestion/Pollers/LatestPoller.cs`:

```csharp
using ArbitrageTracker.Core.State;
using ArbitrageTracker.Data;
using ArbitrageTracker.Data.Entities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ArbitrageTracker.Ingestion.Pollers;

public sealed class LatestPoller(
    IWikiPricesClient client,
    MarketState state,
    PriceUpdateChannel channel,
    IServiceScopeFactory scopeFactory,
    ILogger<LatestPoller> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var prices = await client.GetLatestAsync(ct);
                long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

                foreach (var p in prices) state.UpdateLatest(p);

                using (var scope = scopeFactory.CreateScope())
                {
                    var repo = scope.ServiceProvider.GetRequiredService<SnapshotRepository>();
                    await repo.SaveLatestAsync(prices.Select(p => new PriceSnapshot
                    {
                        ItemId = p.ItemId, CapturedAt = now,
                        High = p.High, HighTime = p.HighTime, Low = p.Low, LowTime = p.LowTime
                    }), ct);
                    await repo.PruneAsync(now - 7 * 24 * 3600, ct);
                }

                await channel.Writer.WriteAsync(new PriceUpdate(now, prices.Count), ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                log.LogError(ex, "Latest poll failed");
            }
            await Task.Delay(TimeSpan.FromSeconds(60), ct);
        }
    }
}
```

- [ ] **Step 4: Create the 5m and 1h pollers**

`src/ArbitrageTracker.Ingestion/Pollers/FiveMinutePoller.cs`:

```csharp
using ArbitrageTracker.Core.State;
using ArbitrageTracker.Data;
using ArbitrageTracker.Data.Entities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ArbitrageTracker.Ingestion.Pollers;

public sealed class FiveMinutePoller(
    IWikiPricesClient client, MarketState state,
    IServiceScopeFactory scopeFactory, ILogger<FiveMinutePoller> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var buckets = await client.Get5mAsync(ct);
                foreach (var b in buckets) state.AddBucket5m(b);

                using var scope = scopeFactory.CreateScope();
                var repo = scope.ServiceProvider.GetRequiredService<SnapshotRepository>();
                await repo.UpsertBucketsAsync(buckets.Select(b => new BucketSnapshot
                {
                    ItemId = b.ItemId, Interval = "5m", Timestamp = b.Timestamp,
                    AvgHighPrice = b.AvgHighPrice, AvgLowPrice = b.AvgLowPrice,
                    HighPriceVolume = b.HighPriceVolume, LowPriceVolume = b.LowPriceVolume
                }), ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                log.LogError(ex, "5m poll failed");
            }
            await Task.Delay(TimeSpan.FromMinutes(5), ct);
        }
    }
}
```

`src/ArbitrageTracker.Ingestion/Pollers/HourlyPoller.cs`: identical to `FiveMinutePoller` but calls `client.Get1hAsync`, `state.AddBucket1h`, `Interval = "1h"`, and `Task.Delay(TimeSpan.FromHours(1), ct)`. Write the full file mirroring the structure above with those four substitutions.

- [ ] **Step 5: Build to verify**

Run: `dotnet build src/ArbitrageTracker.Ingestion`
Expected: Build succeeded.

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "feat(ingestion): add price update channel and background pollers"
```

---

## Task 14: Web host wiring — DI, DbContext, HttpClient, hosted services

**Files:**
- Modify: `src/ArbitrageTracker.Web/Program.cs`
- Create: `src/ArbitrageTracker.Web/appsettings.json` entries

- [ ] **Step 1: Register everything in Program.cs**

Replace the service-registration region of `src/ArbitrageTracker.Web/Program.cs` (keep the template's Blazor + app pipeline lines) so it contains:

```csharp
using ArbitrageTracker.Core.Detection;
using ArbitrageTracker.Core.Sizing;
using ArbitrageTracker.Core.State;
using ArbitrageTracker.Data;
using ArbitrageTracker.Ingestion;
using ArbitrageTracker.Ingestion.Pollers;
using ArbitrageTracker.Web.Components;
using ArbitrageTracker.Web.Hubs;
using ArbitrageTracker.Web.Pipeline;
using ArbitrageTracker.Web.Validation;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents().AddInteractiveServerComponents();
builder.Services.AddSignalR();

// Persistence
builder.Services.AddDbContext<ArbitrageDbContext>(o =>
    o.UseSqlite(builder.Configuration.GetConnectionString("Sqlite") ?? "Data Source=data/arbitrage.db"));
builder.Services.AddScoped<SnapshotRepository>();

// Time
builder.Services.AddSingleton(TimeProvider.System);

// Core singletons (shared hot state + stateless services)
builder.Services.AddSingleton<MarketState>();
builder.Services.AddSingleton<OpportunityDetector>();
builder.Services.AddSingleton<PositionSizer>();
builder.Services.AddSingleton<PriceUpdateChannel>();
builder.Services.AddSingleton<OpportunityCache>();

// Settings store (loaded from DB / defaults)
builder.Services.AddSingleton<SettingsStore>();

// Wiki API client with REQUIRED descriptive User-Agent (Wiki blocks default agents).
builder.Services.AddHttpClient<IWikiPricesClient, WikiPricesClient>(c =>
{
    c.BaseAddress = new Uri("https://prices.runescape.wiki/api/v1/osrs/");
    c.DefaultRequestHeaders.UserAgent.ParseAdd(
        builder.Configuration["Wiki:UserAgent"]
        ?? "ArbitrageTracker/1.0 (personal flipping tool; contact stefan.daniel.schranz96@gmail.com)");
});

// Background services
builder.Services.AddHostedService<MappingLoader>();
builder.Services.AddHostedService<LatestPoller>();
builder.Services.AddHostedService<FiveMinutePoller>();
builder.Services.AddHostedService<HourlyPoller>();
builder.Services.AddHostedService<DetectionPipeline>();
builder.Services.AddHostedService<ProxyOutcomeJob>();

var app = builder.Build();

// Apply migrations on startup.
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ArbitrageDbContext>();
    db.Database.Migrate();
}

if (!app.Environment.IsDevelopment())
    app.UseExceptionHandler("/Error", createScopeForErrors: true);

app.UseStaticFiles();
app.UseAntiforgery();

app.MapRazorComponents<App>().AddInteractiveServerRenderMode();
app.MapHub<OpportunitiesHub>("/hubs/opportunities");

app.Run();
```

- [ ] **Step 2: Add connection string + Wiki UA to appsettings.json**

Add to `src/ArbitrageTracker.Web/appsettings.json`:

```json
{
  "ConnectionStrings": { "Sqlite": "Data Source=data/arbitrage.db" },
  "Wiki": { "UserAgent": "ArbitrageTracker/1.0 (personal flipping tool; contact stefan.daniel.schranz96@gmail.com)" }
}
```

- [ ] **Step 3: Note — referenced types come from later tasks**

`OpportunityCache`, `SettingsStore`, `DetectionPipeline`, `ProxyOutcomeJob`, `OpportunitiesHub`, and `App` are created in Tasks 15–19. The project will not build until those exist. Proceed to Task 15; build verification happens at the end of Task 19.

- [ ] **Step 4: Commit**

```bash
git add -A
git commit -m "feat(web): wire DI, DbContext, HttpClient, hosted services in Program.cs"
```

---

## Task 15: Settings store + opportunity cache + detection pipeline

**Files:**
- Create: `src/ArbitrageTracker.Web/Pipeline/SettingsStore.cs`, `src/ArbitrageTracker.Web/Pipeline/OpportunityCache.cs`, `src/ArbitrageTracker.Web/Pipeline/DetectionPipeline.cs`

- [ ] **Step 1: Create `SettingsStore`**

Holds the live `DetectionSettings` + `SizingSettings`, persisted to the `AppSettings` table as JSON. Raises an event when changed so the pipeline re-runs.

`src/ArbitrageTracker.Web/Pipeline/SettingsStore.cs`:

```csharp
using System.Text.Json;
using ArbitrageTracker.Core.Detection;
using ArbitrageTracker.Core.Sizing;

namespace ArbitrageTracker.Web.Pipeline;

public sealed class SettingsStore
{
    private readonly object _gate = new();
    public DetectionSettings Detection { get; private set; } = DetectionSettings.Default;
    public SizingSettings Sizing { get; private set; } = new();

    public event Action? Changed;

    public void Update(DetectionSettings detection, SizingSettings sizing)
    {
        lock (_gate)
        {
            Detection = detection;
            Sizing = sizing;
        }
        Changed?.Invoke();
    }
}
```

- [ ] **Step 2: Create `OpportunityCache`**

Holds the latest ranked opportunity list for new SignalR clients / page loads.

`src/ArbitrageTracker.Web/Pipeline/OpportunityCache.cs`:

```csharp
using ArbitrageTracker.Core.Domain;

namespace ArbitrageTracker.Web.Pipeline;

public sealed class OpportunityCache
{
    private volatile IReadOnlyList<Opportunity> _current = Array.Empty<Opportunity>();
    public IReadOnlyList<Opportunity> Current => _current;
    public void Set(IReadOnlyList<Opportunity> opportunities) => _current = opportunities;
}
```

- [ ] **Step 3: Create `DetectionPipeline`**

Consumes the channel, runs the detector over all known items, ranks, persists opportunity snapshots, updates the cache, and pushes to the SignalR hub.

`src/ArbitrageTracker.Web/Pipeline/DetectionPipeline.cs`:

```csharp
using ArbitrageTracker.Core.Detection;
using ArbitrageTracker.Core.Domain;
using ArbitrageTracker.Core.State;
using ArbitrageTracker.Data;
using ArbitrageTracker.Data.Entities;
using ArbitrageTracker.Ingestion;
using ArbitrageTracker.Web.Hubs;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ArbitrageTracker.Web.Pipeline;

public sealed class DetectionPipeline(
    PriceUpdateChannel channel,
    MarketState state,
    OpportunityDetector detector,
    SettingsStore settings,
    OpportunityCache cache,
    IHubContext<OpportunitiesHub> hub,
    IServiceScopeFactory scopeFactory,
    TimeProvider clock,
    ILogger<DetectionPipeline> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        await foreach (var _ in channel.Reader.ReadAllAsync(ct))
        {
            try
            {
                var opps = new List<Opportunity>();
                foreach (int id in state.KnownItemIds)
                    if (state.TryGetSnapshot(id, out var snap) && snap is not null)
                    {
                        var opp = detector.Detect(snap, settings.Detection);
                        if (opp is not null) opps.Add(opp);
                    }

                var ranked = opps.OrderByDescending(o => o.RankScore).ToList();
                cache.Set(ranked);

                long now = clock.GetUtcNow().ToUnixTimeSeconds();
                using (var scope = scopeFactory.CreateScope())
                {
                    var repo = scope.ServiceProvider.GetRequiredService<SnapshotRepository>();
                    foreach (var o in ranked.Take(50))
                        await repo.SaveOpportunityAsync(new OpportunitySnapshot
                        {
                            ItemId = o.ItemId, DetectedAt = now,
                            BuyPrice = o.BuyPrice, SellPrice = o.SellPrice,
                            NetMargin = o.NetMargin, ExpectedCycleProfit = o.ExpectedCycleProfit,
                            SafetyScore = o.SafetyScore
                        }, ct);
                }

                await hub.Clients.All.SendAsync("OpportunitiesUpdated", ranked, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                log.LogError(ex, "Detection pipeline iteration failed");
            }
        }
    }
}
```

- [ ] **Step 4: Commit**

```bash
git add -A
git commit -m "feat(web): add settings store, opportunity cache, detection pipeline"
```

---

## Task 16: SignalR hub

**Files:**
- Create: `src/ArbitrageTracker.Web/Hubs/OpportunitiesHub.cs`

- [ ] **Step 1: Create the hub**

`src/ArbitrageTracker.Web/Hubs/OpportunitiesHub.cs`:

```csharp
using ArbitrageTracker.Core.Domain;
using ArbitrageTracker.Web.Pipeline;
using Microsoft.AspNetCore.SignalR;

namespace ArbitrageTracker.Web.Hubs;

public sealed class OpportunitiesHub(OpportunityCache cache) : Hub
{
    /// <summary>New clients pull the current snapshot immediately on connect.</summary>
    public IReadOnlyList<Opportunity> GetCurrent() => cache.Current;
}
```

- [ ] **Step 2: Commit**

```bash
git add -A
git commit -m "feat(web): add SignalR opportunities hub"
```

---

## Task 17: Forward proxy validation job

**Files:**
- Create: `src/ArbitrageTracker.Web/Validation/ProxyOutcomeJob.cs`

Periodically (every 5 min) finds opportunity snapshots detected ~1h ago that haven't been validated, and marks `ProxyValidated = true` if a profitable spread + two-sided volume persisted in the buckets recorded after detection. This is the only honest measure of whether the score predicts anything — we never see our own fills.

- [ ] **Step 1: Create the job**

`src/ArbitrageTracker.Web/Validation/ProxyOutcomeJob.cs`:

```csharp
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
```

- [ ] **Step 2: Commit**

```bash
git add -A
git commit -m "feat(web): add forward proxy validation job for scoring calibration"
```

---

## Task 18: Blazor UI — opportunities grid with live updates + notifications

**Files:**
- Modify: `src/ArbitrageTracker.Web/Components/App.razor`, `Routes.razor`, `_Imports.razor` (created by template)
- Create: `src/ArbitrageTracker.Web/Components/Pages/Opportunities.razor`
- Create: `src/ArbitrageTracker.Web/wwwroot/notify.js`

- [ ] **Step 1: Ensure SignalR client + imports**

Add to `src/ArbitrageTracker.Web/Components/_Imports.razor`:

```razor
@using Microsoft.AspNetCore.SignalR.Client
@using ArbitrageTracker.Core.Domain
```

Add the SignalR client package:

```bash
dotnet add src/ArbitrageTracker.Web package Microsoft.AspNetCore.SignalR.Client
```

- [ ] **Step 2: Create the notification JS helper**

`src/ArbitrageTracker.Web/wwwroot/notify.js`:

```javascript
export function requestPermission() {
  if ("Notification" in window && Notification.permission === "default") {
    Notification.requestPermission();
  }
}

export function notify(title, body) {
  if ("Notification" in window && Notification.permission === "granted") {
    new Notification(title, { body });
  }
}
```

- [ ] **Step 3: Create the opportunities page**

`src/ArbitrageTracker.Web/Components/Pages/Opportunities.razor`:

```razor
@page "/"
@rendermode InteractiveServer
@implements IAsyncDisposable
@inject NavigationManager Nav
@inject IJSRuntime JS

<PageTitle>Arbitrage Opportunities</PageTitle>

<h1>Opportunities</h1>
<p>Live @opportunities.Count picks. Connection: <strong>@connectionState</strong></p>

<table class="grid">
    <thead>
        <tr>
            <th>Item</th><th>Buy</th><th>Sell</th><th>Net margin</th><th>Margin %</th>
            <th>Limit</th><th>Cycle profit</th><th>Safety</th><th>Age (s)</th>
        </tr>
    </thead>
    <tbody>
        @foreach (var o in opportunities)
        {
            <tr>
                <td>@o.Name</td>
                <td>@o.BuyPrice.ToString("N0")</td>
                <td>@o.SellPrice.ToString("N0")</td>
                <td>@o.NetMargin.ToString("N0")</td>
                <td>@o.MarginPercent.ToString("F1")%</td>
                <td>@o.BuyLimit</td>
                <td>@o.ExpectedCycleProfit.ToString("N0")</td>
                <td>@o.SafetyScore.ToString("F0")</td>
                <td>@o.PriceAgeSeconds</td>
            </tr>
        }
    </tbody>
</table>

@code {
    private HubConnection? _hub;
    private IJSObjectReference? _notifyModule;
    private List<Opportunity> opportunities = new();
    private string connectionState = "connecting";

    // Notification threshold: ping when a fresh high-value pick appears.
    private const double SafetyThreshold = 70;
    private const long ProfitThreshold = 200_000;
    private HashSet<int> _alreadyAlerted = new();

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender) return;

        _notifyModule = await JS.InvokeAsync<IJSObjectReference>("import", "./notify.js");
        await _notifyModule.InvokeVoidAsync("requestPermission");

        _hub = new HubConnectionBuilder()
            .WithUrl(Nav.ToAbsoluteUri("/hubs/opportunities"))
            .WithAutomaticReconnect()
            .Build();

        _hub.On<List<Opportunity>>("OpportunitiesUpdated", async opps =>
        {
            opportunities = opps;
            await AlertOnNewHighValue(opps);
            await InvokeAsync(StateHasChanged);
        });

        _hub.Reconnecting += _ => { connectionState = "reconnecting"; return InvokeAsync(StateHasChanged); };
        _hub.Reconnected += _ => { connectionState = "connected"; return InvokeAsync(StateHasChanged); };

        await _hub.StartAsync();
        connectionState = "connected";
        opportunities = await _hub.InvokeAsync<List<Opportunity>>("GetCurrent");
        await InvokeAsync(StateHasChanged);
    }

    private async Task AlertOnNewHighValue(List<Opportunity> opps)
    {
        if (_notifyModule is null) return;
        foreach (var o in opps)
        {
            if (o.SafetyScore >= SafetyThreshold
                && o.ExpectedCycleProfit >= ProfitThreshold
                && _alreadyAlerted.Add(o.ItemId))
            {
                await _notifyModule.InvokeVoidAsync("notify",
                    $"Flip: {o.Name}",
                    $"Profit ~{o.ExpectedCycleProfit:N0} | safety {o.SafetyScore:F0}");
            }
        }
        // Forget items that dropped out so they can re-alert later.
        _alreadyAlerted.IntersectWith(opps.Select(o => o.ItemId));
    }

    public async ValueTask DisposeAsync()
    {
        if (_hub is not null) await _hub.DisposeAsync();
        if (_notifyModule is not null) await _notifyModule.DisposeAsync();
    }
}
```

- [ ] **Step 4: Ensure routing renders the page**

Confirm `src/ArbitrageTracker.Web/Components/Routes.razor` uses a `<Router>` over the app assembly (the `blazor --empty` template generates this). If `App.razor` does not reference `notify.js`, no change needed — it is imported dynamically. Ensure `App.razor`'s `<head>` includes `<base href="/" />` (template default).

- [ ] **Step 5: Build and run smoke test**

Run: `dotnet build`
Expected: Build succeeded (all Task 14 referenced types now exist).

Run: `dotnet run --project src/ArbitrageTracker.Web` and open the printed localhost URL.
Expected: page loads, connection state reaches "connected", and within ~1–2 minutes (after the first `/latest` poll + 5m poll) rows begin to appear. Stop with Ctrl+C.

> Note: the grid needs at least one `/5m` poll to populate volumes for scoring; opportunities with no 5m bucket yet score low on liquidity. This is expected on a cold start.

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "feat(web): add live opportunities grid with SignalR and desktop notifications"
```

---

## Task 19: Settings UI

**Files:**
- Create: `src/ArbitrageTracker.Web/Components/Pages/Settings.razor`
- Modify: `src/ArbitrageTracker.Web/Components/Pages/Opportunities.razor` (add nav link)

- [ ] **Step 1: Create the settings page**

`src/ArbitrageTracker.Web/Components/Pages/Settings.razor`:

```razor
@page "/settings"
@rendermode InteractiveServer
@using ArbitrageTracker.Core.Detection
@using ArbitrageTracker.Core.Sizing
@inject ArbitrageTracker.Web.Pipeline.SettingsStore Store

<PageTitle>Settings</PageTitle>
<h1>Settings</h1>

<div class="field"><label>Max unit price (gp)</label>
    <input type="number" @bind="maxUnitPrice" /></div>
<div class="field"><label>Min cycle profit (gp)</label>
    <input type="number" @bind="minCycleProfit" /></div>
<div class="field"><label>Min margin %</label>
    <input type="number" step="0.1" @bind="minMarginPercent" /></div>
<div class="field"><label>Min safety score (0-100)</label>
    <input type="number" @bind="minSafety" /></div>
<div class="field"><label>Bankroll (gp)</label>
    <input type="number" @bind="bankroll" /></div>

<button @onclick="Save">Save</button>
@if (saved) { <span>Saved ✓</span> }

@code {
    private long maxUnitPrice, minCycleProfit, minSafety, bankroll;
    private double minMarginPercent;
    private bool saved;

    protected override void OnInitialized()
    {
        maxUnitPrice = Store.Detection.MaxUnitPrice;
        minCycleProfit = Store.Detection.MinCycleProfit;
        minMarginPercent = Store.Detection.MinMarginPercent;
        minSafety = (long)Store.Detection.MinSafetyScore;
        bankroll = Store.Sizing.Bankroll;
    }

    private void Save()
    {
        var detection = Store.Detection with
        {
            MaxUnitPrice = maxUnitPrice,
            MinCycleProfit = minCycleProfit,
            MinMarginPercent = minMarginPercent,
            MinSafetyScore = minSafety
        };
        var sizing = Store.Sizing with { Bankroll = bankroll };
        Store.Update(detection, sizing);
        saved = true;
    }
}
```

- [ ] **Step 2: Add a nav link on the opportunities page**

In `src/ArbitrageTracker.Web/Components/Pages/Opportunities.razor`, under the `<h1>Opportunities</h1>` line, add:

```razor
<nav><a href="/settings">Settings</a></nav>
```

- [ ] **Step 3: Build and verify**

Run: `dotnet build`
Expected: Build succeeded.

Run: `dotnet run --project src/ArbitrageTracker.Web`, open `/settings`, change "Min cycle profit", click Save. Expected: "Saved ✓" appears; returning to `/` the grid re-filters on the next poll. Stop with Ctrl+C.

- [ ] **Step 4: Commit**

```bash
git add -A
git commit -m "feat(web): add settings page for live detection/sizing thresholds"
```

---

## Task 20: Full test + integration verification

**Files:** none (verification only)

- [ ] **Step 1: Run the complete test suite**

Run: `dotnet test`
Expected: All Core test classes pass (GrandExchangeTax, FlipCalculator, SafetyComponents, SafetyScorer, MarketState, OpportunityDetector, PositionSizer).

- [ ] **Step 2: Cold-start integration check**

Run: `dotnet run --project src/ArbitrageTracker.Web`. Leave running ~6 minutes.
Expected:
- Logs show "Loaded N item mappings" (N in the thousands).
- A `/latest` poll every 60s, a `/5m` poll within 5 min.
- `data/arbitrage.db` is created and grows.
- The grid populates with ranked opportunities; safety scores are non-zero after the first 5m poll.
- No unhandled exceptions in the console.

- [ ] **Step 3: Final commit / tag the milestone**

```bash
git add -A
git commit -m "test: verify full suite and cold-start integration" --allow-empty
git tag mvp-complete
```

---

## Self-Review Notes

- **Spec coverage:** ingestion (Tasks 12–13), detection with all six filters incl. buy-limit + liquidity + capital cap (Task 8), tax model with 5M cap + exempt list (Task 3), safety score as clean-fill probability with all four components (Tasks 5–6), bankroll sizing + slot overflow flag (Task 9), SQLite persistence + retention (Tasks 10–11), real-time Blazor UI + SignalR (Tasks 16, 18), threshold notifications (Task 18), forward proxy validation loop (Task 17), settings UI (Task 19). All outline scope items map to a task.
- **Deferred from outline (documented):** the detail drawer + sparkline and the calibration *view* are not in this MVP plan — the proxy data is captured (Task 17) but visualised later; the `ItemDetail.razor` component is a fast-follow. Tax-exempt item IDs default to an empty set (wired through `DetectionSettings.TaxExemptItemIds`) and are seeded manually post-MVP, matching the outline's open question.
- **Type consistency:** `Opportunity`, `SafetyBreakdown`, `SizedPosition`, `DetectionSettings`, `SizingSettings`, `MarketState`, `OpportunityDetector(TimeProvider)`, `FlipCalculator.Compute`, `GrandExchangeTax.Calculate(sellPrice, exempt)` names are used identically across producer and consumer tasks.
