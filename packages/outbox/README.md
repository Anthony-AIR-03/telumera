# packages/outbox

Shared implementation of the transactional outbox pattern per
[ADR 0004](../../docs/adr/0004-transactional-outbox-and-idempotent-consumers.md) — built once here so a
context that needs it (Site Registry today; Deployment, Errors, Alerting, Notification, Conversions per
the ADR) doesn't reimplement the entity, EF Core mapping, and publisher loop from scratch.

- `OutboxEvent` — the row written in the same EF Core transaction as the domain state change it
  describes. Field names (`TenantId`, `SiteId`, `CorrelationId`) match
  [`EventEnvelope<TData>`](../event-contracts/EventEnvelope.cs)'s domain-agnostic vocabulary rather than
  any one service's — a consuming service maps its own concept (e.g. `WorkspaceId`) onto `TenantId` when
  it creates the row.
- `OutboxModelBuilderExtensions.ConfigureOutboxEvent()` — call from a service's own `OnModelCreating`.
  Each service still owns its own `outbox_events` table in its own database
  ([ADR 0005](../../docs/adr/0005-context-level-data-isolation.md)); this only shares the mapping.
- `OutboxPublisher<TDbContext>` — a `BackgroundService` that polls for unpublished rows and publishes them
  through the local Dapr sidecar as a fully-formed CloudEvent (`id`/`type`/`source`/`subject` explicitly
  set, per ADR 0002's envelope-ownership rule). Register via
  `services.AddOutboxPublisher<TDbContext>(topic: "...", source: "telumera.<service>")`.

See `services/site-registry/README.md` for a real consumer.
