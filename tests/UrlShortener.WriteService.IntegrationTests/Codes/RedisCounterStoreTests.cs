using UrlShortener.WriteService.Infrastructure.Codes;

namespace UrlShortener.WriteService.IntegrationTests.Codes;

[Trait("Category", "Integration")]
[Collection(RedisCollection.Name)]
public sealed class RedisCounterStoreTests(RedisFixture redis)
{
    private RedisCounterStore NewStore() => new(redis.Connection, $"test:counter:{Guid.NewGuid():N}");

    [Fact]
    public async Task ReserveAsync_OnAnEmptyCounter_ReturnsTheBatchSize()
    {
        var store = NewStore();

        (await store.ReserveAsync(1000, default)).ShouldBe(1000L);
    }

    [Fact]
    public async Task ReserveAsync_CalledRepeatedly_AccumulatesBatches()
    {
        var store = NewStore();

        await store.ReserveAsync(1000, default);

        (await store.ReserveAsync(1000, default)).ShouldBe(2000L);
    }

    [Fact]
    public async Task ReserveAsync_ConcurrentCallers_GetDisjointBatches()
    {
        var store = NewStore();

        var ends = await Task.WhenAll(Enumerable.Range(0, 200).Select(_ => store.ReserveAsync(1000, default).AsTask()));

        ends.Distinct().Count().ShouldBe(200);
        ends.Max().ShouldBe(200_000L);
    }

    [Fact]
    public async Task Allocators_SharingARealRedis_NeverHandOutTheSameId()
    {
        var key = $"test:counter:{Guid.NewGuid():N}";
        var allocators = Enumerable.Range(0, 6)
            .Select(_ => new BatchingIdAllocator(new RedisCounterStore(redis.Connection, key), batchSize: 100))
            .ToArray();

        var batches = await Task.WhenAll(allocators.Select(allocator => Task.Run(async () =>
        {
            var ids = new long[3_000];
            for (var i = 0; i < ids.Length; i++)
            {
                ids[i] = await allocator.NextAsync(default);
            }

            return ids;
        })));

        batches.SelectMany(ids => ids).Distinct().Count().ShouldBe(18_000);
    }
}
