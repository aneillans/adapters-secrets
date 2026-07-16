#!/usr/bin/env bash
# Seeds secrets into HashiCorp Vault's KV v2 engine. Vault runs in dev mode, so
# it is already unsealed and the root token is known -- no bootstrap dance, just
# write the data. Each seed key becomes a KV item at {mount}/{basePath}/{key}
# holding the value under a single "{valueKey}" field, which is exactly what the
# adapter reads back.
#
# Required env: VAULT_URL, VAULT_TOKEN, VAULT_MOUNT, VAULT_VALUE_KEY, SMOKE_SEED_JSON
#   VAULT_BASE_PATH is optional (empty = mount root).
set -euo pipefail

: "${VAULT_URL:?}" "${VAULT_TOKEN:?}" "${VAULT_MOUNT:?}" "${VAULT_VALUE_KEY:?}" "${SMOKE_SEED_JSON:?}"
BASE_PATH="${VAULT_BASE_PATH:-}"
VAULT_URL="${VAULT_URL%/}"
BASE_PATH="${BASE_PATH#/}"; BASE_PATH="${BASE_PATH%/}"

log() { echo "[vault-seed]" "$@" >&2; }
log "seeding into mount '${VAULT_MOUNT}' base path '${BASE_PATH:-/}' (value field '${VAULT_VALUE_KEY}')"

while IFS=$'\t' read -r key value; do
  path="$key"
  [ -n "$BASE_PATH" ] && path="${BASE_PATH}/${key}"
  log "  + ${VAULT_MOUNT}/${path}"
  body="$(jq -n --arg k "$VAULT_VALUE_KEY" --arg v "$value" '{data: {($k): $v}}')"
  code="$(curl -sS -o /dev/null -w '%{http_code}' \
    -H "X-Vault-Token: ${VAULT_TOKEN}" \
    -X POST "${VAULT_URL}/v1/${VAULT_MOUNT}/data/${path}" \
    -d "$body")"
  if [ "$code" -lt 200 ] || [ "$code" -ge 300 ]; then
    log "ERROR writing ${path} -> HTTP ${code}"; exit 1
  fi
done < <(jq -r 'to_entries[] | "\(.key)\t\(.value)"' <<<"$SMOKE_SEED_JSON")

log "done"
