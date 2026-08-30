#!/usr/bin/env bash
# Creates the per-context PostgreSQL role for each context's database on the Azure Flexible Server
# deploy.bicep provisions, per docs/adr/0005-context-level-data-isolation.md — mirrors
# infrastructure/compose/db-init/postgres/00-init-context-databases.sh's exact roles/databases, just
# against a remote server instead of an initdb script. ARM has no Postgres role resource type, so this
# can't be expressed in Bicep itself; ADR 0005 explicitly calls for it to be "scripted" instead.
#
# services/services.bicep's Container Apps start out pointed at the Postgres admin login (the
# only credential that exists before this script runs) — admin access to a database it doesn't own is
# still valid access, so the deployment is already functional without this step. Run this afterward to
# tighten it to the intended per-context isolation, then update the two Key Vault secrets and restart
# the two Container Apps to pick up the new connection strings (see README.md).
#
# Requires Docker (uses the postgres:16-alpine image as a disposable psql client — no local psql
# install needed, matching what's already used for local dev). No account-specific values baked in:
# every value is a required argument.
#
# Usage:
#   ./init-postgres-databases.sh <server-fqdn> <admin-username> <admin-password>
set -euo pipefail

SERVER_FQDN="${1:?Usage: $0 <server-fqdn> <admin-username> <admin-password>}"
ADMIN_USERNAME="${2:?Usage: $0 <server-fqdn> <admin-username> <admin-password>}"
ADMIN_PASSWORD="${3:?Usage: $0 <server-fqdn> <admin-username> <admin-password>}"

# context_key:database_name:role_name
CONTEXTS=(
  "identity-workspace:telumera_access:svc_access"
  "site-registry:telumera_sites:svc_sites"
)

echo "Generated per-context passwords (save these — printed once, not stored anywhere):"

for entry in "${CONTEXTS[@]}"; do
  IFS=':' read -r context db role <<< "$entry"
  # postgres:16-alpine has no openssl; /dev/urandom + base64 (busybox coreutils) needs no extra tools.
  role_password=$(docker run --rm postgres:16-alpine sh -c "head -c 24 /dev/urandom | base64" | tr -d '/+=\n')

  echo "provisioning role '${role}' owning database '${db}' for context '${context}'"

  # -i is required for the heredoc below to actually reach psql's stdin — without it docker run has
  # no attached stdin, psql sees EOF immediately, executes nothing, and (silently, with no error) exits
  # 0, which this script previously didn't distinguish from real success.
  docker run --rm -i -e PGPASSWORD="$ADMIN_PASSWORD" postgres:16-alpine psql \
    -h "$SERVER_FQDN" -p 5432 -U "$ADMIN_USERNAME" -d "$db" -v ON_ERROR_STOP=1 <<-EOSQL
    DO \$\$
    BEGIN
      IF NOT EXISTS (SELECT FROM pg_roles WHERE rolname = '${role}') THEN
        CREATE ROLE ${role} LOGIN PASSWORD '${role_password}';
      ELSE
        ALTER ROLE ${role} PASSWORD '${role_password}';
      END IF;
    END
    \$\$;
    ALTER DATABASE ${db} OWNER TO ${role};
    GRANT ALL ON SCHEMA public TO ${role};
    REVOKE ALL ON DATABASE ${db} FROM PUBLIC;
EOSQL

  # ALTER DATABASE ... OWNER TO only changes the database object's own owner, not any tables already
  # created inside it (e.g. by Database.Migrate() running once as the admin before this script runs
  # against an existing deployment) — REASSIGN OWNED transfers those too. A no-op on a genuinely fresh
  # database with nothing to reassign.
  docker run --rm -i -e PGPASSWORD="$ADMIN_PASSWORD" postgres:16-alpine psql \
    -h "$SERVER_FQDN" -p 5432 -U "$ADMIN_USERNAME" -d "$db" -v ON_ERROR_STOP=1 <<-EOSQL
    REASSIGN OWNED BY ${ADMIN_USERNAME} TO ${role};
EOSQL

  echo "  ${context}: role=${role} password=${role_password}"
done

echo
echo "Next: update the two Key Vault secrets with connection strings using these roles instead of"
echo "the admin login, then restart both Container Apps to pick up the change (see README.md's"
echo "'Tightening database access' section)."
