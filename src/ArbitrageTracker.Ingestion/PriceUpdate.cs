namespace ArbitrageTracker.Ingestion;

/// <summary>Signal that fresh /latest data landed and detection should run.</summary>
public sealed record PriceUpdate(long ReceivedAt, int ItemCount);
