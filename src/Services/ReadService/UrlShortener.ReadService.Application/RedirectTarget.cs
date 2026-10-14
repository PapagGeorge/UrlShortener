using UrlShortener.Domain;

namespace UrlShortener.ReadService.Application;

/// <summary>
/// What a redirect needs and nothing more (no <c>CreatedAt</c>/<c>CreatedBy</c>), so the hot path loads and caches as little as possible.
/// The URL was validated when it was created, so it is not re-validated on every read.
/// </summary>
public sealed record RedirectTarget(string OriginalUrl, DateTimeOffset? ExpiresAt)
{
    public bool IsExpired(DateTimeOffset now) => ShortUrl.IsExpired(ExpiresAt, now);
}
