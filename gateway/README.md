# gateway/

The API Gateway / BFF (`Telumera.Gateway`) — the single authenticated public API surface in front of
identity-workspace/site-registry. Holds no module data of its own (see
[docs/architecture/c4-container.md](../docs/architecture/c4-container.md)).

## Scope of this cut

A transparent reverse-proxy forwarder (`GatewayForwarder.cs`) — same paths, same request/response shapes
as the underlying services, not hand-written routes per existing endpoint. `/workspaces/**` forwards to
identity-workspace, `/sites/**` to site-registry; anything else `404`s. One carved-out exception:
`GET /workspaces/{id}/sites` is site-registry's endpoint (a workspace's sites, not one of
identity-workspace's own resources), so the forwarder checks for that specific three-segment shape before
falling back to the first-segment table — found as a real bug during dashboard verification, where the
naive table sent it to identity-workspace and got a `404` back. Real response-composition endpoints
(aggregating multiple services into one call, e.g. "workspace overview") are deliberately not built yet —
no dashboard screen exists to consume one. Add those when an actual UI need justifies them, not
speculatively.

## Two design questions `docs/architecture/c4-container.md` left open, resolved here

- **Transport to identity-workspace/site-registry**: Dapr service invocation, per
  `docs/adr/0002-dapr-pubsub-abstraction.md`'s rule for synchronous cross-service calls, matching the
  precedent `services/site-registry/MembershipClient.cs` already established. The C4 doc's own notes said
  this "should be made concrete in each service's own docs as it's built" — this is that.
- **Auth model**: the gateway validates the caller's Entra ID token itself (same
  `Microsoft.Identity.Web`/`AzureAd` config/`ApiScope` policy as both other services) and forwards the
  **original bearer token unchanged** to whichever service it's proxying to. Each downstream service keeps
  independently validating the token and running its own membership checks exactly as it does when called
  directly — no new gateway-to-service trust model, no gateway-issued credentials.

## Auth

Every path except `/health/live`/`/health/ready` requires a valid Entra ID bearer token with the
`access_as_user` scope — see `docs/runbooks/local-environment.md`'s "Auth" section for how to get one for
manual testing. Authorization runs before `GatewayForwarder` does, so an unknown path still `401`s before
it ever gets the chance to `404`.

## CORS

`apps/dashboard-web` (Vite dev server, a different origin) needs this to call the gateway at all —
without it the browser blocks every request before it reaches auth or `GatewayForwarder`. Allowed origins
come from `Cors__AllowedOrigins` config (`CORS_ALLOWED_ORIGINS` in `.env`, comma-separated) — not
hardcoded, not `AllowAnyOrigin`. `UseCors` runs before `UseAuthentication`/`UseAuthorization` (standard
ASP.NET Core ordering — CORS has to handle preflight before auth does).

## Dapr

`gateway-dapr` is outbound-only — nothing invokes the gateway back via Dapr, so unlike
`identity-workspace-dapr` it has no `-app-port`.

**Deliberately excluded from `infrastructure/dapr/components/resiliency.yaml`'s custom retry/circuit-breaker
policy** (`gateway` is not in that file's `scopes`), unlike site-registry's `MembershipClient`. That
policy's `trip: consecutiveFailures >= 5` treats *any* non-2xx response from the invoked app as a
failure — correct for `MembershipClient` (a GET whose only outcomes are success or a genuine transport
error, by design), wrong for this forwarder: it proxies arbitrary REST responses, so a legitimate
401/403/404/400 is a normal business outcome here, not an infrastructure failure, and retrying a
non-idempotent POST/PUT/PATCH on a false "failure" risks creating duplicate resources. Found the hard way
during verification — a real `403` response tripped the circuit breaker and turned it into a spurious
`500`. Gateway's invoke calls run on Dapr's plain default behavior instead: real responses pass straight
through regardless of status code.
