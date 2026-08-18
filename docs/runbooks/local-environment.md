# Local self-hosted runtime

Covers the Docker Compose stack in `infrastructure/compose/` and the Dapr components in
`infrastructure/dapr/` — built in M00.3. This is the runbook `docs/adr/0002-dapr-pubsub-abstraction.md`
asks for so "why isn't my event arriving" doesn't become a recurring debugging cost.

## Prerequisites

- Docker Desktop (or another Docker Engine + Compose v2) running locally.
- No Dapr CLI install required — the placement service and every sidecar run as containers.

## Quick start

```bash
cd infrastructure/compose
cp .env.example .env      # fill in local values, or leave the placeholders — they're dev-only
docker compose up -d
./scripts/health-check.sh # or scripts/health-check.ps1 on Windows PowerShell
```

`docker compose down` stops the stack and keeps data. `docker compose down -v` also deletes the named
volumes — use this to force `db-init/` to re-run, since Postgres/ClickHouse only run their init scripts
against a completely empty data volume.

## What's running, and what isn't yet

Shared platform infrastructure (PostgreSQL, ClickHouse, RabbitMQ, Redis, MinIO, a Dapr placement service,
one headless `dapr-smoke-test-sidecar`), plus the first two real M00.4 services:
`identity-workspace` (`http://localhost:5101`) and `site-registry` (`http://localhost:5102`, with its own
`site-registry-dapr` sidecar). `gateway/` and the rest of `services/*` are still empty scaffolds. See
`infrastructure/compose/README.md` for the full port/credential reference and
`infrastructure/dapr/README.md` for how to add a real service's sidecar once one exists.

## Secrets model

- **Local dev secrets** (Postgres/ClickHouse/RabbitMQ/Redis/MinIO passwords): live in
  `infrastructure/compose/.env`, which is git-ignored (`.env` / `.env.*`, see repo-root `.gitignore`).
  Only `.env.example` (placeholder values) is committed. These are throwaway credentials for a
  developer's own machine — never reused in the NAS or Azure environment.
- **Dapr component secrets** (e.g. the RabbitMQ connection string used by
  `infrastructure/dapr/components/pubsub-rabbitmq.yaml`): resolved at sidecar startup from the sidecar
  container's own environment via the `local-env-secret-store` component
  (`secretstores.local.env`), so the connection string itself never appears in a committed YAML file —
  only the env var *name* does.
- **NAS / production**: per `docs/adr/0002-dapr-pubsub-abstraction.md`'s portability table, self-hosted
  production secrets are Docker secrets/environment files (not committed, not the same values as local
  dev), Azure uses Key Vault + managed identity. Neither is built yet — see
  `docs/architecture/nas-deployment-profile.md` for the NAS plan.
- **Not a secret at all**: the public browser ingestion token Site Registry issues per site is
  deliberately public by design — see `docs/adr/0006-public-browser-ingestion-tokens.md`. It is never
  stored in `.env` or a Dapr secret store.

## Auth

`identity-workspace` and `site-registry` both require a valid Entra ID bearer token (the
`access_as_user` delegated scope on the `Telumera API` app registration) on every endpoint except
`/health/live` and `/health/ready`. `AzureAd__TenantId` / `AzureAd__ClientId` must be set in `.env`
(from `AZURE_AD_TENANT_ID` / `AZURE_AD_API_CLIENT_ID`) or Microsoft.Identity.Web fails fast at startup —
see `.env.example` for where these come from in the Entra admin center.

There's no dashboard sign-in flow yet (`apps/dashboard-web/src/stores/auth.ts`'s `login()` is still a
placeholder — that's a separate, later task), so to get a real token for manual testing:

```bash
./scripts/get-dev-token.sh   # or scripts/get-dev-token.ps1 on Windows PowerShell
```

This runs the OAuth2 device code flow against a second, separate app registration (`Telumera CLI Test
Client` — public client, no secret, `AZURE_AD_TEST_CLIENT_ID` in `.env`) that exists purely for this —
open the printed URL, enter the code, and the access token prints to stdout once you've signed in. Export
it and use it directly, or feed it to the integration test:

```bash
export TELUMERA_TEST_ACCESS_TOKEN=$(./scripts/get-dev-token.sh)
curl -H "Authorization: Bearer $TELUMERA_TEST_ACCESS_TOKEN" -X POST http://localhost:5101/workspaces \
  -H "Content-Type: application/json" -d '{"Name":"Test Workspace"}'
```

## Debugging "why isn't my event arriving"

1. **Is the sidecar even up?** `curl http://localhost:3500/v1.0/healthz` (smoke-test sidecar; a real
   service's sidecar will be on a different host port — check its compose entry).
2. **Did the sidecar load the pubsub component at all?**
   `curl http://localhost:3500/v1.0/metadata` and check `"components"` includes `pubsub`. If it's
   missing, the sidecar couldn't resolve `RABBITMQ_CONNECTION_STRING` from its environment (check the
   `environment:` block on that sidecar's compose entry) or RabbitMQ wasn't healthy yet when the sidecar
   started (check `depends_on`).
3. **Did the message reach RabbitMQ at all?** Open the management UI at `http://localhost:15672`
   (credentials from `.env`) and look for the exchange/queue matching the topic name. If nothing shows
   up, the publish call itself failed — check the publishing service's logs, not Dapr.
4. **Did it land in the dead-letter queue instead?** `pubsub-rabbitmq.yaml` sets `enableDeadLetter: true`,
   so after `resiliency.yaml`'s 5 retries are exhausted, a failing message goes to a per-queue
   dead-letter queue rather than vanishing — check the management UI for a queue with a
   `-dead-letter`-style suffix. Dead-lettered messages are inspectable and replayable from there (per
   ADR 0002 — this is a hard requirement, not a nice-to-have).
5. **Is retry/backoff actually applied to this service?** `infrastructure/dapr/components/resiliency.yaml`
   only applies to the `scopes` listed in that file. A new service's Dapr `app-id` must be added to that
   list, or it silently gets Dapr's unbounded default retry behavior instead of the documented policy.

## Adding a real service's sidecar

Once a service exists under `services/<name>/` (M00.4+), give it its own sidecar the same way
`dapr-smoke-test-sidecar` is defined in `infrastructure/compose/docker-compose.yml`:

```yaml
  <name>-dapr:
    image: daprio/daprd:1.14.4
    command:
      - "./daprd"
      - "-app-id=<name>"
      - "-app-port=<the service's HTTP port>"
      - "-dapr-http-port=3500"
      - "-placement-host-address=dapr-placement:50006"
      - "-resources-path=/components"
      - "-config=/config/config.yaml"
    environment:
      RABBITMQ_CONNECTION_STRING: "amqp://${RABBITMQ_DEFAULT_USER}:${RABBITMQ_DEFAULT_PASS}@rabbitmq:5672"
    volumes:
      - ../dapr/components:/components:ro
      - ../dapr/config:/config:ro
    network_mode: "service:<name>" # shares the app container's network namespace so localhost calls work
    depends_on:
      <name>:
        condition: service_started
```

Then add `<name>` to `infrastructure/dapr/components/resiliency.yaml`'s `scopes` list.
