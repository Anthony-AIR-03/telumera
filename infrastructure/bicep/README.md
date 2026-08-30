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

Run against a real Azure subscription (M00.5): `az deployment sub what-if` reported 9 resource changes to
create (the resource group plus all 8 resources in `modules/platform.bicep`, including both RBAC role
assignments) with no errors — dependency ordering, cross-resource references (Container Apps environment →
Log Analytics workspace, both role assignments → the managed identity's `principalId`), and the ACR/Key
Vault uniqueness-suffixed names all resolved correctly. Not yet actually deployed (`az deployment sub
create`, not `what-if`) — that, plus deploying a real service into the resulting environment, is the
separate "Azure development deployment" backlog task.
