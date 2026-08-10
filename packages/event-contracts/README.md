# packages/event-contracts

Versioned contracts for the CloudEvents-style event envelope (`id`, `type`, `source`, `subject`, `time`,
`tenantId`, `siteId`, `correlationId`, `dataVersion`, `data`) per [ADR 0002](../../docs/adr/0002-dapr-pubsub-abstraction.md)
and plan §8.

- `EventEnvelope<TData>` — the Telumera-specific fields (`tenantId`, `siteId`, `correlationId`,
  `dataVersion`, `data`) that live inside a Dapr CloudEvent's `data` payload. The transport-level
  CloudEvent fields (`id`, `type`, `source`, `subject`, `time`) are owned by Dapr and supplied as
  overrides on publish, not duplicated here.
- `EventTypes` — `type` string constants for the initial event catalogue (plan §8.3).

Per-event `data` schemas (e.g. what `analytics.page-view.received.v1`'s payload actually looks like) are
added incrementally as each module defines its own events, not fabricated ahead of need.
