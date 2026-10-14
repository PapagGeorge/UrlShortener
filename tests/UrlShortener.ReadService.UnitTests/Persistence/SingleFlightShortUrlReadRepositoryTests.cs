using UrlShortener.Domain;
using UrlShortener.ReadService.Application;
using UrlShortener.ReadService.Infrastructure.Persistence;

namespace UrlShortener.ReadService.UnitTests.Persistence;

public sealed class SingleFlightShortUrlReadRepositoryTests
{
    private static ShortCode Code(string value = "0aZ9xYb") => ShortCode.Create(value).Value;

    private static readonly RedirectTarget Target = new("https://example.com/a", null);

    /// <summary>A loader that blocks until released, so tests control exactly when the shared lookup completes.</summary>
    private sealed class GatedLoader
    {
        private readonly TaskCompletionSource _gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _calls;

        public int Calls => Volatile.Read(ref _calls);

        public CancellationToken LastToken { get; private set; }

        public Func<ShortCode, RedirectTarget?> Result { get; set; } = _ => Target;

        public Exception? FailWith { get; set; }

        public void Release() => _gate.TrySetResult();

        public async Task<RedirectTarget?> Load(ShortCode code, CancellationToken ct)
        {
            Interlocked.Increment(ref _calls);
            LastToken = ct;
            await _gate.Task;
            return FailWith is not null ? throw FailWith : Result(code);
        }
    }

    private static SingleFlightShortUrlReadRepository Create(GatedLoader loader, TimeSpan? timeout = null) => new(loader.Load, timeout);

    [Fact]
    public async Task ConcurrentLookupsOfTheSameCode_ShareASingleDatabaseQuery()
    {
        var loader = new GatedLoader();
        var sut = Create(loader);

        var lookups = Enumerable.Range(0, 50).Select(_ => sut.FindAsync(Code(), default)).ToArray();
        loader.Release();
        var results = await Task.WhenAll(lookups);

        loader.Calls.ShouldBe(1);
        results.ShouldAllBe(r => r == Target);
    }

    [Fact]
    public async Task LookupsOfDifferentCodes_AreIndependent()
    {
        var loader = new GatedLoader { Result = code => new RedirectTarget($"https://example.com/{code.Value}", null) };
        var sut = Create(loader);

        var a = sut.FindAsync(Code("0000001"), default);
        var b = sut.FindAsync(Code("0000002"), default);
        loader.Release();

        (await a)!.OriginalUrl.ShouldBe("https://example.com/0000001");
        (await b)!.OriginalUrl.ShouldBe("https://example.com/0000002");
        loader.Calls.ShouldBe(2);
    }

    [Fact]
    public async Task CodesDifferingOnlyByCase_AreDifferentLookups()
    {
        var loader = new GatedLoader();
        var sut = Create(loader);

        var upper = sut.FindAsync(Code("Abcdefgh"), default);
        var lower = sut.FindAsync(Code("abcdefgh"), default);
        loader.Release();
        await Task.WhenAll(upper, lower);

        loader.Calls.ShouldBe(2);
    }

    [Fact]
    public async Task ResultIsNotCached_ALaterLookupQueriesAgain()
    {
        var loader = new GatedLoader();
        loader.Release();
        var sut = Create(loader);

        await sut.FindAsync(Code(), default);
        await sut.FindAsync(Code(), default);

        loader.Calls.ShouldBe(2);   // single-flight, not a cache: freshness stays the cache layer's job
    }

    [Fact]
    public async Task UnknownCode_SharesANullResult()
    {
        var loader = new GatedLoader { Result = _ => null };
        var sut = Create(loader);

        var lookups = Enumerable.Range(0, 5).Select(_ => sut.FindAsync(Code(), default)).ToArray();
        loader.Release();

        (await Task.WhenAll(lookups)).ShouldAllBe(r => r == null);
        loader.Calls.ShouldBe(1);
    }

    [Fact]
    public async Task WhenTheQueryFails_EveryWaiterSeesTheFailure_AndTheNextLookupRetries()
    {
        var loader = new GatedLoader { FailWith = new TimeoutException("db slow") };
        var sut = Create(loader);

        var lookups = Enumerable.Range(0, 5).Select(_ => sut.FindAsync(Code(), default)).ToArray();
        loader.Release();

        foreach (var lookup in lookups)
        {
            await Should.ThrowAsync<TimeoutException>(() => lookup);
        }

        loader.FailWith = null;
        (await sut.FindAsync(Code(), default)).ShouldBe(Target);   // the failure was not remembered
        loader.Calls.ShouldBe(2);
    }

    [Fact]
    public async Task AWaiterThatCancels_OnlyCancelsItself()
    {
        var loader = new GatedLoader();
        var sut = Create(loader);
        using var cts = new CancellationTokenSource();

        var impatient = sut.FindAsync(Code(), cts.Token);
        var patient = sut.FindAsync(Code(), default);
        await cts.CancelAsync();
        loader.Release();

        await Should.ThrowAsync<OperationCanceledException>(() => impatient);
        (await patient).ShouldBe(Target);
        loader.LastToken.IsCancellationRequested.ShouldBeFalse();   // the shared query was never told to stop
    }

    [Fact]
    public async Task TheFirstCallersCancellation_DoesNotAbortTheSharedQuery()
    {
        var loader = new GatedLoader();
        var sut = Create(loader);
        using var first = new CancellationTokenSource();

        var firstLookup = sut.FindAsync(Code(), first.Token);
        var second = sut.FindAsync(Code(), default);
        await first.CancelAsync();
        loader.Release();

        (await second).ShouldBe(Target);
        await Should.ThrowAsync<OperationCanceledException>(() => firstLookup);
    }

    [Fact]
    public async Task TheSharedQuery_IsBoundedByATimeout()
    {
        var loader = new GatedLoader();   // never released: simulates a hung database
        var sut = Create(loader, timeout: TimeSpan.FromMilliseconds(50));

        _ = sut.FindAsync(Code(), default);
        await Task.Delay(300);

        loader.LastToken.IsCancellationRequested.ShouldBeTrue();
    }

    [Fact]
    public async Task NothingLeaksAfterTheLookupsComplete()
    {
        var loader = new GatedLoader();
        loader.Release();
        var sut = Create(loader);

        await Task.WhenAll(Enumerable.Range(0, 20).Select(i => sut.FindAsync(Code($"000000{i % 10}"), default)));

        sut.InFlightCount.ShouldBe(0);
    }
}
