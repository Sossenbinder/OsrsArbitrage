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
    double RankScore,
    long BuyVolume5m,
    long SellVolume5m);

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
