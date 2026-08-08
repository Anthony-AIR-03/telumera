# ADR 0005: Context-Level Data Isolation

- **Status:** Accepted
- **Date:** 2026-08-07
- **Related:** `docs/adr/0001-service-oriented-modular-platform.md`, `docs/adr/0003-postgresql-plus-clickhouse.md`, `docs/architecture/bounded-contexts-and-data-ownership.md`

## Context

ADR 0001 and the bounded-contexts document already establish that no service reads another service's
tables directly. ADR 0003 previously left the physical isolation mechanism ambiguous — "database (or
schema)" for PostgreSQL, "logical schemas" for ClickHouse (which doesn't actually have schemas as an
isolation primitive; its equivalent is `CREATE DATABASE`). Schema-level permissions can enforce the same
boundary as database-level isolation if configured correctly, but leaving the choice ambiguous means the
boundary is enforced by documentation discipline in some deployments and by credentials in others — and
it's easier to accidentally query across a schema boundary inside one shared database than across a
database boundary with per-database credentials.

Object storage has the same ambiguity one level further: ADR 0003 describes it as shared infrastructure
without specifying whether different contexts' artifacts (e.g. Error Service's source maps vs. Session
Insights' replay chunks) are namespaced separately, despite having materially different privacy,
retention, and deletion requirements.

## Decision

**One bounded context = one data owner**, already decided. This ADR fixes the physical convention so
that ownership is enforceable by credentials, not just by code review:

- **PostgreSQL:** one database per owning context, on a shared PostgreSQL server/cluster:

  ```text
  PostgreSQL server/cluster
  │
  ├── telumera_access        (Identity & Workspace)
  ├── telumera_sites         (Site Registry)
  ├── telumera_deployments   (Deployment Service)
  ├── telumera_errors        (Error Service — relational part)
  ├── telumera_conversions   (Conversion Service)
  ├── telumera_ai            (AI Insights)
  ├── telumera_alerting      (Alerting)
  └── telumera_notifications (Notification)
  ```

  Each context gets its own database, its own database user/credential, and permissions scoped to only
  its own database. A context may use schemas *within* its own database freely — that's an internal
  implementation detail, not a cross-context boundary.

- **ClickHouse:** one logical database per data-owning context, on a shared ClickHouse server:

  ```text
  ClickHouse server
  │
  ├── telumera_analytics    (Analytics Service)
  ├── telumera_performance  (Performance Service)
  ├── telumera_errors       (Error Service — event part)
  ├── telumera_conversions  (Conversion Service, if it stores rollups in ClickHouse)
  └── telumera_sessions     (Session Insights)
  ```

  Separate users/roles per database where ClickHouse's access-control model makes that practical. This
  does **not** mean a separate ClickHouse server or container per context — one physical ClickHouse
  instance remains appropriate for a self-hosted, single-operator deployment.

- **Object storage:** one bucket/container namespace per owning context:

  ```text
  Object storage
  │
  ├── telumera-errors    (Error Service — source maps)
  ├── telumera-seo       (SEO Scanner — crawl reports)
  ├── telumera-sessions  (Session Insights — replay chunks)
  └── telumera-exports   (cross-context user-initiated exports, if that ends up living in a
                          shared export service rather than per-context)
  ```

  Separate access policies/credentials per bucket where the hosting infrastructure (MinIO self-hosted,
  Azure Blob) makes that practical.

This is a **naming and isolation convention**, not a scaling decision — every engine above still runs as
one physical server/instance for cost reasons appropriate to a self-hosted, single-operator platform;
only the logical namespace is per-context.

## Consequences

- **Positive:** the no-cross-context-access rule becomes enforceable by database/bucket
  credentials rather than relying entirely on code review to catch an accidental cross-schema query.
- **Positive:** each context's data can be backed up, restored, migrated, or (per
  `docs/privacy/privacy-threat-model.md`'s retention/deletion requirements) deleted independently of every
  other context's data, without needing to carve it out of a shared database first.
- **Positive:** if a context's storage tech or scale needs ever diverge enough to warrant its own physical
  server (the "different storage technology" / "independent scaling" triggers in
  `docs/adr/0001-service-oriented-modular-platform.md`), moving one already-isolated database off the
  shared server is a connection-string change, not a data-migration project.
- **Negative:** more databases/namespaces to create and manage credentials for than a single shared
  database per engine — mitigated by scripting this as part of M00.3's local runtime setup and the Azure
  Bicep templates, so it's provisioned consistently rather than by hand per context.
