namespace ArbitrageTracker.Core.Sizing;

public sealed record SizingSettings
{
    public long Bankroll { get; init; } = 0;
    public int Slots { get; init; } = 8;
    public double PerSlotFraction { get; init; } = 0.125;

    public long PerSlotBudget => (long)(Bankroll * PerSlotFraction);
}
