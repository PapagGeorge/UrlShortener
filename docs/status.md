# Project status

Where the implementation stands and how to pick it up again. The plan is in [`technical-design.md`](technical-design.md) (§8 lists the phases); this file records **progress, open decisions and what comes next**. Update it at the end of every phase, in the same PR.

**Last updated:** 2026-10-15 · **Current position:** Phases 0–6 are merged (6e pending merge). **Phase 6 is complete. Next: Phase 7 (Docker, compose, deploy).**

## Phases

| Phase | What | State | PR |
|---|---|---|---|
| 0 | Skeleton, build config, architecture tests, CI | ✅ done | #1 |
| 1 | `Result`, handlers, logging decorators, Domain value objects | ✅ done | #2 |
| 2 | Base62, Feistel obfuscator, Redis id allocator | ✅ done | #3 |
| 3a | Write Application layer (create, delete-expired, validation) | ✅ done | #4 |
| 3b | Postgres persistence, `InitialCreate` migration, cleanup job | ✅ done | #5 |
| 3c | Write Api: `POST /urls`, composition root, ServiceDefaults | ✅ done | #6 |
| 4a | Read Application layer (`GetOriginalUrl`, ports) | ✅ done | #7 |
| 4b | Read Infrastructure: read-only DB, Redis cache, DB roles | ✅ done | #8 |
| 4c | Read Api: `GET /{code}` 302/404/410, single-flight, bounded pool | ✅ done | #9 |
| 5 | Gateway: YARP routes, rate limiting, trusted proxies, body limit | ✅ done | #10 |
| 6 | Observability & hardening (6a metrics, 6b OpenTelemetry, 6c readiness, 6d OpenAPI/Scalar, 6e Redis resilience) | ✅ done | #12–#17 |
| 7 | Packaging & deploy (Dockerfiles, compose, GHCR, systemd) | ⏭ **next** | – |
| 8 | Load check (k6/bombardier), p50/p99 | ⬜ not started | – |

Both services and the Gateway work end to end in tests. Nothing has been containerised or deployed yet.

## Phase 6 slices (all done; kept as a record)

1. **6a Metrics (no new package). Handler-level part DONE** (`HandlerMetrics`: `urlshortener.handler.requests{request,outcome}` + `.duration`, via `MetricsCommandDecorator`/`MetricsQueryDecorator`, outermost in both services). **Cache part DONE:** `RedirectMetrics` (own `Meter` "UrlShortener.Read"), `urlshortener.cache.lookups{result=hit|miss}`. Create outcomes are already covered by the handler counter. **Optional leftover:** counters for expired URLs deleted and counter batches reserved (write).
   Original scope: `System.Diagnostics.Metrics` is part of the BCL: add `Meter("UrlShortener")` with `urls_created{custom_alias}`, `counter_batches_reserved`, `expired_urls_deleted` (write) and `redirects{outcome}`, `cache_hits`, `cache_misses` (read). Test with `MeterListener`.
2. **6b OpenTelemetry in `ServiceDefaults`: DONE** (4 approved packages, 1.19.x). ASP.NET Core + HttpClient traces and metrics, our meters by wildcard `UrlShortener.*`, `/health` not traced, `service.name` = the assembly name. Exported over OTLP **only when `OTEL_EXPORTER_OTLP_ENDPOINT` is set** (Phase 7 sets it to Jaeger and Prometheus). **Not included (packages not approved):** Npgsql and Redis spans (`Npgsql.OpenTelemetry`, `OpenTelemetry.Instrumentation.StackExchangeRedis`) and .NET runtime metrics (`OpenTelemetry.Instrumentation.Runtime`). Tests live in `Gateway.Tests/ServiceDefaults` because ServiceDefaults has no test project.
3. **6c Readiness health checks: DONE.** `/health` = liveness, runs no checks (a dependency outage must never restart a container). `/health/ready` runs the probes registered with `AddReadinessProbe` (3 s timeout, 503 with a bare `Unhealthy` body). Write = Postgres + Redis; Read = Postgres only (Redis down keeps it ready). The counter-exhausted check was dropped on purpose: 62^7 = 3.5e12 codes, and exhaustion already surfaces as a 500. **Phase 7 notes:** use `/health/ready` for compose `depends_on: condition: service_healthy` and `/health` for the container liveness healthcheck; the Gateway's active health checks still probe `/health` (decide whether they should probe `/health/ready`).
4. **6d OpenAPI + Scalar: DONE** (`Microsoft.AspNetCore.OpenApi` 10.0.13, `Scalar.AspNetCore` 2.18.0). Opt-in per service via `AddApiDocumentation()` / `MapApiDocumentation()` (Write and Read; not the Gateway). **Development only**: `/openapi/v1.json` and `/scalar` are 404 in any other environment (tested). Run a service with `ASPNETCORE_ENVIRONMENT=Development` to browse it. Health endpoints are not in the document. Validation errors are documented as 400 (not 422).
5. **6e Resilience decisions: DONE (2026-10-15).** Implemented: Write Redis `FailFast` (`WriteRedisOptions`) and Read `RedisConnectionWarmup` (same cold-burst fix as Write). **Decided not to implement now:** negative cache for 404s (the Gateway rate limit already covers scans; revisit if Phase 8 shows load on Postgres from unknown codes) and a Redis circuit breaker for the Read Service (FailFast already makes an outage cost ~ms; measure in Phase 8 first).

## Open decisions and known gaps

- **P5 – negative caching** of unknown codes (short TTL, e.g. 30 s) so random-code scans do not reach Postgres. Single-flight already protects against a stampede on *one* code, not against many different unknown codes. Decide in 6e; off by default. *Decided in 6e: not now, revisit after the Phase 8 load check.*
- **Redis circuit breaker (Read).** `BacklogPolicy.FailFast` already makes a Redis outage cost ~ms per redirect; a breaker would skip Redis entirely for a few seconds after repeated failures. Probably not needed; measure in Phase 8. *Decided in 6e: not now, revisit after the Phase 8 load check.*
- **Gateway rate limiter is in memory** (per instance). Fine for one Gateway; needs a shared store before running several.
- **`ForwardedHeaders:KnownProxies/KnownNetworks`** are empty by default (forwarded headers ignored). Set them at deploy time to the proxy in front of the Gateway, if there is one.
- **Request body limit** (8 KB) lives in the Gateway. The Write Service has no own limit yet.
- **Cold-burst CI flake: explained and fixed.** The Write Service created its Redis connection lazily inside the first request; under 60 concurrent cold requests the handshake starved for thread-pool threads and hit `ConnectTimeout` (5 s), giving one 500. `RedisConnectionWarmup` now connects at startup. Both concurrency tests also report the captured server errors on failure. If it ever recurs, read the `server errors:` part of the message.
- **Never rotate `Obfuscation:Key`** once codes exist (it re-maps the whole code space). Back it up with the rest of `deploy/.env`.

## Phase 7 and 8 preview (so nothing is forgotten)

- **7a Dockerfiles: DONE (PR pending)** for gateway, write, read (multi-stage, non-root `USER $APP_UID`, port 8080, build context = repo root, `.dockerignore`). Guarded locally by `ArchitectureTests/DockerfileTests` (project files copied, non-root, publish target) and built for real by the `docker-build` job in ci.yml. The aspnet image has **no curl/wget**: compose healthchecks must use bash, e.g. `bash -c 'exec 3<>/dev/tcp/127.0.0.1/8080 && printf "GET /health/ready HTTP/1.0\r\n\r\n" >&3 && head -1 <&3 | grep -q 200'`.
- **7b `docker-compose.yml` + `.env.example` + `scripts/smoke.sh`: DONE (PR pending)** (Postgres 17 with the roles applied by `deploy/postgres/10-roles.sh`, Redis 8.6 with AOF, Jaeger 2.22 for traces, Prometheus 3.15 with the OTLP receiver for metrics; per-signal OTLP endpoints now supported in ServiceDefaults). Verified by the CI job `compose-smoke` (full stack + smoke test on every PR). **Still to do: `deploy/docker-compose.prod.yml` (images from GHCR), `update.sh`, systemd timer.**
- Wire `deploy/postgres/roles.sql` into the Postgres init (fill the `{{...}}` placeholders from `.env`; letters and digits only in passwords). Write connects as `urlshortener_write`, Read as `urlshortener_read`.
- **Postgres `max_connections`** must exceed the sum of the pools (Read pool defaults to 32, Write to Npgsql's 100 unless set in the connection string).
- `publish.yml` (main only, after CI): push to GHCR with `GITHUB_TOKEN`.
- **Compose smoke test in CI (agreed):** a CI job brings the full compose stack up on the GitHub runner and runs create → redirect → expire, so SIT also runs on every PR.
- **Debug from the Mac (agreed):** a small compose with only Postgres and Redis for the Linux box; the services run in Rider on the Mac with connection strings in user-secrets. Ports on the LAN or behind an SSH tunnel, strong passwords, never public. Optional: `docker context` over `ssh://` to the Linux box (CLI only, no engine on the Mac).
- SIT on the Linux box: create → redirect → expire (`docker compose up -d --build`), never on the Mac.

## How to resume

```bash
cd ~/Dev/Url.Shortener
git checkout main && git pull
gh pr list                       # any open PR? merge it first (CI must be green)
export PATH="$HOME/.dotnet:$PATH"   # already in ~/.zshrc in a new terminal
dotnet test                      # on the Mac: unit + architecture + Docker-free HTTP tests only
```

Rules of the road (details in `CLAUDE.md`):

- **Feature branch + PR into `main`**; `main` is protected and needs the CI check `build-and-test` green. Merge is done by the owner.
- **TDD**: failing test first, then the smallest code. Say which service/layer is touched and which tests come first.
- **Ask before adding any NuGet package** (name + version + why) or changing the solution layout.
- **No Docker on the Mac, ever.** Integration tests (`[Trait("Category","Integration")]`) run in CI and on the Linux box; `tests/Directory.Build.props` excludes them locally unless `CI=true`.
- Packages already approved and in use: xUnit v2, Shouldly, NSubstitute, `Microsoft.Extensions.TimeProvider.Testing`, NetArchTest, FluentValidation, Npgsql EF Core + EF Design (pinned centrally to 10.0.13), StackExchange.Redis, Testcontainers (Redis, PostgreSql), `Microsoft.AspNetCore.Mvc.Testing`, `Yarp.ReverseProxy`.

## Tests at a glance (as of PR #10, merged)

| Project | Count | Needs Docker |
|---|---|---|
| Domain.UnitTests | 96 | no |
| WriteService.UnitTests | 173 | no |
| WriteService.IntegrationTests | 54 (27 run on the Mac, 27 in CI) | CI only for 27 |
| ReadService.UnitTests | 74 | no |
| ReadService.IntegrationTests | 68 (25 run on the Mac, 43 in CI) | CI only for 43 |
| Gateway.Tests | 79 | no |
| ArchitectureTests | 21 | no |

## Environment notes

- .NET 10 SDK lives in `~/.dotnet` (first on `PATH` via `~/.zshrc`); the system `dotnet` is 9. `dotnet-ef` is a local tool (`dotnet tool restore`).
- `gh` is logged in as `PapagGeorge`; repo: https://github.com/PapagGeorge/UrlShortener (public, never commit secrets).
- Containers (services, Redis, Postgres, …) run on the Linux PC, not on this Mac.
