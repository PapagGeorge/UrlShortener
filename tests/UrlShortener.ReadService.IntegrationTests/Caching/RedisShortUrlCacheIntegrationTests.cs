using Microsoft.Extensions.Logging.Abstractions;
using StackExchange.Redis;
using UrlShortener.Domain;
using UrlShortener.ReadService.Application;
using UrlShortener.ReadService.Infrastructure.Caching;

namespace UrlShortener.ReadService.IntegrationTests.Caching;

[Trait("Category", "Integration")]
[Collection(ReadStackCollection.Name)]
public sealed class RedisShortUrlCacheIntegrationTests(ReadStackFixture stack) : IAsyncLifetime
{
    private static readonly ShortCode Code = ShortCode.Create("0aZ9xYb").Value;

    public Task InitializeAsync() => stack.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    private RedisShortUrlCache Cache() => new(stack.Redis, NullLogger<RedisShortUrlCache>.Instance);

    [Fact]
    public async Task SetThenGet_RoundTripsTheTarget()
    {
        var target = new RedirectTarget("https://example.com/a|b?x=1", new DateTimeOffset(2030, 1, 2, 3, 4, 5, 678, TimeSpan.Zero));

        await Cache().SetAsync(Code, target, TimeSpan.FromHours(1), default);

        (await Cache().GetAsync(Code, default)).ShouldBe(target);
    }

    [Fact]
    public async Task Set_StoresUnderTheDocumentedKeyAndFormat()
    {
        await Cache().SetAsync(Code, new RedirectTarget("https://e.com", null), TimeSpan.FromHours(1), default);

        (await stack.Redis.GetDatabase().StringGetAsync("url:0aZ9xYb")).ToString().ShouldBe("-|https://e.com");
    }

    [Fact]
    public async Task Set_AppliesTheTtl()
    {
        await Cache().SetAsync(Code, new RedirectTarget("https://e.com", null), TimeSpan.FromMinutes(30), default);

        var ttl = await stack.Redis.GetDatabase().KeyTimeToLiveAsync("url:0aZ9xYb");

        ttl.ShouldNotBeNull();
        ttl.Value.ShouldBeInRange(TimeSpan.FromMinutes(29), TimeSpan.FromMinutes(30));
    }

    [Fact]
    public async Task Entry_DisappearsOnceItsTtlHasPassed()
    {
        await Cache().SetAsync(Code, new RedirectTarget("https://e.com", null), TimeSpan.FromSeconds(1), default);

        await Task.Delay(TimeSpan.FromSeconds(2));

        (await Cache().GetAsync(Code, default)).ShouldBeNull();
    }

    [Fact]
    public async Task Get_UnknownCode_ReturnsNull()
    {
        (await Cache().GetAsync(Code, default)).ShouldBeNull();
    }

    [Fact]
    public async Task Keys_AreCaseSensitive()
    {
        await Cache().SetAsync(ShortCode.Create("Abcdefgh").Value, new RedirectTarget("https://upper.example", null), TimeSpan.FromHours(1), default);

        (await Cache().GetAsync(ShortCode.Create("abcdefgh").Value, default)).ShouldBeNull();
        (await Cache().GetAsync(ShortCode.Create("Abcdefgh").Value, default)).ShouldNotBeNull();
    }

    [Fact]
    public async Task CorruptValueInRedis_IsAMiss()
    {
        await stack.Redis.GetDatabase().StringSetAsync("url:0aZ9xYb", "not-a-valid-entry");

        (await Cache().GetAsync(Code, default)).ShouldBeNull();
    }

    [Fact]
    public async Task WhenRedisIsUnreachable_ReadsAreMissesAndWritesAreSkippedQuickly()
    {
        var options = ConfigurationOptions.Parse("redis-down.invalid:6379");
        options.AbortOnConnectFail = false;
        options.ConnectTimeout = 500;
        options.AsyncTimeout = 200;
        await using var unreachable = await ConnectionMultiplexer.ConnectAsync(options);
        var cache = new RedisShortUrlCache(unreachable, NullLogger<RedisShortUrlCache>.Instance);

        var started = DateTime.UtcNow;
        (await cache.GetAsync(Code, default)).ShouldBeNull();
        await Should.NotThrowAsync(() => cache.SetAsync(Code, new RedirectTarget("https://e.com", null), TimeSpan.FromHours(1), default));

        (DateTime.UtcNow - started).ShouldBeLessThan(TimeSpan.FromSeconds(3));
    }
}
