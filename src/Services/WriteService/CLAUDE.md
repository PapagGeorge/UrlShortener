# Write Service

Container `write-service`. Endpoint `POST /urls`. Creates short URLs and cleans up expired ones.

## Projects

- `UrlShortener.WriteService.Application`: `CreateShortUrlCommand`, `DeleteExpiredShortUrlsCommand`, validators.
  Ports: `IShortUrlWriteRepository`, `IIdAllocator`, `ICodeObfuscator`, plus the built-in `TimeProvider`.
- `UrlShortener.WriteService.Infrastructure`: `WriteDbContext` + migrations, Redis counter, obfuscator, cleanup job.
- `UrlShortener.WriteService.Api`: Minimal API and composition root. Registers **only** write-side services (no cache).

## Rules

- **Validation**: FluentValidation validators, run by a `ValidationDecorator` that returns a validation-error `Result` (→ `400`). The validators use the Domain constants, so they never disagree with the value objects.
- **Schema owner**: this service owns the `short_urls` table and **all migrations**, and applies them at startup.
  `short_code` is the PK. Add an index on `expires_at`. Don't add an index on `original_url` (there is no dedup).
- **Generated codes**: counter ID → permutation → base62, padded to exactly 7 characters. Details and required tests are in `docs/architecture.md`.
- **Counter**: Redis `INCRBY 1000` reserves a batch, and IDs are handed out locally. Refills are guarded by a `SemaphoreSlim`. Fail loudly once the counter goes past `62^7`.
- **Permutation keys** come from options (secrets) and are never hard-coded or logged.
- **Aliases**: a unique violation on the PK → `409`, never `500`.
- **Cleanup**: a `BackgroundService` runs on a configurable interval and calls `DeleteExpiredShortUrlsCommand`, deleting in batches of about 1000.
- No deduplication: every request creates a new code.

## Commands

```bash
dotnet test tests/UrlShortener.WriteService.UnitTests
dotnet test tests/UrlShortener.WriteService.IntegrationTests
dotnet ef migrations add <Name> \
  -p src/Services/WriteService/UrlShortener.WriteService.Infrastructure \
  -s src/Services/WriteService/UrlShortener.WriteService.Api
```

A migration changes the schema the Read Service depends on. Check `ReadDbContext` and run the Read integration tests too.

## Configuration (environment variables use `__`, e.g. `ConnectionStrings__Postgres`)

| Key | Required | Notes |
|---|---|---|
| `ConnectionStrings:Postgres` | yes | Secret. Read/write user |
| `ConnectionStrings:Redis` | yes | Secret if it has a password. The service starts even if Redis is down (`AbortOnConnectFail=false`): aliases keep working, generated codes fail with 500 |
| `Obfuscation:Key` | yes | **Secret**, base64, at least 32 bytes. Generate with `openssl rand -base64 32`. **Never rotate it** once codes exist: it re-maps the whole code space |
| `ShortUrl:PublicBaseUrl` | yes | e.g. `https://short.example`: absolute, no path. Its host is also used to reject URLs that point back at us |
| `Database:MigrateOnStartup` | no (true) | Applies pending migrations at startup |
| `Cleanup:Interval` / `Cleanup:BatchSize` | no (10 min / 1000) | Expired-URL cleanup job |
| `Counter:BatchSize` / `Counter:RedisKey` | no (1000 / `urlshortener:counter`) | Id batching |

Locally use `dotnet user-secrets` for the secret ones (never `appsettings*.json`). Options are validated at startup, so a missing or weak value stops the service immediately.

## HTTP tests without Docker

`tests/UrlShortener.WriteService.IntegrationTests/Api` hosts the real pipeline with only the repository and allocator replaced by in-memory fakes (`WriteApiFactory`), so it runs on the Mac. Tests that need real Postgres/Redis carry `[Trait("Category", "Integration")]` and run in CI.

