#!/usr/bin/env bash
# Provisions one PostgreSQL database + owning role per bounded context, per
# docs/adr/0005-context-level-data-isolation.md. Runs once, automatically, the
# first time the postgres container starts against an empty data volume (see
# https://hub.docker.com/_/postgres -> "Initialization scripts"). It will NOT
# re-run on an existing volume — to re-provision from scratch locally, run
# `docker compose down -v` first (see infrastructure/compose/README.md).
#
# All contexts share one local-dev password (APP_DB_PASSWORD) on purpose: this
# is throwaway local infrastructure, not a production credential story. Each
# role can only log in to its own database (REVOKE ALL ... FROM PUBLIC below),
# so the shared password does not weaken the no-cross-context-access rule —
# it just keeps local onboarding to one password instead of eight.
#
# Add a new context by adding one line below; nothing else needs to change.
set -euo pipefail

# context_key:database_name:role_name
CONTEXTS=(
  "identity-workspace:telumera_access:svc_access"
  "site-registry:telumera_sites:svc_sites"
  "deployments:telumera_deployments:svc_deployments"
  "errors:telumera_errors:svc_errors"
  "conversions:telumera_conversions:svc_conversions"
  "ai-insights:telumera_ai:svc_ai"
  "alerting:telumera_alerting:svc_alerting"
  "notifications:telumera_notifications:svc_notifications"
)

for entry in "${CONTEXTS[@]}"; do
  IFS=':' read -r context db role <<< "$entry"
  echo "provisioning postgres database '${db}' (role '${role}') for context '${context}'"

  psql -v ON_ERROR_STOP=1 --username "$POSTGRES_USER" <<-EOSQL
    CREATE ROLE ${role} LOGIN PASSWORD '${APP_DB_PASSWORD}';
    CREATE DATABASE ${db} OWNER ${role};
    REVOKE ALL ON DATABASE ${db} FROM PUBLIC;
EOSQL
done
