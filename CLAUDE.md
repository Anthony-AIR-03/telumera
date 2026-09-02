# Telumera

Codename used inside the planning docs: **InsightFlow**. "Telumera" is the actual project name — when
the planning docs (CSV/MD/XLSX) say "InsightFlow" or "insightflow", read that as this project.

## What it is

A **self-hosted, modular analytics and developer-intelligence platform** — privacy-first alternative to
things like PostHog/Plausible/Sentry combined, built module-by-module as a learning + portfolio project.

- **Primary frontend:** Vue 3 + TypeScript
- **Primary backend:** ASP.NET Core + C#
- **Optional native component:** C++ edge agent (M11, host/endpoint telemetry)
- **Initial customer/deployment target:** `anthony-air.nl`
- **Long-term model:** multi-site, multi-tenant, independently-enabled product modules

Core principle: **auditable analytics, not false precision** — every metric must be traceable to raw
vs. processed event counts, with documented definitions, bot/duplicate classification, sampling state,
and privacy/consent config exposed rather than hidden.

## Architecture (see `planning/Telumera_Modular_Project_Plan.md` §3 for full detail)

- Service-oriented modular monolith-of-services in **one monorepo**, not microservices-for-their-own-sake.
- Each module owns its domain rules, owns its own DB/schema, exposes an API or publishes events, and
  **never reads another module's tables directly**. No shared business database.
- Cross-module communication: synchronous read APIs, Dapr service invocation, versioned pub/sub events
  (CloudEvents-style envelope, see §8.1), or explicitly published materialized summaries.
- A logical boundary only becomes a separate deployable service if it needs independent scaling, release
  timing, storage tech, security boundary, failure isolation, or has a distinct learning objective.
- Portability target: self-hosted (Docker Compose, RabbitMQ, PostgreSQL, ClickHouse, MinIO) and Azure
  (Container Apps, Service Bus, Azure Database for PostgreSQL, Blob Storage, Key Vault) without app rewrites.
- Storage split: **PostgreSQL** for transactional/config data (users, sites, goals, issues, deployments,
  alerts, audit), **ClickHouse** for high-volume event/time-series data, **object storage** for source
  maps/reports/exports, **Redis** only for ephemeral/derived state (never source of truth).

Repository layout is fully specified in plan §11 (`apps/`, `gateway/`, `services/*`, `agents/edge-agent-cpp/`,
`packages/*`, `infrastructure/*`, `docs/*`, `tests/*`) — use that layout once implementation starts.

## Module roadmap (dependency-driven, not date-driven)

| ID | Module | Depends on |
|---|---|---|
| M00 | Platform Foundation (control plane, identity, site registry, event contracts, Dapr, CI, Docker Compose + Bicep) | — |
| M01 | Product Analytics (tracking SDK, collector, ClickHouse model, dashboards) | M00 |
| M02 | Performance Monitoring (Web Vitals, percentiles, regression detection) | M00 (M01 optional) |
| M03 | Error Tracking (grouping, source maps, issue lifecycle) | M00 |
| M04 | Deployment Intelligence (release tracking, before/after comparison) | M00 |
| M05 | Goals and Funnels | M01 |
| M06 | SEO and Content Quality | M00 |
| M07 | Heatmaps and Session Insights | M01 |
| M08 | AI Insights (Claude via typed tools, no raw DB access) | ≥1 data module |
| M09 | Alerts and Uptime | M00 |
| M10 | Multi-site and Team Hardening | M00 |
| M11 | C++ Edge Agent (optional) | M00 (+M09 recommended) |

First practical build target (plan §15) is a thin **vertical slice**, not full infra:
register a site → issue a publish-only key → SDK on a local Vue page → track one page view → validate in
collector → publish via Dapr → process in Analytics Service → store in ClickHouse → query it → show it on
the dashboard → trace end-to-end → prove the same event ID isn't double-counted. Expand from there rather
than building out every shared service up front.

## Working conventions for this project

- **Definition of Ready** (plan §13): before starting a task, purpose is clear, one service owns it,
  acceptance criteria + input/output contracts are known, privacy impact and auth requirements considered,
  dependencies linked, test approach identified.
- **Definition of Done** (plan §14): acceptance criteria pass, no cross-service DB reads, contracts
  versioned, tests exist, logs/metrics/traces added, privacy/auth reviewed, retry/failure behavior
  implemented, docs/runbooks updated, local stack still works, CI passes, demoed with real/deterministic data.
- Events use the CloudEvents-style envelope in plan §8.1 (`id`, `type`, `source`, `subject`, `time`,
  `tenantId`, `siteId`, `correlationId`, `dataVersion`, `data`). Additive fields only; breaking changes get
  a new version; every consumer must be idempotent.
- Privacy is a first-class requirement, not a checkbox: no fingerprinting/cross-site tracking/raw IP
  retention/form-value capture by default; support cookie-free + consent-aware modes; verify against
  current Dutch/EU guidance before the anthony-air.nl launch.

## Architecture and decision docs (`docs/`)

M00.1 (Product and architecture decisions) is complete. Read these before making any architectural
decision that isn't already covered by them, rather than re-deriving it from the planning doc:

- `docs/architecture/vision-and-scope.md` — vision, target users, non-goals, the Accuracy Principle, and
  how analytics/observability/AI relate.
- `docs/architecture/c4-context.md` / `docs/architecture/c4-container.md` — C4 diagrams (Mermaid).
- `docs/architecture/bounded-contexts-and-data-ownership.md` — per-service data ownership table; the
  source of truth for "which service owns this, and how may another module read it."
- `docs/adr/0001-service-oriented-modular-platform.md`, `0002-dapr-pubsub-abstraction.md`,
  `0003-postgresql-plus-clickhouse.md`, `0004-transactional-outbox-and-idempotent-consumers.md`,
  `0005-context-level-data-isolation.md`, `0006-public-browser-ingestion-tokens.md` — accepted
  architecture decisions with rationale.
- `docs/privacy/privacy-threat-model.md` — privacy risks and mitigations; check any module touching
  identifiers, IP, URLs, DOM content, or AI access against this before implementing.

M00.2 (repo/service templates) is complete: monorepo skeleton, ASP.NET Core/worker/Vue templates,
shared event-contract package, .editorconfig/.gitattributes + ESLint/Prettier, CONTRIBUTING.md.

M00.3 (local self-hosted runtime) is complete: `infrastructure/compose/docker-compose.yml` runs
PostgreSQL, ClickHouse, RabbitMQ, Redis, MinIO, a Dapr placement service, and a headless Dapr sidecar
used to validate pub/sub end to end — infrastructure only, since `gateway/` and `services/*` are still
empty scaffolds (M00.4+). Per-context database/user provisioning (`docs/adr/0005`) is scripted in
`infrastructure/compose/db-init/`. Dapr components (RabbitMQ pub/sub, env-based secret store, bounded
retry/backoff + dead-letter resiliency policy) live in `infrastructure/dapr/`, per
`docs/adr/0002-dapr-pubsub-abstraction.md`. `infrastructure/compose/scripts/health-check.{sh,ps1}` is
the one-command verification; `seed-demo-data.{sh,ps1}` seeds a demo workspace/site/events (explicitly
temporary bootstrap tables, not the real service schemas). NAS deployment plan (reverse proxy/TLS,
resource limits, backups, network exposure) is documented in
`docs/architecture/nas-deployment-profile.md`, not yet deployed. Debugging guide:
`docs/runbooks/local-environment.md`. `infrastructure/bicep/` (Azure counterpart) is still open.

M00.4 (Identity, workspaces and sites) is **started, first cut only**: `services/identity-workspace/`
(a bare `Workspace` entity, `POST`/`GET /workspaces`) and `services/site-registry/` (`POST`/`GET /sites`,
browser token issuance per `docs/adr/0006`, publishing `site.created.v1` through a per-service
transactional outbox per `docs/adr/0004` — see `services/site-registry/README.md`). Both are wired into
`infrastructure/compose/docker-compose.yml` and covered by `tests/integration/Telumera.Tests.Integration/`.

Both services now require a valid Entra ID bearer token (`access_as_user` scope on the `Telumera API` app
registration) on every business endpoint — Microsoft.Identity.Web, config via `AzureAd__TenantId`/
`AzureAd__ClientId` in `.env`. See `docs/runbooks/local-environment.md`'s "Auth" section. There's a
second, separate app registration (`Telumera CLI Test Client`, public client, device-code flow) used only
by `infrastructure/compose/scripts/get-dev-token.sh`/`.ps1` to get a real token for manual testing —
`apps/dashboard-web`'s actual sign-in flow (`src/stores/auth.ts`) is still a placeholder, deliberately
deferred since the dashboard can't call through to these services end-to-end without a gateway anyway.

Membership and roles are now implemented: `identity-workspace` owns `User` (JIT-provisioned from the
caller's Entra `oid`, no separate signup flow) and `Membership` (`Viewer`/`Developer`/`Admin`/`Owner`,
`services/identity-workspace/Role.cs`). Creating a workspace auto-grants the creator `Owner`.
`POST /workspaces/{id}/members` adds members by raw Entra Object ID (no Microsoft Graph email lookup —
deliberately deferred to M10's "workspace roles and invitations" hardening per
`planning/Telumera_Modular_Project_Plan.md`). `site-registry` now actually enforces `WorkspaceId`
membership (`Developer`+ to create a site, `Viewer`+ to read one) by calling identity-workspace's new
internal membership endpoint through Dapr service invocation (`docs/adr/0002`) — this is what closes the
"any authenticated caller could act on any workspace" gap flagged during the Entra ID auth cut.
`identity-workspace` has its own Dapr sidecar now (`-app-port` set) to receive those invocations.

Key rotation is implemented: a site's browser tokens live in `SiteToken` (`services/site-registry/`), not
on `Site` — `POST /sites/{id}/tokens/rotate` issues a new active token without touching existing ones
("overlapping keys during safe migration," per the backlog wording), `POST
/sites/{id}/tokens/{tokenId}/revoke` is the separate, explicit step that invalidates one. Both require
`Developer`+ and publish `site.key.rotated.v1` through the same outbox pattern as `site.created.v1`, with
an `Action` field (`"issued"`/`"revoked"`) since the event catalogue defines only one key-related event
type — see `services/site-registry/README.md`.

Module enablement settings are implemented: `SiteModuleSetting` (`services/site-registry/`) tracks
per-site enabled/disabled state for `Analytics`/`Performance`/`Errors` — the three modules the backlog
task names explicitly, not the full M04–M09 roadmap. `POST /sites` creates all three enabled by default;
`PATCH /sites/{id}/modules/{module}` (`Developer`+) flips one and publishes `site.settings.changed.v1`
through the same outbox pattern as the other two Site Registry events — this constant existed since M00.1
but had no publisher until now.

This closes out every **backend-only** M00.4 backlog item (Entra ID auth, workspace entity, membership
and roles, site registration, key issuance, key rotation, module enablement).

`gateway/` is now built: a transparent reverse-proxy forwarder (`GatewayForwarder.cs`) in front of
identity-workspace/site-registry — same paths, routed via Dapr service invocation (`docs/adr/0002`), the
caller's own bearer token forwarded unchanged so each downstream service's existing auth/membership
checks keep working untouched. No response-composition endpoints yet (nothing needs one until the
dashboard does) — see `gateway/README.md` for the two design questions
`docs/architecture/c4-container.md` left open (transport mechanism, auth-forwarding model) now resolved
concretely. `http://localhost:5100`, its own `gateway-dapr` sidecar (outbound-only, no `-app-port`).

The M00.4 backlog is now fully closed: `apps/dashboard-web` has real sign-in (MSAL.js against a third,
separate Entra app registration — `Telumera Dashboard`, Single-page application platform, distinct from
`Telumera API` and the CLI test client) and real screens (`DashboardView.vue` workspace list/create →
`WorkspaceDetailView.vue` sites/members → `SiteDetailView.vue` tokens/modules). Needed two small backend
additions found while scoping this: `GET /workspaces` (identity-workspace) and
`GET /workspaces/{id}/sites` (site-registry) — nothing previously let a caller list "mine" without
already knowing an ID. The gateway also needed CORS (`Cors__AllowedOrigins` config) added, since the
dashboard is a different origin. End-to-end browser verification (not just "should work") surfaced three
real bugs, all fixed: the gateway's `/workspaces/**` → identity-workspace routing table didn't know
`GET /workspaces/{id}/sites` actually lives on site-registry (see `gateway/README.md`); the MSAL popup
flow needed its own dedicated redirect page (`apps/dashboard-web/auth-popup.html`) rather than the SPA
root — pointing it at the SPA root made the whole app boot a second time inside the popup and race
MSAL's own response-relay handling; and `logout()` was switched from `logoutPopup()` (a full Azure AD
front-channel logout that pops up a real Microsoft "end your session?" prompt — more than a dashboard
"Log out" button should trigger, and confusing to hit unexpectedly) to a local-only `clearCache()` (see
`src/lib/msal.ts`/`src/stores/auth.ts`).

What's left: `services/event-collector/` — separately blocked on M01's tracking SDK, not planned until
then (see the M00.4 epic's Asana history for why it's not scoped as part of this work).

M00.5 ("Messaging, observability and CI/CD") is complete. `.github/workflows/ci.yml` builds/lints/tests
both the .NET and TypeScript projects on every PR (integration tests excluded — they need the full local
stack plus a real Entra ID token, out of scope for a first CI pass). The CloudEvent envelope conventions
themselves turned out to already be complete from earlier milestones — the decision in
`docs/adr/0002-dapr-pubsub-abstraction.md` (M00.1) and the typed contract in `packages/event-contracts/`
(M00.2) — so that backlog item just needed to be marked done, not built. The transactional outbox pattern
is now a shared library, `packages/outbox/` (entity, EF Core mapping, Dapr-publishing `BackgroundService`),
per ADR 0004's call for this; `services/site-registry/` migrated onto it with no change to the published
event contract (see `services/site-registry/README.md`). The consumer idempotency pattern is also done as
a shared library, `packages/idempotency/` (processed-event marker + `TryBeginProcessingEventAsync` guard,
ADR 0004's "persist processed event IDs" strategy) — no real event subscriber exists in the codebase yet
(Event Collector, the natural first one, is blocked on M01's tracking SDK), so this is validated by
`packages/idempotency.Tests/` (in-memory EF Core) rather than a real end-to-end consumer; a future
subscriber adopts it directly. Distributed tracing is done at collector-only scope (explicitly chosen
over also standing up a Jaeger/Grafana UI, which isn't tracked as a future step anywhere — add it later
if it turns out to matter): an OTel Collector (`infrastructure/observability/`) receives OTLP traces from
every .NET service and every Dapr sidecar and logs them via the `debug` exporter, and
`OutboxEvent.CorrelationId` (`packages/outbox/`) now carries the actual request's W3C trace id instead of
a disposable random value, closing the real gap the backlog item named ("propagate correlation and trace
identifiers"). `.github/workflows/container-build.yml` builds each independently deployable service's
Dockerfile (`identity-workspace`, `site-registry`, `gateway` — `apps/dashboard-web` isn't containerized)
and pushes versioned images to GHCR (`ghcr.io/<owner>/telumera-<service>`) on push to `main` and on `v*`
release tags (CONTRIBUTING.md's release convention), tagged by short SHA plus `latest`/the release tag;
PRs get a build-only run (no push) to catch a broken Dockerfile before merge. Confirmed working on a real
GitHub Actions run (Container Build #1, triggered by this epic's own commits): all three jobs succeeded
in under two minutes and pushed both tags to GHCR with no permission issues —
`ghcr.io/anthony-air-03/telumera-{identity-workspace,site-registry,gateway}` all exist now, Private
(inherited from the repo). `infrastructure/bicep/` provisions the shared platform layer only (Container
Apps environment, Azure Container Registry, Log Analytics + Application Insights, a Key Vault, and a
managed identity wired with `AcrPull`/`Key Vault Secrets User` RBAC) — deliberately narrower than the
plan's full Azure portability table (no PostgreSQL/Service Bus/Blob Storage/actual container apps yet),
matching the backlog task's own wording; those belong to the still-open "Azure development deployment"
task once there's a real service to justify provisioning continuously-billed resources for. Every name is
parameter-derived, no account-specific values (same rule `docs/architecture/nas-deployment-profile.md`
follows) — installed the standalone Bicep CLI to compile/lint-check it (`bicep build`, `bicep lint`, both
clean), then went further and ran `az deployment sub what-if` against a real Azure subscription (Anthony's
own free-tier one, logged in and out for the check): 9 resources to create, no errors, all cross-resource
references resolved correctly. That run caught a real issue — `westeurope` (the original default) is
rejected on this subscription tier ("region is currently not accepting new customers"), not a template
bug; the default is now `northeurope`, confirmed working, documented in `infrastructure/bicep/README.md`.
The "Azure development deployment" task itself is now done too: `infrastructure/bicep/deploy.bicep`
deploys the full control plane (gateway, identity-workspace, site-registry — everything M00.4 built) on
top of the baseline via a new `modules/services.bicep` (PostgreSQL Flexible Server with one database per
context per ADR 0005, an Azure Service Bus namespace/topic backing Dapr's pubsub component per ADR 0002,
and the three Container Apps themselves, Dapr-enabled and topologically matching
`infrastructure/compose/docker-compose.yml`). Actually deployed for real and exercised end-to-end through
the live gateway with a real Entra token: `POST`/`GET /workspaces`, `POST /sites` (exercising
site-registry's Dapr service-invocation membership check and its transactional outbox). Dapr's
managed-identity auth against Azure Service Bus — the single highest-risk piece of this whole
deployment — worked on the first real attempt (publish → `204`, confirmed in container logs). Two real
ARM-level bugs surfaced only by an actual deployment, not `what-if` (a Key Vault name and a Container App
name both over their length limits) — both fixed, documented in `infrastructure/bicep/README.md`. Also
tightened both services from the Postgres admin login to their own `svc_access`/`svc_sites` roles
(ADR 0005's actual requirement, not just "works because admin has access everywhere") via
`scripts/init-postgres-databases.sh` — re-verified the same requests still succeed on the scoped
credentials. To be torn down (`az group delete`) once M00.5 testing wraps up — this was always a
deliberately throwaway validation run, not a persistent environment. Last backlog item,
`docs/runbooks/rollback-and-migrations.md`, documents what "rollback" actually means per environment
(self-hosted image redeploy, Azure Container Apps revision activation) and the expand/contract rule for
schema migrations — grounded in two real examples already in this repo's history
(`RenameOutboxEventTenantId`, `ChangeOutboxEventCorrelationIdToString`) that took the direct-rename
shortcut this rule now says not to repeat once a real rollback path needs protecting. This closes out
every M00.5 backlog item.

M01.1 ("Analytics definitions and privacy model") is complete: `docs/analytics/definitions-and-privacy-model.md`
defines exactly what every Product Analytics metric means — page-view rules (SPA navigation, reloads,
redirects, canonical URL/route-exclusion handling), session rules (30-minute inactivity timeout, no
midnight cutoff, cross-tab `localStorage`, campaign-context restart), visitor rules (server-side daily
hash by default per `docs/privacy/privacy-threat-model.md`, an opt-in consent-gated persistent mode),
bounce/engagement (an explicit engaged-session formula instead of copying GA's ambiguous single-interaction
rule), active-time measurement, URL/query-string policy (allowlist-only, denylist safety net), IP/geography
policy (no raw IP storage — schema-level, not just policy), and retention/deletion defaults — all before
any collection code exists, per the Accuracy Principle. Covers all 8 M01.1 Asana subtasks.

M01.2 ("Browser tracking SDK") is complete: `packages/browser-sdk` implements the client-side SDK against
M01.1's definitions doc exactly, and follows `docs/adr/0006-public-browser-ingestion-tokens.md` (the config
field is `siteToken`, never "SDK key") and the privacy threat model's consent/debug-mode requirements. This
is the first TypeScript package in `packages/*` (the other two, `outbox`/`idempotency`, are C#) and the
first use of a test runner anywhere in the repo — Vitest + jsdom, chosen because it's Vite's own runner and
the repo already depends on Vite for `apps/dashboard-web`, avoiding a second bundler/test-runner family.
Vite library mode builds two outputs from one `src/index.ts` entry (`browser-sdk.mjs` ESM,
`browser-sdk.global.js` IIFE setting `window.telumera`), with `.d.ts` emitted separately via
`tsc -p tsconfig.build.json` since Vite's build only handles JS. One design decision confirmed with Anthony
before implementing: debug mode implies dry-run (logs the exact outgoing payload, never actually sends) —
a site owner who wants to watch real traffic while debugging points `endpoint` at staging instead of
toggling a second flag. There's no Event Collector yet (M01.3), so the SDK posts to a fully configurable
`endpoint` with no path assumed. CI gained a `browser-sdk` job in `.github/workflows/ci.yml` mirroring
`dashboard-web`'s job shape (type-check, test, build, ci:lint, format:check) — each workspace is wired
explicitly by name in this repo, so a new `packages/*` TS package doesn't get picked up automatically.
Verified two ways: 50 Vitest cases across every module (page-view double-fire suppression, session
restart/sampling, consent gating, batching/retry/backoff, engagement idle/visibility handling, custom-event
validation), and a manual smoke test of the actual built `dist/browser-sdk.global.js` — Chrome's extension
automation in this environment refuses `file://` navigation, so the built bundle was instead loaded and
executed in a small jsdom harness (Node), confirming `window.telumera` is set, the router double-fire is
suppressed against the init page view, nothing is sent before `setConsent(true)`, and after consent the
debug transport logs the exact payload while still making zero real network calls. Covers all 12 M01.2
Asana subtasks.

M01.3 ("Collection API") is complete: `services/event-collector` accepts the SDK's batched
`POST /v1/events` and publishes accepted events onto Dapr pub/sub — the browser-facing counterpart to
M01.2. It's a genuinely different shape of service from identity-workspace/site-registry: its bounded-
contexts row says it owns "none persistent" data, and ADR 0004 explicitly exempts its high-volume
ingestion path from the transactional-outbox pattern those two use — so no PostgreSQL database, no EF
Core, no Entra auth (it's the one public/anonymous backend endpoint, per ADR 0006). Three design
decisions were confirmed before building: publish via an in-memory bounded `Channel` +
`BackgroundService` (not a synchronous per-request Dapr call) so ingestion latency is decoupled from
publish latency, at the ADR-0004-sanctioned cost of dropping an event on a crash between accept and
publish; load-test with NBomber (`tests/load/Telumera.LoadTests`) to stay in the .NET ecosystem rather
than adding k6 as a second tooling family; and a small, additive patch to the already-shipped
`packages/browser-sdk` (M01.2) adding a client-generated `id` to `OutgoingEvent`, since the Collector's
duplicate-protection subtask needed a per-event id the SDK didn't emit before this epic.

Site Registry gained two new internal endpoints (`GET /internal/tokens/{token}`,
`GET /internal/tokens`) — no existing endpoint could resolve a raw token string to a site, since every
one is keyed by an authenticated caller's already-known site ID. Same unauthenticated,
trust-the-Dapr-network-boundary pattern identity-workspace's internal membership endpoint already
established. The Collector keeps an in-memory `SiteProjection` (token → site/workspace/allowed-origins/
enabled-modules) built from these two endpoints and kept current by subscribing to
`site.created.v1`/`site.settings.changed.v1`/`site.key.rotated.v1` on site-registry's existing
`site-events` topic — the first real Dapr pub/sub *subscriber* in the repo (via `Dapr.AspNetCore`'s
`.WithTopic()` on a minimal API endpoint, since site-registry multiplexes three different payload shapes
onto that one topic, dispatched by inspecting the CloudEvent's own `type` field rather than binding a
single typed model).

Verified live against a real `docker compose up` stack (Docker Desktop wasn't running at first; the user
started it mid-session specifically so this could be verified for real rather than just compiled) —
this caught two real bugs `dotnet build`/`dotnet format` couldn't have: (1) a startup race where the
app's one-shot projection warm-up ran before its own Dapr sidecar's HTTP port was ready, silently
degrading to cache-miss-only forever with no self-healing — fixed by moving warm-up into a
`BackgroundService` (`SiteProjectionSyncService`) that retries quickly on startup and periodically
resyncs; (2) `EventValidation` rejected the SDK's own built-in `page_view` events, because it mirrored
`events.ts`'s scalar-only property-value rule, but that rule only actually applies to the SDK's public
`track()` API — `page_view`'s own `properties.query` is a legitimate nested object the built-in event
never validates client-side. Fixed by bounding property values by serialized size instead of value kind.
After both fixes, confirmed end-to-end: a real batch through `/v1/events` returns 202 and actually lands
on the `collector-events` RabbitMQ exchange (checked via its `publish_in` counter, before/after); a
retried duplicate event id increments that counter by zero extra; an unknown token 404s and a
mismatched Origin 403s while a missing one passes through (defense-in-depth per ADR 0006, not the access
boundary); a malformed event inside an otherwise-valid batch is rejected without failing the whole
batch; and — the most load-bearing check — publishing synthetic `site.settings.changed.v1` and
`site.key.rotated.v1` events directly against the running stack changed the Collector's live in-memory
projection within seconds with no restart, proving the subscription wiring actually works end-to-end,
not just that it compiles. Covers all 8 M01.3 Asana subtasks.

M01.4 ("Analytics processing pipeline") is complete: `services/analytics` is the first real subscriber
of the Collector's `collector-events` topic, and the first real ClickHouse writer anywhere in the repo.
It normalizes, enriches, dedupes, and persists events to ClickHouse's `telumera_analytics.events` table
(pre-provisioned since M00.3), then publishes `analytics.processed.v1` (per-event granularity — the
metric-window/anomaly rollup split `bounded-contexts-and-data-ownership.md` flags as an open question is
deferred to whichever epic actually builds Alerting/AI, per a confirmed decision). It owns a *second*,
much smaller PostgreSQL database (`telumera_analytics_control`) purely for `packages/idempotency`'s
consumer-dedup markers — the first real consumer of that package (built in M00.5, only unit-tested until
now) — matching ADR 0003's "a service using both PostgreSQL and ClickHouse" pattern. Scaffolded like
`event-collector` (`Microsoft.NET.Sdk.Web`, not `templates/worker-service/`) since Dapr pub/sub delivery
is HTTP-push into the app regardless of whether a service is conceptually a "worker" — confirmed by
M01.3's own container logs. `ClickHouseWriter.cs` talks to ClickHouse over its plain HTTP interface
rather than a NuGet client library (`ClickHouse.Client`'s latest stable has no confirmed net10.0 target),
matching how every other infra HTTP API in this repo is already called directly rather than through a
client SDK.

Four decisions were confirmed before building: enrichment moved fully into this service (the Collector's
own always-stubbed `GeoLookup.cs`/`Country` field was removed — a design mistake from M01.3 that
duplicated this epic's scope and worked against the Collector's own "no analytics queries" philosophy;
it now passes through raw `TruncatedIp`/`UserAgent`/`ClientTimestamp` instead); the SDK gained a second
follow-up patch (`title`/`channel`/`referrer`/`utm` added to every outgoing event's `properties` — the
SDK already computed `channel`/`referrer`/`utm` client-side for session-restart logic but never attached
it to the payload, so "Normalize referrer and campaign data" had nothing to normalize before this);
`analytics.processed.v1` publishes per-event only, not the full rollup split; and dead-letter recovery
is a standalone console tool, `tools/dead-letter-recovery` (the repo's first `tools/` directory), using
RabbitMQ's management HTTP API rather than admin endpoints on the always-running service. Device/browser/
OS categorization (`UserAgentClassifier.cs`) is hand-rolled substring heuristics into bounded categories,
deliberately not a UA-parsing library — the privacy threat model wants "bounded categories... not full
high-entropy fingerprint strings," so a full parser would work against the actual requirement, not toward
it. The idempotency-marker-vs-ClickHouse-write ordering (`EventProcessor.cs`) is a disclosed trade-off:
the Postgres marker commits *before* the ClickHouse write (not after, and not in one transaction — the
two stores can't share one), accepting rare event loss on a crash between the two over the alternative of
double-counting, since the subtask's literal goal is preventing retry duplicates from inflating metrics.

Verified live against the real running stack (same standard M01.3 set): a real batch through
`/v1/events` (with `channel`/`referrer`/`utm`/`title` in its properties, matching what the patched SDK
now sends) produces a correctly normalized ClickHouse row — trailing slash and fragment stripped from
the trusted `url` field, UTM fields extracted, `channel` validated, device/browser/OS correctly
classified from a real Chrome/Windows user agent string, visitor hash passed through unchanged; a
resubmitted duplicate event id produces no second row and the Postgres marker table shows why; a
Googlebot user agent is marked `is_bot = 1` while still being written, never dropped;
`analytics.processed.v1` actually publishes exactly once per processed event (RabbitMQ's
`analytics-events` exchange `publish_in` counter, same verification method M01.3 used for
`collector-events`); and `tools/dead-letter-recovery list` successfully queried the real
`dlq-analytics-collector-events` queue over RabbitMQ's management API, confirming the Dapr dead-letter
naming convention assumption was correct. This session also hit the exact same Dapr-sidecar-recreate
network-namespace quirk M01.3 first surfaced (recreating an app container orphans its
`network_mode: service:x` sidecar) — recognized immediately this time and fixed by recreating both
sidecars alongside their apps, rather than rediscovering it as a new bug. Covers all 10 M01.4 Asana
subtasks.

M01.5 ("Analytics storage and aggregation") is complete: `services/analytics` gained a `sessions`
ClickHouse table and five `daily_*_rollup` tables (site, page, acquisition, technology, geography) — the
layer downstream of M01.4's raw `events` table that nothing consumed until now. Two architectural facts
drove the design: no scheduled-job pattern existed anywhere in the repo (the closest precedent,
`event-collector`'s `SiteProjectionSyncService`, is the `BackgroundService` + `PeriodicTimer` shape this
epic's new `AnalyticsAggregationService` reuses), and no ClickHouse materialized view/TTL usage existed
either — sessions and rollups are periodically *recomputed*, not streaming materialized views, because a
30-minute inactivity gap (the session boundary, per `docs/analytics/definitions-and-privacy-model.md` §2)
can't be resolved by an insert-triggered view that can't know whether a later event still belongs to the
same session. A single watermark (`aggregation_checkpoints`, a new small Postgres control table next to
`packages/idempotency`'s `processed_events`, same "control table beside the real ClickHouse data" pattern)
drives both session recomputation and, cascading from it, rollup recomputation for exactly the (site,
date) buckets touched — this single mechanism *is* the "late-event handling" subtask, not a separate
window-recalculation path: a late event for a session closed days ago simply makes that session_id dirty
on the next tick regardless of age. The watermark trails "now" by a configurable safety buffer (300s
default) rather than advancing straight to the current time, to tolerate Dapr's at-least-once
redelivery/retry lag (ADR 0004) without silently dropping a straggler. `events` also gained the "event
version" M01.5's raw-event-table subtask asked for (`data_version`, from `EventEnvelope.DataVersion`) and
a 90-day TTL (`docs/analytics/definitions-and-privacy-model.md` §8's raw-retention default) — TTL on a
`DateTime64` column outright fails in ClickHouse 24.8 without a `toDateTime()` cast first, caught only by
testing the DDL against a live instance. `sessions`/rollups get no TTL — §8 already treats aggregate
retention as indefinite by default. The minimum-event-count-per-bucket floor §8 explicitly deferred to
this epic is now decided and documented there: rollup rows with fewer than 5 distinct visitors get
`is_below_privacy_floor = 1`, flagged not suppressed, same "marked not hidden" convention bot traffic
already uses. `daily_page_rollup` — the one rollup needing both per-event and per-session source data —
combines them via `UNION ALL` + `max()` per metric rather than a `FULL OUTER JOIN`, discovered to be both
simpler and less error-prone after live-testing both approaches directly against ClickHouse. M01.5 also
closes "Implement data reconciliation job" via a new `tools/reconciliation-report` console tool (following
`tools/dead-letter-recovery`'s established bare-console-tool shape) comparing exact Postgres
`processed_events` vs. ClickHouse `events` counts per day — giving the marker-before-write crash-window
trade-off M01.4's README already disclosed an actual detection mechanism for the first time, rather than
leaving it a documented-but-unverified risk; RabbitMQ's cumulative exchange counters are shown for context
only, explicitly not treated as precise per-day inputs (no historical per-day counter exists there),
per the Accuracy Principle. `viewport_category` (mentioned in the CSV's technology-rollup wording) and
per-site configurable retention were both deliberately not built — no SDK signal or Site Registry field
exists for either, and inventing one wasn't implied by a storage/aggregation epic; documented as gaps
rather than stubbed. `daily_geography_rollup.country` will be 100% empty until M01.8 ships real GeoIP,
same known limitation M01.1/M01.3/M01.4 already carry forward.

Every aggregation query (session recompute, all five rollups, the tuple-based dirty-date scoping subquery,
and a full late-event round trip) was developed and verified directly against a live ClickHouse instance
before being committed to C# — this caught two real bugs no amount of code review would have: the TTL
cast issue above, and a "ClickHouse: aggregate function found inside another aggregate function" error
from reusing an outer SELECT alias inside a second expression in the same list (fixed by nesting the
aggregation in its own subquery). Beyond that, the actual compiled `AnalyticsAggregationService` was
verified running for real inside a freshly built `docker compose up` container — not just exercised via
manual SQL: its first tick auto-backfilled all pre-existing session data with no errors, and a later tick
picked up a directly-inserted new event entirely on its own periodic schedule with no manual trigger,
producing the same result as the hand-verified queries. HTTP-level verification through the SDK's actual
`/v1/events` path (`AnalyticsAggregationTests.cs`, following `AnalyticsProcessingTests.cs`'s pattern) was
written and confirmed to build and skip cleanly, but not run against a real Entra ID token in this
session — no interactive device-code login was available; the aggregation logic itself was validated at
the ClickHouse layer directly instead. Covers all 11 M01.5 Asana subtasks.

M01.6 ("Analytics query API") is complete: `services/analytics` gained seven `GET
/sites/{siteId:guid}/analytics/...` endpoints (overview, time-series, pages, acquisition, technology,
geography, custom-events) reading M01.5's `sessions`/`daily_*_rollup` tables — the first read surface for
any of this module's data. This is also the first authenticated surface on this service (the existing
`/subscriptions/collector-events` endpoint stays unauthenticated, coexisting via ASP.NET Core's per-endpoint
`.RequireAuthorization` rather than a blanket policy) and the first real consumer of the `redis` container
that's run unused since M00.3 (`Microsoft.Extensions.Caching.StackExchangeRedis`'s standard
`IDistributedCache`, not a hand-rolled client — unlike `ClickHouseWriter`'s raw-HTTP approach, Redis's
official client has no net10.0 compatibility gap to work around). Four design questions
`docs/architecture/c4-container.md` explicitly left open for this epic were resolved and documented in
`services/analytics/README.md`'s new section: siteId→workspaceId resolution (a new unauthenticated `GET
/internal/sites/{id}` on `services/site-registry`, mirroring its existing `/internal/tokens/{token}`
exactly, feeding a duplicated `MembershipClient`/`Role.cs` pair rather than forwarding the caller's own
bearer token onward — the latter would have been genuinely new, unprecedented plumbing); the pagination
envelope (`{ items, total, page, pageSize }`, `total` via `count() OVER()` in the same query rather than a
second round trip — the first paginated endpoint in the repo); the cache key/TTL shape; and the query
timeout mechanism (`ClickHouseQueryClient`, a second HttpClient/class separate from the aggregation
writer's, pairing a client-side `HttpClient.Timeout` with a server-side `SETTINGS max_execution_time` —
the client timeout alone stops this service from waiting but doesn't stop ClickHouse itself from burning
CPU on a runaway query, which is the actual "protect ClickHouse" goal). `gateway/GatewayForwarder.cs`
needed one more routing carve-out (`/sites/{id}/analytics/**`, mirroring its existing
`workspaces/.../sites` one) since its table already routes the `sites` segment to site-registry. Every
query-string value not validated against a fixed C# allowlist (sort column/direction, time-series
interval) is passed through ClickHouse's native `{name:Type}` parameter binding rather than string-
interpolated — verified live against a real ClickHouse instance that this rejects injection attempts as
inert literal data, the same live-first discipline M01.5 established (the window-function pagination
count, hourly time-series bucketing, and the custom-events "allowed properties"
`JSONExtractKeys`/`arrayJoin` query were all developed and confirmed against real data before being
written into C#). Comparison periods (`?compare=true`) are wired into the overview endpoint as their
primary, reusable home (`DateRangeParsing.GetPreviousPeriod`) rather than all seven, per the CSV's own
singular framing. Two interpretation calls are flagged rather than treated as literal spec: "allowed
properties" on the custom-events endpoint means observed property keys, not a platform-enforced allowlist
(none exists); the technology endpoint has no screen/viewport dimension since no SDK signal for one exists
anywhere in the pipeline, a gap M01.5 already carried forward rather than a new omission.

Initially verified as far as this session's tooling allowed without an interactive Entra ID login
(`get-dev-token.sh`/`.ps1` requires a human to complete a device-code browser sign-in): build/format
clean; every ClickHouse query validated live against real data (including a live-caught raw-string-
interpolation brace-escaping bug, `$"""..."""` vs. `$$"""..."""`, that would not have compiled); the full
stack rebuilt and restarted with the three changed services (`analytics`, `site-registry`, `gateway`) plus
their Dapr sidecars recreated (the same known network-namespace quirk M01.3/M01.4 already hit); confirmed
live that an unauthenticated call 401s through both the gateway and the service directly, a malformed
bearer token 401s cleanly with no unhandled exception, the new internal site-lookup endpoint returns the
right not-found shape, and the new gateway carve-out reaches `analytics` (not a stray 404) while the
existing `/sites/{id}` route is unaffected.

The authorized path itself was then closed out the same session, at the user's prompt: the user ran
`get-dev-token.sh` themselves (the interactive device-code sign-in this environment can't complete
unattended) and shared the resulting token. Against it: registered a real workspace/site through the
gateway, posted real events through the Collector, confirmed the aggregation pipeline picked them up, and
called all seven query endpoints through the gateway with real auth — overview (with `?compare=true`),
paginated/sorted pages, technology all returned correct values; an unknown site 404s; a real member 200s.
Confirmed the Redis cache directly (`redis-cli KEYS`/`TTL`) — the correct key present with the correct
~60s TTL, not just inferred from response timing. Then ran the actual automated suites against the same
token rather than relying only on ad hoc curl checks: all 4 `AnalyticsQueryTests` pass, and all 4 M01.5
`AnalyticsAggregationTests` pass too — closing a verification gap that milestone had also left open, as a
bonus. Running the full pre-existing suite alongside these surfaced one real, pre-existing bug unrelated
to this epic: `AnalyticsProcessingTests.CountClickHouseRowsAsync` (M01.3/M01.4) called
`JsonNode.GetValue<int>()` directly on a ClickHouse `count()` result, which is `UInt64` and therefore
quoted as a JSON string by ClickHouse's output format — throws `InvalidOperationException`, not a silent
wrong answer, so this specific automated test had apparently never actually been run to a passing state
before (M01.4's own "verified live" checks were the manual curl-based kind, not `dotnet test` itself, per
CLAUDE.md's own account of that session). Fixed using the same `JsonElement.ValueKind`-branching pattern
this epic's own `ClickHouseJsonExtensions.GetLong` already uses rather than guessing at ClickHouse's
quoting behavior. All 25 integration tests in the repo now pass together against a real token — likely the
first time the complete suite has ever been run end-to-end, not just individually verified per-milestone.
Covers all 10 M01.6 Asana subtasks.

## Planning artifacts (`planning/`)

- `Telumera_Modular_Project_Plan.md` — the full architecture/roadmap doc summarized above; treat as the
  source of truth for scope and sequencing questions.
- `Telumera_Asana_Import.csv` — 333-row backlog (milestones/epics/tasks per module with Status, Priority,
  Estimate, Dependencies, Work Type columns) — check here before re-deriving a task breakdown.
- `Telumera_Planning_Workbook.xlsx` — roadmap, backlog, service catalogue, event contracts, board field
  config in spreadsheet form.
