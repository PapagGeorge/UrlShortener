using System.Collections.Concurrent;
using UrlShortener.Domain;
using UrlShortener.ReadService.Application;
using UrlShortener.ReadService.Application.Ports;

namespace UrlShortener.ReadService.Infrastructure.Persistence;

/// <summary>
/// Protects the database from a cache stampede: when many requests miss the cache for the same code at the same moment
/// (a hot link whose cache entry just expired, or a flushed Redis), they share <b>one</b> query instead of each opening a connection.
/// This is single-flight, not a cache: the result is shared only by the lookups in flight and then forgotten.
/// </summary>
/// <remarks>
/// The shared query is independent of any single caller: it runs with its own timeout (never a caller's token, so one client
/// disconnecting cannot abort it for the others), and the loader creates its own DI scope (a caller's scope may end first).
/// Each caller can still cancel its own wait.
/// </remarks>
public sealed class SingleFlightShortUrlReadRepository(
    Func<ShortCode, CancellationToken, Task<RedirectTarget?>> load,
    TimeSpan? queryTimeout = null) : IShortUrlReadRepository
{
    private static readonly TimeSpan DefaultQueryTimeout = TimeSpan.FromSeconds(10);

    private readonly ConcurrentDictionary<string, Lazy<Task<RedirectTarget?>>> _inFlight = new(StringComparer.Ordinal);

    internal int InFlightCount => _inFlight.Count;

    public Task<RedirectTarget?> FindAsync(ShortCode code, CancellationToken ct)
    {
        Lazy<Task<RedirectTarget?>>? created = null;
        created = new Lazy<Task<RedirectTarget?>>(() => RunAsync(code, created!));

        // Lazy guarantees the query starts once even if two callers race to publish their entry.
        var shared = _inFlight.GetOrAdd(code.Value, created);
        return shared.Value.WaitAsync(ct);
    }

    private async Task<RedirectTarget?> RunAsync(ShortCode code, Lazy<Task<RedirectTarget?>> self)
    {
        using var timeout = new CancellationTokenSource(queryTimeout ?? DefaultQueryTimeout);
        try
        {
            // Yield first: the query must not run inside the lock of the Lazy, nor on the caller's stack.
            await Task.Yield();
            return await load(code, timeout.Token);
        }
        finally
        {
            // Forget it as soon as it finishes (success or failure), so a failure is never remembered.
            _inFlight.TryRemove(new KeyValuePair<string, Lazy<Task<RedirectTarget?>>>(code.Value, self));
        }
    }
}
