using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using UrlShortener.BuildingBlocks.Results;
using UrlShortener.Domain;
using UrlShortener.ReadService.Application;
using UrlShortener.ReadService.Application.GetOriginalUrl;
using UrlShortener.ReadService.Application.Ports;

namespace UrlShortener.ReadService.UnitTests.Application;

public sealed class GetOriginalUrlQueryHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 14, 12, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan DefaultTtl = TimeSpan.FromHours(24);
    private const string Code = "0aZ9xYb";
    private const string LongUrl = "https://example.com/some/very/long/url";

    private readonly IShortUrlCache _cache = Substitute.For<IShortUrlCache>();
    private readonly IShortUrlReadRepository _repository = Substitute.For<IShortUrlReadRepository>();
    private readonly GetOriginalUrlQueryHandler _sut;

    public GetOriginalUrlQueryHandlerTests()
    {
        _sut = new GetOriginalUrlQueryHandler(_cache, _repository, new FakeTimeProvider(Now), new RedirectCachePolicy(DefaultTtl));
        _cache.GetAsync(Arg.Any<ShortCode>(), Arg.Any<CancellationToken>()).Returns((RedirectTarget?)null);
        _repository.FindAsync(Arg.Any<ShortCode>(), Arg.Any<CancellationToken>()).Returns((RedirectTarget?)null);
    }

    private static ShortCode ParsedCode(string value = Code) => ShortCode.Create(value).Value;

    private static RedirectTarget Target(DateTimeOffset? expiresAt = null) => new(LongUrl, expiresAt);

    private Task<Result<GetOriginalUrlResult>> Query(string? code = Code, CancellationToken ct = default) =>
        _sut.Handle(new GetOriginalUrlQuery(code), ct);

    // ---- malformed input never reaches Redis or the database ----

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("has space!!")]
    [InlineData("abc-def")]                              // 7 chars but not base62
    [InlineData("1234567890123456789012345678901")]      // 31 chars
    public async Task Handle_MalformedCode_IsNotFoundWithoutTouchingTheCacheOrTheDatabase(string? code)
    {
        var result = await Query(code);

        result.Error.Code.ShouldBe("ShortUrl.NotFound");
        result.Error.Type.ShouldBe(ErrorType.NotFound);
        await _cache.DidNotReceiveWithAnyArgs().GetAsync(default, default);
        await _repository.DidNotReceiveWithAnyArgs().FindAsync(default, default);
    }

    // ---- cache hit ----

    [Fact]
    public async Task Handle_CacheHit_ReturnsTheUrlWithoutTheDatabaseOrAnotherCacheWrite()
    {
        _cache.GetAsync(ParsedCode(), Arg.Any<CancellationToken>()).Returns(Target());

        var result = await Query();

        result.Value.OriginalUrl.ShouldBe(LongUrl);
        await _repository.DidNotReceiveWithAnyArgs().FindAsync(default, default);
        await _cache.DidNotReceiveWithAnyArgs().SetAsync(default, default!, default, default);
    }

    [Fact]
    public async Task Handle_CacheHitThatHasSinceExpired_IsGone()
    {
        _cache.GetAsync(ParsedCode(), Arg.Any<CancellationToken>()).Returns(Target(expiresAt: Now.AddSeconds(-1)));

        var result = await Query();

        result.Error.Code.ShouldBe("ShortUrl.Expired");
        result.Error.Type.ShouldBe(ErrorType.Gone);
        await _repository.DidNotReceiveWithAnyArgs().FindAsync(default, default);
    }

    // ---- cache miss ----

    [Fact]
    public async Task Handle_CacheMiss_LoadsFromTheDatabaseAndFillsTheCacheWithTheDefaultTtl()
    {
        _repository.FindAsync(ParsedCode(), Arg.Any<CancellationToken>()).Returns(Target());

        var result = await Query();

        result.Value.OriginalUrl.ShouldBe(LongUrl);
        await _cache.Received(1).SetAsync(ParsedCode(), Target(), DefaultTtl, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_CacheMiss_TtlIsCappedByTheExpirationWhenItComesSooner()
    {
        var target = Target(expiresAt: Now.AddHours(1));
        _repository.FindAsync(ParsedCode(), Arg.Any<CancellationToken>()).Returns(target);

        await Query();

        await _cache.Received(1).SetAsync(ParsedCode(), target, TimeSpan.FromHours(1), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_CacheMiss_TtlIsTheDefaultWhenTheExpirationIsFarAway()
    {
        var target = Target(expiresAt: Now.AddDays(30));
        _repository.FindAsync(ParsedCode(), Arg.Any<CancellationToken>()).Returns(target);

        await Query();

        await _cache.Received(1).SetAsync(ParsedCode(), target, DefaultTtl, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_CacheMiss_UnknownCodeIsNotFoundAndNothingIsCached()
    {
        var result = await Query();

        result.Error.Code.ShouldBe("ShortUrl.NotFound");
        await _cache.DidNotReceiveWithAnyArgs().SetAsync(default, default!, default, default);
    }

    [Fact]
    public async Task Handle_CacheMiss_ExpiredInTheDatabaseIsGoneAndNeverCached()
    {
        _repository.FindAsync(ParsedCode(), Arg.Any<CancellationToken>()).Returns(Target(expiresAt: Now.AddMinutes(-5)));

        var result = await Query();

        result.Error.Code.ShouldBe("ShortUrl.Expired");
        await _cache.DidNotReceiveWithAnyArgs().SetAsync(default, default!, default, default);
    }

    [Fact]
    public async Task Handle_ExpiringExactlyNow_IsGone()
    {
        _repository.FindAsync(ParsedCode(), Arg.Any<CancellationToken>()).Returns(Target(expiresAt: Now));

        (await Query()).Error.Type.ShouldBe(ErrorType.Gone);
    }

    // ---- plumbing ----

    [Fact]
    public async Task Handle_UsesTheExactCaseSensitiveCodeAndForwardsTheToken()
    {
        using var cts = new CancellationTokenSource();
        _repository.FindAsync(ParsedCode("AbCdEfGh"), cts.Token).Returns(Target());

        var result = await Query("AbCdEfGh", cts.Token);

        result.IsSuccess.ShouldBeTrue();
        await _cache.Received(1).GetAsync(ParsedCode("AbCdEfGh"), cts.Token);
        await _repository.DidNotReceive().FindAsync(ParsedCode("abcdefgh"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void RedirectTarget_IsExpiredUsesTheSharedDomainRule()
    {
        Target(Now).IsExpired(Now).ShouldBeTrue();
        Target(Now).IsExpired(Now.AddSeconds(-1)).ShouldBeFalse();
        Target(null).IsExpired(Now.AddYears(50)).ShouldBeFalse();
    }
}
