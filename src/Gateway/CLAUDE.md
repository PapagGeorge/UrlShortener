# Gateway

YARP reverse proxy in front of the two services. The only thing exposed to clients.

## Routes (all in `UrlShortener.Gateway/appsettings.json`)

| Route | Match | Cluster | Notes |
|---|---|---|---|
| `create` | `POST /urls` | `write` | Rate limited (policy `create-url`), body capped at 8 KB (`413`), order 0 |
| `redirect` | `GET`/`HEAD /{code}` where `code` matches `[A-Za-z0-9_-]{7,30}` | `read` | Never rate limited, order 10 |
| (built in) | `GET /health` | the Gateway itself | A literal route with the default order, so it wins over `/{code}` |

Everything else is a `404`/`405` **from the Gateway** and never reaches a backend (e.g. `GET /urls`, `POST /abc1234`, `/a/b`, a 6-character path). Only two routes exist; `ConfigurationTests` pins the shipped config.

## Rate limiting

Fixed window per client address on `POST /urls` only: **30 per minute** by default (`RateLimit:PermitLimit`, `RateLimit:Window`). Over the limit: `429` + `Retry-After` + problem body (`code: RateLimit.Exceeded`). IPv6 clients share one bucket per `/64`; IPv4-mapped IPv6 counts as IPv4.

The limiter is in memory, so it is per Gateway instance (fine for one instance; use a shared store before running several).

## Forwarded headers: the empty-list trap

`ForwardedHeaders:KnownProxies` / `KnownNetworks` (CIDR) list the proxies **in front of the Gateway** whose `X-Forwarded-For` is believed. **Both are empty by default**: forwarded headers are ignored and the socket address is the client.

ASP.NET Core treats two *empty* lists as "trust every sender", which would let anyone spoof their IP and walk around the rate limit. So with nothing configured the middleware is switched off (`ForwardedHeaders.None`). A network that covers everything (`0.0.0.0/0`, `::/0`) is rejected at startup. Do not "simplify" this.

Requests are forwarded with `X-Forwarded-*` **set** (not appended), so a client's own `X-Forwarded-For` never reaches the backends.

## Resilience

Active health checks hit each backend's `/health` every 10 s. Request timeouts: `write` 15 s, `read` 5 s (a redirect must be fast). A dead backend gives a `502`/`503`/`504`, never an internal address.

## Configuration

| Key | Default | Notes |
|---|---|---|
| `RateLimit:PermitLimit` / `RateLimit:Window` | 30 / `00:01:00` | Validated at startup. Careful with `TimeSpan`: `24:00:00` is 24 *days* |
| `ForwardedHeaders:KnownProxies` / `:KnownNetworks` | empty | Set on the box to the proxy in front of the Gateway |
| `ReverseProxy:Clusters:*:Destinations:*:Address` | `http://write-service:8080/`, `http://read-service:8080/` | Compose service names |

## Tests (no Docker)

`tests/UrlShortener.Gateway.Tests` runs the real Gateway against two real HTTP servers on localhost (`FakeBackend`). The body-size limit test uses real Kestrel, because the in-memory TestServer has no request-size feature and would not enforce it.

```bash
dotnet test tests/UrlShortener.Gateway.Tests
```
