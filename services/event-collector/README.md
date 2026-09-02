# services/event-collector

Event Collector (M01.3) — accepts batched browser events from `packages/browser-sdk` and publishes
them onto Dapr pub/sub, per `docs/architecture/bounded-contexts-and-data-ownership.md`'s Event Collector
row and `docs/adr/0006-public-browser-ingestion-tokens.md`. "Fast validation and acceptance... no
analytics queries or heavy processing" (`docs/architecture/c4-container.md`).

## A different shape of service

Unlike `identity-workspace`/`site-registry`, this service owns **no persistent data at all** — its
bounded-contexts row says exactly that ("Temporary in-flight buffers, plus a local read-model projection
of Site Registry data... | none persistent"), and `docs/adr/0004-transactional-outbox-and-idempotent-consumers.md`
explicitly exempts its high-volume ingestion path from the transactional-outbox pattern the other services
use. So: no PostgreSQL database, no EF Core, no `packages/outbox`/`packages/idempotency` (both require a
`DbContext`). It's also the **one public, anonymous endpoint** in the backend — no
`Microsoft.Identity.Web`, no Entra bearer token — since the browser site token it validates is public by
design (ADR 0006), not a secret to gate behind auth.

## Site projection

A `SiteProjection` (in-memory, `ConcurrentDictionary`-backed) resolves a raw `siteToken` string to its
site's `SiteId`/`WorkspaceId`/`AllowedOrigins`/`EnabledModules` without hitting site-registry on every
request. It's populated once at startup via `GET /internal/tokens` (a new site-registry endpoint added
for this epic — see below) and kept current by subscribing to `site.created.v1` /
`site.settings.changed.v1` / `site.key.rotated.v1` on the shared `site-events` Dapr topic. A token the
projection doesn't recognize falls back to a live `GET /internal/tokens/{token}` call — the only
cache-miss/warm-up case `docs/architecture/c4-container.md` calls out for a direct call instead of the
projection.

Known gap (see `SiteProjection.cs`'s doc comment): site-registry's `POST /sites` doesn't publish a
`site.key.rotated.v1` for a site's *initial* token, only for later rotate/revoke calls — so a brand-new
site's first token is only ever discovered via warm-up or the cache-miss fallback, never a subscription
event. Harmless functionally (every request still resolves correctly, just via the slower path until the
next warm-up); not fixed here since it would mean changing an already-shipped M00.4 endpoint's
event-publishing behavior, out of scope for this epic.

## New site-registry endpoints

Two internal, unauthenticated endpoints (same "trust the Dapr-invoke network boundary" pattern as
identity-workspace's `GetInternalMembership` — always 200, never 404): `GET /internal/tokens/{token}`
(single lookup, the cache-miss path) and `GET /internal/tokens` (bulk list, the warm-up/resync path). No
existing endpoint could do this — every other site-registry endpoint is keyed by an authenticated
caller's already-known site ID, not a raw token an anonymous browser presents.

## `POST /v1/events`

Batched, versioned — body shape `{ "events": [...] }` matches `packages/browser-sdk`'s `EventQueue`
transport exactly. Per event: `id`, `name`, `siteToken`, `sessionId`, `url`, `timestamp` required;
`environment`, `visitorId`, `properties` optional. All events in one batch must share the same
`siteToken` (a batch always comes from one SDK instance in practice; a mixed batch is rejected as
malformed).

Pipeline per request: resolve the site token against the projection (404 if unknown/revoked) → Origin/
Referer check against `AllowedOrigins` (defense in depth per ADR 0006, not the access boundary — a
*missing* Origin/Referer is let through, a *mismatched* one is rejected) → per-event schema validation
(`EventValidation.cs` — bounds name/property-count/property-size the same way for every event kind,
by serialized size rather than restricting value kinds, since the SDK's own built-in page_view/
engagement events send a nested `query` object that its scalar-only `track()` rule doesn't apply to)
→ Analytics-module
check (every event type the SDK sends today belongs to Analytics; Performance/Errors modules cover event
categories no publisher emits yet) → duplicate check (`DuplicateEventCache`, keyed by the event's own
`id` — see the SDK-patch note below) → trusted server metadata (`receivedAt`, `HttpContext.TraceIdentifier`
as the request id, the *server-observed* IP — never the client-submitted one — truncated per definitions
doc §7) → the definitions-doc §3 visitor hash for default-mode events (`VisitorHash.cs`; an SDK-supplied
`visitorId` from persistent mode is passed through untouched) → enqueued for publishing.

**Publish strategy** (confirmed design decision, not the outbox pattern): the endpoint enqueues into a
bounded in-memory `Channel` and returns `202 Accepted` immediately, decoupled from Dapr publish latency.
A `CollectorPublisher` background service drains the channel and publishes each event as a CloudEvent to
Dapr pub/sub on one topic, `collector-events` (matching site-registry's "one topic per publishing
service" convention rather than one topic per event type). A publish failure is logged and the event
dropped — no retry, no dead-letter — the accepted trade-off ADR 0004 explicitly sanctions for this path.
The channel itself drops the *oldest* buffered event under sustained overload rather than ever blocking
ingestion.

**Duplicate protection**: keyed by the event's own client-generated `id` (see below) rather than
`packages/idempotency` — that package needs a `DbContext`, which this "none persistent" service
intentionally doesn't have. A short-TTL `IMemoryCache` comfortably covers the SDK's own retry window.
Single-instance only — not shared across replicas; noted as a future Redis-backed hardening item if this
service is ever scaled horizontally.

**Rate/payload limits**: ASP.NET Core's built-in `RateLimiter` (no new package), partitioned by client
IP rather than site token — the token lives in the JSON body, not a header, so IP-based partitioning
avoids reading the request body twice. Plus a global Kestrel `MaxRequestBodySize` and a max-events-per-batch
cap.

**Enrichment lives downstream (M01.4)**: the published payload carries the raw `TruncatedIp` and
`UserAgent` this service already computed for the visitor hash, not a resolved country/device/browser —
an earlier version of this service did its own GeoIP lookup here (a stubbed `IGeoLookup`/`NoOpGeoLookup`,
since removed), which overlapped with `services/analytics`'s own "Enrich coarse geography"/"Enrich device
and browser categories" subtasks and worked against this service's own "no analytics queries or heavy
processing" design. `services/analytics/README.md` covers the actual enrichment.

## Data-quality tallies (M01.8)

`QualityCounters` accumulates a per-`(siteId, outcome)` count in memory as `/v1/events` runs —
`accepted`, `rejected_validation`, `rejected_unknown_token`, `rejected_origin`,
`rejected_module_disabled`, `duplicate`, `dropped_overload`. `QualityRollupPublisher` (a
`BackgroundService`) drains and publishes them every `Quality:PublishIntervalSeconds` (60s) as
`collector.quality.v1` **delta batches** onto a new `quality-events` topic; `services/analytics`
dedups on the CloudEvent id and sums them into `event_quality_daily`. Best-effort by design: a crash
between accept and the next publish loses that partial interval, and a failed publish restores the
deltas for the next attempt. Unknown-token rejections are recorded under `Guid.Empty` since they can't
be attributed to a site — they surface only in a platform-level view, not a per-site one. `bot` and
`delayed` are **not** counted here — they're derived by `services/analytics` from the durable `events`
table at query time.

## `packages/browser-sdk` patches

- Added `id: string` (client-generated via `crypto.randomUUID()`) to `OutgoingEvent` — this service's
  duplicate-protection subtask needed a per-event id the SDK didn't emit before M01.3.
- (M01.4) Added `title`/`channel`/`referrer`/`utm` to every outgoing event's `properties` — `services/
  analytics`'s "Normalize URLs and page titles"/"Normalize referrer and campaign data" subtasks had
  nothing to normalize without them; the SDK already computed `channel`/`referrer`/`utm` client-side for
  session-restart logic but never attached it to the payload.

## Not in this service

A real GeoIP provider or device/browser categorization (both now `services/analytics`'s job), any real
downstream subscriber of `collector-events` besides `services/analytics`, and multi-instance-safe
(Redis-backed) dedup/projection sharing.
