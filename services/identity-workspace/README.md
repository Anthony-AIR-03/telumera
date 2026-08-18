# services/identity-workspace

Identity & Workspace (M00.4 first cut) — a bare `Workspace` entity, per
`docs/architecture/bounded-contexts-and-data-ownership.md`'s Identity & Workspace row. Membership, roles,
and the Entra ID user/identity binding are all deferred to a later M00.4 task; this exists only to give
Site Registry a `WorkspaceId` to own a site under.

## Auth

`POST`/`GET /workspaces` require a valid Entra ID bearer token with the `access_as_user` scope
(`Telumera API` app registration) — see `docs/runbooks/local-environment.md`'s "Auth" section for how to
get one for manual testing. `/health/live` and `/health/ready` stay open. Authentication only, not yet
authorization by workspace membership — any validly authenticated caller can create/read any workspace
until the membership/roles task lands.
