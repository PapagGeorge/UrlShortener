namespace UrlShortener.WriteService.Infrastructure.Codes;

/// <summary>The shared, atomic counter behind the id allocator (Redis in production).</summary>
public interface ICounterStore
{
    /// <summary>Atomically adds <paramref name="count"/> to the counter and returns the new value, i.e. the last id of the reserved batch.</summary>
    ValueTask<long> ReserveAsync(int count, CancellationToken ct);
}
