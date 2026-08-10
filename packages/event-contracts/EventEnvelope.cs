namespace Telumera.EventContracts;

/// <summary>
/// Telumera-specific fields carried inside a Dapr CloudEvent's <c>data</c> payload, per the explicit
/// envelope-ownership rule in <see href="../../docs/adr/0002-dapr-pubsub-abstraction.md">ADR 0002</see>:
/// Dapr owns the transport-level CloudEvent (<c>id</c>, <c>type</c>, <c>source</c>, <c>subject</c>,
/// <c>time</c>), so those are supplied as overrides on publish rather than duplicated here.
/// </summary>
/// <param name="TenantId">The workspace that owns this event.</param>
/// <param name="SiteId">The site the event relates to.</param>
/// <param name="CorrelationId">Correlation id for tracing the event back to the request or session that caused it.</param>
/// <param name="DataVersion">
/// Version of <typeparamref name="TData"/>'s shape. A breaking payload change gets a new event type
/// (e.g. <c>.v2</c>) and, typically, a new <typeparamref name="TData"/> type — a service may support
/// multiple input versions during a migration (ADR 0002 §8.2).
/// </param>
/// <param name="Data">The event-specific payload.</param>
public sealed record EventEnvelope<TData>(
    string TenantId,
    string SiteId,
    string CorrelationId,
    int DataVersion,
    TData Data);
