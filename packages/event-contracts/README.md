# packages/event-contracts

Versioned contracts for the CloudEvents-style event envelope (`id`, `type`, `source`, `subject`, `time`,
`tenantId`, `siteId`, `correlationId`, `dataVersion`, `data`) and per-event `data` payloads, per
[ADR 0002](../../docs/adr/0002-dapr-pubsub-abstraction.md) and plan §8.

Populated in the M00.2 "Create shared event-contract package" task — not yet created. Per-event `data`
schemas are added incrementally as each module defines its own events, not fabricated ahead of need.
