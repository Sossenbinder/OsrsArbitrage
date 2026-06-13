namespace ArbitrageTracker.Core.Detection;

public sealed record DetectionSettings
{
    public long MaxUnitPrice { get; init; } = 1_000_000;
    public long MinCycleProfit { get; init; } = 50_000;
    public double MinMarginPercent { get; init; } = 0.0;
    public double MinSafetyScore { get; init; } = 50.0;
    public long MaxAgeSeconds { get; init; } = 1800;

    /// <summary>
    /// Reject "too good to be true" spreads. A real liquid flip rarely exceeds a low double-digit
    /// margin; an extreme margin almost always means one side's price is stale/illiquid bad data.
    /// 0 disables the cap.
    /// </summary>
    public double MaxMarginPercent { get; init; } = 20.0;

    /// <summary>
    /// Minimum recent traded volume required on BOTH the buy and sell side (last 5m bucket).
    /// Guards against one-sided markets where you can buy but not cleanly sell (or vice versa).
    /// </summary>
    public long MinTwoSidedVolume { get; init; } = 20;
    public IReadOnlySet<int> AllowList { get; init; } = new HashSet<int>();
    public IReadOnlySet<int> DenyList { get; init; } = new HashSet<int>();
    public IReadOnlySet<int> TaxExemptItemIds { get; init; } = new HashSet<int>();

    public static DetectionSettings Default => new();
}
