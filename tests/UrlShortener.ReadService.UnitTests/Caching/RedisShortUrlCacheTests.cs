using Microsoft.Extensions.Logging;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using StackExchange.Redis;
using UrlShortener.Domain;
using UrlShortener.ReadService.Application;
using UrlShortener.ReadService.Infrastructure.Caching;

namespace UrlShortener.ReadService.UnitTests.Caching;

public sealed class RedisShortUrlCacheTests
{
    private static readonly ShortCode Code = ShortCode.Create("0aZ9xYb").Value;
    private static readonly RedirectTarget Target = new("https://example.com/a", new DateTimeOffset(2027, 1, 1, 0, 0, 0, TimeSpan.Zero));

    private readonly IDatabase _database = Substitute.For<IDatabase>();
    private readonly TestLogger<RedisShortUrlCache> _logger = new();
    private readonly RedisShortUrlCache _sut;

    public RedisShortUrlCacheTests()
    {
        var connection = Substitute.For<IConnectionMultiplexer>();
        connection.GetDatabase(Arg.Any<int>(), Arg.Any<object?>()).Returns(_database);
        _sut = new RedisShortUrlCache(connection, _logger);
    }

    private void StoredValue(RedisValue value) =>
        _database.StringGetAsync(Arg.Any<RedisKey>(), Arg.Any<CommandFlags>()).Returns(value);

    // ---- reads ----

    [Fact]
    public async Task GetAsync_UsesTheDocumentedKeyAndPreservesCase()
    {
        StoredValue(RedisValue.Null);

        await _sut.GetAsync(ShortCode.Create("AbCdEfGh").Value, default);

        await _database.Received(1).StringGetAsync((RedisKey)"url:AbCdEfGh", Arg.Any<CommandFlags>());
    }

    [Fact]
    public async Task GetAsync_ExistingEntry_ReturnsTheTarget()
    {
        StoredValue(CacheEntryFormat.Serialize(Target));

        var result = await _sut.GetAsync(Code, default);

        result.ShouldBe(Target);
    }

    [Fact]
    public async Task GetAsync_MissingKey_ReturnsNullWithoutWarning()
    {
        StoredValue(RedisValue.Null);

        (await _sut.GetAsync(Code, default)).ShouldBeNull();
        _logger.Entries.ShouldBeEmpty();
    }

    [Fact]
    public async Task GetAsync_CorruptValue_IsAMissAndIsLogged()
    {
        StoredValue("garbage-without-separator");

        (await _sut.GetAsync(Code, default)).ShouldBeNull();
        _logger.Entries.ShouldHaveSingleItem().Level.ShouldBe(LogLevel.Warning);
    }

    [Fact]
    public async Task GetAsync_WhenRedisIsDown_IsAMissAndLogsAWarningInsteadOfThrowing()
    {
        var failure = new RedisException("redis is down");
        _database.StringGetAsync(Arg.Any<RedisKey>(), Arg.Any<CommandFlags>()).Throws(failure);

        (await _sut.GetAsync(Code, default)).ShouldBeNull();

        var entry = _logger.Entries.ShouldHaveSingleItem();
        entry.Level.ShouldBe(LogLevel.Warning);
        entry.Exception.ShouldBeSameAs(failure);
    }

    [Fact]
    public async Task GetAsync_WhenRedisTimesOut_IsAMiss()
    {
        _database.StringGetAsync(Arg.Any<RedisKey>(), Arg.Any<CommandFlags>()).Throws(new TimeoutException("slow"));

        (await _sut.GetAsync(Code, default)).ShouldBeNull();
    }

    [Fact]
    public async Task GetAsync_WhenTheCallerCancels_StillCancels()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        _database.StringGetAsync(Arg.Any<RedisKey>(), Arg.Any<CommandFlags>()).Returns(new TaskCompletionSource<RedisValue>().Task);

        await Should.ThrowAsync<OperationCanceledException>(() => _sut.GetAsync(Code, cts.Token));
    }

    // ---- writes ----

    [Fact]
    public async Task SetAsync_WritesTheSerializedEntryWithTheTtl()
    {
        var ttl = TimeSpan.FromMinutes(90);

        await _sut.SetAsync(Code, Target, ttl, default);

        await _database.Received(1).StringSetAsync(
            (RedisKey)"url:0aZ9xYb",
            (RedisValue)CacheEntryFormat.Serialize(Target),
            ttl,
            When.Always,
            CommandFlags.None);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public async Task SetAsync_NonPositiveTtl_WritesNothing(int seconds)
    {
        await _sut.SetAsync(Code, Target, TimeSpan.FromSeconds(seconds), default);

        _database.ReceivedCalls().ShouldBeEmpty();
    }

    [Fact]
    public async Task SetAsync_WhenRedisIsDown_DoesNotThrowAndLogsAWarning()
    {
        _database.StringSetAsync(Arg.Any<RedisKey>(), Arg.Any<RedisValue>(), Arg.Any<TimeSpan?>(), Arg.Any<When>(), Arg.Any<CommandFlags>())
            .Throws(new RedisException("connection reset"));

        await Should.NotThrowAsync(() => _sut.SetAsync(Code, Target, TimeSpan.FromHours(1), default));

        _logger.Entries.ShouldHaveSingleItem().Level.ShouldBe(LogLevel.Warning);
    }
}
