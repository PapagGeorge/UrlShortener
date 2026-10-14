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
