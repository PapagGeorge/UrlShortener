# Technical Design & Implementation Plan

Companion to [`system-design.md`](system-design.md) (the *why*) and [`architecture.md`](architecture.md) (the *what*).
This document is the *how and in what order*: types, contracts, algorithms, failure modes, and a phased TDD plan.
Status: **draft, nothing implemented yet** (only docs and per-service `CLAUDE.md` exist).

---

## 0. Prerequisites and open decisions

| # | Item | Proposal |
|---|---|---|
| P1 | Local SDK is **.NET 9.0.203**, the project targets **.NET 10** | Install the .NET 10 SDK; add `global.json` (`rollForward: latestFeature`) |
| P2 | JSON field names: `system-design.md` uses snake_case, `CLAUDE.md` response uses `shortUrl` | **camelCase** everywhere (matches `CLAUDE.md`): `originalUrl`, `alias`, `expiresAt` → `shortUrl` |
| P3 | Permutation: `architecture.md` allows affine **or** Feistel | **Feistel + cycle-walking** (see §3.2). Affine is linear, so two known (id, code) pairs reveal A and B |
| P4 | Redis counter loss (restart without persistence) re-issues old IDs → collisions on generated codes | Redis AOF (`appendonly yes`, `everysec`) **and** a retry-on-PK-violation for generated codes (§3.4). Not only aliases |
| P5 | Negative lookups (random codes) always hit Postgres | Optional short-TTL negative cache (e.g. 30 s). Decide in Phase 6; off by default |
| P6 | Rate limit is in-memory per gateway instance | Fine for one gateway instance; document the limit |

**Decided (2026-10-14):** P1 install .NET 10 (done, user-space in `~/.dotnet`, must be first on `PATH`) · P2 camelCase · P3 Feistel · P4 AOF + retry on generated codes · P5 (negative cache) still open, decided in Phase 6 · collation `"C"` for `short_code` (see §3.4).

---

## 1. Solution structure

Matches `CLAUDE.md`. Project references (enforced by `ArchitectureTests`):

```text
Domain            → BuildingBlocks (for Result<T> and typed errors; no packages)
BuildingBlocks    → Microsoft.Extensions.Logging.Abstractions
ServiceDefaults   → OpenTelemetry + ASP.NET Core packages
X.Application     → Domain, BuildingBlocks (+ FluentValidation in Write only)
X.Infrastructure  → X.Application, EF Core/Npgsql, StackExchange.Redis
X.Api             → X.Infrastructure, X.Application, ServiceDefaults
Gateway           → ServiceDefaults, Yarp (references no service)
```

Central package management (`Directory.Packages.props`), `Directory.Build.props` with `Nullable`, `TreatWarningsAsErrors`, `ImplicitUsings`, `LangVersion latest`. Adding the packages listed in `CLAUDE.md` is covered by this plan; any other package needs a question first.

---

## 2. Shared kernel

### 2.1 BuildingBlocks

```csharp
public readonly record struct Error(string Code, string Description, ErrorType Type);
public enum ErrorType { Validation, NotFound, Conflict, Gone, Failure }

public class Result { bool IsSuccess; Error Error; static Success(); static Failure(Error) }
public sealed class Result<T> : Result { T Value; implicit operator Result<T>(T); static Failure(Error) }

public interface ICommandHandler<in TCommand, TResult> { Task<Result<TResult>> Handle(TCommand c, CancellationToken ct); }
public interface IQueryHandler<in TQuery, TResult>     { Task<Result<TResult>> Handle(TQuery q, CancellationToken ct); }
```

- `LoggingDecorator<TIn,TOut>` implements both handler interfaces via two small decorators (`LoggingCommandDecorator`, `LoggingQueryDecorator`). Logs handler name, outcome (`IsSuccess`, `Error.Code`), duration. Never logs the request payload (URLs may be sensitive).
- DI helper `Decorate<TService, TDecorator>()` (hand-written, no Scrutor, as that would be a new package).
- `ErrorType → HTTP` mapping lives in each `Api` (`ResultExtensions.ToHttpResult()`), not in BuildingBlocks (it must not know ASP.NET Core).

| ErrorType | HTTP |
|---|---|
| Validation | 400 ProblemDetails (with `errors`) |
| NotFound | 404 |
| Conflict | 409 |
| Gone | 410 |
| Failure | 500 |

### 2.2 Domain

| Type | Rules |
|---|---|
| `ShortCode` (record struct, private ctor) | `TryParse(string?, out ShortCode)` / `Create(string) → Result<ShortCode>`. Length 7 (generated) or 8–30 (alias); chars `[A-Za-z0-9_-]` for 8–30, `[0-9A-Za-z]` for exactly 7. Case-sensitive. Reserved words rejected (case-insensitive) |
| `Alias` | Wraps `ShortCode` creation for user input: length **8–30**, `[A-Za-z0-9_-]`, not reserved |
| `OriginalUrl` | Absolute URI, scheme `http`/`https` only, max **2048** chars, no user-info (`user:pass@`), host present. Stored as given (no canonicalization, since there is no dedup) |
| `ShortUrl` (entity) | `ShortCode`, `OriginalUrl`, `CreatedAt`, `ExpiresAt?`, `CreatedBy?`. `static Create(code, url, now, expiresAt?)` enforces `expiresAt > now`. `IsExpired(DateTimeOffset now) => ExpiresAt <= now` |
| `DomainRules` (constants) | `GeneratedCodeLength = 7`, `AliasMinLength = 8`, `AliasMaxLength = 30`, `MaxUrlLength = 2048`, `ReservedWords` |

Decision: `ShortCode.TryParse` is the **read-path gate** (cheap, allocation-free, no Redis/DB for malformed input). Since generated codes use a stricter alphabet than aliases, one parser covers both: `len == 7 → base62 only`, `8..30 → alias charset`, else invalid.

Domain errors are static members (`ShortUrlErrors.AliasTaken`, `.InvalidUrl`, `.AliasReserved`, …) so handlers, tests and HTTP mapping share one vocabulary.

---

## 3. Write Service

### 3.1 `POST /urls`

```http
POST /urls
{ "originalUrl": "https://…", "alias": "my-custom-alias" /*opt*/, "expiresAt": "2027-01-01T00:00:00Z" /*opt*/ }

201 Created   Location: {PublicBaseUrl}/{code}   { "shortUrl": "https://short.example/Ab3xY9Z" }
400 ProblemDetails (validation)   409 alias taken   429 (gateway)
```

`PublicBaseUrl` is a validated option (`ValidateOnStart`, absolute https URL, no trailing slash handling bugs: normalise once).

**Handler flow** (`CreateShortUrlCommandHandler`):

1. (Validation decorator has already run.)
2. `now = timeProvider.GetUtcNow()`.
3. If `alias` given → `code = Alias.Create(alias)`; else `id = await allocator.NextAsync(ct)` → `code = obfuscator.ToCode(id)`. A generated code can, with probability ~1 in 3.5·10¹², equal a reserved word such as `swagger` or `openapi` (all 7 characters, valid base62): if `code.IsReserved`, allocate another ID. Aliases cannot hit this because of the 8-character minimum.
4. `ShortUrl.Create(...)` → `repository.AddAsync(...)`.
5. Repository returns `Result` (`Conflict` on PK violation `23505`).
6. Alias path: conflict → `AliasTaken` (409). Generated path: conflict → **allocate a fresh ID and retry, max 3 attempts**, then `Failure` (500) + error log (P4).
7. Return `CreateShortUrlResponse(code)`; the Api builds the URL from `PublicBaseUrl`.

Validator (FluentValidation, uses Domain constants): `originalUrl` required/valid; `alias` null or valid; `expiresAt` null or `> now` (uses `TimeProvider`) and not absurdly far (optional cap, e.g. 10 years).

### 3.2 Code generation

```text
id (long, 0 ≤ id < 62^7)  →  Permute(id)  →  Base62 (62^7 domain)  →  left-pad to 7 with '0'
```

- `Base62` alphabet fixed: `0-9A-Za-z`. `Encode(ulong, width)`, `Decode(string)`.
- **Permutation = Feistel network with cycle-walking.** `N = 62^7 = 3,521,614,606,208 < 2^42`. Run a balanced 4-round Feistel over 42 bits (21+21), round function = HMAC-SHA256(key_i, half) truncated to 21 bits. If the output ≥ N, feed it back in (cycle-walk) until it lands in `[0, N)`. Expected iterations ≈ 2^42 / N ≈ 1.25.
  This is a bijection on `[0, N)` by construction, so **no collisions** for distinct IDs.
- Keys: `Obfuscation:Key` (base64, ≥ 32 bytes) from configuration/secrets; four round keys derived with HKDF. `ValidateOnStart` rejects missing/short keys. Never logged.
- **Changing the key after launch re-maps the space and can collide with existing rows**: document it as "never rotate".
- Port: `ICodeObfuscator { ShortCode ToCode(long id); }` (Application). Implementation in Infrastructure (uses `System.Security.Cryptography`, so it is not in Domain).

Required tests (from `architecture.md`): `Decode(Encode(x)) == x`; always 7 chars; permutation is injective over a large range (e.g. 0..2M plus boundary values `N-1`); `id ≥ N` throws; different keys give different outputs; known-vector test to pin the algorithm.

### 3.3 Counter (`IIdAllocator`)

- `RedisIdAllocator`: holds `(next, end)` of the current batch. `NextAsync`: fast path `Interlocked`-free under a `SemaphoreSlim(1,1)`; when exhausted, `INCRBY counter 1000` → `end = result`, `next = end - 999`.
- Counter key `urlshortener:counter`. Start value: first batch begins at `1` (id 0 unused; acceptable).
- Guard: if any ID in the batch would be `≥ N` → throw `CounterExhaustedException` (fails loudly, health check goes unhealthy).
- Redis down → throws; handler returns `Failure` (500). Alias creation does **not** need Redis and keeps working (nice degradation property; test it).
- Metric `counter_batches_reserved` incremented per refill.
- Optional later: lower batch size or "refill ahead" at 80% to avoid latency spikes.

### 3.4 Persistence

`WriteDbContext` → table `short_urls` (snake_case via explicit config, no `UseSnakeCaseNamingConvention` package):

| Column | Type | Constraint |
|---|---|---|
| `short_code` | `varchar(30)` | PK |
| `original_url` | `text` | NOT NULL |
| `created_at` | `timestamptz` | NOT NULL |
| `expires_at` | `timestamptz` | NULL, **index** `ix_short_urls_expires_at` (partial: `WHERE expires_at IS NOT NULL`) |
| `created_by` | `text` | NULL |

- Value objects mapped with `HasConversion`. Collation for `short_code`: **`"C"`** so the PK comparison is byte-wise and case-sensitive (default ICU/glibc collations may not be); assert this in an integration test (`Abc12345` and `abc12345` can coexist).
- Unique violation detection: `DbUpdateException` with inner `PostgresException { SqlState: "23505" }`, wrapped in the repository (the only place that knows EF/Npgsql).
- Migrations applied at startup (`Database.MigrateAsync`) behind a feature flag; with several replicas use a Postgres advisory lock so two instances don't race. Initial migration `InitialCreate`.

### 3.5 Expired-URL cleanup

`ExpiredUrlCleanupService : BackgroundService`, interval from options (`Cleanup:Interval`, default 10 min), uses `PeriodicTimer`.
Each tick resolves a scope → `DeleteExpiredShortUrlsCommandHandler` → repository `DeleteExpiredAsync(now, batchSize: 1000, ct)` implemented as
`DELETE FROM short_urls WHERE short_code IN (SELECT short_code FROM short_urls WHERE expires_at <= @now LIMIT 1000)` (via `ExecuteSqlInterpolatedAsync`), looped until a batch returns `< 1000` rows. Exceptions are caught and logged per tick so the loop never dies. Metric `expired_urls_deleted`.
Multi-replica safe (idempotent deletes). Deleting a row that is still cached makes the Read Service serve a stale 302 until the TTL ends, bounded by `min(defaultTtl, expiresAt-now)` and acceptable (the entry is already expired and the Read Service checks `IsExpired`).

---

## 4. Read Service

### 4.1 `GET /{shortCode}`

`GetOriginalUrlQueryHandler`:

```text
ShortCode.TryParse fails          → 404 (no Redis, no DB)
cache.GetAsync(code)
  hit  → entry
  miss → repository.FindAsync(code) → null → 404
                                   → entry; cache.SetAsync(entry, ttl)   (skip if already expired)
entry.IsExpired(now) → 410, else 302 Location: originalUrl
```

- Endpoint route constraint: none (we want malformed → 404, not routing fallthrough). Reserved paths (`/health`, `/openapi`, …) are registered before the catch-all and are why those words are reserved.
- Response `302` with `Cache-Control: no-store` (so intermediaries do not cache a redirect we may later expire). **Never 301.**
- Metrics: `redirects{outcome}`, `cache_hits`, `cache_misses`.

### 4.2 Cache

- Port `IShortUrlCache { Task<CachedUrl?> GetAsync(ShortCode, ct); Task SetAsync(ShortCode, CachedUrl, TimeSpan ttl, ct); }`.
- Key `url:{short_code}`; value is a compact string `"{expiresAtUnixSeconds or -}|{originalUrl}"` (no JSON allocation on the hot path).
- TTL = `min(DefaultTtl, expiresAt - now)`; ≤ 0 → not cached. `DefaultTtl` option, default 24 h.
- **Redis failure handling**: every cache call is wrapped; on exception → log warning, treat as miss (and skip the write). Use a short `SyncTimeout`/`AsyncTimeout` (e.g. 100 ms) so a sick Redis cannot blow the < 100 ms budget. Consider a simple circuit breaker (skip Redis for N seconds after consecutive failures) in Phase 6.
- Cache stampede on a hot miss: acceptable at this scale; note optional single-flight per key (`ConcurrentDictionary<string, Task>`) as a later improvement.
- Negative cache: see P5.

### 4.3 Persistence

`ReadDbContext`: `UseQueryTrackingBehavior(NoTracking)`, entity mapped to the same table/columns/collation as Write, **no migrations**. Query is a projection (`Select(x => new { x.OriginalUrl, x.ExpiresAt })`, `AsNoTracking`), so it never materialises the whole entity. Connects as role `urlshortener_read` (`GRANT SELECT ON short_urls`), created by `deploy/postgres-init.sql`, which compose mounts at init.

Because the Domain `ShortUrl` is shared, decide in Phase 4 whether Read maps the entity or a read-model record (`CachedUrl`). **Recommendation: read-model record** in Read.Application. It keeps the Domain change surface small and avoids loading `CreatedAt/CreatedBy`.

---

## 5. Gateway (YARP)

| Route | Match | Cluster | Notes |
|---|---|---|---|
| `write` | `POST /urls` | `write-service` | Rate-limited policy `create-url` |
| `read` | `GET /{**code}` limited to a single segment | `read-service` | Lowest priority, after the reserved paths |
| `health` | `GET /health` | handled by the gateway itself | |

- Rate limit: `AddRateLimiter` fixed/sliding window partitioned by `RemoteIpAddress`, e.g. 30 req/min per IP, `OnRejected` → `429` + `Retry-After` + ProblemDetails.
- `ForwardedHeadersOptions` with `KnownProxies`/`KnownNetworks` configured via options (do **not** trust `X-Forwarded-For` from arbitrary clients, or the rate limit is bypassable).
- Destinations from config (`http://write-service:8080`, `http://read-service:8080`). Active health checks on `/health`.
- Request body size limit on `POST /urls` (e.g. 8 KB) at the gateway and in the Write Service.

---

## 6. Cross-cutting

### 6.1 ServiceDefaults

`AddServiceDefaults()` registers: OpenTelemetry (traces: AspNetCore, HttpClient, EF/Npgsql, Redis; metrics: runtime, AspNetCore, `Meter("UrlShortener")`; OTLP exporter via standard `OTEL_EXPORTER_OTLP_ENDPOINT`), `service.name` from `OTEL_SERVICE_NAME`, ProblemDetails, exception handler (`IExceptionHandler` → 500 ProblemDetails, no stack trace in non-Development), health checks (`/health` liveness, `/health/ready` with Postgres + Redis tags), OpenAPI + Scalar in Development only.

### 6.2 Health

- Write ready: Postgres + Redis + counter-not-exhausted. Read ready: Postgres only (Redis down must **not** make Read unready, since it degrades gracefully).

### 6.3 Security checklist

- Only `http`/`https` targets (blocks `javascript:`, `data:`, `file:`).
- Self-redirect loops (`originalUrl` host == our public host) rejected.
- No secrets in repo; permutation key + DB passwords via env/user-secrets.
- Read DB user is SELECT-only; the Write Service owns DDL (use a separate migration role only if it is cheap to add).
- Logs never include full connection strings or the permutation key.
- Out of scope (documented): malware/spam filtering, auth, abuse reporting.

### 6.4 Options (all `ValidateOnStart`)

`PublicBaseUrl`, `Obfuscation:Key`, `Counter:BatchSize` (1000), `Cleanup:Interval/BatchSize`, `Cache:DefaultTtl`, `ConnectionStrings:Postgres`, `ConnectionStrings:Redis`, `RateLimit:PermitLimit/Window`, `ForwardedHeaders:KnownProxies`.

---

## 7. Test strategy

| Layer | Project | Style | Examples |
|---|---|---|---|
| Domain | `Domain.UnitTests` | pure | `ShortCode`/`Alias`/`OriginalUrl` validity tables, `IsExpired` boundaries with `FakeTimeProvider` |
| Write app | `WriteService.UnitTests` | NSubstitute ports | alias conflict → 409; generated conflict retries then fails; expired date rejected; cleanup loop until short batch |
| Read app | `ReadService.UnitTests` | NSubstitute ports | malformed → no port calls (`DidNotReceive`); hit; miss+fill; expired → 410 and never cached; cache throws → still redirects |
| Infra / API | `*.IntegrationTests` | Testcontainers Postgres + Redis, `WebApplicationFactory` | PK violation → 409; collation case-sensitivity; counter concurrency (N parallel allocators → no duplicates); TTL on cache key; Read tests build schema from Write migrations |
| Architecture | `ArchitectureTests` | NetArchTest | all rules in `CLAUDE.md`, plus "Domain/Application have no reference to EF/Redis/ASP.NET/OTel" |
| E2E smoke | compose-based (CI optional) | HTTP | create → redirect → expire → 410 |

Shared test fixtures: one Postgres + one Redis container per test collection; `FakeTimeProvider` (Microsoft.Extensions.TimeProvider.Testing: confirm before adding).

---

## 8. Phased delivery (each phase is a PR; TDD inside every step)

| Phase | Scope | Exit criteria |
|---|---|---|
| **0 Skeleton** | `global.json`, `Directory.*.props`, solution, empty projects + references, `ArchitectureTests` (rules failing → passing), `.gitignore`, `git init`, `ci.yml` (build + test) | `dotnet build/test` green in CI; arch tests enforce dependency rules |
| **1 Domain + BuildingBlocks** | `Result`, errors, handler interfaces, logging decorators, `ShortCode`, `Alias`, `OriginalUrl`, `ShortUrl` | Domain unit tests green, 100 % branch coverage on value objects |
| **2 Code generation** | `Base62`, Feistel obfuscator, `IIdAllocator` + Redis impl | Bijection tests, concurrency test with real Redis |
| **3 Write Service** | `CreateShortUrlCommand`, validators + decorator, `WriteDbContext` + `InitialCreate`, endpoint, cleanup job | Integration: create generated + alias, 400/409/201, cleanup deletes expired |
| **4 Read Service** | Query, cache, `ReadDbContext`, endpoint, metrics | Integration: 302/404/410, cache fill + TTL, Redis-down fallback |
| **5 Gateway** | YARP routes, rate limiting, forwarded headers | Integration via `WebApplicationFactory`: 429 after limit, routing correct |
| **6 Observability & hardening** | ServiceDefaults OTel, Jaeger/Prometheus config (`--web.enable-otlp-receiver`), health checks, circuit breaker/negative cache decision (P5) | Traces span gateway→service→DB/Redis; metrics visible |
| **7 Packaging & deploy** | Dockerfiles, `docker-compose.yml`, `deploy/` (prod compose, update script, systemd timer, `.env.example`, `postgres-init.sql`), `publish.yml` | `docker compose up` works end to end; images land in GHCR |
| **8 Load check** | k6/bombardier script on the redirect path | Report p50/p99 with warm cache; confirm < 100 ms target locally |

Per CLAUDE.md workflow, before each step I state the service(s)/layer(s) touched and the failing tests I will write first.

---

## 9. Risks

| Risk | Mitigation |
|---|---|
| Domain change forces both services to redeploy | Keep Domain tiny; Read uses its own read model |
| Write migration breaks Read | Read integration tests build schema from Write migrations; CLAUDE.md reminder |
| Redis counter reset → duplicate IDs | AOF + PK-violation retry (P4) |
| Redis latency harms redirects | Short timeouts, fail-open to DB, optional breaker |
| Key rotation remaps codes | Documented as forbidden; key is fixed for the lifetime of the data |
| Permutation keys leaked in a public repo | Only in `deploy/.env` on the box, `.env.example` has placeholders |
| Postgres collation makes codes case-insensitive | Explicit `"C"` collation + test |
