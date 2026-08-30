# infrastructure/compose

Docker Compose stack for the local self-hosted runtime (M00.3). Brings up shared platform
infrastructure — no application services exist yet (`gateway/` and `services/*` are still empty
scaffolds), so this stack is infra-only for now. See `docs/runbooks/local-environment.md` for the
full quick-start, secrets model, and Dapr debugging guide.

```bash
cp .env.example .env   # fill in local values
docker compose up -d
./scripts/health-check.sh   # scripts/health-check.ps1 on native Windows PowerShell
```

## Services

| Service | Image | Host port(s) | Purpose |
|---|---|---|---|
| `postgres` | `postgres:16-alpine` | `5432` | Transactional/config data, one database per bounded context (`db-init/postgres/`) |
| `clickhouse` | `clickhouse/clickhouse-server:24.8-alpine` | `8123` (HTTP), `9004` (native, remapped — `9000` is MinIO's) | Event/time-series data, one database per bounded context (`db-init/clickhouse/`) |
| `rabbitmq` | `rabbitmq:3.13-management-alpine` | `5672` (AMQP), `15672` (management UI) | Backs the Dapr `pubsub` component |
| `redis` | `redis:7.4-alpine` | `6379` | Ephemeral/derived state only — never a source of truth (ADR 0003) |
| `minio` | `minio/minio:RELEASE.2024-10-13T13-34-11Z` | `9000` (S3 API), `9001` (console) | Object storage, one bucket per bounded context once a service needs one |
| `otel-collector` | `otel/opentelemetry-collector:0.112.0` | `4317` (OTLP gRPC), `4318` (OTLP HTTP) | Receives distributed traces from services + Dapr, logs them (`infrastructure/observability/`) |
| `dapr-placement` | `daprio/dapr:1.14.4` | — (internal only) | Required by every Dapr sidecar in self-hosted mode |
| `dapr-smoke-test-sidecar` | `daprio/daprd:1.14.4` | `3500` (HTTP), `50001` (gRPC) | Headless sidecar (no attached app) used to prove the `pubsub` component works before any real service exists |

Per-context database/user provisioning follows `docs/adr/0005-context-level-data-isolation.md` — see
`db-init/postgres/00-init-context-databases.sh` and `db-init/clickhouse/00-init-context-databases.sh`.
These only run once, against a fresh volume; `docker compose down -v` forces them to re-run.

## Credentials

All values come from `.env` (git-ignored; copy from `.env.example`). These are local-dev-only
credentials — see `docs/runbooks/local-environment.md`'s secrets model for how this differs from the
NAS/Azure environments.

## Seed data

`scripts/seed-demo-data.sh` (`.ps1` on Windows) inserts a demo workspace/site into Postgres and a
handful of representative page-view events into ClickHouse, so the stack has something to look at
without waiting for real services. See the script header for exactly what it creates and why it's
explicitly temporary bootstrap data, not the real Identity/Site Registry schema.
