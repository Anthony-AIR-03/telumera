using System.Collections.Concurrent;

namespace Telumera.Services.EventCollector.Api;

/// <summary>
/// The ingestion outcomes this service counts (M01.8 data-quality dashboard). Only the outcomes this
/// service alone can see live here — <c>bot</c> and <c>delayed</c> are derived from the durable
/// `events` table by services/analytics at query time, not published from here.
/// </summary>
public static class QualityDimensions
{
    public const string Accepted = "accepted";
    public const string RejectedValidation = "rejected_validation";
    public const string RejectedUnknownToken = "rejected_unknown_token";
    public const string RejectedOrigin = "rejected_origin";
    public const string RejectedModuleDisabled = "rejected_module_disabled";
    public const string Duplicate = "duplicate";
    public const string DroppedOverload = "dropped_overload";
}

/// <summary>
/// In-memory per-(site, dimension) tally of ingestion outcomes, drained and published as
/// <c>collector.quality.v1</c> deltas by <see cref="QualityRollupPublisher"/>. Best-effort by design:
/// a crash between accept and the next 60s publish loses that partial interval. That is an acceptable
/// cost for an operational data-quality view — the authoritative raw-vs-processed reconcile is
/// <c>tools/reconciliation-report</c>, not this. Rejections that never resolved a token
/// (<see cref="QualityDimensions.RejectedUnknownToken"/>) are recorded under <see cref="Guid.Empty"/>
/// since they can't be attributed to a site.
/// </summary>
public sealed class QualityCounters
{
    private readonly ConcurrentDictionary<(Guid SiteId, string Dimension), long> _pending = new();

    public void Record(Guid siteId, string dimension, long count = 1) =>
        _pending.AddOrUpdate((siteId, dimension), count, (_, current) => current + count);

    /// <summary>Atomically takes and clears the accumulated deltas.</summary>
    public IReadOnlyList<QualityDelta> Drain()
    {
        var drained = new List<QualityDelta>(_pending.Count);
        foreach (var key in _pending.Keys)
        {
            if (_pending.TryRemove(key, out var value) && value != 0)
            {
                drained.Add(new QualityDelta(key.SiteId, key.Dimension, value));
            }
        }
        return drained;
    }

    /// <summary>Adds deltas back after a failed publish so they're retried on the next interval.</summary>
    public void Restore(IEnumerable<QualityDelta> deltas)
    {
        foreach (var delta in deltas)
        {
            Record(delta.SiteId, delta.Dimension, delta.Count);
        }
    }
}

public readonly record struct QualityDelta(Guid SiteId, string Dimension, long Count);
