namespace UrlShortener.WriteService.Application.Ports;

/// <summary>Hands out globally unique counter ids across all Write Service instances.</summary>
public interface IIdAllocator
{
    /// <exception cref="CounterExhaustedException">The code space (62^7 ids) has been used up.</exception>
    ValueTask<long> NextAsync(CancellationToken ct);
}
