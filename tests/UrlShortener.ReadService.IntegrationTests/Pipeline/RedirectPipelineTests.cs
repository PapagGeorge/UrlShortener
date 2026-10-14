using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using StackExchange.Redis;
using UrlShortener.BuildingBlocks.Results;
using UrlShortener.ReadService.Application;
using UrlShortener.ReadService.Application.GetOriginalUrl;
using UrlShortener.ReadService.Infrastructure.Caching;
using UrlShortener.ReadService.Infrastructure.Persistence;

namespace UrlShortener.ReadService.IntegrationTests.Pipeline;

/// <summary>The real query handler over the real repository (Postgres, SELECT-only user) and the real cache (Redis).</summary>
[Trait("Category", "Integration")]
[Collection(ReadStackCollection.Name)]
public sealed class RedirectPipelineTests(ReadStackFixture stack) : IAsyncLifetime
{
    private static readonly TimeSpan DefaultTtl = TimeSpan.FromHours(24);

    private readonly FakeTimeProvider _time = new(DateTimeOffset.UtcNow);

    public Task InitializeAsync() => stack.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    private async Task<Result<GetOriginalUrlResult>> Query(string? code, IConnectionMultiplexer? redis = null)
    {
        await using var context = stack.CreateReadContext();
        var handler = new GetOriginalUrlQueryHandler(
            new RedisShortUrlCache(redis ?? stack.Redis, NullLogger<RedisShortUrlCache>.Instance),
            new ShortUrlReadRepository(context),
            _time,
            new RedirectCachePolicy(DefaultTtl));

        return await handler.Handle(new GetOriginalUrlQuery(code), default);
    }

    private static async Task<bool> Cached(IConnectionMultiplexer redis, string code) =>
        await redis.GetDatabase().KeyExistsAsync($"url:{code}");

    [Fact]
    public async Task FirstRead_GoesToTheDatabaseAndFillsTheCache()
    {
        await stack.SeedAsync("0aZ9xYb", "https://example.com/target");

        var result = await Query("0aZ9xYb");

        result.Value.OriginalUrl.ShouldBe("https://example.com/target");
        (await Cached(stack.Redis, "0aZ9xYb")).ShouldBeTrue();
    }

    [Fact]
    public async Task SecondRead_IsServedFromTheCache_EvenIfTheRowIsGone()
    {
        await stack.SeedAsync("0aZ9xYb", "https://example.com/target");
        await Query("0aZ9xYb");

        await stack.DeleteRowAsync("0aZ9xYb");

        (await Query("0aZ9xYb")).Value.OriginalUrl.ShouldBe("https://example.com/target");
    }

    [Fact]
    public async Task CacheTtl_IsCappedByTheUrlExpiration()
    {
        await stack.SeedAsync("0aZ9xYb", "https://example.com/target", expiresAt: _time.GetUtcNow().AddHours(1));

        await Query("0aZ9xYb");

        var ttl = await stack.Redis.GetDatabase().KeyTimeToLiveAsync("url:0aZ9xYb");
        ttl.ShouldNotBeNull();
        ttl.Value.ShouldBeLessThanOrEqualTo(TimeSpan.FromHours(1));
        ttl.Value.ShouldBeGreaterThan(TimeSpan.FromMinutes(59));
    }

    [Fact]
    public async Task CacheTtl_IsTheDefaultWhenTheUrlNeverExpires()
    {
        await stack.SeedAsync("0aZ9xYb", "https://example.com/target");

        await Query("0aZ9xYb");

        var ttl = await stack.Redis.GetDatabase().KeyTimeToLiveAsync("url:0aZ9xYb");
        ttl!.Value.ShouldBeGreaterThan(DefaultTtl - TimeSpan.FromMinutes(1));
    }

    [Fact]
    public async Task ExpiredUrl_IsGoneAndNeverCached()
    {
        await stack.SeedAsync("0aZ9xYb", "https://example.com/old", expiresAt: _time.GetUtcNow().AddMinutes(5), createdAt: _time.GetUtcNow());
        _time.Advance(TimeSpan.FromMinutes(10));

        var result = await Query("0aZ9xYb");

        result.Error.Type.ShouldBe(ErrorType.Gone);
        (await Cached(stack.Redis, "0aZ9xYb")).ShouldBeFalse();
    }

    [Fact]
    public async Task CachedUrl_ThatExpiresLater_BecomesGone()
    {
        await stack.SeedAsync("0aZ9xYb", "https://example.com/soon", expiresAt: _time.GetUtcNow().AddMinutes(5));
        (await Query("0aZ9xYb")).IsSuccess.ShouldBeTrue();

        _time.Advance(TimeSpan.FromMinutes(6));

        (await Query("0aZ9xYb")).Error.Type.ShouldBe(ErrorType.Gone);
    }

    [Fact]
    public async Task UnknownCode_IsNotFoundAndNotCached()
    {
        var result = await Query("0000000");

        result.Error.Type.ShouldBe(ErrorType.NotFound);
        (await Cached(stack.Redis, "0000000")).ShouldBeFalse();
    }

    [Theory]
    [InlineData("not a code")]
    [InlineData("abc")]
    public async Task MalformedCode_IsNotFoundWithoutCreatingAnyCacheKey(string code)
    {
        var result = await Query(code);

        result.Error.Type.ShouldBe(ErrorType.NotFound);
        (await stack.Redis.GetServer(stack.Redis.GetEndPoints()[0]).DatabaseSizeAsync()).ShouldBe(0L);
    }

    [Fact]
    public async Task CasingMatters_EndToEnd()
    {
        await stack.SeedAsync("Abcdefgh", "https://example.com/upper");

        (await Query("Abcdefgh")).IsSuccess.ShouldBeTrue();
        (await Query("abcdefgh")).Error.Type.ShouldBe(ErrorType.NotFound);
    }

    [Fact]
    public async Task WhenRedisIsDown_RedirectsStillWorkFromTheDatabase()
    {
        await stack.SeedAsync("0aZ9xYb", "https://example.com/target");
        var options = ConfigurationOptions.Parse("redis-down.invalid:6379");
        options.AbortOnConnectFail = false;
        options.ConnectTimeout = 500;
        options.AsyncTimeout = 200;
        await using var unreachable = await ConnectionMultiplexer.ConnectAsync(options);

        var result = await Query("0aZ9xYb", unreachable);

        result.Value.OriginalUrl.ShouldBe("https://example.com/target");
    }
}
