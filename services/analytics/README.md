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
coarse geography (`IGeoLookup`, moved here from `event-collector` in this same epic — still a
`NoOpGeoLookup` stub, a real provider is deferred to M01.8 exactly as it was there).

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

## Not in this service

A real GeoIP provider, the metric-window/anomaly rollup event types, and multi-instance-safe sharing of
anything (single-instance only, same caveat `event-collector`'s dedup cache already carries).
