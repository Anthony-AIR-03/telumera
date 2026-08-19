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

Deliberately still deferred within M00.4: module-enablement toggles, the dashboard settings UI (including
real sign-in), and the `gateway/`/`services/event-collector/` scaffolds.

## Planning artifacts (`planning/`)

- `Telumera_Modular_Project_Plan.md` — the full architecture/roadmap doc summarized above; treat as the
  source of truth for scope and sequencing questions.
- `Telumera_Asana_Import.csv` — 333-row backlog (milestones/epics/tasks per module with Status, Priority,
  Estimate, Dependencies, Work Type columns) — check here before re-deriving a task breakdown.
- `Telumera_Planning_Workbook.xlsx` — roadmap, backlog, service catalogue, event contracts, board field
  config in spreadsheet form.
