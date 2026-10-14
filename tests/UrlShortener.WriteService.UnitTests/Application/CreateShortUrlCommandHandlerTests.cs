using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using UrlShortener.BuildingBlocks.Results;
using UrlShortener.Domain;
using UrlShortener.WriteService.Application.CreateShortUrl;
using UrlShortener.WriteService.Application.Ports;

namespace UrlShortener.WriteService.UnitTests.Application;

public sealed class CreateShortUrlCommandHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 14, 12, 0, 0, TimeSpan.Zero);
    private const string LongUrl = "https://example.com/some/very/long/url";

    private readonly IShortUrlWriteRepository _repository = Substitute.For<IShortUrlWriteRepository>();
    private readonly IIdAllocator _allocator = Substitute.For<IIdAllocator>();
    private readonly ICodeObfuscator _obfuscator = Substitute.For<ICodeObfuscator>();
    private readonly CreateShortUrlCommandHandler _sut;

    public CreateShortUrlCommandHandlerTests()
    {
        var time = new FakeTimeProvider(Now);
        _sut = new CreateShortUrlCommandHandler(_repository, _allocator, _obfuscator, time);
        _repository.TryAddAsync(Arg.Any<ShortUrl>(), Arg.Any<CancellationToken>()).Returns(true);
    }

    private static ShortCode Code(string value) => ShortCode.Create(value).Value;

    private void AllocateIds(params (long Id, string Code)[] sequence)
    {
        _allocator.NextAsync(Arg.Any<CancellationToken>())
            .Returns(sequence.Select(s => ValueTask.FromResult(s.Id)).First(), sequence.Skip(1).Select(s => ValueTask.FromResult(s.Id)).ToArray());
        foreach (var (id, code) in sequence)
        {
            _obfuscator.ToCode(id).Returns(Code(code));
        }
    }

    // ---- generated codes ----

    [Fact]
    public async Task Handle_WithoutAlias_AllocatesIdObfuscatesAndStoresTheUrl()
    {
        AllocateIds((42, "0aZ9xYb"));
        var expiresAt = Now.AddDays(7);

        var result = await _sut.Handle(new CreateShortUrlCommand(LongUrl, null, expiresAt), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShortCode.Value.ShouldBe("0aZ9xYb");
        await _repository.Received(1).TryAddAsync(
            Arg.Is<ShortUrl>(s => s.ShortCode.Value == "0aZ9xYb"
                                  && s.OriginalUrl.Value == LongUrl
                                  && s.CreatedAt == Now
                                  && s.ExpiresAt == expiresAt),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WithoutAlias_PassesTheCancellationTokenDown()
    {
        AllocateIds((1, "0000001"));
        using var cts = new CancellationTokenSource();

        await _sut.Handle(new CreateShortUrlCommand(LongUrl, null, null), cts.Token);

        await _allocator.Received(1).NextAsync(cts.Token);
        await _repository.Received(1).TryAddAsync(Arg.Any<ShortUrl>(), cts.Token);
    }

    [Fact]
    public async Task Handle_GeneratedCodeCollides_RetriesWithAFreshId()
    {
        AllocateIds((1, "0000001"), (2, "0000002"));
        _repository.TryAddAsync(Arg.Is<ShortUrl>(s => s.ShortCode.Value == "0000001"), Arg.Any<CancellationToken>()).Returns(false);

        var result = await _sut.Handle(new CreateShortUrlCommand(LongUrl, null, null), CancellationToken.None);

        result.Value.ShortCode.Value.ShouldBe("0000002");
        await _allocator.Received(2).NextAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_GeneratedCodeCollidesEveryTime_FailsAfterThreeAttempts()
    {
        AllocateIds((1, "0000001"), (2, "0000002"), (3, "0000003"), (4, "0000004"));
        _repository.TryAddAsync(Arg.Any<ShortUrl>(), Arg.Any<CancellationToken>()).Returns(false);

        var result = await _sut.Handle(new CreateShortUrlCommand(LongUrl, null, null), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("ShortUrl.CodeGenerationFailed");
        result.Error.Type.ShouldBe(ErrorType.Failure);
        await _allocator.Received(3).NextAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_GeneratedCodeIsAReservedWord_SkipsItWithoutStoringIt()
    {
        AllocateIds((1, "swagger"), (2, "0000002"));

        var result = await _sut.Handle(new CreateShortUrlCommand(LongUrl, null, null), CancellationToken.None);

        result.Value.ShortCode.Value.ShouldBe("0000002");
        await _repository.DidNotReceive().TryAddAsync(
            Arg.Is<ShortUrl>(s => s.ShortCode.Value == "swagger"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WhenTheAllocatorFails_PropagatesTheException()
    {
        _allocator.NextAsync(Arg.Any<CancellationToken>()).Returns<ValueTask<long>>(_ => throw new CounterExhaustedException());

        await Should.ThrowAsync<CounterExhaustedException>(
            () => _sut.Handle(new CreateShortUrlCommand(LongUrl, null, null), CancellationToken.None));
    }

    // ---- aliases ----

    [Fact]
    public async Task Handle_WithAlias_UsesTheAliasAndNeverTouchesTheCounter()
    {
        var result = await _sut.Handle(new CreateShortUrlCommand(LongUrl, "my-custom-alias", null), CancellationToken.None);

        result.Value.ShortCode.Value.ShouldBe("my-custom-alias");
        await _allocator.DidNotReceive().NextAsync(Arg.Any<CancellationToken>());
        _obfuscator.DidNotReceive().ToCode(Arg.Any<long>());
        await _repository.Received(1).TryAddAsync(
            Arg.Is<ShortUrl>(s => s.ShortCode.Value == "my-custom-alias"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_AliasAlreadyTaken_ReturnsConflictWithoutRetrying()
    {
        _repository.TryAddAsync(Arg.Any<ShortUrl>(), Arg.Any<CancellationToken>()).Returns(false);

        var result = await _sut.Handle(new CreateShortUrlCommand(LongUrl, "my-custom-alias", null), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("ShortUrl.AliasTaken");
        result.Error.Type.ShouldBe(ErrorType.Conflict);
        await _repository.Received(1).TryAddAsync(Arg.Any<ShortUrl>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("short")]       // below the minimum length
    [InlineData("has space!")]
    public async Task Handle_InvalidAlias_ReturnsValidationErrorAndStoresNothing(string alias)
    {
        var result = await _sut.Handle(new CreateShortUrlCommand(LongUrl, alias, null), CancellationToken.None);

        result.Error.Code.ShouldBe("Alias.Invalid");
        await _repository.DidNotReceive().TryAddAsync(Arg.Any<ShortUrl>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ReservedAlias_ReturnsReservedError()
    {
        var result = await _sut.Handle(new CreateShortUrlCommand(LongUrl, "swagger", null), CancellationToken.None);

        result.Error.Code.ShouldBe("Alias.Reserved");
    }

    // ---- shared input rules (defence in depth: the validation decorator normally stops these earlier) ----

    [Theory]
    [InlineData("not a url")]
    [InlineData("ftp://example.com/file")]
    [InlineData(null)]
    public async Task Handle_InvalidOriginalUrl_ReturnsValidationErrorAndAllocatesNothing(string? url)
    {
        var result = await _sut.Handle(new CreateShortUrlCommand(url, null, null), CancellationToken.None);

        result.Error.Code.ShouldBe("OriginalUrl.Invalid");
        await _allocator.DidNotReceive().NextAsync(Arg.Any<CancellationToken>());
        await _repository.DidNotReceive().TryAddAsync(Arg.Any<ShortUrl>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ExpirationNotInTheFuture_ReturnsValidationErrorAndStoresNothing()
    {
        AllocateIds((1, "0000001"));

        var result = await _sut.Handle(new CreateShortUrlCommand(LongUrl, null, Now), CancellationToken.None);

        result.Error.Code.ShouldBe("ShortUrl.ExpirationInPast");
        await _repository.DidNotReceive().TryAddAsync(Arg.Any<ShortUrl>(), Arg.Any<CancellationToken>());
    }
}
