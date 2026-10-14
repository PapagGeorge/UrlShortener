using UrlShortener.WriteService.Application.Ports;
using UrlShortener.WriteService.Infrastructure.Codes;

namespace UrlShortener.WriteService.UnitTests.Codes;

public sealed class BatchingIdAllocatorTests
{
    /// <summary>Behaves like Redis INCRBY: atomically adds <c>count</c> and returns the new value.</summary>
    private sealed class FakeCounterStore(long initial = 0) : ICounterStore
    {
        private long _counter = initial;
        private int _reservations;

        public int Reservations => Volatile.Read(ref _reservations);

        public List<int> RequestedSizes { get; } = [];

        public Exception? FailWith { get; set; }

        public ValueTask<long> ReserveAsync(int count, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            if (FailWith is not null)
            {
                throw FailWith;
            }

            lock (RequestedSizes)
            {
                RequestedSizes.Add(count);
            }

            Interlocked.Increment(ref _reservations);
            return ValueTask.FromResult(Interlocked.Add(ref _counter, count));
        }
    }

    [Fact]
    public async Task NextAsync_FirstIds_StartAtOneAndAreSequential()
    {
        var sut = new BatchingIdAllocator(new FakeCounterStore(), batchSize: 1000);

        var ids = new[] { await sut.NextAsync(default), await sut.NextAsync(default), await sut.NextAsync(default) };

        ids.ShouldBe([1L, 2L, 3L]);
    }

    [Fact]
    public async Task NextAsync_ReservesTheConfiguredBatchSize()
    {
        var store = new FakeCounterStore();
        var sut = new BatchingIdAllocator(store, batchSize: 1000);

        await sut.NextAsync(default);

        store.RequestedSizes.ShouldBe([1000]);
    }

    [Fact]
    public async Task NextAsync_WithinABatch_DoesNotTouchTheStoreAgain()
    {
        var store = new FakeCounterStore();
        var sut = new BatchingIdAllocator(store, batchSize: 1000);

        for (var i = 0; i < 1000; i++)
        {
            await sut.NextAsync(default);
        }

        store.Reservations.ShouldBe(1);
    }

    [Fact]
    public async Task NextAsync_WhenBatchIsExhausted_ReservesTheNextOneAndStaysContinuous()
    {
        var store = new FakeCounterStore();
        var sut = new BatchingIdAllocator(store, batchSize: 3);

        var ids = new List<long>();
        for (var i = 0; i < 7; i++)
        {
            ids.Add(await sut.NextAsync(default));
        }

        ids.ShouldBe([1L, 2L, 3L, 4L, 5L, 6L, 7L]);
        store.Reservations.ShouldBe(3);
    }

    [Fact]
    public async Task NextAsync_AnotherInstanceSharingTheStore_NeverReceivesTheSameId()
    {
        var store = new FakeCounterStore();
        var a = new BatchingIdAllocator(store, batchSize: 5);
        var b = new BatchingIdAllocator(store, batchSize: 5);

        var fromA = new[] { await a.NextAsync(default), await a.NextAsync(default) };
        var fromB = new[] { await b.NextAsync(default), await b.NextAsync(default) };

        fromA.Intersect(fromB).ShouldBeEmpty();
    }

    [Fact]
    public async Task NextAsync_ManyConcurrentCallsOnOneInstance_AreUniqueAndReserveMinimalBatches()
    {
        var store = new FakeCounterStore();
        var sut = new BatchingIdAllocator(store, batchSize: 100);

        var ids = await Task.WhenAll(Enumerable.Range(0, 5_000).Select(_ => Task.Run(() => sut.NextAsync(default).AsTask())));

        ids.Distinct().Count().ShouldBe(5_000);
        store.Reservations.ShouldBe(50);
    }

    [Fact]
    public async Task NextAsync_ManyInstancesRunningInParallel_NeverCollide()
    {
        var store = new FakeCounterStore();
        var allocators = Enumerable.Range(0, 8).Select(_ => new BatchingIdAllocator(store, batchSize: 50)).ToArray();

        var batches = await Task.WhenAll(allocators.Select(allocator => Task.Run(async () =>
        {
            var ids = new long[2_000];
            for (var i = 0; i < ids.Length; i++)
            {
                ids[i] = await allocator.NextAsync(default);
            }

            return ids;
        })));

        batches.SelectMany(ids => ids).Distinct().Count().ShouldBe(16_000);
    }

    [Fact]
    public async Task NextAsync_AtTheEndOfTheCodeSpace_HandsOutTheLastValidIdThenFailsLoudly()
    {
        // Counter at N-3: the next batch (size 5) covers N-2 .. N+2. Only N-2 and N-1 are valid ids.
        var lastBeforeBatch = (long)Base62.CodeSpace - 3;
        var sut = new BatchingIdAllocator(new FakeCounterStore(lastBeforeBatch), batchSize: 5);

        (await sut.NextAsync(default)).ShouldBe((long)Base62.CodeSpace - 2);
        (await sut.NextAsync(default)).ShouldBe((long)Base62.CodeSpace - 1);
        await Should.ThrowAsync<CounterExhaustedException>(() => sut.NextAsync(default).AsTask());
        await Should.ThrowAsync<CounterExhaustedException>(() => sut.NextAsync(default).AsTask());
    }

    [Fact]
    public async Task NextAsync_WhenTheWholeBatchIsBeyondTheCodeSpace_Throws()
    {
        var sut = new BatchingIdAllocator(new FakeCounterStore((long)Base62.CodeSpace + 10), batchSize: 5);

        await Should.ThrowAsync<CounterExhaustedException>(() => sut.NextAsync(default).AsTask());
    }

    [Fact]
    public async Task NextAsync_WhenTheStoreFails_PropagatesAndRecoversOnTheNextCall()
    {
        var store = new FakeCounterStore { FailWith = new TimeoutException("redis down") };
        var sut = new BatchingIdAllocator(store, batchSize: 10);

        await Should.ThrowAsync<TimeoutException>(() => sut.NextAsync(default).AsTask());

        store.FailWith = null;
        (await sut.NextAsync(default)).ShouldBe(1L);
    }

    [Fact]
    public async Task NextAsync_WithCancelledToken_Throws()
    {
        var sut = new BatchingIdAllocator(new FakeCounterStore(), batchSize: 10);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(() => sut.NextAsync(cts.Token).AsTask());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Constructor_NonPositiveBatchSize_Throws(int batchSize)
    {
        Should.Throw<ArgumentOutOfRangeException>(() => new BatchingIdAllocator(new FakeCounterStore(), batchSize));
    }
}
