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
