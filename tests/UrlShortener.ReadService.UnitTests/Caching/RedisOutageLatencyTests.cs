using System.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using StackExchange.Redis;
using UrlShortener.Domain;
using UrlShortener.ReadService.Application;
using UrlShortener.ReadService.Infrastructure;
using UrlShortener.ReadService.Infrastructure.Caching;

namespace UrlShortener.ReadService.UnitTests.Caching;

/// <summary>
/// A Redis outage must cost a redirect (almost) nothing. By default StackExchange.Redis queues commands while disconnected and fails them
/// about a second later regardless of the timeout, which would make every redirect ~2 s slower. This needs no Docker: the host never resolves.
/// </summary>
public sealed class RedisOutageLatencyTests
{
    private static readonly ShortCode Code = ShortCode.Create("0aZ9xYb").Value;

    [Fact]
    public void RedisOptions_FailFastWhileDisconnected()
    {
        ReadInfrastructureOptions.RedisOptions("redis.invalid:6379", new ReadCacheOptions()).BacklogPolicy.ShouldBe(BacklogPolicy.FailFast);
    }

    [Fact]
    public async Task UnreachableRedis_AddsNegligibleLatencyToACacheReadAndWrite()
    {
        var options = ReadInfrastructureOptions.RedisOptions("redis-down.invalid:6379", new ReadCacheOptions());
        await using var connection = await ConnectionMultiplexer.ConnectAsync(options);
        var cache = new RedisShortUrlCache(connection, NullLogger<RedisShortUrlCache>.Instance);

        var elapsed = new List<TimeSpan>();
        for (var i = 0; i < 3; i++)
        {
            var started = Stopwatch.GetTimestamp();
            (await cache.GetAsync(Code, default)).ShouldBeNull();
            await cache.SetAsync(Code, new RedirectTarget("https://example.com", null), TimeSpan.FromHours(1), default);
            elapsed.Add(Stopwatch.GetElapsedTime(started));
        }

        // Far below the ~2 s the default backlog behaviour costs; generous enough for a slow CI machine.
        elapsed.ShouldAllBe(time => time < TimeSpan.FromMilliseconds(400), $"took {string.Join(", ", elapsed.Select(t => $"{t.TotalMilliseconds:F0} ms"))}");
    }
}
