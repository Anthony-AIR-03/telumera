# packages/idempotency

Shared implementation of the "persist processed event IDs" idempotent-consumer strategy from
[ADR 0004](../../docs/adr/0004-transactional-outbox-and-idempotent-consumers.md), satisfying
[ADR 0002](../../docs/adr/0002-dapr-pubsub-abstraction.md)'s "every consumer must be idempotent" rule
(Dapr pub/sub is at-least-once — redelivery of an already-handled event is expected, not an error).

- `ProcessedEvent` — a row marking one CloudEvent `id` as handled. Written in the same EF Core
  transaction as whatever domain-state change the event triggered.
- `IdempotencyModelBuilderExtensions.ConfigureProcessedEvent()` — call from a consuming service's own
  `OnModelCreating`. Each service owns its own `processed_events` table
  ([ADR 0005](../../docs/adr/0005-context-level-data-isolation.md)); this only shares the mapping —
  same split as [`packages/outbox`](../outbox/)'s `ConfigureOutboxEvent`.
- `IdempotencyGuardExtensions.TryBeginProcessingEventAsync(eventId, eventType)` — an extension on
  `DbContext`. Returns `false` if already processed (skip the side effect, ack); on `true`, stages the
  marker row and expects the caller to make its domain-state writes on the same `DbContext` and call
  `SaveChangesAsync()` once, so the marker commits atomically with the effect it guards.

**Not every consumer needs this table** — ADR 0004 also allows "safe natural keys" (e.g. an upsert keyed
by the event's own natural key is already idempotent without a separate marker). Reach for this package
when the handler's side effect isn't naturally idempotent on its own.

No real subscriber exists in the codebase yet (`services/site-registry/README.md`'s "No consumer yet" —
Event Collector, the natural first one, is blocked on M01's tracking SDK), so this is validated by
`packages/idempotency.Tests/` rather than a real end-to-end consumer. Adopt it once a service actually
subscribes to a Dapr topic.
