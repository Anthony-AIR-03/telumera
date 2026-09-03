# Analytics module runbook

> Status: M01.8. Covers operating the Product Analytics pipeline
> (`services/event-collector` → Dapr pub/sub → `services/analytics` → ClickHouse → aggregation → the
> query API + live hub) in the self-hosted deployment. Structure mirrors `local-environment.md`.

## The pipeline, end to end

```
browser SDK ──POST /v1/events──▶ event-collector ──channel──▶ CloudEvent ──"collector-events"──▶
  analytics /subscriptions/collector-events ──▶ EventProcessor (normalize, enrich, dedupe, GeoIP)
  ──▶ ClickHouse telumera_analytics.events ──▶ AnalyticsAggregationService (60s) ──▶ sessions +
  daily_*_rollup ──▶ GET /sites/{id}/analytics/* (via gateway)

side paths:
  EventProcessor ──▶ Redis live:{siteId} ──▶ LiveHub /hubs/live (SignalR, direct, not via gateway)
  event-collector QualityCounters ──"quality-events" (60s)──▶ analytics ──▶ event_quality_daily
```

## Health checks

| Check | How |
|---|---|
| Service up | `GET http://<svc>/health/live` and `/health/ready` on `event-collector` / `analytics` |
| Ingestion flowing | `GET /sites/{id}/analytics/quality?from&to` — `accepted` for today rising |
| Pipeline reconciles | `dotnet run --project tools/reconciliation-report -- --date <utc-date>` → accepted ≈ processed ≈ stored |
| Aggregation running | `analytics` logs an "Analytics aggregation tick complete" line ~every 60s |
| Live panel | open a site's analytics screen; the "Live" dot is green and the count reacts within ~5s |
| Dead-letter empty | `dotnet run --project tools/dead-letter-recovery -- list` |

## Common failures

- **Sidecar orphaned after recreating an app container.** Recreating `analytics` / `event-collector`
  alone leaves its `network_mode: service:x` Dapr sidecar bound to a dead namespace — pub/sub silently
  stops, and `localhost:3500` refuses inside the new app container so every Dapr service-invocation
  path 500s (on the NAS this surfaced as the whole dashboard showing "Couldn't load" and the live
  panel offline). Always recreate both together:
  `docker compose up -d --force-recreate analytics analytics-dapr`. (Hit in M01.3/M01.4/M01.6, and on
  the first NAS deploy.) `deploy-nas.yml` force-recreates all 5 `*-dapr` sidecars after every `up -d`
  for this reason.
- **ClickHouse down / unreachable.** `EventProcessor` throws after committing the idempotency marker →
  the event is redelivered by Dapr (5 retries, exp backoff) → then dead-lettered to
  `dlq-analytics-collector-events`. Fix ClickHouse, then
  `dotnet run --project tools/dead-letter-recovery -- replay`. `reconciliation-report` will show
  `processed > stored` until the replay lands.
- **Aggregation watermark stuck.** Symptom: `sessions` / rollups stop updating while `events` keeps
  growing; the tick log shows the same `PreviousWatermark` every time or logs an error. Inspect
  `aggregation_checkpoints` in `telumera_analytics_control`; to force a full rescan, set its
  `watermark` back (e.g. `UPDATE aggregation_checkpoints SET watermark = '1970-01-01' WHERE job_name =
  'session-and-rollup-aggregation'`) — safe, just does more work on the next tick.
- **Live count always 0 / hub won't connect.** Check Redis is up (`docker compose exec redis
  redis-cli -a "$REDIS_PASSWORD" ping`); check the browser console for a 401 on the WS handshake
  (token expiry — reload) or a CORS error (`Cors__AllowedOrigins` on `analytics` must list the
  dashboard origin, and NPM's **Websockets Support** toggle must be on for `hubs.telumera.nl`).
- **Geography empty.** Expected until a database is installed — see below.

## GeoIP database

`services/analytics` reads `GeoIp:DatabasePath` (`/geoip/GeoLite2-Country.mmdb`). Absent ⇒ a startup
warning and `events.country` stays empty; the pipeline is otherwise fine.

- Install / refresh: `cd infrastructure/compose && ./scripts/refresh-geoip.sh` (needs
  `MAXMIND_LICENSE_KEY` in `.env`; or `./scripts/refresh-geoip.sh dbip` for the no-account DB-IP Lite).
- Refresh **monthly**. Then `docker compose restart analytics analytics-dapr` — the reader reopens the
  file on start.
- **No historical backfill**: geography is correct from install forward only (the source IP is never
  stored — `docs/analytics/definitions-and-privacy-model.md` §7).

## Retention

- Raw `events`: **90-day TTL**, enforced by ClickHouse background merges, applied idempotently in
  `EnsureSchemaAsync`. No cleanup job. (`definitions-and-privacy-model.md` §8.)
- `sessions` and `daily_*_rollup`: **no TTL** — aggregate retention is indefinite by default.
- `event_quality_daily`: no TTL; it's small (a handful of rows per site per day).
- Per-site configurable retention is **not built** — a global 90 days for now.

## Data-quality dashboard

`event_quality_daily` (`SummingMergeTree`) accumulates `collector.quality.v1` delta batches; `bot` /
`delayed` are computed from `events` at query time; `deadLetterQueueDepth` is a live gauge from
`DeadLetterProbe`. If the quality screen looks stale, check the `quality-events` subscription is
delivering (`analytics` debug log "Applied N collector.quality.v1 deltas") and that
`Quality__RabbitMqUser/Password` are set so the DLQ probe can authenticate.

## ClickHouse schema migrations

`EnsureSchemaAsync` (runs at every `analytics` start) is the only migration path — `CREATE TABLE IF
NOT EXISTS` plus idempotent `ALTER`s. A change to a `ReplacingMergeTree` **ORDER BY key** can't be an
`ALTER`, so those are done drop-and-recompute from the durable `sessions` table
(`docs/runbooks/rollback-and-migrations.md`). No manual SQL — a normal image deploy applies it.

- **`daily_acquisition_rollup` → `referrer_host` dimension** (`analytics-referrer-source`): first start
  of the new image drops the old-schema table and rebuilds it from every existing session. Expect two
  `warn` lines from `ClickHouseWriter` in the `analytics` logs ("Migrating daily_acquisition_rollup…"
  / "rebuilt from `sessions`: N rollup rows written"). Fires once per environment; a fresh deploy skips
  it (table doesn't exist yet). Only this rollup is rebuilt — the aggregation watermark is not rewound.
  Rolling `analytics` back past this needs a manual `DROP TABLE daily_acquisition_rollup` (the older
  image's incremental INSERT is positional and errors against the wider table; reads still work).

## Deploying to the NAS (`telumera.nl`)

Deploys are automated: every push to `main` triggers **Container Build** (builds + pushes the GHCR
images) then **Deploy to NAS** (`.github/workflows/deploy-nas.yml`, a self-hosted runner on the NAS
that pulls those images and runs `docker compose … up -d`). One-time runner/NAS setup and the
pipeline diagram are in **`nas-runner-setup.md`**. Manual deploy or rollback: **Actions → Deploy to
NAS → Run workflow** with an `image_tag`.

Prereqs (one-time): `telumera.nl` in Cloudflare with `*.telumera.nl` → the existing `<tunnel>`;
tunnel route `*.telumera.nl` → `Nginx Proxy Manager`; `.env.nas` in
`$NAS_DEPLOY_DIR/infrastructure/compose/` on the NAS (from `infrastructure/compose/.env.nas.example`);
the three Entra app registrations have `https://telumera.nl` and `https://telumera.nl/auth-popup.html`
as redirect URIs. `$NAS_DEPLOY_DIR` is the `NAS_DEPLOY_DIR` repo Variable — see `nas-runner-setup.md`.

To deploy by hand from the NAS deploy directory (what the workflow does):

```bash
cd "$NAS_DEPLOY_DIR/infrastructure/compose"
docker compose -f docker-compose.yml -f docker-compose.nas.yml --env-file .env.nas pull
docker compose -f docker-compose.yml -f docker-compose.nas.yml --env-file .env.nas up -d --remove-orphans
./scripts/refresh-geoip.sh          # once, then monthly — NOT run by the deploy
```

`scripts/health-check.sh` is a local-dev tool — it probes `localhost:` ports the NAS override
unpublishes (`ports: !reset []`), so its RabbitMQ/MinIO/Dapr checks report false failures on the NAS.
Use `docker compose … ps` plus the `/health/ready` checks the deploy workflow runs instead.

Then in **Nginx Proxy Manager** (LAN-only admin), add four Proxy Hosts, all on the shared external
`npm` network, Force SSL + HTTP/2, Block Common Exploits:

| Host | → container : port | Websockets |
|---|---|---|
| `telumera.nl`, `www.telumera.nl` | `dashboard-web` : 80 | off |
| `api.telumera.nl` | `gateway` : 8080 | off |
| `hubs.telumera.nl` | `analytics` : 8080 | **on** |
| `collect.telumera.nl` | `event-collector` : 8080 | off |

First deploy is a **vertical slice**: register `anthony-air.nl` as a site through
`https://api.telumera.nl` with a real token, paste the generated snippet into the portfolio, and watch
one real page view traverse to the dashboard + the live panel + `reconciliation-report`.

## Backup & restore

- `./scripts/backup.sh [BACKUP_DIR]` (cron on the NAS) — `pg_dump` per context DB, every
  `telumera_analytics` table as `FORMAT Native`, RabbitMQ definitions, plus a row-count manifest.
  Keeps `BACKUP_RETAIN` (default 7). MinIO needs its own off-box target (not automated).
- `./scripts/restore-test.sh <backup-dir>` — spins throwaway PG + ClickHouse containers, restores the
  Postgres dumps and validates the ClickHouse Native exports against the manifest, tears down.
- **Full ClickHouse restore** (real incident): bring `analytics` up so `EnsureSchemaAsync` creates the
  tables, then `gzip -dc ch_<tbl>.native.gz | clickhouse-client --database telumera_analytics --query
  "INSERT INTO <tbl> FORMAT Native"` per table.

## Rollback

Per `rollback-and-migrations.md`: redeploy the previous `sha-…` image tag; **do not** run migrations
backward. `analytics` auto-migrates on startup, so every migration must be safe for the previous
image too (expand/contract). The live projection and `event_quality_daily` hold no irreplaceable
state — a rollback that drops them loses at most the current 5-minute live window and same-day quality
deltas.
