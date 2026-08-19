# services/site-registry

Site Registry (M00.4 first cut) — site registration and browser ingestion token issuance, per
`docs/architecture/bounded-contexts-and-data-ownership.md`'s Site Registry row and
`docs/adr/0006-public-browser-ingestion-tokens.md`.

## Scope of this cut

`POST /sites` (register a site, issue its first browser token), `GET /sites/{id}`, and token management:
`GET /sites/{id}/tokens`, `POST /sites/{id}/tokens/rotate`, `POST /sites/{id}/tokens/{tokenId}/revoke`.

## Key rotation

A site's browser tokens live in `SiteToken`, not on `Site` — a site can have several over its lifetime.
**Rotating never revokes anything**: `POST /sites/{id}/tokens/rotate` issues a new active token and
leaves every existing one untouched, so an old and new token stay valid simultaneously — this is the
"overlapping keys during safe migration" the M00.4 backlog item asks for, letting a site owner update
their embed before the old token stops working. Revocation is a separate, explicit action
(`POST /sites/{id}/tokens/{tokenId}/revoke`). Both require `Developer`+ in the site's workspace, same bar
as creating a site.

Both actions publish `site.key.rotated.v1` through the outbox — the event catalogue
(`docs/architecture/bounded-contexts-and-data-ownership.md`) defines only one key-related event type, so
the payload carries an `Action` field (`"issued"` or `"revoked"`) to distinguish which transition
happened, rather than inventing an undocumented second event type.

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

`site.created.v1` and `site.key.rotated.v1` publish today with no subscriber — Event Collector (which
would keep a local projection of this data, including which tokens are currently valid) isn't built yet.
Both contracts are real and covered by `tests/integration/Telumera.Tests.Integration/`, they just have
nothing downstream reacting to them yet.
