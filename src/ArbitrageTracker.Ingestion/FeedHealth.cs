namespace ArbitrageTracker.Ingestion;

/// <summary>
/// Tracks whether the upstream price feed is actually healthy. SignalR being "connected"
/// only means the browser can reach our server — it says nothing about whether the OSRS Wiki
/// API is responding. Trading on a silently-dead feed is a direct way to lose money, so the
/// UI surfaces this explicitly.
/// </summary>
public sealed class FeedHealth
{
    private long _lastLatestSuccessUnix;
    private int _consecutiveFailures;
    private volatile string? _lastError;

    public void RecordLatestSuccess(long unixSeconds)
    {
        Interlocked.Exchange(ref _lastLatestSuccessUnix, unixSeconds);
        Interlocked.Exchange(ref _consecutiveFailures, 0);
        _lastError = null;
    }

    public void RecordLatestFailure(string error)
    {
        Interlocked.Increment(ref _consecutiveFailures);
        _lastError = error;
    }

    public long LastLatestSuccessUnix => Interlocked.Read(ref _lastLatestSuccessUnix);
    public int ConsecutiveFailures => _consecutiveFailures;
    public string? LastError => _lastError;
}
