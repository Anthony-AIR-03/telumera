# Telumera — Bounded Contexts and Data Ownership

> Status: Draft (M00.1)

## Rule

**No shared business database.** Every service below owns its data exclusively. PostgreSQL and ClickHouse
may each host several owning-service-specific databases on one physical server (see
`docs/adr/0005-context-level-data-isolation.md` for the per-engine naming convention), but only the
owning service may access its own database. No other service, worker, or dashboard query may read another
service's database directly — ever, including for "just this one report."

Cross-context information moves only through one of:

1. **Synchronous read APIs** — the owning service exposes an endpoint; callers never bypass it.
2. **Dapr service invocation** — same idea, via the Dapr sidecar instead of a raw HTTP call.
3. **Versioned pub/sub events** — the owning service publishes a fact (see the CloudEvents envelope in
   `docs/adr/0002-dapr-pubsub-abstraction.md`); other services subscribe and keep their own projection.
4. **Materialized summaries explicitly published for other modules** — e.g. a daily rollup another
   service is allowed to read, published on purpose, not discovered by inspecting a schema.

A logical boundary becomes a **separately deployable service** (rather than just a class/module inside
one) when it needs at least one of: independent scaling, independent release timing, a different storage
technology, a different security boundary, failure isolation, a distinct learning objective, or clear
ownership of high-volume processing (see `docs/adr/0001-service-oriented-modular-platform.md` for the
full reasoning).

**A deployment unit is not automatically a separate data owner.** The table below currently has one
service per row and one datastore per service, but that's because every context so far maps 1:1 to a
single deployable — it isn't a rule that each *process* gets its own database. If a context ever splits
into multiple deployables (e.g. a query API and a background worker for the same context), they share
that context's datastore(s); they don't each get their own. Ownership is decided at the bounded-context
level, and a context may contain more than one process/deployment unit accessing the datastore(s) it owns.

## Contexts

| Service | Owns (entities) | Owns (datastore) | Other modules access it via |
|---|---|---|---|
| Web Dashboard | UI state only | none (no business data) | n/a — it is a caller, not a data owner |
| API Gateway / BFF | none | none | n/a — composes responses from other services' APIs |
| Identity & Workspace | Local user profiles, workspaces, memberships, roles, and the binding to each user's external Entra ID identity — **not** passwords or authentication credentials, which Entra ID owns exclusively (see `Rel(identity, entra, "Delegates authentication")` in `docs/architecture/c4-container.md`) | Identity PostgreSQL database | Read APIs (auth/authorization checks); no direct DB access ever |
| Site Registry | Sites, domains, browser site tokens (public ingestion tokens — see `docs/adr/0006-public-browser-ingestion-tokens.md`), settings, enabled-module flags | Site PostgreSQL database | Read API for cache misses/admin/diagnostics; `site.created.v1` / `site.settings.changed.v1` / `site.disabled.v1` / `site.key.rotated.v1` events keep other services' projections current |
| Event Collector | Temporary in-flight buffers, plus a local read-model projection of Site Registry data (token, allowed origins, module enablement, rate limits) kept current via Site Registry's events | none persistent | Publishes accepted events (`analytics.page-view.received.v1`, `performance.sample.received.v1`, `error.occurrence.received.v1`, etc.) — never queried directly by other services; validates inbound events against its local projection rather than calling Site Registry per event |
| Analytics Service | Page views, sessions, visitors, acquisition, custom events | Analytics ClickHouse schema | Analytics Query API (`docs/architecture/c4-container.md`); publishes `analytics.processed.v1` for Conversion/Alerting/AI — see granularity note below |
| Performance Service | Web Vitals samples, timing distributions | Performance ClickHouse schema | Performance Query API; subscribes to `deployment.completed.v1` for release correlation |
| Error Service | Error occurrences, issue lifecycle | Error PostgreSQL + ClickHouse; source maps in the `telumera-errors` object-storage namespace | Error Query API; publishes `error.issue.changed.v1` for Alerting/AI |
| Deployment Service | Releases, deployments | Deployment PostgreSQL database | Publishes `deployment.completed.v1`; consumed by Analytics/Performance/Errors/AI for release correlation |
| Conversion Service | Goal definitions, funnel configuration, conversion rollups | Conversion configuration + rollup storage | Subscribes to `analytics.processed.v1`; exposes its own conversion/funnel query API |
| SEO Scanner | Crawl results, technical SEO findings | SEO metadata + reports in the `telumera-seo` object-storage namespace | Publishes `seo.scan.completed.v1` for Alerting/AI |
| Session Insights | Click/scroll aggregates, optional replay chunks | ClickHouse + replay chunks in the `telumera-sessions` object-storage namespace | Own query API for heatmap/replay dashboards |
| AI Insights | Generated insights, prompt/tool audit trail | AI PostgreSQL database | Never given raw DB access to any other service — only typed, versioned metric-query tools it calls against their public APIs (see `planning/Telumera_Modular_Project_Plan.md` §7, M08) |
| Alerting | Alert rules, alert state, evaluations | Alert PostgreSQL database | Subscribes to `analytics.processed.v1` / `error.issue.changed.v1` / `seo.scan.completed.v1`; publishes `notification.requested.v1` |
| Notification | Delivery log | Delivery log storage | Subscribes to `notification.requested.v1` from any module; owns no other module's data |
| C++ Edge Agent (M11, optional) | Local encrypted buffer only | none persistent server-side | Publishes host telemetry the same way the browser SDK publishes browser events |

## Consequences of this rule

- Every cross-module read must be justified by a real API or event, not "it's faster to just join across
  schemas." If a report genuinely needs data from two modules, it belongs in a service that consumes both
  modules' public APIs/events, not in a database view spanning both schemas.
- New modules added later (M05–M11) must be evaluated against this same table before implementation —
  add a row, decide the datastore, decide the publish/subscribe contract, before writing the service.

## `analytics.processed.v1` granularity

The event catalogue names `analytics.processed.v1` as the thing Conversion, Alerting, and AI Insights all
subscribe to, but doesn't yet fix its granularity — and per-item, per-batch, and per-window publishers
have very different coupling and traffic implications, so this has to be decided before Analytics ships
it, not left implicit:

- **Conversions** needs event-level input to evaluate funnel steps, so it needs a genuinely per-event (or
  small-batch) stream.
- **Alerting** and **AI Insights** should not receive a firehose of every page view — they need
  lower-volume "something meaningful happened" signals (a metric crossed a threshold, a rollup completed,
  an anomaly was detected), not raw event pass-through.

If one event type can't reasonably serve both shapes, split it before implementation — e.g. a high-volume
`analytics.event.processed.v1` for consumers that genuinely need event-level data (Conversions), and
lower-volume signals such as `analytics.metric-window.updated.v1` / `analytics.anomaly.detected.v1` for
Alerting/AI. This doesn't need to be built ahead of need — it needs to be *decided* ahead of M01
implementation so Analytics doesn't ship a contract the other two have to route around later.

## Required vs. optional dependencies

Modules can depend on each other in two different ways, and they fail differently when the upstream
module is disabled or unavailable:

- **Required dependency** — the dependent module's core function is built on the upstream module's data
  and cannot exist without it. Example: `Conversions → Analytics` (funnels are computed from analytics
  events; without Analytics there is nothing to compute a funnel from). If Analytics is disabled,
  Conversions becomes explicitly unavailable/degraded — it does not silently produce wrong numbers, and
  no *unrelated* module is affected.
- **Optional enrichment dependency** — the dependent module works standalone; the upstream module only
  adds extra context. Example: `Performance → Deployment Intelligence` (Web Vitals percentiles work with
  or without release data; Deployment Intelligence just adds "did this regress after a release" framing).
  If Deployment Intelligence is disabled, Performance keeps working and simply can't show that
  correlation.

The rule is therefore: **a module may have explicitly documented required dependencies. Loss of a
required dependency must fail predictably (the dependent module goes explicitly unavailable/degraded)
without corrupting or breaking any unrelated module. Loss of an optional dependency may only remove
enrichment, never core function.** Every module row's "Other modules access it via" column that lists a
`Subscribes to` relationship is declaring a required dependency on that publisher unless documented
otherwise.
