#!/usr/bin/env bash
# One-command verification that the local self-hosted runtime is actually
# working — not just "containers are running", but that each dependency is
# reachable and, for Dapr, that a publish actually reaches RabbitMQ.
#
# Usage: ./health-check.sh   (run from infrastructure/compose/, or anywhere —
# it cd's to its own directory first)
set -uo pipefail
cd "$(dirname "${BASH_SOURCE[0]}")/.."

FAILED=0

pass() { echo "  OK   $1"; }
fail() { echo "  FAIL $1"; FAILED=1; }

section() { echo; echo "== $1 =="; }

# Load .env for credentials used in checks below (RabbitMQ management API,
# Redis, MinIO). Does not require .env to exist — falls back to
# .env.example's placeholder values so this script still runs (and fails
# loudly) against a stack that was never configured.
ENV_FILE=".env"
[[ -f "$ENV_FILE" ]] || ENV_FILE=".env.example"
set -a
# shellcheck disable=SC1090
source "$ENV_FILE"
set +a

section "Containers"
EXPECTED_SERVICES=(postgres clickhouse rabbitmq redis minio otel-collector dapr-placement dapr-smoke-test-sidecar)
for svc in "${EXPECTED_SERVICES[@]}"; do
  cid=$(docker compose ps -q "$svc" 2>/dev/null)
  if [[ -z "$cid" ]]; then
    fail "$svc: not running (try: docker compose up -d)"
    continue
  fi
  health=$(docker inspect --format='{{if .State.Health}}{{.State.Health.Status}}{{else}}{{.State.Status}}{{end}}' "$cid")
  if [[ "$health" == "healthy" || "$health" == "running" ]]; then
    pass "$svc: $health"
  else
    fail "$svc: $health"
  fi
done

section "PostgreSQL — per-context databases"
if docker compose exec -T postgres psql -U postgres -tAc "SELECT 1 FROM pg_database WHERE datname='telumera_access'" 2>/dev/null | grep -q 1; then
  pass "telumera_access database exists"
else
  fail "telumera_access database missing — did db-init run? (docker compose down -v && up to force it)"
fi

section "ClickHouse — per-context databases"
if docker compose exec -T clickhouse clickhouse client --user admin --password "${CLICKHOUSE_ADMIN_PASSWORD}" -q "EXISTS DATABASE telumera_analytics" 2>/dev/null | grep -q 1; then
  pass "telumera_analytics database exists"
else
  fail "telumera_analytics database missing — did db-init run?"
fi

section "RabbitMQ — management API"
if curl -sf -u "${RABBITMQ_DEFAULT_USER}:${RABBITMQ_DEFAULT_PASS}" http://localhost:15672/api/overview >/dev/null; then
  pass "management API reachable at http://localhost:15672"
else
  fail "management API not reachable"
fi

section "Redis"
if docker compose exec -T redis redis-cli -a "${REDIS_PASSWORD}" ping 2>/dev/null | grep -q PONG; then
  pass "PING -> PONG"
else
  fail "PING failed"
fi

section "MinIO"
if curl -sf http://localhost:9000/minio/health/live >/dev/null; then
  pass "live endpoint reachable at http://localhost:9000"
else
  fail "live endpoint not reachable"
fi

section "Dapr sidecar (smoke-test) + pub/sub round trip"
if curl -sf -o /dev/null -w '' http://localhost:3500/v1.0/healthz; then
  pass "sidecar healthz OK"
else
  fail "sidecar healthz not reachable — is dapr-smoke-test-sidecar up?"
fi

if curl -sf http://localhost:3500/v1.0/metadata 2>/dev/null | grep -q '"name":"pubsub"'; then
  pass "pubsub component loaded"
else
  fail "pubsub component not loaded — check RABBITMQ_CONNECTION_STRING resolution (docs/runbooks/local-environment.md)"
fi

if curl -sf -o /dev/null -w '' -X POST http://localhost:3500/v1.0/publish/pubsub/telumera.health-check.v1 \
    -H 'Content-Type: application/json' \
    -d '{"id":"health-check","tenantId":"health-check","siteId":"health-check","dataVersion":1}'; then
  pass "publish through pubsub succeeded (see RabbitMQ management UI for the resulting exchange)"
else
  fail "publish through pubsub failed"
fi

echo
if [[ $FAILED -eq 0 ]]; then
  echo "All checks passed."
else
  echo "One or more checks failed — see docs/runbooks/local-environment.md for debugging steps."
fi
exit $FAILED
