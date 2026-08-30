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

M00.5 ("Messaging, observability and CI/CD") is in progress. `.github/workflows/ci.yml` builds/lints/tests
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
follows) — installed the standalone Bicep CLI to actually compile/lint-check it (`bicep build`, `bicep
lint`, both clean) rather than hand-verify syntax; not deployed against a real Azure subscription, which
is exactly what the deployment task still needs to do. Still open: Azure development deployment and
rollback/migration-rules documentation.

## Planning artifacts (`planning/`)

- `Telumera_Modular_Project_Plan.md` — the full architecture/roadmap doc summarized above; treat as the
  source of truth for scope and sequencing questions.
- `Telumera_Asana_Import.csv` — 333-row backlog (milestones/epics/tasks per module with Status, Priority,
  Estimate, Dependencies, Work Type columns) — check here before re-deriving a task breakdown.
- `Telumera_Planning_Workbook.xlsx` — roadmap, backlog, service catalogue, event contracts, board field
  config in spreadsheet form.
