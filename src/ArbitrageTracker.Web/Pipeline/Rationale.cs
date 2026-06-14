using ArbitrageTracker.Core.Domain;

namespace ArbitrageTracker.Web.Pipeline;

/// <summary>One weighted input to the safety score, with a plain-language reason.</summary>
public sealed record SafetyFactor(
    string Label, double Value, int WeightPct, string Status, string Explanation);

/// <summary>
/// Turns raw numbers into the "why" behind each judgement — the credibility layer.
/// Pure presentation logic; mirrors the weights in <c>SafetyScorer</c>.
/// </summary>
public static class Rationale
{
    public static string Tier(double safety) =>
        safety >= 80 ? "High confidence"
        : safety >= 65 ? "Solid"
        : safety >= 50 ? "Moderate"
        : "Thin";

    public static string TierClass(double safety) =>
        safety >= 80 ? "tier-high"
        : safety >= 65 ? "tier-solid"
        : safety >= 50 ? "tier-moderate"
        : "tier-thin";

    private static string Status(double v) => v >= 0.66 ? "good" : v >= 0.4 ? "ok" : "weak";

    public static IReadOnlyList<SafetyFactor> Factors(SafetyBreakdown b) => new[]
    {
        new SafetyFactor("Two-sided liquidity", b.Liquidity, 25, Status(b.Liquidity),
            "Both sides trading right now (geometric mean of recent buy/sell volume) — a dead side scores zero."),
        new SafetyFactor("Market depth", b.DemandDepth, 25, Status(b.DemandDepth),
            "Structural 'staple' safety: runes, ammo and food trade in size every window; niche PvP/wilderness items trade thinly and score low."),
        new SafetyFactor("Price stability", b.Volatility, 20, Status(b.Volatility),
            "Price held steady recently, so the spread is unlikely to move against you mid-flip."),
        new SafetyFactor("Spread persistence", b.Persistence, 15, Status(b.Persistence),
            "The profitable gap recurred across recent windows — not a one-off print."),
        new SafetyFactor("Price freshness", b.Freshness, 15, Status(b.Freshness),
            "Both quotes traded very recently, so this is live, not stale."),
    };

    /// <summary>One-sentence verdict naming the strongest support and the weakest link.</summary>
    public static string Summary(Opportunity o)
    {
        var factors = Factors(o.SafetyBreakdown);
        var strongest = factors.MaxBy(f => f.Value)!;
        var weakest = factors.MinBy(f => f.Value)!;

        string lead = $"{Tier(o.SafetyScore)} — {o.SafetyScore:F0}/100.";
        if (weakest.Value >= 0.66)
            return $"{lead} Every factor is strong; {strongest.Label.ToLowerInvariant()} leads at {strongest.Value:P0}.";
        return $"{lead} Strongest support is {strongest.Label.ToLowerInvariant()} ({strongest.Value:P0}); "
             + $"the weak point is {weakest.Label.ToLowerInvariant()} at {weakest.Value:P0}.";
    }

    /// <summary>Compact age, e.g. 45s / 7m / 1h 3m.</summary>
    public static string Age(long seconds)
    {
        if (seconds < 90) return $"{seconds}s";
        if (seconds < 3600) return $"{seconds / 60}m";
        return $"{seconds / 3600}h {(seconds % 3600) / 60}m";
    }

    public static string FreshnessClass(long seconds) =>
        seconds <= 300 ? "fresh-good" : seconds <= 900 ? "fresh-ok" : "fresh-weak";

    /// <summary>
    /// Lowest sell price at which the flip still breaks even. Derived from the actual net margin
    /// (which already accounts for tax — including the 0% tax on sub-50gp items), not a flat 2%
    /// assumption, so it's correct on the exact price that decides profit vs loss.
    /// </summary>
    public static long BreakEvenSell(long sellPrice, long netMargin) => sellPrice - netMargin;

    /// <summary>How robust the margin is to slippage. Margin IS the cushion before break-even.</summary>
    public static string MarginTier(double marginPct) =>
        marginPct >= 5 ? "comfortable"
        : marginPct >= 3 ? "ok"
        : marginPct >= 2 ? "thin"
        : "razor";

    public static string MarginTierClass(double marginPct) =>
        marginPct >= 5 ? "m-good"
        : marginPct >= 3 ? "m-ok"
        : "m-weak";

    /// <summary>Compact fill-time estimate, e.g. ~4m / ~2.3h / 8h+.</summary>
    public static string FillLabel(double hours) =>
        hours <= 0 ? "instant"
        : hours < 1 ? $"~{Math.Max(1, (int)Math.Round(hours * 60))}m"
        : hours <= 8 ? $"~{hours:0.#}h"
        : "8h+";

    public static bool FillSlow(double hours) => hours > 8;

    /// <summary>Per-dose price label, e.g. "200/dose".</summary>
    public static string DosePerDose(long price, int dose) => $"{price / (double)dose:0.#}/dose";

    /// <summary>Abbreviated coins, e.g. 1.25M / 548.9k / 312.</summary>
    public static string Gp(long v)
    {
        double a = Math.Abs(v);
        return a >= 1_000_000_000 ? $"{v / 1_000_000_000.0:0.##}B"
            : a >= 1_000_000 ? $"{v / 1_000_000.0:0.##}M"
            : a >= 10_000 ? $"{v / 1_000.0:0.#}k"
            : v.ToString("N0");
    }
}
