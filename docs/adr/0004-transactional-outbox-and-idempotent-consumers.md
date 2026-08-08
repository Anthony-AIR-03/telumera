# ADR 0004: Transactional Outbox and Idempotent Consumers

- **Status:** Accepted
- **Date:** 2026-08-07
- **Related:** `docs/adr/0002-dapr-pubsub-abstraction.md`, `docs/architecture/bounded-contexts-and-data-ownership.md`

## Context

ADR 0002 requires every consumer to be idempotent, which solves duplicate delivery. It does not solve the
producer-side dual-write problem: a service that both updates its own transactional state and publishes
an integration event is making two separate writes (one to PostgreSQL, one to Dapr pub/sub) that can fail
independently.

Example failure:

```text
1. Deployment record committed to PostgreSQL
2. Process crashes
3. deployment.completed.v1 was never published
```

PostgreSQL now says the deployment happened; every other context that would have reacted to it
(Analytics, Performance, Errors, AI Insights, per `docs/architecture/bounded-contexts-and-data-ownership.md`)
never finds out. The reverse ordering has the mirror-image failure: the event publishes, then the local
transaction fails, and now downstream contexts react to something that never actually committed.

This applies to any context that writes transactional state and publishes an integration event about it
— Site Registry, Deployment, Errors, Alerting, Notification, and Conversions (depending on its
implementation). It does not apply to append-only, high-volume analytics ingestion (Collector →
Analytics/Performance/Errors event streams), which has different reliability mechanics and should not be
forced through a PostgreSQL outbox just for consistency's sake.

## Decision

Contexts that update transactional PostgreSQL state and publish an integration event about that state
change use the **transactional outbox pattern**:

```text
Single PostgreSQL transaction
│
├── UPDATE/INSERT the domain state (e.g. deployment record)
└── INSERT outbox_event  (event type, payload, created_at, published_at = null)
```

A separate publisher process (in-process background worker or a dedicated small worker, per-service)
polls or is notified of new outbox rows, publishes them through Dapr, and marks `published_at` on
success. If the publish fails or the process crashes, the row is still there and gets retried — the event
is never lost because it was never dependent on the publish call succeeding in the same transaction as
the state write.

On the consumer side, this combines with the existing idempotent-consumer requirement
(`docs/adr/0002-dapr-pubsub-abstraction.md`): the outbox can produce at-least-once delivery just like any
other at-least-once source, so consumers still dedupe by event `id`.

**Not required** for append-only analytics ingestion — the Collector's accept-and-publish path and
similar high-volume, append-mostly streams may use different reliability mechanics (e.g. accepting
some risk of an unpublished sample vs. the throughput cost of an outbox table per event) as long as that
choice is documented in the owning service's own docs.

## Consequences

- **Positive:** closes the dual-write gap for every context whose correctness depends on "the event
  matches what's in the database" — which is most of the transactional (non-analytics-ingestion)
  contexts.
- **Positive:** reuses the same idempotency requirement already established in ADR 0002 — consumers don't
  need new logic, only producers change.
- **Negative:** adds an outbox table and a publisher process/loop to every context that adopts it — real
  implementation cost, not just a config change. Should be built once as a shared pattern/library (M00.5)
  rather than reimplemented per service.
- **Negative:** introduces publish latency (bounded by the publisher's poll/notify interval) between the
  state commit and the event actually reaching subscribers, which downstream consumers must tolerate.
