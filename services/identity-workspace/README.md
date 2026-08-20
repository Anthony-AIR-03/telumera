# services/identity-workspace

Identity & Workspace, per `docs/architecture/bounded-contexts-and-data-ownership.md`'s Identity &
Workspace row: `Workspace`, `User` (a local profile bound to the caller's external Entra ID identity —
not passwords/credentials, which Entra ID owns exclusively), and `Membership` (a user's role within one
workspace).

## Roles

`Role` enum, ordered so `>=` comparisons express "at least this role": `Viewer` (0) < `Developer` (1) <
`Admin` (2) < `Owner` (3). Whoever creates a workspace (`POST /workspaces`) is automatically granted
`Owner` in the same transaction — otherwise a newly created workspace would have no members able to
manage it.

## User provisioning

There's no separate signup flow anywhere in this platform. `CurrentUserAccessor` JIT-provisions the
caller's `User` row from their validated token's `oid` claim on first sight, and opportunistically
refreshes `DisplayName`/`Email` from token claims on every call. Adding someone as a member
(`POST /workspaces/{id}/members`) who has never signed in yet also creates a stub `User` row for their
Entra Object ID — their profile fields fill in whenever they first authenticate.

## Adding members

`POST /workspaces/{id}/members` takes a raw Entra Object ID (a GUID, found in the Entra admin center's
Users list) plus a role — not an email lookup. `docs/adr/0006`-style email-based invitations (via
Microsoft Graph) are explicitly out of scope here; the project plan places that under M10's "workspace
roles and invitations" hardening work, not this cut. Requires the caller to be `Admin`+ in the target
workspace.

## Listing workspaces

`GET /workspaces` lists workspaces the caller has any membership in (plus their role in each) — added
for `apps/dashboard-web`'s workspace picker, since every other endpoint here takes an ID you'd already
have to know. Inherently scoped to the caller's own memberships, so no per-workspace role check beyond a
valid token.

`GET /workspaces/{id}` also returns the caller's own `Role` in that workspace (`WorkspaceDetailDto`) —
previously returned the raw `Workspace` entity and discarded the role it already computes for the 403
check. Added for `WorkspaceDetailView.vue`'s client-side role-gating (hide the create-site/add-member
forms below the caller's actual permission), not a new authorization mechanism — the backend still
enforces every real check independently.

## Internal membership-check endpoint

`GET /internal/workspaces/{workspaceId}/members/{entraObjectId}` — used by other services (currently
site-registry's `MembershipClient`) to check a caller's role via Dapr service invocation
(`docs/adr/0002-dapr-pubsub-abstraction.md`), since no service may read another's database directly.
**Deliberately unauthenticated at the HTTP level** — it's reachable through the Dapr sidecar within the
compose network, not the service's public host port. This trusts the network boundary rather than
implementing a service-to-service auth scheme, which doesn't exist yet anywhere in this project; a known,
explicitly documented simplification rather than an oversight.

## Auth

All endpoints except `/health/live`/`/health/ready` require a valid Entra ID bearer token with the
`access_as_user` scope (`Telumera API` app registration) — see `docs/runbooks/local-environment.md`'s
"Auth" section for how to get one for manual testing. `GET`/`POST /workspaces/{id}` and the members
endpoints additionally check the caller's actual membership role in that workspace (403 if insufficient),
not just that the token is valid — this closes the gap flagged during the Entra ID auth cut, where any
authenticated caller could act on any workspace.
