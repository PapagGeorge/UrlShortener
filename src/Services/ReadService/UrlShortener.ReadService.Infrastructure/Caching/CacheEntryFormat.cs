using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using UrlShortener.ReadService.Application;

namespace UrlShortener.ReadService.Infrastructure.Caching;

/// <summary>
/// The Redis value for a redirect: <c>{expiresAtUnixMilliseconds or -}|{originalUrl}</c>.
/// A plain string avoids serializer allocations on the hot path. The URL may itself contain '|', so only the first one separates.
/// </summary>
internal static class CacheEntryFormat
{
    private const char Separator = '|';
    private const string NoExpiration = "-";

    public static string Serialize(RedirectTarget target)
    {
        var expiry = target.ExpiresAt is { } expiresAt
            ? expiresAt.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture)
            : NoExpiration;

        return $"{expiry}{Separator}{target.OriginalUrl}";
    }

    /// <summary>A corrupt or unexpected value is reported as a failure, so the caller treats it as a cache miss.</summary>
    public static bool TryParse(string? value, [NotNullWhen(true)] out RedirectTarget? target)
    {
        target = null;
        if (value is null)
        {
            return false;
        }

        var separator = value.IndexOf(Separator);
        if (separator <= 0 || separator == value.Length - 1)
        {
            return false;
        }

        var expiryText = value.AsSpan(0, separator);
        DateTimeOffset? expiresAt = null;
        if (!expiryText.SequenceEqual(NoExpiration))
        {
            if (!long.TryParse(expiryText, NumberStyles.None, CultureInfo.InvariantCulture, out var milliseconds))
            {
                return false;
            }

            expiresAt = DateTimeOffset.FromUnixTimeMilliseconds(milliseconds);
        }

        target = new RedirectTarget(value[(separator + 1)..], expiresAt);
        return true;
    }
}
