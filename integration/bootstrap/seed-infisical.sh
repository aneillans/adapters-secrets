#!/usr/bin/env bash
# Bootstraps a fresh self-hosted Infisical instance to the point where the
# adapter's Universal-Auth machine identity can read/write secrets, then seeds
# the smoke secrets. Prints { clientId, clientSecret, projectId } as JSON to
# stdout; progress goes to stderr.
#
# Sequence (all verified against current Infisical APIs):
#   1. POST /api/v1/admin/bootstrap          -> org id + admin machine-identity token
#   2. POST /api/v1/projects                 -> project id (+ default dev/staging/prod envs)
#   3. POST /api/v1/identities               -> machine identity
#   4. POST .../universal-auth/identities/ID -> attach Universal Auth (clientId)
#   5. POST .../client-secrets               -> clientSecret
#   6. POST /api/v1/projects/ID/memberships/identities/ID -> add to project as admin
#   7. UA login + POST /api/v3/secrets/raw/NAME -> seed secrets
#
# admin/bootstrap only succeeds ONCE per instance. To re-run, recreate the stack
# with `docker compose down -v` first.
#
# Required env: INF_URL, INFISICAL_ADMIN_EMAIL, INFISICAL_ADMIN_PASSWORD,
#               INFISICAL_PROJECT_NAME, INFISICAL_ENVIRONMENT, INFISICAL_SECRET_PATH,
#               SMOKE_SEED_JSON
set -euo pipefail

: "${INF_URL:?}" "${INFISICAL_ADMIN_EMAIL:?}" "${INFISICAL_ADMIN_PASSWORD:?}" \
  "${INFISICAL_PROJECT_NAME:?}" "${INFISICAL_ENVIRONMENT:?}" "${SMOKE_SEED_JSON:?}"
SECRET_PATH="${INFISICAL_SECRET_PATH:-/}"
INF="${INF_URL%/}"

log() { echo "[inf-seed]" "$@" >&2; }

# api METHOD PATH [JSON_BODY] [BEARER] -> response body on stdout, fails on non-2xx
api() {
  local method="$1" path="$2" body="${3:-}" bearer="${4:-}"
  local args=(-sS -X "$method" "${INF}${path}" -H 'Content-Type: application/json')
  [ -n "$bearer" ] && args+=(-H "Authorization: Bearer ${bearer}")
  [ -n "$body" ] && args+=(-d "$body")
  local out code
  out="$(curl "${args[@]}" -w $'\n%{http_code}')"
  code="${out##*$'\n'}"; out="${out%$'\n'*}"
  if [ "$code" -lt 200 ] || [ "$code" -ge 300 ]; then
    log "ERROR ${method} ${path} -> ${code}: ${out}"; exit 1
  fi
  printf '%s' "$out"
}

# 1. admin bootstrap
log "bootstrapping instance admin ${INFISICAL_ADMIN_EMAIL}"
boot="$(api POST /api/v1/admin/bootstrap \
  "$(jq -n --arg e "$INFISICAL_ADMIN_EMAIL" --arg p "$INFISICAL_ADMIN_PASSWORD" --arg o "$INFISICAL_PROJECT_NAME Org" \
      '{email:$e,password:$p,organization:$o}')")"
ORG_ID="$(jq -r '.organization.id' <<<"$boot")"
ADMIN_TOKEN="$(jq -r '.identity.credentials.token' <<<"$boot")"
[ -n "$ORG_ID" ] && [ "$ORG_ID" != null ] || { log "no organization id in bootstrap response"; exit 1; }
[ -n "$ADMIN_TOKEN" ] && [ "$ADMIN_TOKEN" != null ] || { log "no admin token in bootstrap response"; exit 1; }

# 2. project (with default dev/staging/prod environments)
log "creating project '${INFISICAL_PROJECT_NAME}'"
proj="$(api POST /api/v1/projects \
  "$(jq -n --arg n "$INFISICAL_PROJECT_NAME" '{projectName:$n,type:"secret-manager",shouldCreateDefaultEnvs:true}')" \
  "$ADMIN_TOKEN")"
PROJECT_ID="$(jq -r '.project.id // .project._id' <<<"$proj")"
[ -n "$PROJECT_ID" ] && [ "$PROJECT_ID" != null ] || { log "no project id in response: $proj"; exit 1; }
log "project id ${PROJECT_ID}"

# 3. machine identity
ident="$(api POST /api/v1/identities \
  "$(jq -n --arg o "$ORG_ID" '{name:"smoke-identity",organizationId:$o,role:"member"}')" "$ADMIN_TOKEN")"
IDENTITY_ID="$(jq -r '.identity.id' <<<"$ident")"
log "identity id ${IDENTITY_ID}"

# 4. attach Universal Auth (open trusted-IP ranges for local use) -> clientId
ua="$(api POST "/api/v1/auth/universal-auth/identities/${IDENTITY_ID}" \
  '{"accessTokenTTL":2592000,"accessTokenMaxTTL":2592000,"accessTokenNumUsesLimit":0,"clientSecretTrustedIps":[{"ipAddress":"0.0.0.0/0"},{"ipAddress":"::/0"}],"accessTokenTrustedIps":[{"ipAddress":"0.0.0.0/0"},{"ipAddress":"::/0"}]}' \
  "$ADMIN_TOKEN")"
CLIENT_ID="$(jq -r '.identityUniversalAuth.clientId' <<<"$ua")"

# 5. client secret
cs="$(api POST "/api/v1/auth/universal-auth/identities/${IDENTITY_ID}/client-secrets" \
  '{"description":"smoke","ttl":0,"numUsesLimit":0}' "$ADMIN_TOKEN")"
CLIENT_SECRET="$(jq -r '.clientSecret' <<<"$cs")"
[ -n "$CLIENT_ID" ] && [ "$CLIENT_ID" != null ] || { log "no clientId"; exit 1; }
[ -n "$CLIENT_SECRET" ] && [ "$CLIENT_SECRET" != null ] || { log "no clientSecret"; exit 1; }

# 6. add identity to the project as admin (can read + write secrets)
api POST "/api/v1/projects/${PROJECT_ID}/memberships/identities/${IDENTITY_ID}" \
  '{"role":"admin"}' "$ADMIN_TOKEN" >/dev/null
log "identity added to project as admin"

# 7. log in AS the identity (validates the credentials) then seed secrets
login="$(api POST /api/v1/auth/universal-auth/login \
  "$(jq -n --arg c "$CLIENT_ID" --arg s "$CLIENT_SECRET" '{clientId:$c,clientSecret:$s}')")"
IDENT_TOKEN="$(jq -r '.accessToken' <<<"$login")"
[ -n "$IDENT_TOKEN" ] && [ "$IDENT_TOKEN" != null ] || { log "universal-auth login failed"; exit 1; }
log "identity login ok; seeding secrets into env '${INFISICAL_ENVIRONMENT}' path '${SECRET_PATH}'"

while IFS=$'\t' read -r key value; do
  log "  + ${key}"
  api POST "/api/v3/secrets/raw/${key}" \
    "$(jq -n --arg w "$PROJECT_ID" --arg e "$INFISICAL_ENVIRONMENT" --arg p "$SECRET_PATH" --arg v "$value" \
        '{workspaceId:$w,environment:$e,secretPath:$p,secretValue:$v}')" \
    "$IDENT_TOKEN" >/dev/null
done < <(jq -r 'to_entries[] | "\(.key)\t\(.value)"' <<<"$SMOKE_SEED_JSON")

jq -n --arg c "$CLIENT_ID" --arg s "$CLIENT_SECRET" --arg p "$PROJECT_ID" \
  '{clientId:$c,clientSecret:$s,projectId:$p}'
