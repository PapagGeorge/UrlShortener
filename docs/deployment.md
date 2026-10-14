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

- Tags: `sha-<short-sha>` and `latest`. GHCR names must be lowercase.
- Multi-stage builds: `mcr.microsoft.com/dotnet/sdk:10.0` → `mcr.microsoft.com/dotnet/aspnet:10.0`, running as a non-root user.
- The build context is the repo root, so each Dockerfile can copy `src/Shared/`. Each image contains only its own service.

## GitHub Actions

- **`ci.yml`**: runs on every PR and on push to `main`, on `ubuntu-latest`: restore → build (warnings as errors) → all tests, including Testcontainers.
- **`publish.yml`**: runs on push to `main` only, after CI succeeds (`workflow_run`, or `needs:` in the same workflow). It runs `docker/login-action` (GHCR, `GITHUB_TOKEN`), then `docker/build-push-action` with `platforms: linux/amd64` for the three images, using the GitHub Actions build cache.
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

- Builds the images from source (or pulls them, see `deploy/`).
- Services: `gateway`, `read-service`, `write-service`, `postgres`, `redis`, `jaeger`, `prometheus`.
- Only the gateway and the UIs (Jaeger, Prometheus) expose ports. The services talk over the internal network.
- Integration tests mark themselves `[Trait("Category", "Integration")]`. They are the only tests that need a Docker daemon.

## Running integration tests

1. **Automatically, on every PR**: `ci.yml` runs them on GitHub's runners. This is the default and needs nothing from the Mac.
2. **On the Linux box** (ad hoc, e.g. against a feature branch): `git fetch && git checkout <branch> && dotnet test` (needs the .NET 10 SDK there), or without installing the SDK:
   `docker run --rm -v "$PWD":/src -w /src -v /var/run/docker.sock:/var/run/docker.sock mcr.microsoft.com/dotnet/sdk:10.0 dotnet test`
3. **From the Mac against the Linux box's Docker** (optional, no local Docker): point Testcontainers at the remote daemon, e.g. an SSH-forwarded socket plus `TESTCONTAINERS_HOST_OVERRIDE=<linux-box-address>`. Only on a trusted network, and never expose the Docker API publicly.
4. **SIT**: `docker compose up -d --build` on the Linux box and exercise the real HTTP flow (create, redirect, expire).

Avoid running heavy test containers on the same Docker host as production if the box is small. Use a separate compose project name and different ports.

## Ubuntu box (`deploy/`)

| File | Purpose |
|---|---|
| `docker-compose.prod.yml` | `image: ghcr.io/...:${IMAGE_TAG:-latest}`, never `build:` |
| `update.sh` | `docker compose pull && docker compose up -d --remove-orphans && docker image prune -f` |
| `urlshortener-update.service` + `.timer` | A systemd timer runs `update.sh` every few minutes |
| `.env` | Runtime secrets, **only on the box** (`chmod 600`, git-ignored) |

**Rollback**: set `IMAGE_TAG=sha-<old>` in `.env` and run `update.sh`.

## Secrets (public repo)

- Nothing secret is ever committed: no keys, passwords, connection strings or real `.env` files. Commit only `.env.example` with placeholders.
- Runtime secrets (DB passwords, permutation keys) live **only** in `deploy/.env` on the Ubuntu box. GitHub never deploys, so it never needs them.
- CI needs no secrets. Publishing uses the built-in `GITHUB_TOKEN`.
- Locally, use `dotnet user-secrets`.
- Never log secrets or full connection strings.
