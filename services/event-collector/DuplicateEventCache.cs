using Microsoft.Extensions.Caching.Memory;

namespace Telumera.Services.EventCollector.Api;

/// <summary>
/// Bounded in-memory dedup for a client-retried event id (packages/browser-sdk's queue.ts retries a
/// failed batch a few times over seconds; this comfortably covers that window). Not
/// packages/idempotency — that package requires a DbContext/persisted table, which doesn't fit a
/// service that owns "none persistent" data. Single-instance only: a duplicate landing on a different
/// Collector replica wouldn't be caught — acceptable for the current single-instance deployment, noted
/// as a future Redis-backed hardening item if this is ever scaled horizontally.
/// </summary>
public sealed class DuplicateEventCache(IMemoryCache cache)
{
    private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(10);

    /// <summary>
    /// Returns true and marks the id seen if this is the first time it's been observed; false if it's
    /// a duplicate. Not perfectly atomic under concurrent requests carrying the same id (a genuine
    /// simultaneous race can let both through) — the same accepted-risk trade-off ADR 0004 already
    /// makes for this ingestion path.
    /// </summary>
    public bool TryClaim(string eventId)
    {
        if (cache.TryGetValue(eventId, out _))
        {
            return false;
        }

        cache.Set(eventId, true, Ttl);
        return true;
    }
}
