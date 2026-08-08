# services/

One folder per bounded-context service, each owning its own datastore and never reading another
service's tables directly (see [docs/architecture/bounded-contexts-and-data-ownership.md](../docs/architecture/bounded-contexts-and-data-ownership.md)
and [ADR 0001](../docs/adr/0001-service-oriented-modular-platform.md)).

Folder naming follows the bounded-context names in that doc (kebab-case), e.g. `identity-workspace/`,
`site-registry/`, `event-collector/`, `analytics/`.

Service folders are created one at a time as each module is implemented, not pre-scaffolded — see
CLAUDE.md's "expand from there rather than building out every shared service up front." None exist yet.
