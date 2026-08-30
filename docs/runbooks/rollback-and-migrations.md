# Rollback and database migration rules

> Status: Active (M00.5). Applies to every service with its own PostgreSQL database
> (`docs/adr/0005-context-level-data-isolation.md`) — currently `identity-workspace` and `site-registry`.

## Why this needs rules, not just intuition

Every service auto-applies its EF Core migrations at startup (`Database.Migrate()` in `Program.cs`) —
convenient for local dev and for the Azure deployment (`infrastructure/bicep/`), but it means **a
migration runs the moment a new image starts**, before anyone has confirmed the new code actually works.
If it doesn't, the fix is to roll the code back — but the database doesn't roll back with it. Whatever
schema the bad deploy's migration left behind is what the *previous* version of the code now has to run
against. A migration that isn't safe for the old code to run against turns "roll back the code" into "the
rollback is also broken," at the worst possible moment.

This is a real gap, not a hypothetical one: two migrations already in this repo's history —
`RenameOutboxEventTenantId` and `ChangeOutboxEventCorrelationIdToString`
(`services/site-registry/Migrations/`) — renamed a column and changed another's type directly, in one
step, with no expand/contract split. That was a reasonable call *at the time*: M00.5's own local dev
database, no production data, no deployed rollback path to protect. It stops being a reasonable call the
moment either service runs somewhere a rollback might actually be needed — the self-hosted NAS deployment
serving real traffic, or an Azure environment treated as long-lived rather than M00.5's throwaway test.
Don't repeat that shortcut past that point.

## What "rollback" actually means, per environment

Rolling back **never means running a migration backward** — EF Core migrations here have `Down()` methods
(generated automatically), but nothing in this project's deploy path ever calls them, and this document
doesn't introduce one. A migration's `Down()` is for local dev iteration (`dotnet ef database update
<previous-migration>` while building a feature), not a production recovery mechanism — running it against
a database real requests have already written to risks losing that data. Rollback here always means: **go
back to running the previous version of the code, against whatever schema state exists right now.** That
only works if every migration was written to allow it (see the rule below).

- **Self-hosted (`infrastructure/compose/`):** redeploy the previous image tag. `container-build.yml`
  tags every image with its commit SHA (`sha-<short-sha>`) specifically so a previous build is always
  addressable — `docker compose` (or whatever runs it on the NAS) pointed at an older `sha-` tag instead
  of `latest`.
- **Azure (`infrastructure/bicep/`):** Container Apps keeps every revision — rolling back means
  activating the previous one (`az containerapp revision activate` / traffic-split back to it), not a new
  deployment. Same underlying rule applies: the previous revision's code has to tolerate today's schema.

## The rule: expand/contract, never both in one migration

Split every schema change that isn't purely additive into two separate migrations, deployed as two
separate releases with a bake period between them:

1. **Expand** (backward-compatible, safe to roll back through): add a new nullable column, a new table, a
   new index. The *old* code, unaware the new column exists, keeps working unchanged. Ship the code that
   uses the new column in the same release or a later one — either way, the old code's rollback path stays
   intact because the schema change alone didn't require it to change.
2. **Contract** (destructive, NOT safe to roll back through): drop the old column/table, rename anything,
   change a column's type, add a `NOT NULL` constraint to a column that used to allow nulls, narrow a
   unique constraint. Only run this once the code that depended on the *old* shape has been out of
   rotation long enough that rolling back to it is no longer a real option — there's no fixed bake period
   mandated here, but "the previous release is still the rollback target" and "I'm about to run a
   contract migration" must never both be true at once.

Never combine an expand and a contract step for the same change in one migration/one release. A rename is
the clearest example: `RenameOutboxEventTenantId` did `workspace_id → tenant_id` in one step. The
expand/contract-correct version would have been: (1) add `tenant_id` nullable, backfill it, dual-write
both columns; ship; (2) once the new code's release is no longer a rollback target, drop `workspace_id`.

This is the same discipline `docs/adr/0002-dapr-pubsub-abstraction.md` already requires for event
contracts ("breaking payload changes require a new event type version... a service may support multiple
input versions during a migration") — never mutate a shape a consumer might still depend on; add a new
one and retire the old one later. Database migrations are the same rule applied to a schema instead of an
event payload.

## Quick reference: does this migration need the expand/contract split?

| Change | Safe in one step? |
|---|---|
| New nullable column | Yes — purely additive |
| New table | Yes — purely additive |
| New index (not replacing one being dropped) | Yes — purely additive |
| Drop a column/table | No — contract only, once nothing rolls back to code that reads it |
| Rename a column/table | No — expand (add new, dual-write/backfill), then contract (drop old) |
| Change a column's type | No — same as rename: add the new-typed column, migrate data, drop the old |
| Add `NOT NULL` to an existing column | No — backfill every row first, then constrain |
| Narrow/add a unique constraint | No — existing data must already satisfy it, verify before constraining |

## Cross-service ordering

Per `docs/adr/0005-context-level-data-isolation.md`, each service owns its own database — a migration in
`site-registry` can never affect `identity-workspace`'s schema, so there's no cross-service migration
ordering to coordinate for schema changes themselves. The thing that *does* need ordering is a contract
step whose old shape another service's Dapr-invoked call or published event still assumes — check
`docs/architecture/bounded-contexts-and-data-ownership.md` for who reads/receives what before running one.
