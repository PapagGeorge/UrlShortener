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
