# ADR 0003: PostgreSQL for Metadata, ClickHouse for Event Analytics

- **Status:** Accepted
- **Date:** 2026-08-06
- **Related:** `docs/architecture/bounded-contexts-and-data-ownership.md`, `docs/architecture/c4-container.md`

## Context

Telumera's services fall into two very different storage-shape categories:

- **Transactional/configuration data**: users, workspaces, site settings, goal definitions, issue
  lifecycle, deployments, alert rules, audit records — moderate volume, needs relational integrity and
  transactions, changes driven by direct user action.
- **Event/time-series data**: page views, sessions, visitor aggregates, performance samples, error
  occurrences, interaction samples — high volume, append-mostly, queried by time range and high-cardinality
  breakdowns (per page, per device, per country, per release).

Using one database engine for both forces a compromise: a row-oriented OLTP database struggles at
analytics-query volume and cardinality; a column-oriented analytics database is a poor fit for
transactional consistency and small, frequently-updated records.

## Decision

Split storage by data shape, per module (ownership detail in
`docs/architecture/bounded-contexts-and-data-ownership.md`):

- **PostgreSQL** — for users and workspaces, site configuration, roles and keys, goal definitions, issue
  lifecycle, deployments, alert rules, and audit records. Each owning service gets its own **database**
  (not just a schema within a shared database) on a shared PostgreSQL server, per
  `docs/adr/0005-context-level-data-isolation.md` — this makes the no-cross-context-access rule
  (`docs/adr/0001-service-oriented-modular-platform.md`) enforceable by credentials/permissions, not only
  by documentation discipline.
- **ClickHouse** — for page-view events, session/visitor aggregates, performance samples, error
  occurrences, interaction samples, and any other time-series or high-cardinality breakdown data. Each
  owning service gets its own ClickHouse **database** (ClickHouse's actual isolation primitive —
  `CREATE DATABASE`, not a schema) on a shared ClickHouse server, under the same
  `docs/adr/0005-context-level-data-isolation.md` convention and the same no-cross-access rule.
- **Object storage** (MinIO self-hosted / Azure Blob in Azure) — for source maps, Lighthouse reports,
  exports, optional session-replay chunks, and other large diagnostic artifacts that don't belong in
  either database. The infrastructure (bucket-hosting service) is shared, but bucket/container namespaces
  are per owning context — see `docs/adr/0005-context-level-data-isolation.md` — because artifacts like
  source maps and session replay have materially different privacy, retention, and deletion requirements.
- **Redis** — used only where genuinely needed for short-lived live-visitor projections, distributed rate
  limits, caches, or ephemeral locks. Redis must never become an undocumented source of truth — anything
  that needs to survive a cache flush belongs in PostgreSQL or ClickHouse instead.

## Consequences

- **Positive:** each module picks the storage technology suited to its actual access pattern instead of
  one-size-fits-all; this also satisfies the "different storage technology" trigger for service boundaries
  in `docs/adr/0001-service-oriented-modular-platform.md` where it applies (e.g. Error Service using both
  PostgreSQL for issue lifecycle and ClickHouse for high-volume occurrences).
- **Positive:** the self-hosted/Azure portability goal from `docs/adr/0002-dapr-pubsub-abstraction.md`
  extends cleanly here — PostgreSQL maps to Azure Database for PostgreSQL (or a containerized instance for
  learning), ClickHouse is self-managed in both environments initially, object storage maps to MinIO or
  Azure Blob.
- **Negative:** running two database engines (plus Redis where used) adds operational surface area for a
  single-person project — M00.3's local self-hosted runtime and health-check tooling need to make sure
  this doesn't become a constant maintenance burden.
- **Negative:** any report that would naturally want to join transactional and event data (e.g. "goal
  conversions by user role") must do so at the API/service layer, not via a database join — this is a
  direct consequence of the no-shared-database rule and needs to be designed into each query API rather
  than retrofitted.
