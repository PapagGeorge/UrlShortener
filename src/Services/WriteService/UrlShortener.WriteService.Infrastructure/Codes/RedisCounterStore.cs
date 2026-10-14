using StackExchange.Redis;

namespace UrlShortener.WriteService.Infrastructure.Codes;

/// <summary>The global counter as a single Redis key. <c>INCRBY</c> is atomic, so concurrent instances always get disjoint batches.</summary>
public sealed class RedisCounterStore(IConnectionMultiplexer connection, string key) : ICounterStore
{
    public const string DefaultKey = "urlshortener:counter";

    public async ValueTask<long> ReserveAsync(int count, CancellationToken ct)
    {
        var database = connection.GetDatabase();
        return await database.StringIncrementAsync(key, count).WaitAsync(ct);
    }
}
