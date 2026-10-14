# CLAUDE.md — URL Shortener (.NET)

A Bit.ly-style URL shortener: a portfolio and practice project for system design and .NET microservices.
**Public repo**: never commit secrets (see `docs/deployment.md`).

## Read on demand

| Before you work on… | Read |
|---|---|
| **Starting a session / picking the work up again** | `docs/status.md` (current phase, next steps, open decisions) |
| Any architectural change | `docs/system-design.md`, `docs/architecture.md` |
| Short codes, aliases, the shared DB, observability | `docs/architecture.md` |
| Dockerfiles, compose, GitHub workflows, secrets | `docs/deployment.md` |

Each service has its own `CLAUDE.md` in `src/Services/<Service>/` (and the Gateway in `src/Gateway/`) with the rules specific to it.

## Non-negotiables

1. **Clean Architecture.** Inside each service, dependencies point inward only: `Api → Infrastructure → Application → Domain`.
   Domain, BuildingBlocks and Application never reference EF Core, Redis, ASP.NET Core, OpenTelemetry or other infrastructure packages.
2. **TDD.** Write a failing test first, run it and watch it fail, then write the smallest code that makes it pass, then refactor. Never write production code without a test that needs it.
3. **Two separate services.** Read and Write each have their own Application, Infrastructure, Api, Dockerfile and container. They share only `Domain` and `BuildingBlocks`, and **never reference each other**.
4. **CQRS without MediatR.** Use our own `ICommandHandler` / `IQueryHandler` from BuildingBlocks. No MediatR, AutoMapper or similar.

## Tech stack

.NET 10 · Minimal APIs · PostgreSQL + EF Core · Redis (StackExchange.Redis) · YARP · FluentValidation · OpenTelemetry → Jaeger / Prometheus · OpenAPI + Scalar (Development only) · xUnit, Testcontainers, NSubstitute, Shouldly, NetArchTest · Docker Compose · GitHub Actions → GHCR

## Solution layout

```text
src/
  Shared/
    UrlShortener.Domain/            # Shared kernel: ShortUrl, ShortCode, Alias, OriginalUrl, rule constants
    UrlShortener.BuildingBlocks/    # Result<T>, errors, ICommandHandler/IQueryHandler, LoggingDecorator
    UrlShortener.ServiceDefaults/   # OpenTelemetry, ProblemDetails, OpenAPI for every Api
  Services/
    WriteService/                   # Application, Infrastructure, Api, Dockerfile, CLAUDE.md
    ReadService/                    # Application, Infrastructure, Api, Dockerfile, CLAUDE.md
  Gateway/
    UrlShortener.Gateway/           # YARP routing + rate limiting
tests/
  UrlShortener.Domain.UnitTests/
  UrlShortener.{Write,Read}Service.UnitTests/
  UrlShortener.{Write,Read}Service.IntegrationTests/
  UrlShortener.Gateway.Tests/
  UrlShortener.ArchitectureTests/
docs/  deploy/  .github/workflows/
```

## Dependency rules (enforced by ArchitectureTests)

- `BuildingBlocks` → only `Microsoft.Extensions.Logging.Abstractions`. `Domain` → only `BuildingBlocks` (for `Result<T>` and typed errors), no packages.
- `X.Application` → `Domain`, `BuildingBlocks`.
- `X.Infrastructure` → `X.Application`, plus infrastructure packages.
- `X.Api` → `X.Infrastructure`, `X.Application`, `ServiceDefaults`.
- `WriteService.*` ↮ `ReadService.*`: no references in either direction. The Gateway references neither.

## Domain (shared kernel)

- `ShortUrl`: `ShortCode`, `OriginalUrl`, `CreatedAt`, `ExpiresAt?`, `CreatedBy?`.
- Value objects validate themselves on creation and are the last line of defense.
- `IsExpired(DateTimeOffset now)` lives on the entity. Never call `DateTime.Now` or `UtcNow`; inject `TimeProvider`.
- Expected failures are typed errors returned in `Result<T>` (from BuildingBlocks), not exceptions.
- Keep the Domain small: a change here forces both services to redeploy.

## HTTP contract

- `POST /urls` → `201` `{ "shortUrl": "..." }` · `400` ProblemDetails · `409` alias taken · `429` rate limited (gateway)
- `GET /{shortCode}` → `302` (never 301) · `404` not found or malformed · `410` expired
- Endpoints are thin: request → command or query → handler → map the `Result` to HTTP.

## Coding conventions

- Nullable enabled, `TreatWarningsAsErrors`, file-scoped namespaces, `sealed` by default, primary constructors where they help.
- Async all the way, and pass `CancellationToken` through every layer.
- Use strongly-typed options with `ValidateOnStart`. Secrets never go in `appsettings*.json`.
- Name things in the domain language (`ShortCode`, `OriginalUrl`, `Alias`). Write code, comments and commits in English.

## Testing

- Domain: pure unit tests. Application: handler and validator tests with NSubstitute for the ports.
- Integration: Testcontainers (real Postgres and Redis) + `WebApplicationFactory` of that service. No EF InMemory provider.
- **The dev Mac never runs Docker.** Every integration test class carries `[Trait("Category", "Integration")]`. `tests/Directory.Build.props` excludes them automatically unless `CI=true`, so a plain `dotnet test` is safe on the Mac; CI (`ubuntu-latest`, which has Docker, sets `CI=true`) runs everything. Never start Docker or Docker Desktop on the Mac.
- Test naming: `MethodOrScenario_Condition_ExpectedResult`.

## Commands

```bash
dotnet build
dotnet test --filter "Category!=Integration"   # on the Mac: unit + architecture tests only
dotnet test tests/UrlShortener.Domain.UnitTests
# Integration tests and `docker compose up --build` (services + Postgres, Redis, Jaeger, Prometheus)
# run in CI and on the Linux box, never on the Mac. See docs/deployment.md.
```

## Git

- Conventional Commits (`feat:`, `fix:`, `test:`, `refactor:`, `chore:`, `docs:`, `ci:`), with an optional scope such as `feat(write):`.
- Use feature branches and PRs into `main`. Never commit directly to `main`.

## Workflow for Claude

- Before you implement a feature: say which service(s) and layer(s) it touches and list the tests you'll write first.
- Work in small steps: one failing test, the code that passes it, then a green build. Don't scaffold whole layers ahead of the tests.
- Ask before you add a NuGet package or change the solution layout.
- Run `dotnet build` and `dotnet test --filter "Category!=Integration"` before you say a step is done. Integration tests are verified by CI on the PR; say so explicitly instead of claiming they passed.
- If a decision in these files turns out to be wrong in practice, raise it. Don't silently diverge.

## Out of scope (for now)

Authentication (`CreatedBy` stays nullable) · click analytics · multi-region · spam/malware filtering · CDN/edge redirects
