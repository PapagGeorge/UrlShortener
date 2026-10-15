using UrlShortener.BuildingBlocks.Messaging;
using UrlShortener.BuildingBlocks.Results;
using UrlShortener.Domain;
using UrlShortener.ReadService.Application.Ports;

namespace UrlShortener.ReadService.Application.GetOriginalUrl;

/// <summary>
/// The hot path. Flow: parse (malformed → 404 with no I/O) → cache → database on a miss → fill the cache → expiry check.
/// </summary>
public sealed class GetOriginalUrlQueryHandler(
    IShortUrlCache cache,
    IShortUrlReadRepository repository,
    TimeProvider time,
    RedirectCachePolicy policy,
    RedirectMetrics metrics) : IQueryHandler<GetOriginalUrlQuery, GetOriginalUrlResult>
{
    public async Task<Result<GetOriginalUrlResult>> Handle(GetOriginalUrlQuery query, CancellationToken ct)
    {
        if (!ShortCode.TryParse(query.ShortCode, out var code))
        {
            return ReadErrors.NotFound;
        }

        var now = time.GetUtcNow();

        var cached = await cache.GetAsync(code, ct);
        if (cached is not null)
        {
            metrics.CacheHit();
            // The TTL normally evicts an entry at its expiry, but never rely on that alone.
            return cached.IsExpired(now) ? ReadErrors.Expired : new GetOriginalUrlResult(cached.OriginalUrl);
        }

        metrics.CacheMiss();
        var target = await repository.FindAsync(code, ct);
        if (target is null)
        {
            return ReadErrors.NotFound;
        }

        if (target.IsExpired(now))
        {
            return ReadErrors.Expired;   // never cache an expired entry
        }

        await cache.SetAsync(code, target, TimeToLive(target, now), ct);
        return new GetOriginalUrlResult(target.OriginalUrl);
    }

    /// <summary>min(default TTL, time until expiry): the cache never outlives the URL.</summary>
    private TimeSpan TimeToLive(RedirectTarget target, DateTimeOffset now) =>
        target.ExpiresAt is { } expiresAt && expiresAt - now < policy.DefaultTtl ? expiresAt - now : policy.DefaultTtl;
}
