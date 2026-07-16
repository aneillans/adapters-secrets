#!/usr/bin/env bash
# Runs the SmokeTests console app against the running stack. Loads static config
# from .env, bootstrap-discovered credentials from .env.generated, and the seed
# map from seed.json, then invokes the app. Exit code = number of failed checks.
set -euo pipefail

DIR="$(cd "$(dirname "$0")" && pwd)"
cd "$DIR"

[ -f .env ] || { echo "Missing .env -- copy .env.example to .env first."; exit 2; }
[ -f .env.generated ] || { echo "Missing .env.generated -- run 'docker compose run --rm bootstrap' first."; exit 2; }

set -a
# shellcheck disable=SC1091
source .env
# shellcheck disable=SC1091
source .env.generated
set +a

export SMOKE_SEED_JSON="$(cat seed.json)"

echo "VaultWarden : ${BITWARDEN_SERVER_URL}"
echo "Infisical   : ${INFISICAL_SITE_URL} (project ${INFISICAL_PROJECT_ID}, env ${INFISICAL_ENVIRONMENT})"
echo

exec dotnet run --project SmokeTests/SmokeTests.csproj -c Release
