# infrastructure/bicep

Azure Bicep templates for the shared platform layer of a Telumera Azure development environment (M00.5).

**Scope** matches the "Bicep baseline" backlog task specifically, not the full Azure portability table in
`planning/Telumera_Modular_Project_Plan.md` §3.4: a Container Apps environment, a container registry,
observability resources, and a secret store. No PostgreSQL, Service Bus, Blob Storage, or actual container
apps here — those need a real service to deploy before they're worth provisioning continuously-billed
resources for, and belong to the separate, still-open "Create Azure development deployment" backlog task
("Deploy the control plane and one sample service").

- `main.bicep` — subscription-scope entry point. Creates the resource group and deploys `modules/platform.bicep` into it.
- `modules/platform.bicep` — the actual resources: Log Analytics workspace, workspace-based Application
  Insights, an Azure Container Registry (Basic tier), a Key Vault (RBAC-authorized, no secrets populated by
  Bicep itself), a Container Apps environment wired to the Log Analytics workspace, and a user-assigned
  managed identity granted `AcrPull` on the registry and `Key Vault Secrets User` on the vault — an ACR
  nobody can pull from or a vault nobody can read isn't a usable baseline, so that wiring is included here
  rather than left for the deployment task to rediscover.
- `main.bicepparam` — example parameters for a `dev` environment. No secrets or account-specific values —
  the subscription/tenant come from whatever `az` session is logged in at deploy time.

## No account-specific values anywhere

Every name is derived from parameters (`environmentName`, `location`) plus a deterministic uniqueness
suffix (`uniqueString(resourceGroup().id)`) for the two resource types that need a globally unique name
(ACR, Key Vault) — same portability rule `docs/architecture/nas-deployment-profile.md` already follows.
Deploying this with a different Azure account requires no code change, only your own `az login` and
optionally a different `.bicepparam` file (e.g. `staging.bicepparam`).

## Deploying

```bash
az login
az deployment sub create \
  --location northeurope \
  --template-file main.bicep \
  --parameters main.bicepparam
```

**`westeurope` doesn't work on every subscription** — confirmed against a real free/trial subscription
during M00.5, `az deployment sub what-if` rejected it with `RequestDisallowedByAzure` ("the selected
region is currently not accepting new customers"), a subscription-tier restriction, not a template bug.
`northeurope` is confirmed working and is the default; if it stops working too, `az account list-locations
-o table` shows what's available for your subscription.

## Validating without deploying

```bash
bicep build main.bicep          # compiles to ARM JSON, catches syntax/type errors
bicep lint main.bicep           # style/best-practice diagnostics
bicep build-params main.bicepparam
az deployment sub what-if --location northeurope --template-file main.bicep --parameters main.bicepparam
```

The first three are pure compile/lint checks against the Bicep CLI's bundled resource-type schema, no
Azure login needed. `what-if` does need a real login (`az login`) but creates nothing — it asks Azure to
evaluate the template for real (resolving references, checking region/quota/naming constraints) and report
what it *would* do.

## Verified

Actually deployed for real (M00.5, `az deployment sub create`, not just `what-if`) — see "Azure
development deployment" below for the full picture, including two real ARM-level bugs `what-if` didn't
catch that only surfaced on a real deployment.

## Azure development deployment (`deploy.bicep`)

`deploy.bicep` deploys the full control plane on top of this baseline — `gateway`, `identity-workspace`,
`site-registry`, everything M00.4 actually built — for the separate "Azure development deployment"
backlog task ("Deploy the control plane and one sample service"). It composes `modules/platform.bicep`
(unchanged) with a new `modules/services.bicep`: a PostgreSQL Flexible Server (one database per context,
per `docs/adr/0005-context-level-data-isolation.md`), an Azure Service Bus namespace/topic backing Dapr's
pubsub component (`docs/adr/0002-dapr-pubsub-abstraction.md`'s Azure row), and the three Container Apps
themselves, Dapr-enabled and wired exactly like `infrastructure/compose/docker-compose.yml`'s local
topology (same app-ids, same appPort pattern, gateway is the only one with external ingress).

```bash
az login
az deployment sub create \
  --location northeurope \
  --template-file deploy.bicep \
  --parameters deploy.bicepparam \
  --parameters ghcrToken=$env:GHCR_TOKEN postgresAdminPassword=$env:PG_ADMIN_PASSWORD
```

`ghcrToken` (a GitHub PAT with `read:packages`, for pulling the private GHCR images
`.github/workflows/container-build.yml` publishes) and `postgresAdminPassword` have no defaults and are
never committed — supply them at deploy time. Pulls images from GHCR rather than mirroring them into the
ACR this baseline also provisions — that registry stays available for a future in-Azure build pipeline,
but duplicating already-working, already-tested images into it here has no functional benefit.

### Tightening database access

Both Container Apps initially point at the Postgres **admin** login (ARM has no Postgres role/user
resource type, so Bicep can't create `svc_access`/`svc_sites` directly) — functionally correct (admin has
access to every database) but not what ADR 0005 actually specifies. `scripts/init-postgres-databases.sh`
closes that gap:

```bash
./scripts/init-postgres-databases.sh <server-fqdn> <admin-username> <admin-password>
```

This needs a firewall rule allowing your IP first (`az postgres flexible-server firewall-rule create`,
remove it again after), and creates `svc_access`/`svc_sites` with fresh random passwords, printed once.
Then, for each context: update its Key Vault secret with the new connection string
(`az keyvault secret set` — needs `Key Vault Secrets Officer` on the vault; subscription **Owner does
not** automatically grant Key Vault *data-plane* RBAC, only the control-plane actions ARM-based secret
resources use — a real gap hit during this exercise, fixed by self-assigning the role), then force a
**new revision**, not just a restart (`az containerapp update --revision-suffix <x>` — a bare `revision
restart` reuses the already-resolved Key Vault secret value rather than re-fetching it).

### Verified

Deployed for real (M00.5) and exercised end-to-end through the live gateway with a real Entra ID token:
`POST`/`GET /workspaces`, `POST /sites` (which exercises site-registry's Dapr service-invocation
membership check against identity-workspace, and its transactional outbox). The Dapr pubsub component's
managed-identity auth against Azure Service Bus — the single highest-risk, least-verifiable-in-advance
piece of this whole deployment — worked on the first real attempt (`POST .../v1.0/publish/pubsub/site-events`
→ `204`, confirmed in the site-registry container logs). After tightening to per-context Postgres roles
(above), re-verified the same requests still succeed on `svc_access`/`svc_sites` rather than admin creds.

Two real bugs surfaced only by an actual deployment, not `what-if`:
- **Key Vault name over 24 characters** (`kv-telumera-dev-<13-char-uniqueString>` = 29 chars) —
  `what-if` reported this resource as valid; the real deployment rejected it with `VaultNameNotValid`.
  Fixed by dropping the `telumera-` segment and truncating the uniqueness suffix to 8 chars specifically
  for the vault name.
- **Container App name over 32 characters** (`ca-telumera-identity-workspace-dev` = 35 chars) — same
  story, caught only on the real deployment (`ContainerAppInvalidName`). Fixed by dropping `telumera-`
  from Container App names (the resource group is already telumera-scoped) and tightening
  `deploy.bicep`'s `environmentName` to `@maxLength(9)` so this can't recur with a longer environment
  name later.

Also fixed along the way: `scripts/init-postgres-databases.sh` originally ran `docker run` without `-i`,
so its heredoc SQL never reached `psql`'s stdin — it silently created nothing while reporting success.
And `ALTER DATABASE ... OWNER TO` only changes the *database's* owner, not tables already created inside
it by a different role (e.g. by `Database.Migrate()` running once as admin before the script runs) —
needs `REASSIGN OWNED BY` too.

Torn down after M00.5 testing wraps up (`az group delete --name rg-telumera-dev`) — this was a
deliberately throwaway validation run, not a persistent environment.
