#!/usr/bin/env bash
# Gets a real Entra ID access token for manual testing of identity-workspace/site-registry's
# protected endpoints, via the OAuth2 device code flow (plain HTTP — no MSAL/SDK dependency,
# same "just curl it" style as health-check.sh). Uses the separate "Telumera CLI Test Client"
# app registration (public client, device-code flow enabled) — see
# docs/runbooks/local-environment.md's "Auth" section for why this exists instead of a real
# dashboard sign-in flow.
#
# Usage: ./get-dev-token.sh   (prints instructions + the code to stderr, the access token alone
# to stdout once sign-in completes, so `export TELUMERA_TEST_ACCESS_TOKEN=$(./get-dev-token.sh)`
# works without instructions ending up in the token variable)
set -uo pipefail
cd "$(dirname "${BASH_SOURCE[0]}")/.."

ENV_FILE=".env"
[[ -f "$ENV_FILE" ]] || ENV_FILE=".env.example"
set -a
# shellcheck disable=SC1090
source "$ENV_FILE"
set +a

: "${AZURE_AD_TENANT_ID:?AZURE_AD_TENANT_ID not set in .env}"
: "${AZURE_AD_API_CLIENT_ID:?AZURE_AD_API_CLIENT_ID not set in .env}"
: "${AZURE_AD_TEST_CLIENT_ID:?AZURE_AD_TEST_CLIENT_ID not set in .env — register the Telumera CLI Test Client app first (docs/runbooks/local-environment.md)}"

# Extracts a top-level string/number field from a flat (non-nested) JSON object — avoids a
# hard dependency on jq, which isn't guaranteed present in every dev environment this repo
# targets (self-hosted NAS, Windows Git Bash, etc.). Tries a quoted string value first (stops
# only at the closing quote, so commas inside the value — e.g. device-code flow's "message"
# field — don't truncate it), then falls back to a bare numeric value.
json_field() {
  local value
  value=$(echo "$1" | sed -n "s/.*\"$2\":\"\([^\"]*\)\".*/\1/p" | head -1)
  if [[ -n "$value" ]]; then
    echo "$value"
    return
  fi
  echo "$1" | sed -n "s/.*\"$2\":\([0-9.eE+-]*\).*/\1/p" | head -1
}

SCOPE="api://${AZURE_AD_API_CLIENT_ID}/access_as_user"

DEVICE_RESPONSE=$(curl -sf -X POST \
  "https://login.microsoftonline.com/${AZURE_AD_TENANT_ID}/oauth2/v2.0/devicecode" \
  -d "client_id=${AZURE_AD_TEST_CLIENT_ID}" \
  -d "scope=${SCOPE}")

if [[ -z "$DEVICE_RESPONSE" ]]; then
  echo "Failed to start the device code flow — check AZURE_AD_TENANT_ID/AZURE_AD_TEST_CLIENT_ID in .env." >&2
  exit 1
fi

DEVICE_CODE=$(json_field "$DEVICE_RESPONSE" "device_code")
MESSAGE=$(json_field "$DEVICE_RESPONSE" "message")
INTERVAL=$(json_field "$DEVICE_RESPONSE" "interval")
EXPIRES_IN=$(json_field "$DEVICE_RESPONSE" "expires_in")
INTERVAL=${INTERVAL:-5}
EXPIRES_IN=${EXPIRES_IN:-900}

echo "$MESSAGE" >&2
echo >&2

DEADLINE=$((SECONDS + EXPIRES_IN))
while (( SECONDS < DEADLINE )); do
  sleep "$INTERVAL"

  TOKEN_RESPONSE=$(curl -s -X POST \
    "https://login.microsoftonline.com/${AZURE_AD_TENANT_ID}/oauth2/v2.0/token" \
    -d "grant_type=urn:ietf:params:oauth:grant-type:device_code" \
    -d "client_id=${AZURE_AD_TEST_CLIENT_ID}" \
    -d "device_code=${DEVICE_CODE}")

  ERROR=$(json_field "$TOKEN_RESPONSE" "error")

  if [[ -z "$ERROR" ]]; then
    ACCESS_TOKEN=$(json_field "$TOKEN_RESPONSE" "access_token")
    if [[ -n "$ACCESS_TOKEN" ]]; then
      echo "Signed in." >&2
      echo "$ACCESS_TOKEN"
      exit 0
    fi
  fi

  case "$ERROR" in
    authorization_pending) continue ;;
    slow_down) INTERVAL=$((INTERVAL + 5)); continue ;;
    *)
      echo "Sign-in failed: ${ERROR:-unknown error}" >&2
      echo "$TOKEN_RESPONSE" >&2
      exit 1
      ;;
  esac
done

echo "Timed out waiting for sign-in." >&2
exit 1
