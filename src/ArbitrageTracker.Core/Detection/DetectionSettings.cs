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
