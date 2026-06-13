namespace ArbitrageTracker.Core.Detection;

/// <summary>
/// Detection is permissive by default: it surfaces every <em>valid</em> opportunity and lets the
/// user filter in the UI. The only defaults that bite are <b>validity</b> gates, not preferences —
/// stale prices, post-tax losses, one-sided markets and implausible (glitch) margins are still
/// rejected because those aren't opportunities at all. Preference thresholds (min profit, min
/// safety, min margin, price cap) default to "off" and live as table filters instead.
/// </summary>
public sealed record DetectionSettings
{
    // --- preference thresholds: off by default (the user filters in the table) ---
    public long MaxUnitPrice { get; init; } = long.MaxValue;
    public long MinCycleProfit { get; init; } = 0;
    public double MinMarginPercent { get; init; } = 0.0;
    public double MinSafetyScore { get; init; } = 0.0;

    // --- validity gates: keep, these prevent garbage/loss-making rows ---

    /// <summary>Both sides must have traded within this window, or the spread is stale (wrong), not live.</summary>
    public long MaxAgeSeconds { get; init; } = 1800;

    /// <summary>
    /// Reject implausible "too good to be true" margins as almost-certain bad data (a real liquid
    /// flip never clears triple digits). This is a garbage filter, not a preference — a genuine
    /// fresh spike well below this still shows. 0 disables it.
    /// </summary>
    public double MaxMarginPercent { get; init; } = 100.0;

    /// <summary>Both sides must show at least this much recent volume — i.e. the market is genuinely two-sided.</summary>
    public long MinTwoSidedVolume { get; init; } = 1;

    public IReadOnlySet<int> AllowList { get; init; } = new HashSet<int>();
    public IReadOnlySet<int> DenyList { get; init; } = new HashSet<int>();
    public IReadOnlySet<int> TaxExemptItemIds { get; init; } = new HashSet<int>();

    public static DetectionSettings Default => new();
}
