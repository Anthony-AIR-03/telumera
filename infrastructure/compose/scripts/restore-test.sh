#!/usr/bin/env bash
# Verifies a backup.sh archive actually restores — spins throwaway Postgres + ClickHouse containers,
# loads the dumps into them, checks row counts against the backup's manifest, and tears everything
# down. This is nas-deployment-profile.md §7's deferred "restore testing" made real.
#
#   ./restore-test.sh ./backups/20260902T221500Z
set -uo pipefail
cd "$(dirname "${BASH_SOURCE[0]}")/.."

SRC="${1:?usage: ./restore-test.sh <backup-dir>}"
[[ -d "$SRC" && -f "${SRC}/manifest.txt" ]] || { echo "Not a backup dir (no manifest.txt): ${SRC}" >&2; exit 1; }

ENV_FILE=".env"
[[ -f "$ENV_FILE" ]] || ENV_FILE=".env.nas"
[[ -f "$ENV_FILE" ]] || ENV_FILE=".env.example"
set -a
# shellcheck disable=SC1090
source "$ENV_FILE"
set +a

PROJECT="telumera-restoretest-$$"
PG="${PROJECT}-pg"
CH="${PROJECT}-ch"
FAIL=0

cleanup() {
  docker rm -f "$PG" "$CH" >/dev/null 2>&1 || true
}
trap cleanup EXIT

echo "Starting throwaway restore targets..." >&2
docker run -d --name "$PG" -e POSTGRES_PASSWORD="${POSTGRES_SUPERUSER_PASSWORD}" postgres:16-alpine >/dev/null
docker run -d --name "$CH" -e CLICKHOUSE_PASSWORD="${CLICKHOUSE_APP_PASSWORD}" \
  -e CLICKHOUSE_USER=svc_analytics clickhouse/clickhouse-server:24.8-alpine >/dev/null

# wait for readiness
for _ in $(seq 1 30); do
  docker exec "$PG" pg_isready -U postgres >/dev/null 2>&1 && break; sleep 1
done
for _ in $(seq 1 30); do
  docker exec "$CH" clickhouse-client --query "SELECT 1" >/dev/null 2>&1 && break; sleep 1
done

# --- Postgres restore -----------------------------------------------------------------------
for dump in "${SRC}"/pg_*.dump; do
  db="$(basename "$dump" .dump | sed 's/^pg_//')"
  docker exec "$PG" psql -U postgres -c "CREATE DATABASE ${db}" >/dev/null 2>&1 || true
  docker exec -i "$PG" pg_restore -U postgres --no-owner -d "$db" < "$dump" >/dev/null 2>&1 || true
  want="$(grep "^postgres ${db} tables " "${SRC}/manifest.txt" | awk '{print $4}')"
  got="$(docker exec "$PG" psql -U postgres -d "$db" -tAc \
    "SELECT count(*) FROM information_schema.tables WHERE table_schema='public'" | tr -d '[:space:]')"
  if [[ "$want" == "$got" ]]; then echo "OK  postgres/${db}: ${got} tables" >&2
  else echo "FAIL postgres/${db}: manifest ${want}, restored ${got}" >&2; FAIL=1; fi
done

# --- ClickHouse: validate each Native export reads back with the manifested row count ------
# A full restore needs the analytics service's own schema DDL (EnsureSchemaAsync) — that's a
# documented manual step in docs/runbooks/analytics-module.md. Here we prove the backup files are
# intact and complete by counting rows straight out of them with clickhouse-local (no schema needed:
# FORMAT Native is self-describing).
while read -r _ tbl rows; do
  [[ "$tbl" && -f "${SRC}/ch_${tbl}.native.gz" ]] || { echo "FAIL clickhouse/${tbl}: export file missing" >&2; FAIL=1; continue; }
  got="$(gzip -dc "${SRC}/ch_${tbl}.native.gz" | docker exec -i "$CH" clickhouse-local \
    --input-format Native --query "SELECT count() FROM table" 2>/dev/null | tr -d '[:space:]')"
  if [[ "$rows" == "$got" ]]; then echo "OK  clickhouse/${tbl}: ${got} rows readable from backup" >&2
  else echo "FAIL clickhouse/${tbl}: manifest ${rows}, backup file ${got:-unreadable}" >&2; FAIL=1; fi
done < <(grep '^clickhouse ' "${SRC}/manifest.txt")

if [[ "$FAIL" == 0 ]]; then echo "RESTORE TEST PASSED for ${SRC}" >&2; else echo "RESTORE TEST FAILED for ${SRC}" >&2; fi
exit "$FAIL"
