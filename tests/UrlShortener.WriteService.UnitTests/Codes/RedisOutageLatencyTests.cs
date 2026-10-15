using System.Diagnostics;
using StackExchange.Redis;
using UrlShortener.WriteService.Infrastructure.Codes;

namespace UrlShortener.WriteService.UnitTests.Codes;

/// <summary>
/// With Redis down, a generated-code request must fail at once instead of queueing for about a second per attempt.
/// No Docker needed: the host never resolves.
/// </summary>
public sealed class RedisOutageLatencyTests
{
    [Fact]
    public void RedisOptions_FailFastWhileDisconnected_AndNeverAbortStartup()
    {
        var options = WriteRedisOptions.Create("redis.invalid:6379");

        options.BacklogPolicy.ShouldBe(BacklogPolicy.FailFast);
        options.AbortOnConnectFail.ShouldBeFalse();
    }

    [Fact]
    public void RedisOptions_KeepTheTimeoutsOfTheConnectionString()
    {
        WriteRedisOptions.Create("redis.invalid:6379,connectTimeout=1234").ConnectTimeout.ShouldBe(1234);
    }

    [Fact]
    public async Task UnreachableRedis_FailsTheCounterReservationAtOnce()
    {
        await using var connection = await ConnectionMultiplexer.ConnectAsync(WriteRedisOptions.Create("redis-down.invalid:6379,connectTimeout=300"));
        var store = new RedisCounterStore(connection, "urlshortener:counter");
        await Should.ThrowAsync<RedisException>(async () => await store.ReserveAsync(1000, default));   // warm-up: the first attempt may pay a one-off cost

        var started = Stopwatch.GetTimestamp();
        await Should.ThrowAsync<RedisException>(async () => await store.ReserveAsync(1000, default));
        var elapsed = Stopwatch.GetElapsedTime(started);

        elapsed.ShouldBeLessThan(TimeSpan.FromMilliseconds(300), $"a failed reservation took {elapsed.TotalMilliseconds:F0} ms");
    }
}
