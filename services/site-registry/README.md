# services/site-registry

Site Registry (M00.4 first cut) — site registration and browser ingestion token issuance, per
`docs/architecture/bounded-contexts-and-data-ownership.md`'s Site Registry row and
`docs/adr/0006-public-browser-ingestion-tokens.md`.

## Scope of this cut

`POST /sites` (register a site, issue its browser token) and `GET /sites/{id}`. `WorkspaceId` is stored
as a plain value, not validated against identity-workspace's API — real validation arrives once an
authenticated caller puts a trusted workspace context on the request (Entra ID integration is deferred to
a later M00.4 task, so no auth is enforced on these endpoints yet either).

## Outbox and event publishing

`POST /sites` writes the `Site` row and an `OutboxEvent` row in one EF Core transaction
(`docs/adr/0004-transactional-outbox-and-idempotent-consumers.md` — Site Registry is explicitly named as
a context that needs this). `OutboxPublisher` (a `BackgroundService`) polls for unpublished rows every
~2s and publishes them through the `site-registry-dapr` sidecar's HTTP API as a fully-formed CloudEvent
(`Content-Type: application/cloudevents+json`), so Dapr uses Telumera's own `id`/`type`/`source`/`subject`
per ADR 0002 rather than auto-generating them.

**Topic**: every event type this service publishes goes on one Dapr pub/sub topic, `site-events`, with
the CloudEvent's `type` field (e.g. `site.created.v1`) distinguishing event kinds within it — one topic
per *context*, not per event type, since a future subscriber (Event Collector's Site Registry projection)
will want all of this context's events in one place rather than subscribing to several topics. Documented
here per ADR 0004's requirement that a context's topic-naming choice be written down somewhere.

This is a per-service implementation of the outbox pattern; ADR 0004 calls for a shared library in M00.5
that this can migrate to without changing the published event contract.

## No consumer yet

`site.created.v1` publishes today with no subscriber — Event Collector (which would keep a local
projection of this data) isn't built yet. The contract is real and covered by
`tests/integration/Telumera.Tests.Integration/`, it just has nothing downstream reacting to it yet.
