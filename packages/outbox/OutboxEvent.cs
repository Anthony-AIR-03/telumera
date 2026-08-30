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

    /// <summary>
    /// Ties this event back to the request or session that caused it — the W3C trace id
    /// (<see cref="System.Diagnostics.Activity.Current"/>'s <c>TraceId</c>) of the HTTP request that
    /// wrote this row when one was active, so a single id correlates the request, the outbox row, and
    /// (once distributed tracing is configured, see infrastructure/observability/) the published
    /// CloudEvent to the same trace. A plain string, not a <see cref="Guid"/> — trace ids are 32 hex
    /// chars, not Guid-formatted.
    /// </summary>
    public required string CorrelationId { get; init; }

    /// <summary>Serialized event-specific payload (the CloudEvent's <c>data.data</c>), stored as jsonb.</summary>
    public required string DataJson { get; init; }

    public DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset? PublishedAt { get; set; }
}
