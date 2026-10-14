using Microsoft.Extensions.Logging;
using StackExchange.Redis;
using UrlShortener.Domain;
using UrlShortener.ReadService.Application;
using UrlShortener.ReadService.Application.Ports;

namespace UrlShortener.ReadService.Infrastructure.Caching;

/// <summary>
/// Cache-aside store in Redis, key <c>url:{short_code}</c> (case preserved).
/// A Redis outage, a timeout or a corrupt value is a miss plus a warning, never an exception:
/// the redirect must not fail because of the cache. Only the caller's own cancellation propagates.
/// </summary>
public sealed partial class RedisShortUrlCache(IConnectionMultiplexer connection, ILogger<RedisShortUrlCache> logger) : IShortUrlCache
{
    public async Task<RedirectTarget?> GetAsync(ShortCode code, CancellationToken ct)
    {
        try
        {
            var value = await connection.GetDatabase().StringGetAsync(Key(code)).WaitAsync(ct);
            if (value.IsNull)
            {
                return null;
            }

            if (CacheEntryFormat.TryParse(value.ToString(), out var target))
            {
                return target;
            }

            LogCorruptEntry(code.Value);
            return null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogCacheUnavailable("read", ex);
            return null;
        }
    }

    public async Task SetAsync(ShortCode code, RedirectTarget target, TimeSpan ttl, CancellationToken ct)
    {
        if (ttl <= TimeSpan.Zero)
        {
            return;   // already expired: never cache it
        }

        try
        {
            await connection.GetDatabase()
                .StringSetAsync(Key(code), CacheEntryFormat.Serialize(target), ttl, When.Always, CommandFlags.None)
                .WaitAsync(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogCacheUnavailable("write", ex);
        }
    }

    private static RedisKey Key(ShortCode code) => $"url:{code.Value}";

    [LoggerMessage(Level = LogLevel.Warning, Message = "Redis cache {Operation} failed; serving without the cache")]
    private partial void LogCacheUnavailable(string operation, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Ignoring a corrupt cache entry for {ShortCode}")]
    private partial void LogCorruptEntry(string shortCode);
}
