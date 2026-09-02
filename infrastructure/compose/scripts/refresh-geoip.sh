#!/usr/bin/env bash
# Downloads/refreshes the MaxMind-format country database used by services/analytics for geography
# enrichment (docs/analytics/definitions-and-privacy-model.md §7 — country granularity only). Same
# "just curl it, no SDK" style as get-dev-token.sh / health-check.sh.
#
# The .mmdb file is never committed (.gitignore: *.mmdb) — MaxMind's GeoLite2 licence forbids
# redistribution and the file is several MB. Run this once at setup and monthly thereafter.
#
# Providers (pick with GEOIP_PROVIDER in .env, or the first arg):
#   maxmind  (default) — needs a free MaxMind account; set MAXMIND_LICENSE_KEY in .env
#   dbip               — DB-IP Lite Country, CC BY 4.0, no account/key required
#
# Usage:
#   ./refresh-geoip.sh              # provider from $GEOIP_PROVIDER, default maxmind
#   ./refresh-geoip.sh dbip         # force DB-IP Lite
set -uo pipefail
cd "$(dirname "${BASH_SOURCE[0]}")/.."

ENV_FILE=".env"
[[ -f "$ENV_FILE" ]] || ENV_FILE=".env.example"
set -a
# shellcheck disable=SC1090
source "$ENV_FILE"
set +a

PROVIDER="${1:-${GEOIP_PROVIDER:-maxmind}}"
DEST_DIR="geoip"
DEST="${DEST_DIR}/GeoLite2-Country.mmdb" # analytics always reads this path (GeoIp__DatabasePath)
TMP_DIR="$(mktemp -d)"
trap 'rm -rf "$TMP_DIR"' EXIT

mkdir -p "$DEST_DIR"

case "$PROVIDER" in
  maxmind)
    : "${MAXMIND_LICENSE_KEY:?MAXMIND_LICENSE_KEY not set in .env — create a free account at https://www.maxmind.com and generate a licence key, or run: ./refresh-geoip.sh dbip}"
    URL="https://download.maxmind.com/app/geoip_download?edition_id=GeoLite2-Country&license_key=${MAXMIND_LICENSE_KEY}&suffix=tar.gz"
    echo "Downloading MaxMind GeoLite2-Country..." >&2
    curl -fsSL "$URL" -o "$TMP_DIR/geoip.tar.gz" || { echo "Download failed (check MAXMIND_LICENSE_KEY)." >&2; exit 1; }
    tar -xzf "$TMP_DIR/geoip.tar.gz" -C "$TMP_DIR"
    FOUND="$(find "$TMP_DIR" -name '*.mmdb' | head -1)"
    ;;
  dbip)
    MONTH="$(date -u +%Y-%m)"
    URL="https://download.db-ip.com/free/dbip-country-lite-${MONTH}.mmdb.gz"
    echo "Downloading DB-IP Lite Country (${MONTH})..." >&2
    curl -fsSL "$URL" -o "$TMP_DIR/dbip.mmdb.gz" || { echo "Download failed for ${MONTH} — the current month may not be published yet; try last month." >&2; exit 1; }
    gunzip -f "$TMP_DIR/dbip.mmdb.gz"
    FOUND="$TMP_DIR/dbip.mmdb"
    ;;
  *)
    echo "Unknown provider '$PROVIDER' (expected: maxmind | dbip)." >&2
    exit 2
    ;;
esac

[[ -f "$FOUND" ]] || { echo "No .mmdb found in the downloaded archive." >&2; exit 1; }
mv "$FOUND" "$DEST"
echo "Installed $(du -h "$DEST" | cut -f1) -> $DEST" >&2
echo "Restart the analytics container to pick it up: docker compose restart analytics analytics-dapr" >&2
