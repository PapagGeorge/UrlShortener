# Deployment

## Flow

```text
MacBook M1 (Rider) ──git push──▶ GitHub (public repo)
                                    │
                                    ├─ ci.yml       (every PR + main): build + all tests
                                    └─ publish.yml  (main only, after CI): build linux/amd64 images → push to GHCR
                                                                              │
Ubuntu x86 box ◀──── docker compose pull (systemd timer) ─────────────────────┘
```

- **Mac**: development only, no Docker. Deployment images are never built on the Mac.
- **GitHub**: GitHub-hosted runners only. They build the images natively on `ubuntu-latest` (x86) and push them to GHCR.
- **Ubuntu box**: **pull-based**. GitHub never reaches into the box: there is no self-hosted runner and no open inbound port.

## Images

| Image | Dockerfile |
|---|---|
| `ghcr.io/papaggeorge/urlshortener-gateway` | `src/Gateway/Dockerfile` |
| `ghcr.io/papaggeorge/urlshortener-write-service` | `src/Services/WriteService/Dockerfile` |
| `ghcr.io/papaggeorge/urlshortener-read-service` | `src/Services/ReadService/Dockerfile` |
| `ghcr.io/papaggeorge/urlshortener-web` | `web/Dockerfile` (build context `web/`) |

- Tags: `sha-<short-sha>` and `latest`. GHCR names must be lowercase.
- Multi-stage builds: `mcr.microsoft.com/dotnet/sdk:10.0` → `mcr.microsoft.com/dotnet/aspnet:10.0`, running as a non-root user.
- The build context is the repo root, so each Dockerfile can copy `src/Shared/`. Each image contains only its own service.

## GitHub Actions

- **`ci.yml`**: runs on every PR and on push to `main`, on `ubuntu-latest`: restore → build (warnings as errors) → all tests, including Testcontainers.
- **`publish.yml`**: runs on push to `main` only, after CI succeeds (`workflow_run`, or `needs:` in the same workflow). It runs `docker/login-action` (GHCR, `GITHUB_TOKEN`), then `docker/build-push-action` with `platforms: linux/amd64` for the four images, using the GitHub Actions build cache.
- Permissions default to `contents: read`. Only the publish job gets `packages: write`.
- **Never** use `pull_request_target`. **Never** use `runs-on: self-hosted`. The repo is public, and fork PRs must not be able to run code on our machines.
- Pin actions to a major version (e.g. `actions/checkout@v4`).
- Protect `main` with a branch protection rule that requires CI to pass.

## Where things run

| Environment | Runs | Docker? |
|---|---|---|
| **Dev Mac** | Rider, `dotnet build`, unit + architecture tests (`--filter "Category!=Integration"`) | **No, never** |
| **GitHub Actions** | Everything, including integration tests (Testcontainers) | Yes (`ubuntu-latest`) |
| **Linux box** | The full compose stack and manual SIT (system integration testing) | Yes |

## Compose stack (`docker-compose.yml`, for the Linux box)

- `docker-compose.yml` builds the images from source (SIT and the CI smoke test); `deploy/docker-compose.prod.yml` pulls the published ones. CI checks that the two never drift apart (`scripts/check-compose-drift.sh`).
- Services: `web`, `gateway`, `read-service`, `write-service`, `postgres`, `redis`, `jaeger`, `prometheus`.
- Only the gateway, the web UI (`WEB_BIND`/`WEB_PORT`, default `127.0.0.1:3000`) and the observability UIs (Jaeger, Prometheus) expose ports. The services talk over the internal network.
- Integration tests mark themselves `[Trait("Category", "Integration")]`. They are the only tests that need a Docker daemon.

## Running integration tests

1. **Automatically, on every PR**: `ci.yml` runs them on GitHub's runners. This is the default and needs nothing from the Mac.
2. **On the Linux box** (ad hoc, e.g. against a feature branch): `git fetch && git checkout <branch> && dotnet test` (needs the .NET 10 SDK there), or without installing the SDK:
   `docker run --rm -v "$PWD":/src -w /src -v /var/run/docker.sock:/var/run/docker.sock mcr.microsoft.com/dotnet/sdk:10.0 dotnet test`
3. **From the Mac against the Linux box's Docker** (optional, no local Docker): point Testcontainers at the remote daemon, e.g. an SSH-forwarded socket plus `TESTCONTAINERS_HOST_OVERRIDE=<linux-box-address>`. Only on a trusted network, and never expose the Docker API publicly.
4. **SIT**: automatically on every PR (`compose-smoke` in ci.yml runs `scripts/smoke.sh` against the whole stack); by hand on the Linux box: `docker compose up -d --build --wait && scripts/smoke.sh`.

Avoid running heavy test containers on the same Docker host as production if the box is small. Use a separate compose project name and different ports.

## Ubuntu box (`deploy/`)

| File | Purpose |
|---|---|
| `docker-compose.prod.yml` | `image: ghcr.io/...:${IMAGE_TAG:-latest}`, never `build:` |
| `update.sh` | `git pull --ff-only`, `docker compose pull`, `up -d --remove-orphans --wait`, `image prune -f`. One run at a time (flock), never touches volumes |
| `urlshortener-update.service` + `.timer` | A systemd timer runs `update.sh` every few minutes |
| `.env.example` | Placeholders for `.env` (the real `.env` is created on the box) |
| `.env` | Runtime secrets, **only on the box** (`chmod 600`, git-ignored) |

**Rollback**: set `IMAGE_TAG=sha-<old>` in `.env` and run `update.sh`. Set it back to `latest` to follow main again.

### First-time setup on the Linux box

```bash
sudo useradd -m -G docker deploy                      # the user the timer runs as (must be in the docker group)
sudo git clone https://github.com/PapagGeorge/UrlShortener.git /opt/urlshortener
sudo chown -R deploy: /opt/urlshortener
sudo -u deploy cp /opt/urlshortener/deploy/.env.example /opt/urlshortener/deploy/.env
sudo -u deploy chmod 600 /opt/urlshortener/deploy/.env
sudo -u deploy nano /opt/urlshortener/deploy/.env     # fill in the passwords, OBFUSCATION_KEY, PUBLIC_BASE_URL, GATEWAY_BIND
sudo -u deploy /opt/urlshortener/deploy/update.sh     # first start by hand, watch it succeed
sudo cp /opt/urlshortener/deploy/urlshortener-update.* /etc/systemd/system/
sudo systemctl daemon-reload && sudo systemctl enable --now urlshortener-update.timer
```

- **GHCR visibility:** the first `publish.yml` run creates the packages as *private*. Either make the four packages public (GitHub profile, Packages, each package, Settings, Change visibility; the images contain no secrets) or run `docker login ghcr.io` as `deploy` with a token that has only `read:packages`.
- The Postgres roles (`deploy/postgres/10-roles.sh`) are applied only on an **empty** data volume. Changing the passwords later means `docker compose down -v`, which erases the data.
- Check the timer: `systemctl list-timers urlshortener-update.timer` and `journalctl -u urlshortener-update.service -n 50`.

## Secrets (public repo)

- Nothing secret is ever committed: no keys, passwords, connection strings or real `.env` files. Commit only `.env.example` with placeholders.
- Runtime secrets (DB passwords, permutation keys) live **only** in `deploy/.env` on the Ubuntu box. GitHub never deploys, so it never needs them.
- CI needs no secrets. Publishing uses the built-in `GITHUB_TOKEN`.
- Locally, use `dotnet user-secrets`.
- Never log secrets or full connection strings.

## Web UI (`web/`)

React + TypeScript (Vite), tests with Vitest and Testing Library. Not part of the .NET solution; it has its own `package.json`.

- **Container**: `node:22-alpine` builds the app, `nginx-unprivileged` serves it on 8080 (non-root). nginx proxies `/api/*` to the Gateway (`API_UPSTREAM`, default `gateway:8080`) with the prefix removed, so the browser talks to one origin and the backend needs no CORS.
- **Develop on the Mac** (Node only, no Docker): `cd web && npm ci && npm run dev`. The dev server proxies `/api` to `http://localhost:8080`; point `API_TARGET` at the Linux box (or an SSH tunnel to its Gateway port) to use a real backend. `npm test` runs the tests.
- **CI**: the `web` job runs `npm ci`, `npm test` and `npm run build`; `docker-build` builds the image; `compose-smoke` also checks the UI shell and the `/api` proxy (`scripts/smoke.sh <gateway-url> <web-url>`).
- **Rate limit**: the Gateway sees the UI container's address, not the visitor's, because `ForwardedHeaders` trusts no proxy by default. All UI users therefore share one rate-limit bucket (30 creates/min). Direct calls to the Gateway are unaffected. To fix it, give the web container a fixed address and set `ForwardedHeaders__KnownProxies__0` on the Gateway (see `docs/status.md`).
