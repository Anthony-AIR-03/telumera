# services/site-registry

Site Registry (M00.4 first cut) — site registration and browser ingestion token issuance, per
`docs/architecture/bounded-contexts-and-data-ownership.md`'s Site Registry row and
`docs/adr/0006-public-browser-ingestion-tokens.md`.

## Scope of this cut

`POST /sites` (register a site, issue its first browser token), `GET /sites/{id}`,
`GET /workspaces/{id}/sites` (list a workspace's sites — added for `apps/dashboard-web`'s site picker,
same `Viewer`+ check as the other site endpoints), token management (`GET /sites/{id}/tokens`,
`POST /sites/{id}/tokens/rotate`, `POST /sites/{id}/tokens/{tokenId}/revoke`), and module enablement
(`GET /sites/{id}/modules`, `PATCH /sites/{id}/modules/{module}`).

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

## Module enablement settings

Per-site enabled/disabled state for `Analytics`/`Performance`/`Errors` (`services/site-registry/Module.cs`
— only the three modules the M00.4 backlog task names explicitly, i.e. the next modules on the roadmap;
not every module in the full CLAUDE.md roadmap, since none of those are close to being built and this
list only grows when a module actually needs a toggle). `POST /sites` creates one `SiteModuleSetting` row
per module, all `Enabled: true` by default — new tracking installs start with everything on, site owners
opt out of specific modules rather than opting in. `PATCH /sites/{id}/modules/{module}` (`Developer`+,
same bar as creating a site) flips one; a no-op PATCH (value unchanged) doesn't publish a duplicate event.
Publishes `site.settings.changed.v1` through the outbox — the payload's `Module` field serializes as its
name (`"Analytics"`, not a raw int) via the same `JsonStringEnumConverter` pattern the API's own JSON uses,
applied explicitly since outbox payloads are serialized outside the ASP.NET Core request pipeline and
don't pick up `ConfigureHttpJsonOptions` automatically.

## Auth and membership enforcement

Every endpoint except `/health/live`/`/health/ready` requires a valid Entra ID bearer token with the
`access_as_user` scope (`Telumera API` app registration) — see `docs/runbooks/local-environment.md`'s
"Auth" section for how to get one for manual testing.

On top of that, `WorkspaceId` is checked against the caller's actual membership role in
identity-workspace — `MembershipClient` calls identity-workspace's internal membership endpoint through
the Dapr sidecar's service-invocation building block
(`docs/adr/0002-dapr-pubsub-abstraction.md` — "synchronous cross-service calls ... use Dapr service
invocation rather than hardcoded service URLs"), since Site Registry may never read Identity &
Workspace's database directly. `POST /sites` requires `Developer`+, `GET /sites/{id}` requires any
membership (`Viewer`+) in the site's workspace — `403` otherwise. This is what actually closes the gap
flagged during the Entra ID auth cut, where any authenticated caller could act on any workspace.

`GET /sites/{id}` returns the caller's own `Role` alongside the site (`SiteDetailDto`) — previously
computed via `MembershipClient` for the 403 check and then discarded, returning the raw `Site` entity.
Added for `SiteDetailView.vue`'s client-side gating of token rotate/revoke and module toggles (both
already require `Developer`+ server-side; this only lets the UI reflect that before the caller tries
and gets a 403), not a new authorization mechanism.

## Outbox and event publishing

`POST /sites` writes the `Site` row and an `OutboxEvent` row (`Telumera.Outbox.OutboxEvent`, from
`packages/outbox/`) in one EF Core transaction (`docs/adr/0004-transactional-outbox-and-idempotent-consumers.md`
— Site Registry is explicitly named as a context that needs this). `OutboxPublisher<SiteRegistryDbContext>`
(a `BackgroundService`, registered via `builder.Services.AddOutboxPublisher<SiteRegistryDbContext>(topic:
"site-events", source: "telumera.site-registry")`) polls for unpublished rows every ~2s and publishes them
through the `site-registry-dapr` sidecar's HTTP API as a fully-formed CloudEvent
(`Content-Type: application/cloudevents+json`), so Dapr uses Telumera's own `id`/`type`/`source`/`subject`
per ADR 0002 rather than auto-generating them.

**Topic**: every event type this service publishes goes on one Dapr pub/sub topic, `site-events`, with
the CloudEvent's `type` field (e.g. `site.created.v1`) distinguishing event kinds within it — one topic
per *context*, not per event type, since a future subscriber (Event Collector's Site Registry projection)
will want all of this context's events in one place rather than subscribing to several topics. Documented
here per ADR 0004's requirement that a context's topic-naming choice be written down somewhere.

The outbox entity and publisher now live in `packages/outbox/` (M00.5) rather than as a per-service
implementation — ADR 0004 called for this so a future context (Deployment, Errors, Alerting, ...) can
adopt the pattern without reimplementing it. `SiteRegistryDbContext` still owns its own `outbox_events`
table (`modelBuilder.ConfigureOutboxEvent()` in `OnModelCreating`) — per `docs/adr/0005`, there is no
shared outbox table, only a shared mapping/publisher. The published event contract is unchanged; the only
visible effect of the migration was renaming the row's `WorkspaceId` column to `TenantId`
(`Migrations/20260830100134_RenameOutboxEventTenantId.cs`) to match `EventEnvelope<TData>`'s
domain-agnostic field name, since the entity is no longer site-registry-specific code.

**Correlation id** (M00.5, distributed tracing): each `OutboxEvent`'s `CorrelationId` is
`Activity.Current?.TraceId` (`Program.cs`'s `CurrentCorrelationId()`) — the W3C trace id of the HTTP
request that wrote the row, not a disposable random value. This ties the request, the outbox row, and
(once Dapr's own trace propagation reaches a real subscriber) the published event to the same trace —
see `docs/runbooks/local-environment.md`'s "Checking distributed tracing". Required a second migration
(`Migrations/20260830104728_ChangeOutboxEventCorrelationIdToString.cs`) changing the column from `uuid` to
`character varying(64)`, since a trace id is a 32-hex-char string, not Guid-formatted.

## No consumer yet

`site.created.v1`, `site.key.rotated.v1`, and `site.settings.changed.v1` all publish today with no
subscriber — Event Collector (which would keep a local projection of this data, including which tokens
are currently valid and which modules are enabled) isn't built yet. All three contracts are real and
covered by `tests/integration/Telumera.Tests.Integration/`, they just have nothing downstream reacting to
them yet.
