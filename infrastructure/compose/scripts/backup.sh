#!/usr/bin/env bash
# Backs up every stateful store in the running compose stack into a timestamped directory, plus a
# manifest of row counts that restore-test.sh checks against. Same "just docker exec it" style as
# health-check.sh. Run from a cron on the NAS.
#
#   ./backup.sh [BACKUP_DIR]        # default: ./backups
#
# Restore inventory (docs/architecture/nas-deployment-profile.md §7):
#   PostgreSQL  — pg_dump per context database (telumera_access / _sites / _analytics_control)
#   ClickHouse  — every telumera_analytics table as FORMAT Native (portable, engine-agnostic)
#   RabbitMQ    — definitions export only (exchanges/queues/bindings; message bodies are transient)
#   MinIO       — noted, not automated here (needs its own off-box target — see the profile)
set -uo pipefail
cd "$(dirname "${BASH_SOURCE[0]}")/.."

ENV_FILE=".env"
[[ -f "$ENV_FILE" ]] || ENV_FILE=".env.nas"
[[ -f "$ENV_FILE" ]] || ENV_FILE=".env.example"
set -a
# shellcheck disable=SC1090
source "$ENV_FILE"
set +a

BACKUP_ROOT="${1:-./backups}"
RETAIN="${BACKUP_RETAIN:-7}"
STAMP="$(date -u +%Y%m%dT%H%M%SZ)"
DEST="${BACKUP_ROOT}/${STAMP}"
mkdir -p "$DEST"

COMPOSE=(docker compose)
CH_TABLES=(events sessions daily_site_rollup daily_page_rollup daily_acquisition_rollup daily_technology_rollup daily_geography_rollup event_quality_daily)
PG_DATABASES=(telumera_access telumera_sites telumera_analytics_control)

echo "Backing up to ${DEST}" >&2
: > "${DEST}/manifest.txt"

# --- PostgreSQL -------------------------------------------------------------------------------
for db in "${PG_DATABASES[@]}"; do
  "${COMPOSE[@]}" exec -T -e PGPASSWORD="${POSTGRES_SUPERUSER_PASSWORD}" postgres \
    pg_dump -U postgres --no-owner --format=custom "$db" > "${DEST}/pg_${db}.dump" \
    && echo "pg ${db}: $(du -h "${DEST}/pg_${db}.dump" | cut -f1)" >&2 \
    || { echo "FAILED pg_dump ${db}" >&2; exit 1; }
done

# --- ClickHouse -----------------------------------------------------------------------------
ch() {
  "${COMPOSE[@]}" exec -T clickhouse clickhouse-client \
    --user "${CLICKHOUSE_APP_PASSWORD:+svc_analytics}" --password "${CLICKHOUSE_APP_PASSWORD}" \
    --database telumera_analytics "$@"
}
for tbl in "${CH_TABLES[@]}"; do
  ch --query "SELECT * FROM ${tbl} FORMAT Native" | gzip > "${DEST}/ch_${tbl}.native.gz" \
    || { echo "FAILED ClickHouse export ${tbl}" >&2; exit 1; }
  rows="$(ch --query "SELECT count() FROM ${tbl}" | tr -d '[:space:]')"
  echo "clickhouse ${tbl} ${rows}" >> "${DEST}/manifest.txt"
  echo "clickhouse ${tbl}: ${rows} rows" >&2
done

for db in "${PG_DATABASES[@]}"; do
  count="$("${COMPOSE[@]}" exec -T -e PGPASSWORD="${POSTGRES_SUPERUSER_PASSWORD}" postgres \
    psql -U postgres -d "$db" -tAc "SELECT count(*) FROM information_schema.tables WHERE table_schema='public'" | tr -d '[:space:]')"
  echo "postgres ${db} tables ${count}" >> "${DEST}/manifest.txt"
done

# --- RabbitMQ definitions ------------------------------------------------------------------
"${COMPOSE[@]}" exec -T rabbitmq rabbitmqctl export_definitions /tmp/defs.json >/dev/null 2>&1 \
  && "${COMPOSE[@]}" exec -T rabbitmq cat /tmp/defs.json > "${DEST}/rabbitmq-definitions.json" \
  && echo "rabbitmq definitions exported" >&2 \
  || echo "WARN: RabbitMQ definitions export failed (non-fatal — messages are transient)" >&2

echo "MinIO: not backed up here — configure an off-box target (nas-deployment-profile.md §7)." >&2

# --- Prune -----------------------------------------------------------------------------------
mapfile -t old < <(ls -1d "${BACKUP_ROOT}"/*/ 2>/dev/null | sort | head -n -"${RETAIN}")
for dir in "${old[@]}"; do
  echo "pruning old backup ${dir}" >&2
  rm -rf "$dir"
done

echo "Backup ${STAMP} complete." >&2
echo "$DEST"
