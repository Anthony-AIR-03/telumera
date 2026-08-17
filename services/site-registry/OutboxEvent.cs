namespace Telumera.Services.SiteRegistry.Api;

/// <summary>
/// Transactional outbox row (docs/adr/0004-transactional-outbox-and-idempotent-consumers.md) — written
/// in the same EF Core transaction as the domain state change it describes, then drained by
/// <see cref="OutboxPublisher"/>. A per-service implementation for now; ADR 0004 calls for this to
/// become a shared library in M00.5 without changing the on-the-wire event contract.
/// </summary>
public sealed class OutboxEvent
{
    /// <summary>Also used as the published CloudEvent's <c>id</c>.</summary>
    public Guid Id { get; init; }

    /// <summary>CloudEvent <c>type</c> — one of the constants in Telumera.EventContracts.EventTypes.</summary>
    public required string EventType { get; init; }

    public required Guid WorkspaceId { get; init; }

    public required Guid SiteId { get; init; }

    public required Guid CorrelationId { get; init; }

    /// <summary>Serialized event-specific payload (the CloudEvent's <c>data.data</c>), stored as jsonb.</summary>
    public required string DataJson { get; init; }

    public DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset? PublishedAt { get; set; }
}
