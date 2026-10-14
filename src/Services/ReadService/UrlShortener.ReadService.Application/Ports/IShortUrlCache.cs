using UrlShortener.Domain;

namespace UrlShortener.ReadService.Application.Ports;

/// <summary>
/// A cache-aside store of redirects. Implementations must <b>not throw</b> when the cache is merely unavailable:
/// a read becomes a miss and a write is skipped (with a warning), so a Redis outage never breaks a redirect.
/// </summary>
public interface IShortUrlCache
{
    Task<RedirectTarget?> GetAsync(ShortCode code, CancellationToken ct);

    Task SetAsync(ShortCode code, RedirectTarget target, TimeSpan ttl, CancellationToken ct);
}
