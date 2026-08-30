using Microsoft.EntityFrameworkCore;

namespace Telumera.Idempotency;

public static class IdempotencyGuardExtensions
{
    /// <summary>
    /// Checks whether <paramref name="eventId"/> has already been processed and, if not, stages a
    /// <see cref="ProcessedEvent"/> row for it on <paramref name="db"/>'s change tracker — the caller
    /// is expected to make its own domain-state writes on the same <paramref name="db"/> and call
    /// <c>SaveChangesAsync</c> once, so the "processed" marker commits atomically with the side effect
    /// it guards (mirrors packages/outbox's single-transaction principle, applied to consumption
    /// instead of publishing). Typical use in a Dapr subscription handler:
    /// <code>
    /// if (!await db.TryBeginProcessingEventAsync(evt.Id, evt.Type))
    /// {
    ///     return Results.Ok(); // already handled — ack without redoing the side effect
    /// }
    /// // ... domain-state writes on db ...
    /// await db.SaveChangesAsync();
    /// </code>
    /// </summary>
    /// <returns>
    /// <c>true</c> if this is the first time <paramref name="eventId"/> has been seen (caller should
    /// proceed and save); <c>false</c> if it was already processed (caller should skip the side effect
    /// and ack).
    /// </returns>
    /// <remarks>
    /// This check-then-add is not race-proof against two concurrent deliveries of the same event
    /// landing in the same instant on different service instances — both could read "not yet
    /// processed" before either commits. <see cref="ProcessedEvent.Id"/>'s primary key still prevents
    /// double-processing from corrupting state (the second <c>SaveChangesAsync</c> fails), it just
    /// surfaces as an exception rather than a clean <c>false</c> return in that narrow window. Not
    /// handled here — revisit if concurrent redelivery to multiple instances of the same consumer
    /// turns out to matter in practice.
    /// </remarks>
    public static async Task<bool> TryBeginProcessingEventAsync(
        this DbContext db, Guid eventId, string eventType, CancellationToken cancellationToken = default)
    {
        var alreadyProcessed = await db.Set<ProcessedEvent>()
            .AnyAsync(e => e.Id == eventId, cancellationToken);

        if (alreadyProcessed)
        {
            return false;
        }

        db.Set<ProcessedEvent>().Add(new ProcessedEvent
        {
            Id = eventId,
            EventType = eventType,
            ProcessedAt = DateTimeOffset.UtcNow,
        });

        return true;
    }
}
