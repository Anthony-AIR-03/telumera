# services/site-registry

Site Registry (M00.4 first cut) — site registration and browser ingestion token issuance, per
`docs/architecture/bounded-contexts-and-data-ownership.md`'s Site Registry row and
`docs/adr/0006-public-browser-ingestion-tokens.md`.

## Scope of this cut

`POST /sites` (register a site, issue its browser token) and `GET /sites/{id}`.

## Auth and membership enforcement

Both endpoints require a valid Entra ID bearer token with the `access_as_user` scope (`Telumera API` app
registration) — see `docs/runbooks/local-environment.md`'s "Auth" section for how to get one for manual
testing. `/health/live` and `/health/ready` stay open.

On top of that, `WorkspaceId` is checked against the caller's actual membership role in
identity-workspace — `MembershipClient` calls identity-workspace's internal membership endpoint through
the Dapr sidecar's service-invocation building block
(`docs/adr/0002-dapr-pubsub-abstraction.md` — "synchronous cross-service calls ... use Dapr service
invocation rather than hardcoded service URLs"), since Site Registry may never read Identity &
Workspace's database directly. `POST /sites` requires `Developer`+, `GET /sites/{id}` requires any
membership (`Viewer`+) in the site's workspace — `403` otherwise. This is what actually closes the gap
flagged during the Entra ID auth cut, where any authenticated caller could act on any workspace.

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
