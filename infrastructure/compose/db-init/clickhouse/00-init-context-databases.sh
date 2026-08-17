#!/usr/bin/env bash
# Provisions one ClickHouse database + owning user per bounded context, per
# docs/adr/0005-context-level-data-isolation.md. ClickHouse has no schema
# concept as an isolation primitive, so CREATE DATABASE is the equivalent of
# Postgres's per-context database. Runs once, automatically, the first time
# the clickhouse-server container starts against an empty data volume (files
# in /docker-entrypoint-initdb.d/ are executed in name order on first boot;
# .sh files are simply run, so this uses the same bash-heredoc pattern as
# db-init/postgres/00-init-context-databases.sh to get env-var interpolation
# without relying on the .sql-file path, which is not env-substituted).
#
# All contexts share one local-dev password (CLICKHOUSE_APP_PASSWORD) for the
# same reason as the Postgres init script: throwaway local infrastructure,
# one password to onboard with, per-user GRANTs still enforce the
# per-context boundary.
#
# Add a new context by adding one line below; nothing else needs to change.
# Note: telumera_analytics, telumera_performance and telumera_sessions are
# pre-provisioned ahead of the services that will own them (M01/M07) so the
# vertical slice in docs/architecture/vision-and-scope.md §7 has a database
# to write into as soon as the Analytics Service exists.
set -euo pipefail

# context_key:database_name:user_name
CONTEXTS=(
  "analytics:telumera_analytics:svc_analytics"
  "performance:telumera_performance:svc_performance"
  "errors:telumera_errors:svc_errors_events"
  "conversions:telumera_conversions:svc_conversions_events"
  "session-insights:telumera_sessions:svc_sessions"
)

for entry in "${CONTEXTS[@]}"; do
  IFS=':' read -r context db user <<< "$entry"
  echo "provisioning clickhouse database '${db}' (user '${user}') for context '${context}'"

  clickhouse client --user admin --password "${CLICKHOUSE_PASSWORD}" --multiquery <<-EOSQL
    CREATE DATABASE IF NOT EXISTS ${db};
    CREATE USER IF NOT EXISTS ${user} IDENTIFIED WITH sha256_password BY '${CLICKHOUSE_APP_PASSWORD}';
    GRANT ALL ON ${db}.* TO ${user};
EOSQL
done
