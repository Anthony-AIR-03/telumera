namespace Telumera.Services.Analytics.Api;

/// <summary>Consumer-side shape of <c>collector.quality.v1</c> — duplicated from event-collector's own
/// record by design (decoupled, same as every other event consumer in this repo).</summary>
public sealed record CollectorQualityPayload(string Date, IReadOnlyList<CollectorQualityCount> Counts);

public sealed record CollectorQualityCount(string SiteId, string Dimension, long Count);

/// <summary>Last-known depth of the analytics dead-letter queue, refreshed by <see cref="DeadLetterProbe"/>.</summary>
public sealed class DeadLetterGauge
{
    private long _depth = -1; // -1 = not yet probed

    public long Depth => Interlocked.Read(ref _depth);

    public void Set(long value) => Interlocked.Exchange(ref _depth, value);
}
