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
  --location westeurope \
  --template-file main.bicep \
  --parameters main.bicepparam
```

## Validating without deploying

```bash
bicep build main.bicep          # compiles to ARM JSON, catches syntax/type errors
bicep lint main.bicep           # style/best-practice diagnostics
bicep build-params main.bicepparam
```

No Azure login needed for either — both are pure compile/lint checks against the Bicep CLI's bundled
resource-type schema.

## Not yet done

Actually deploying this against a real subscription and validating the outputs (registry login server, Key
Vault URI, managed identity, Container Apps environment) — that's the separate "Azure development
deployment" backlog task.
