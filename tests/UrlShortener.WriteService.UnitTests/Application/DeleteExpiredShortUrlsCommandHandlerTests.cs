using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using UrlShortener.WriteService.Application.DeleteExpiredShortUrls;
using UrlShortener.WriteService.Application.Ports;

namespace UrlShortener.WriteService.UnitTests.Application;

public sealed class DeleteExpiredShortUrlsCommandHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 14, 12, 0, 0, TimeSpan.Zero);

    private readonly IShortUrlWriteRepository _repository = Substitute.For<IShortUrlWriteRepository>();
    private readonly DeleteExpiredShortUrlsCommandHandler _sut;

    public DeleteExpiredShortUrlsCommandHandlerTests()
    {
        _sut = new DeleteExpiredShortUrlsCommandHandler(_repository, new FakeTimeProvider(Now));
    }

    [Fact]
    public async Task Handle_NothingExpired_DeletesNothingWithASingleCall()
    {
        _repository.DeleteExpiredAsync(Now, 1000, Arg.Any<CancellationToken>()).Returns(0);

        var result = await _sut.Handle(new DeleteExpiredShortUrlsCommand(), CancellationToken.None);

        result.Value.ShouldBe(0);
        await _repository.Received(1).DeleteExpiredAsync(Arg.Any<DateTimeOffset>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_PartialBatch_StopsAfterOneCall()
    {
        _repository.DeleteExpiredAsync(Now, 1000, Arg.Any<CancellationToken>()).Returns(999);

        var result = await _sut.Handle(new DeleteExpiredShortUrlsCommand(), CancellationToken.None);

        result.Value.ShouldBe(999);
        await _repository.Received(1).DeleteExpiredAsync(Arg.Any<DateTimeOffset>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_FullBatches_KeepsGoingUntilAShortBatchAndSumsTheTotal()
    {
        _repository.DeleteExpiredAsync(Now, 1000, Arg.Any<CancellationToken>()).Returns(1000, 1000, 250);

        var result = await _sut.Handle(new DeleteExpiredShortUrlsCommand(), CancellationToken.None);

        result.Value.ShouldBe(2250);
        await _repository.Received(3).DeleteExpiredAsync(Arg.Any<DateTimeOffset>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_CustomBatchSize_IsPassedToTheRepository()
    {
        _repository.DeleteExpiredAsync(Now, 50, Arg.Any<CancellationToken>()).Returns(10);

        await _sut.Handle(new DeleteExpiredShortUrlsCommand(BatchSize: 50), CancellationToken.None);

        await _repository.Received(1).DeleteExpiredAsync(Now, 50, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_UsesTheTimeProviderAndForwardsTheCancellationToken()
    {
        using var cts = new CancellationTokenSource();
        _repository.DeleteExpiredAsync(Arg.Any<DateTimeOffset>(), Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(0);

        await _sut.Handle(new DeleteExpiredShortUrlsCommand(), cts.Token);

        await _repository.Received(1).DeleteExpiredAsync(Now, 1000, cts.Token);
    }

    [Fact]
    public async Task Handle_CancelledBetweenBatches_StopsInsteadOfLooping()
    {
        using var cts = new CancellationTokenSource();
        _repository.DeleteExpiredAsync(Arg.Any<DateTimeOffset>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                cts.Cancel();
                return 1000;
            });

        await Should.ThrowAsync<OperationCanceledException>(() => _sut.Handle(new DeleteExpiredShortUrlsCommand(), cts.Token));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public async Task Handle_NonPositiveBatchSize_Throws(int batchSize)
    {
        await Should.ThrowAsync<ArgumentOutOfRangeException>(
            () => _sut.Handle(new DeleteExpiredShortUrlsCommand(batchSize), CancellationToken.None));
    }
}
