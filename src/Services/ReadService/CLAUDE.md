# Read Service

Container `read-service`. Endpoint `GET /{shortCode}`. Redirects to the original URL. This is the hot path (~1000 reads per write), so latency matters.

## Projects

- `UrlShortener.ReadService.Application`: `GetOriginalUrlQuery`.
  Ports: `IShortUrlReadRepository`, `IShortUrlCache`, plus the built-in `TimeProvider`.
- `UrlShortener.ReadService.Infrastructure`: `ReadDbContext`, Redis cache.
- `UrlShortener.ReadService.Api`: Minimal API and composition root. Registers **only** read-side services (no counter, no write handlers, no cleanup job).

## Rules

- **Flow**: `ShortCode.TryParse` (malformed → `404` without touching Redis or the DB) → cache → DB on a miss → fill the cache → expiry check.
- **Responses**: `302 Found` (never 301), `404` not found, `410` expired.
- **Database is read-only**: `ReadDbContext` uses `NoTracking`, has **no migrations** and connects with a read-only DB user. It maps the `short_urls` table that the Write Service owns.
- **Cache-aside**: key `url:{short_code}`, value `original_url` + `expiresAt`. TTL = `min(defaultTtl, expiresAt - now)`. Never cache an expired entry.
- If Redis is unavailable, fall back to the DB and log a warning. The redirect must not fail because of the cache.
- Record the `redirects` metric (outcome: found / not_found / expired) and `cache_hits` / `cache_misses`.

## Commands

```bash
dotnet test tests/UrlShortener.ReadService.UnitTests
dotnet test tests/UrlShortener.ReadService.IntegrationTests
```

The integration tests create the schema with the Write Service migrations, so they prove that both services agree on the table.

## Configuration (environment variables use `__`, e.g. `ConnectionStrings__Postgres`)

| Key | Required | Notes |
|---|---|---|
| `ConnectionStrings:Postgres` | yes | **Secret.** Use the SELECT-only role `urlshortener_read` (`deploy/postgres/roles.sql`), never the write role |
| `ConnectionStrings:Redis` | yes | Secret if it has a password. The service starts and redirects from the database even if Redis is down |
| `Cache:DefaultTtl` | no (`1.00:00:00`) | Upper bound of a cached redirect; 1 s to 7 days. **Careful with TimeSpan syntax**: `24:00:00` means 24 *days*, use `1.00:00:00` |
| `Cache:OperationTimeout` | no (`00:00:00.100`) | Per Redis operation, 10 ms to 5 s |

Options are validated at startup. Do not log redirects per request: `appsettings.json` sets `UrlShortener.BuildingBlocks.Logging` to `Warning` (failures and exceptions still log).

## Redis outage behaviour (do not undo)

The connection uses `BacklogPolicy.FailFast`. StackExchange.Redis' default queues commands while disconnected and fails them about a second later whatever the timeout is, which made every redirect ~2 s slower (get + set). `RedisOutageLatencyTests` guards this without Docker.

## HTTP tests without Docker

`tests/UrlShortener.ReadService.IntegrationTests/Api/RedirectEndpointTests` hosts the real pipeline with only the repository and cache replaced by in-memory fakes. `ReadFullStackTests` and the other Postgres/Redis tests carry `[Trait("Category", "Integration")]` and run in CI.

