using UrlShortener.Domain;

namespace UrlShortener.WriteService.Application.Ports;

public interface IShortUrlWriteRepository
{
    /// <summary>Stores the URL. Returns <c>false</c> (instead of throwing) when the short code already exists.</summary>
    Task<bool> TryAddAsync(ShortUrl shortUrl, CancellationToken ct);

    /// <summary>Deletes up to <paramref name="batchSize"/> rows whose expiration is at or before <paramref name="now"/>; returns how many were deleted.</summary>
    Task<int> DeleteExpiredAsync(DateTimeOffset now, int batchSize, CancellationToken ct);
}
