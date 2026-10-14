using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using UrlShortener.BuildingBlocks.Messaging;
using UrlShortener.BuildingBlocks.Results;
using UrlShortener.WriteService.Application.DeleteExpiredShortUrls;
using UrlShortener.WriteService.Infrastructure.Cleanup;

namespace UrlShortener.WriteService.UnitTests.Cleanup;

public sealed class ExpiredUrlCleanupServiceTests
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(10);

    private sealed class RecordingHandler : ICommandHandler<DeleteExpiredShortUrlsCommand, int>
    {
        private int _calls;

        public int Calls => Volatile.Read(ref _calls);

        public List<int> BatchSizes { get; } = [];

        public Func<Exception?> ThrowOnNextCall { get; set; } = () => null;

        public SemaphoreSlim Called { get; } = new(0);

        public Task<Result<int>> Handle(DeleteExpiredShortUrlsCommand command, CancellationToken ct)
        {
            lock (BatchSizes)
            {
                BatchSizes.Add(command.BatchSize);
            }

            Interlocked.Increment(ref _calls);
            Called.Release();
            var failure = ThrowOnNextCall();
            if (failure is not null)
            {
                throw failure;
            }

            return Task.FromResult<Result<int>>(0);
        }
    }

    private readonly RecordingHandler _handler = new();
    private readonly FakeTimeProvider _time = new();
    private readonly TestLogger<ExpiredUrlCleanupService> _logger = new();

    private ExpiredUrlCleanupService CreateSut(int batchSize = 500)
    {
        var services = new ServiceCollection();
        services.AddSingleton<ICommandHandler<DeleteExpiredShortUrlsCommand, int>>(_handler);
        var scopeFactory = services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();
        return new ExpiredUrlCleanupService(
            scopeFactory,
            Options.Create(new CleanupOptions { Interval = Interval, BatchSize = batchSize }),
            _time,
            _logger);
    }

    [Fact]
    public async Task RunOnceAsync_CallsTheHandlerWithTheConfiguredBatchSize()
    {
        var sut = CreateSut(batchSize: 250);

        await sut.RunOnceAsync(CancellationToken.None);

        _handler.BatchSizes.ShouldBe([250]);
    }

    [Fact]
    public async Task RunOnceAsync_WhenTheHandlerThrows_LogsAnErrorAndDoesNotRethrow()
    {
        var boom = new InvalidOperationException("db down");
        _handler.ThrowOnNextCall = () => boom;
        var sut = CreateSut();

        await sut.RunOnceAsync(CancellationToken.None);

        var entry = _logger.Entries.Where(e => e.Level == LogLevel.Error).ShouldHaveSingleItem();
        entry.Exception.ShouldBeSameAs(boom);
    }

    [Fact]
    public async Task RunOnceAsync_AfterAFailure_TheNextRunStillWorks()
    {
        _handler.ThrowOnNextCall = () => new TimeoutException();
        var sut = CreateSut();
        await sut.RunOnceAsync(CancellationToken.None);

        _handler.ThrowOnNextCall = () => null;
        await sut.RunOnceAsync(CancellationToken.None);

        _handler.Calls.ShouldBe(2);
    }

    [Fact]
    public async Task RunOnceAsync_WhenCancelled_PropagatesTheCancellation()
    {
        _handler.ThrowOnNextCall = () => new OperationCanceledException();
        var sut = CreateSut();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(() => sut.RunOnceAsync(cts.Token));
    }

    [Fact]
    public async Task Service_RunsOnceEveryInterval()
    {
        using var sut = CreateSut();
        await sut.StartAsync(CancellationToken.None);

        _time.Advance(Interval);
        await _handler.Called.WaitAsync(TimeSpan.FromSeconds(5));
        _time.Advance(Interval);
        await _handler.Called.WaitAsync(TimeSpan.FromSeconds(5));

        _handler.Calls.ShouldBe(2);
        await sut.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Service_DoesNotRunBeforeTheFirstIntervalElapses()
    {
        using var sut = CreateSut();
        await sut.StartAsync(CancellationToken.None);

        _time.Advance(Interval - TimeSpan.FromSeconds(1));
        await Task.Delay(100);

        _handler.Calls.ShouldBe(0);
        await sut.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Service_KeepsRunningAfterAFailedTick()
    {
        _handler.ThrowOnNextCall = () => new TimeoutException();
        using var sut = CreateSut();
        await sut.StartAsync(CancellationToken.None);

        _time.Advance(Interval);
        await _handler.Called.WaitAsync(TimeSpan.FromSeconds(5));
        _handler.ThrowOnNextCall = () => null;
        _time.Advance(Interval);
        await _handler.Called.WaitAsync(TimeSpan.FromSeconds(5));

        _handler.Calls.ShouldBe(2);
        await sut.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task StopAsync_EndsTheLoopPromptly()
    {
        using var sut = CreateSut();
        await sut.StartAsync(CancellationToken.None);

        var stop = sut.StopAsync(CancellationToken.None);

        (await Task.WhenAny(stop, Task.Delay(TimeSpan.FromSeconds(5)))).ShouldBeSameAs(stop);
    }
}
