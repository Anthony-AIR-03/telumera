namespace Telumera.Idempotency;

/// <summary>
/// Marks a CloudEvent id as handled, per the "persist processed event IDs" strategy in
/// docs/adr/0004-transactional-outbox-and-idempotent-consumers.md and the "every consumer must be
/// idempotent" rule in docs/adr/0002-dapr-pubsub-abstraction.md (Dapr pub/sub is at-least-once, so
/// redelivery of an already-handled event is expected, not an error). Written in the same EF Core
/// transaction as the domain-state change the event triggered — see
/// <see cref="IdempotencyGuardExtensions.TryBeginProcessingEventAsync"/>.
/// </summary>
/// <remarks>
/// Not every consumer needs this table — a handler whose domain write is already naturally idempotent
/// (e.g. an upsert keyed by the event's own natural key) can skip it entirely, per ADR 0004's "or use
/// safe natural keys" alternative.
/// </remarks>
public sealed class ProcessedEvent
{
    /// <summary>The CloudEvent <c>id</c> that was handled.</summary>
    public required Guid Id { get; init; }

    /// <summary>CloudEvent <c>type</c> — kept for observability/debugging, not used for dedup itself.</summary>
    public required string EventType { get; init; }

    public DateTimeOffset ProcessedAt { get; init; }
}
