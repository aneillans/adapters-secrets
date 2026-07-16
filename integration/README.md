# Integration smoke tests (manual, offline)

Real end-to-end checks for the **BitWarden/VaultWarden**, **Infisical**, and
**HashiCorp Vault** adapters against live, throwaway instances running in local
Docker. This is deliberately a **manual, offline** run — it stands up real
servers, seeds them, and drives the actual providers, so it never touches a
hosted service. (A non-blocking, post-merge CI variant lives at
`.github/workflows/integration-smoke.yml`.)

Everything here is disposable: the credentials baked into `docker-compose.yml`
and `.env.example` are meaningless outside your machine, the stack only listens
on `localhost`, and `docker compose down -v` wipes all state.

## What it does

1. `docker compose up` starts **VaultWarden**, **Infisical** (with its Postgres
   and Redis), and **HashiCorp Vault** (dev mode).
2. A one-shot **bootstrap** helper container:
   - VaultWarden — registers an account over HTTP, fetches a **personal API key**,
     and seeds three vault items. Because the vault is end-to-end encrypted this
     reproduces the Bitwarden client crypto in Node (verified byte-for-byte
     against the adapter's own `BitWardenCrypto`). The official `bw` CLI is *not*
     used: modern `bw` hard-refuses non-HTTPS server URLs, which is incompatible
     with the plain-HTTP local stack the adapter reads over.
   - Infisical — bootstraps the instance admin, creates a project, a **Universal
     Auth machine identity**, adds it to the project, and seeds secrets.
   - HashiCorp Vault — seeds the secrets into the KV v2 engine. Vault runs in dev
     mode (auto-unsealed, known root token), so there's no bootstrap dance and no
     discovered credentials — the adapter authenticates with the dev root token.
   - Writes the discovered credentials (VaultWarden + Infisical) to `.env.generated`.
3. `run-smoke.sh` runs the **SmokeTests** console app, which drives all three
   providers and verifies the seeded values. Exit code = number of failed checks.

The three VaultWarden items map onto the adapter's three value-resolution paths:

| Seed key             | VaultWarden item shape           | Adapter reads from      |
| -------------------- | -------------------------------- | ----------------------- |
| `smoke-login-secret` | Login item                       | `login.password`        |
| `smoke-note-secret`  | Secure note                      | `notes`                 |
| `smoke-field-secret` | Secure note + `password` field   | custom field `password` |

Infisical and HashiCorp Vault each get all three as ordinary secrets, plus a
set → get → delete round-trip when `ALLOW_MUTATING_TESTS=true`. (In Vault, each
key becomes a KV v2 item at `{mount}/{base path}/{key}` holding the value under a
single `value` field.)

## Prerequisites

- Docker + Docker Compose v2 (`docker compose`), or Podman with `podman compose`
  (tested with Podman 6). Substitute `podman compose` for `docker compose` below.
- .NET 10 SDK (to run `SmokeTests`)

## Run it

```bash
cd integration
cp .env.example .env                     # tweak ports/credentials if you like

docker compose up -d                     # start VaultWarden + Infisical
docker compose run --rm bootstrap        # register, provision, seed -> writes .env.generated

./run-smoke.sh                           # run the smoke tests
```

Expected tail:

```
Result: N passed, 0 failed, 0 block(s) skipped.
```

Tear down when finished:

```bash
docker compose down -v                   # removes containers AND volumes
```

## Editing the seed data

The seeded `{ key: value }` pairs live in [`seed.json`](./seed.json) (kept out of
`.env` because JSON quotes badly in a dotenv / compose `env_file`). Both the
bootstrap and the smoke tests read it. Keep the `-login-secret` / `-note-secret` /
`-field-secret` key suffixes so the VaultWarden mapping above still exercises all
three paths; any extra keys are seeded as plain login items (VaultWarden) and
ordinary secrets (Infisical).

## Re-running

The bootstrap is **one-shot per fresh stack**: VaultWarden rejects a duplicate
registration and Infisical's `admin/bootstrap` only succeeds on an un-initialised
instance. To bootstrap again, recreate the stack first:

```bash
docker compose down -v && docker compose up -d && docker compose run --rm bootstrap
```

`run-smoke.sh` itself is fully repeatable against an already-seeded stack.

## Running one system at a time

The SmokeTests app skips (does not fail) a provider block when its variables are
absent. To smoke only Infisical, comment out the `BITWARDEN_*` lines in
`.env.generated` before `./run-smoke.sh`, and vice-versa.

## Files

| Path                              | Purpose                                                        |
| --------------------------------- | ------------------------------------------------------------- |
| `docker-compose.yml`              | VaultWarden, Infisical (+ Postgres/Redis), Vault, bootstrap   |
| `Dockerfile.bootstrap`            | Helper image: Node + `jq` + `curl`                            |
| `bootstrap/entrypoint.sh`         | Orchestrates the whole seed, writes `.env.generated`          |
| `bootstrap/vaultwarden-register.mjs` | Registers the account, seeds vault items, fetches the API key |
| `bootstrap/seed-infisical.sh`     | Bootstraps Infisical + seeds secrets via its REST API         |
| `bootstrap/seed-vault.sh`         | Seeds secrets into Vault's KV v2 engine via its REST API      |
| `seed.json`                       | The `{ key: value }` pairs seeded and verified                |
| `run-smoke.sh`                    | Loads env + runs the SmokeTests app                           |
| `SmokeTests/`                     | Standalone console harness driving both providers             |
| `.env.example`                    | Static config template (copy to `.env`)                       |

## Troubleshooting

- **`bootstrap` exits with a VaultWarden registration error** — the account
  already exists. Recreate the stack (`down -v`) and re-run.
- **`admin/bootstrap` returns an error** — the Infisical instance is already
  initialised. Same fix: `down -v` and re-run.
- **Smoke tests can't reach a service** — confirm the ports in `.env`
  (`VAULTWARDEN_PORT`, `INFISICAL_PORT`) match `BITWARDEN_SERVER_URL` /
  `INFISICAL_SITE_URL`, and that `docker compose ps` shows both healthy.
- **Infisical takes a while on first start** — it runs DB migrations on boot; the
  bootstrap waits on `/api/status`, so give it a minute.
- **`POST /api/v1/projects` 404s during Infisical bootstrap** — you're on an old
  image. Do not use the `infisical/infisical:latest-postgres` tag; it is a frozen
  legacy tag serving an outdated API. `docker-compose.yml` pins a current release.
