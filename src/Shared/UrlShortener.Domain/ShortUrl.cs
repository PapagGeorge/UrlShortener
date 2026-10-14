using UrlShortener.BuildingBlocks.Results;

namespace UrlShortener.Domain;

/// <summary>The mapping from a <see cref="Domain.ShortCode"/> to an <see cref="Domain.OriginalUrl"/>, with an optional expiration.</summary>
public sealed class ShortUrl
{
    private ShortUrl(ShortCode shortCode, OriginalUrl originalUrl, DateTimeOffset createdAt, DateTimeOffset? expiresAt, string? createdBy)
    {
        ShortCode = shortCode;
        OriginalUrl = originalUrl;
        CreatedAt = createdAt;
        ExpiresAt = expiresAt;
        CreatedBy = createdBy;
    }

    public ShortCode ShortCode { get; }

    public OriginalUrl OriginalUrl { get; }

    public DateTimeOffset CreatedAt { get; }

    public DateTimeOffset? ExpiresAt { get; }

    public string? CreatedBy { get; }

    /// <param name="now">Supplied by the caller (from <see cref="TimeProvider"/>); the Domain never reads the clock.</param>
    public static Result<ShortUrl> Create(
        ShortCode shortCode,
        OriginalUrl originalUrl,
        DateTimeOffset now,
        DateTimeOffset? expiresAt,
        string? createdBy = null)
    {
        if (expiresAt is not null && expiresAt <= now)
        {
            return DomainErrors.ExpirationInPast;
        }

        // Always UTC (offset 0): the database column is timestamptz and Npgsql rejects any other offset.
        return new ShortUrl(shortCode, originalUrl, now.ToUniversalTime(), expiresAt?.ToUniversalTime(), createdBy);
    }

    /// <summary>A URL is expired from the exact moment of <see cref="ExpiresAt"/>.</summary>
    public bool IsExpired(DateTimeOffset now) => IsExpired(ExpiresAt, now);

    /// <summary>The single expiry rule, also used by read models that never load the whole entity.</summary>
    public static bool IsExpired(DateTimeOffset? expiresAt, DateTimeOffset now) => expiresAt is not null && expiresAt <= now;
}
