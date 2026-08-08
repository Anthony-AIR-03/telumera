# ADR 0001: Service-Oriented Modular Platform in One Monorepo

- **Status:** Accepted
- **Date:** 2026-08-06
- **Related:** `docs/architecture/bounded-contexts-and-data-ownership.md`, `docs/architecture/c4-container.md`

## Context

Telumera is planned as a long-lived platform that grows one module at a time (M00–M11) rather than
shipping as a finished product on day one. Two failure modes need to be avoided:

- A single monolithic application where modules share code, database access, and deploy lockstep — this
  would make "disable a module" or "release analytics without touching errors" impossible, and would let
  accidental coupling creep in constantly (a query joining two modules' tables "just this once").
- A full microservices architecture from day one, with a separate production container and deployment
  pipeline for every small piece of logic — this is operationally unmanageable for a project built and
  run by one person, and most of the module boundaries don't yet need independent scaling or release
  timing.

## Decision

Use a **service-oriented modular platform in one monorepo**.

Each product module (Analytics, Performance, Errors, Deployment Intelligence, Conversions, SEO, Session
Insights, AI Insights, Alerting, Notification, plus the shared control-plane services) must:

1. own its domain rules;
2. own its datastore(s), isolated per `docs/adr/0005-context-level-data-isolation.md` (see also
   `docs/adr/0003-postgresql-plus-clickhouse.md`) — ownership is decided at the module/bounded-context
   level, not the process level: a module that later splits into multiple deployables (e.g. a query API
   and a worker) shares its module's datastore(s) rather than each deployable getting its own;
3. expose an API or publish events — never be read from directly;
4. never read another module's tables directly;
5. be independently buildable and deployable;
6. tolerate another module being disabled;
7. use versioned contracts for anything crossing a service boundary;
8. include its own tests and observability.

A logical boundary only becomes a **separate deployment unit** (its own container/pipeline) when it needs
at least one of: independent scaling, independent release timing, a different storage technology, a
different security boundary, failure isolation, a distinct learning objective, or clear ownership of
high-volume processing. Boundaries that don't meet this bar stay as logically separate code within the
monorepo rather than becoming their own production container.

All modules live in one monorepo (layout in `planning/Telumera_Modular_Project_Plan.md` §11) for shared
tooling, atomic cross-cutting changes (e.g. updating a shared event-contract package), and simpler local
development — this is a monorepo-of-services, not a shared-codebase monolith.

## Consequences

- **Positive:** modules can be built, learned, and shipped one at a time without an all-or-nothing
  release; a module can be disabled without breaking others; storage technology can differ per module
  (PostgreSQL vs. ClickHouse) without forcing a platform-wide choice.
- **Positive:** one monorepo keeps CI, linting, and shared contract packages simple to manage solo.
- **Negative:** requires discipline — every new feature must be checked against "which service owns this,
  and am I about to read another service's table" before writing code. This is process risk, not tooling
  risk, and the CI/review process (M00.5) should catch violations where practical (e.g. lint rules against
  cross-schema connection strings).
- **Negative:** local development requires running multiple services together (Docker Compose, M00.3)
  even for changes to one module, though this is intentional — it's meant to catch integration issues the
  same monolith would have hidden.
