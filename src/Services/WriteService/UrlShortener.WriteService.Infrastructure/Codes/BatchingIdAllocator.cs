using UrlShortener.WriteService.Application.Ports;

namespace UrlShortener.WriteService.Infrastructure.Codes;

/// <summary>
/// Reserves ids from the shared counter in batches and hands them out locally, so most calls never leave the process.
/// Ids unused when an instance dies are lost, which is fine: we need uniqueness, not continuity.
/// </summary>
public sealed class BatchingIdAllocator : IIdAllocator
{
    private readonly ICounterStore _store;
    private readonly int _batchSize;
    private readonly SemaphoreSlim _gate = new(1, 1);

    // The current batch is [_next, _end]; empty while _next > _end.
    private long _next = 1;
    private long _end;

    public BatchingIdAllocator(ICounterStore store, int batchSize)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(batchSize);
        _store = store;
        _batchSize = batchSize;
    }

    public async ValueTask<long> NextAsync(CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            if (_next > _end)
            {
                // Nothing is mutated unless the store call succeeds, so a failed refill can simply be retried.
                var newEnd = await _store.ReserveAsync(_batchSize, ct);
                _end = newEnd;
                _next = newEnd - _batchSize + 1;
            }

            if (_next >= (long)Base62.CodeSpace)
            {
                throw new CounterExhaustedException();
            }

            return _next++;
        }
        finally
        {
            _gate.Release();
        }
    }
}
