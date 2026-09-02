# services/analytics

Analytics Service (M01.4) — the first real subscriber of `services/event-collector`'s `collector-events`
Dapr pub/sub topic. Normalizes, enriches, dedupes, and persists accepted events to ClickHouse, then
publishes `analytics.processed.v1` for future modules (Conversion, Alerting, AI — none built yet) to
subscribe to. Per `docs/architecture/bounded-contexts-and-data-ownership.md`'s Analytics Service row.

## Two databases, two very different jobs

Unlike `event-collector` (no persistent data at all), this service owns **two** databases with
deliberately non-overlapping purposes:

- **ClickHouse** (`telumera_analytics`, pre-provisioned since M00.3) — the actual normalized event data,
  written via `ClickHouseWriter.cs`'s plain HTTP interface calls (`POST http://clickhouse:8123/?query=...`)
  rather than a NuGet client library — `ClickHouse.Client`'s latest stable has no confirmed net10.0
  target, an unnecessary compatibility gamble when this codebase already calls every other infra HTTP
  API directly (Dapr's sidecar, RabbitMQ's management API) instead of through a client SDK.
- **PostgreSQL** (`telumera_analytics_control`, new in this epic) — holds *only*
  `packages/idempotency`'s `processed_events` table (the consumer-dedup markers). ADR 0003 names "a
  service using both PostgreSQL and ClickHouse" as an expected pattern (citing Error Service as the
  precedent this epic is the first to actually build).

## Why this is a web service, not a worker

`templates/worker-service/` (`Microsoft.NET.Sdk.Worker`, no ASP.NET Core) looks like the natural
scaffold for a "consumer," but Dapr pub/sub delivery is HTTP-push into the app — confirmed live in
M01.3's own container logs (`event-collector-dapr`'s sidecar: "waiting on port 8080... This will block
until the app is listening on that port"). That requires Kestrel regardless of whether the service is
conceptually a worker, so this is scaffolded like `event-collector` (`Microsoft.NET.Sdk.Web`,
`Dapr.AspNetCore`'s `.WithTopic()` + `app.MapSubscribeHandler()`), not from the worker template. The
worker template's own `IIdempotencyStore`/`InMemoryIdempotencyStore` is an unrelated, explicitly
placeholder scaffold per its doc comments ("Replace with a persistent store... before this worker
consumes real events") — not what `packages/idempotency` provides; this service uses the real package.

## The dedup-vs-ClickHouse-write ordering trade-off

`packages/idempotency`'s documented pattern assumes the idempotency marker and the domain write commit
in the *same* `SaveChangesAsync` call — that only works when both live in the same relational database.
Here they don't: the marker lives in Postgres, the actual data write goes to ClickHouse, and the two
can't share a transaction. `EventProcessor.cs` commits the Postgres marker **first**, then writes
ClickHouse. A crash between the two either:
- loses the event (marker committed, ClickHouse write never happens — an eventual redelivery from Dapr
  would see the marker and skip it, permanently), or
- (the alternative ordering, not chosen) double-counts it (ClickHouse written, marker never commits — a
  redelivery would reprocess and write a second row).

Marker-first was chosen because the subtask's literal goal is "prevent retry duplicates from affecting
metrics" — the risk it names is **overcounting**, not the rarer crash-window undercounting. Same
accepted-trade-off framing ADR 0004 already uses for the Collector's own publish path.

## Enrichment pipeline (`EventProcessor.cs`)

Per event: URL normalization (`UrlNormalizer.cs`, re-derives `path`/`query_string` from the trusted `Url`
field rather than trusting client-echoed `properties.path` — defense in depth) → referrer/campaign
normalization (`CampaignNormalizer.cs`, validates the `channel`/UTM fields the SDK already classified
client-side rather than re-implementing classification) → device/browser/OS categorization
(`UserAgentClassifier.cs`, hand-rolled substring heuristics into bounded categories — **not** a
UA-parsing library, which would produce more entropy than the privacy threat model's "bounded
categories... not full high-entropy fingerprint strings" row wants) → bot detection (`BotDetector.cs`,
marks `is_bot` rather than dropping — "marked, not silently deleted, so data quality is inspectable") →
coarse geography (`IGeoLookup` — see "Geography enrichment (M01.8)" below).

## Geography enrichment (M01.8)

`GeoLookup.cs` has two implementations behind `IGeoLookup`:

- `MmdbGeoLookup` — opens a local MaxMind-format `.mmdb` **country** database (`MaxMind.GeoIP2`,
  `FileAccessMode.Memory`) and returns the ISO country code for the Collector's already-truncated IP.
  Registered when `GeoIp:DatabasePath` (default `/geoip/GeoLite2-Country.mmdb`) exists.
- `NoOpGeoLookup` — returned when no database file is present (fresh clone, CI, anyone who hasn't run
  `infrastructure/compose/scripts/refresh-geoip.sh`). `events.country` stays empty; the pipeline is
  otherwise unaffected. A one-line startup warning says which mode is active.

The `.mmdb` file is **never committed** (`.gitignore`: `*.mmdb`) — MaxMind's GeoLite2 licence forbids
redistribution. `refresh-geoip.sh` fetches it from MaxMind (needs `MAXMIND_LICENSE_KEY`) or DB-IP Lite
(no account). Country-only by design (`docs/analytics/definitions-and-privacy-model.md` §7); no
historical backfill is possible because the source IP is never stored.

## `analytics.processed.v1` scope

Publishes once per processed event (direct Dapr publish call, same lightweight shape
`event-collector`'s `CollectorPublisher` uses — no outbox table here either). The metric-window/anomaly
rollup split `docs/architecture/bounded-contexts-and-data-ownership.md` flags as an open question
("possibly splitting into `analytics.event.processed.v1` + `analytics.metric-window.updated.v1`/
`analytics.anomaly.detected.v1`") is deferred to whichever epic actually builds Alerting/AI — building
an aggregation-window subsystem for consumers that don't exist yet isn't implied by this epic's own
"Publish analytics.processed event" subtask wording.

## `packages/browser-sdk` / `services/event-collector` patches (this epic)

- SDK: added `title`/`channel`/`referrer`/`utm` to every outgoing event's `properties` — nothing fed
  "Normalize URLs and page titles"/"Normalize referrer and campaign data" before this (the SDK already
  computed `channel`/`referrer`/`utm` client-side for session-restart logic, but never attached it to
  the payload).
- Collector: removed its own `GeoLookup.cs`/`Country` field (an earlier design mistake — it duplicated
  this epic's own "Enrich coarse geography" subtask and worked against the Collector's "no analytics
  queries or heavy processing" philosophy); added `TruncatedIp`/`UserAgent`/`ClientTimestamp` passthrough
  instead, which this service's enrichment pipeline actually consumes.

## `tools/dead-letter-recovery`

A separate console tool (not part of this service) for the "Create dead-letter recovery command"
subtask — see its own top-of-file comment.

## Sessions and daily rollups (M01.5)

`sessions` and five `daily_*_rollup` ClickHouse tables sit downstream of `events`, populated by
`AnalyticsAggregationService` (a `BackgroundService`, same `PeriodicTimer` shape as
`event-collector`'s `SiteProjectionSyncService`) rather than a streaming materialized view — a session's
30-minute inactivity boundary can't be resolved by an insert-triggered view, since it can't know whether a
later event will still land inside the same session. Every table is `ReplacingMergeTree(updated_at)`: an
aggregation pass re-inserts the full row for anything it recomputes, and `FINAL` (or the aggregation
queries' own re-read of the same table) sees the latest version even before a background merge physically
collapses the older one away.

Each tick (`Aggregation:IntervalSeconds`, 60s by default):
1. Reads a watermark from `aggregation_checkpoints` (`telumera_analytics_control`, `AggregationCheckpoint.cs`
   — same "small Postgres control table" pattern as `packages/idempotency`'s `processed_events`).
2. `ClickHouseWriter.RecomputeSessionsAsync` recomputes every session with at least one event whose
   `received_at` is newer than the watermark, straight from `events` via `argMin`/`argMax`/`sumIf`
   combinators (definitions doc §2 entry-context attribution, §4's engaged-session formula, §5's
   active-time via the SDK's `engagement` custom event) — no rows pulled into .NET.
3. `ClickHouseWriter.RecomputeDailyRollupsAsync` recomputes the five rollups for exactly the (site, date)
   buckets those sessions touched.
4. The watermark advances to `now - Aggregation:WatermarkSafetyBufferSeconds` (300s default), not straight
   to `now` — an event can land in ClickHouse noticeably after its own `received_at` (Dapr
   redelivery/retry per ADR 0004, or ordinary publish-to-process lag), so the watermark trails "now" by a
   fixed cushion rather than risking skipping a straggler on a later tick.

**This watermark is also the entire late-event mechanism** — a late event landing today for a session
"closed" days ago makes that `session_id` dirty on the very next tick regardless of the session's own age,
and rollups cascade automatically since they're scoped to whatever dates that tick's sessions touched. No
separate "recalculate window" logic exists or is needed. The very first run ever (watermark unset) rescans
every event that already existed, which doubles as the initial backfill for data written before M01.5
shipped.

The page rollup (`daily_page_rollup`) combines per-event view counts (`events`) with per-session
entry/exit counts (`sessions`) via `UNION ALL` + `max()` per metric rather than a `FULL OUTER JOIN` — each
branch only ever populates its own metric column (`0` elsewhere for that branch), so taking `max()` across
branches for the same `(site_id, date, path)` key is a correct, simpler combine.

Every rollup row carries `is_below_privacy_floor` (`visitors_count < 5`, flagged not suppressed — see
`docs/analytics/definitions-and-privacy-model.md` §8's addendum). `daily_geography_rollup.country` is
populated once an MMDB database is installed ("Geography enrichment (M01.8)" above), empty before that;
no `viewport_category` column exists anywhere — no SDK signal produces one.

Raw `events` gets a 90-day TTL (`ALTER TABLE events MODIFY TTL toDateTime(received_at) + INTERVAL 90 DAY
DELETE`, applied idempotently in `EnsureSchemaAsync` alongside its `CREATE TABLE IF NOT EXISTS` — TTL
needs a `DateTime`/`Date` expression, not `DateTime64` directly, which ClickHouse 24.8 rejects outright).
ClickHouse enforces this via background merges (non-blocking for reads/writes) and drops whole monthly
partitions once fully expired — definitions doc §8's "raw event retention: 90 days" default, satisfying
the "delete expired data without blocking normal queries" subtask with no separate cleanup job. `sessions`
and the five rollups get **no** TTL — §8 already says aggregate/rollup retention is indefinite by default.
Per-site configurable retention is not built (no Site Registry field/sync exists for it) — deferred, same
treatment as GeoIP.

## `tools/reconciliation-report`

M01.5's "Implement data reconciliation job" subtask, following `tools/dead-letter-recovery`'s own shape
(bare console tool, no compose service, run on demand — `dotnet run --project tools/reconciliation-report
-- [--date yyyy-MM-dd]`). Compares two *exact* per-day counts — Postgres `processed_events` ("processed",
the idempotency marker `EventProcessor` commits before its ClickHouse write) against ClickHouse `events`
("stored") — surfacing the marker-before-write crash-window trade-off documented above with an actual
detection mechanism for the first time, rather than leaving it a documented-but-unverified risk. A third
number (RabbitMQ's `collector-events`/`analytics-events` exchange `publish_in` counters, "accepted") is
shown for context only, explicitly labeled cumulative-since-broker-start rather than treated as a precise
per-day input — RabbitMQ has no historical per-day counter, and showing false per-day precision there
would violate the Accuracy Principle this whole module is built around.

## Analytics Query API (M01.6)

Seven `GET /sites/{siteId:guid}/analytics/...` endpoints (`AnalyticsQueryEndpoints.cs`) — overview,
time-series, pages, acquisition, technology, geography, custom-events — reading the M01.5 tables above.
This is the first authenticated surface on this service; `/subscriptions/collector-events` stays
unauthenticated, unaffected (see `Program.cs`'s comment on why both coexist safely). Four design
questions `docs/architecture/c4-container.md` left open for whichever epic actually built this API are
resolved here:

- **siteId → workspaceId resolution.** Analytics has no local site→workspace projection — `event-collector`'s
  `SiteProjection` exists specifically for its *hot* ingestion path, not the right precedent for a
  low-QPS, cache-backed query API. Chose a new unauthenticated `GET /internal/sites/{id}` on
  `services/site-registry` (mirroring its existing `/internal/tokens/{token}` exactly) over forwarding the
  caller's own bearer token to site-registry's authenticated `GET /sites/{id}` — the latter would have
  been genuinely new plumbing (no existing service forwards a caller's own token onward); the former reuses
  the already-established two-internal-call pattern (`SiteLookupClient.cs` → `MembershipClient.cs`,
  duplicated from `services/site-registry/MembershipClient.cs`/`Role.cs`) verbatim.
- **Pagination/sort/filter shape.** No endpoint in this repo paginated before the pages endpoint — settled
  on a plain `{ items, total, page, pageSize }` envelope (`PagedResult<T>`), `sort`/`sortDir` validated
  against a fixed C# allowlist (never interpolated raw), `search`/`pathPrefix` as ClickHouse-parameterized
  substring/prefix filters. `total` comes from `count() OVER()` in the same query (verified live) rather
  than a second round trip.
- **Cache key/TTL shape.** `QueryCache.cs` wraps `IDistributedCache` (Redis, `AddStackExchangeRedisCache`)
  — the first real Redis consumer in the repo (`infrastructure/compose/docker-compose.yml`'s `redis`
  container has run since M00.3 with nothing using it). Key = endpoint + siteId + every query param that
  affects the result; TTL `Cache:TtlSeconds` (60s default).
- **Query protection.** `ClickHouseQueryClient.cs` is a *second*, separate `HttpClient`/class from
  `ClickHouseWriter.cs` — a short `HttpClient.Timeout` (`ClickHouse:QueryTimeoutSeconds`, 10s default)
  alone only stops this service from waiting; every query also carries a server-side
  `SETTINGS max_execution_time` a couple of seconds below that, so ClickHouse itself aborts a runaway
  query rather than just being abandoned by an impatient client.

**Injection safety**: every value derived from a query-string parameter that isn't validated against a
fixed C# allowlist (sort column, sort direction, time-series interval) goes through ClickHouse's native
`{name:Type}` parameter binding (`ClickHouseQueryClient.QueryAsync`'s `parameters` argument), not string
interpolation — verified live against a real ClickHouse instance that a value like `x' OR '1'='1'` is
treated as inert literal data, never re-parsed as SQL.

**Gateway routing**: `/sites/{id}/analytics/**` needed a carve-out in `gateway/GatewayForwarder.cs`
(mirroring its existing `workspaces/.../sites` one) — its `RouteToAppId` table already maps the `sites`
first-path-segment to `site-registry`, which would otherwise misroute every one of these seven endpoints.

**Comparison periods**: `?compare=true` on the overview endpoint returns a `previous` field computed for
the immediately-preceding period of equal length (`DateRangeParsing.GetPreviousPeriod`) — the epic's
"Implement comparison periods" subtask's primary, reusable home; the helper isn't wired into the other six
endpoints yet (not asked for, easy to extend later).

**Interpretation calls worth flagging** (the CSV backlog's wording is compressed): the custom-events
endpoint's "allowed properties" is read as *observed* property keys per event name
(`groupUniqArray(arrayJoin(JSONExtractKeys(properties_json)))`, verified live), not a platform-enforced
allowlist — no such enforcement mechanism exists anywhere in the pipeline. The technology endpoint has no
screen/viewport dimension (CSV: "...and viewport categories") — no SDK signal for it exists, same
documented gap M01.5 already carried forward, not stubbed with fake data here either.

## Live visitor projection + SignalR hub (M01.8)

`LiveVisitorProjection` keeps a per-site rolling 5-minute (`Live:WindowSeconds`) window of active
visitors in Redis — a sorted set scored by last-seen time plus a `visitorId → last path` hash, both
with a self-expiring TTL so idle sites need no janitor. It is written best-effort from
`EventProcessor` **after** the ClickHouse insert, non-bot events only. Per
`docs/analytics/definitions-and-privacy-model.md` §9 it is explicitly **non-authoritative**: never
persisted, never reconciled, never feeds a rollup.

`LiveHub` (`/hubs/live`, `[Authorize("ApiScope")]`) is the first surface the dashboard talks to
**not through the gateway** — a Dapr service-invocation forwarder can't carry a WebSocket, so the
browser connects to this service's origin directly (`hubs.telumera.nl` in prod, `:5104` in dev).
Consequences: a `LiveHub` CORS policy with `AllowCredentials` (this service had no CORS before), and a
`JwtBearerEvents.OnMessageReceived` hook that accepts the bearer from the `access_token` query param
for `/hubs` paths (a browser WebSocket can't set an `Authorization` header). `Subscribe(siteId)` runs
the same `QueryAuthorization.AuthorizeSiteReadAsync` membership check the REST endpoints use.
`LiveBroadcastService` (a `BackgroundService` + `PeriodicTimer`, same shape as
`AnalyticsAggregationService`) pushes a `LiveSnapshot` to each watched site's group every
`Live:BroadcastIntervalSeconds` (5s) — recompute-and-push on a timer, never a per-event fan-out.

`LiveSubscriptions` (which sites have a live watcher) is per-instance — the Redis projection data
itself is multi-instance-safe, but a scale-out would want the broadcast loop to coordinate or the
hub backplane to be Redis. Single-instance for now, same caveat as the aggregation watermark below.

## Not in this service

The metric-window/anomaly rollup event types, `viewport_category` capture, per-site
configurable retention, multi-instance-safe sharing of anything (single-instance only, same caveat
`event-collector`'s dedup cache already carries — now also true of `AnalyticsAggregationService`'s
watermark and `LiveSubscriptions`), and comparison periods on endpoints other than overview.
