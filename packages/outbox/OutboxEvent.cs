namespace Telumera.Outbox;

/// <summary>
/// Transactional outbox row (docs/adr/0004-transactional-outbox-and-idempotent-consumers.md), written
/// in the same EF Core transaction as the domain state change it describes, then drained by
/// <see cref="OutboxPublisher{TDbContext}"/>. Field names match
/// <see cref="Telumera.EventContracts.EventEnvelope{TData}"/>'s (domain-agnostic — a service's own
/// "workspace"/"tenant" concept is mapped in when the row is created) rather than any one service's
/// domain vocabulary, since this entity is shared across contexts.
/// </summary>
public sealed class OutboxEvent
{
    /// <summary>Also used as the published CloudEvent's <c>id</c>.</summary>
    public Guid Id { get; init; }

    /// <summary>CloudEvent <c>type</c> — one of the constants in Telumera.EventContracts.EventTypes.</summary>
    public required string EventType { get; init; }

    public required Guid TenantId { get; init; }

    public required Guid SiteId { get; init; }

    public required Guid CorrelationId { get; init; }

    /// <summary>Serialized event-specific payload (the CloudEvent's <c>data.data</c>), stored as jsonb.</summary>
    public required string DataJson { get; init; }

    public DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset? PublishedAt { get; set; }
}
