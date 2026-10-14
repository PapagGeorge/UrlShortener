using UrlShortener.Domain;

namespace UrlShortener.ReadService.Application.Ports;

public interface IShortUrlReadRepository
{
    /// <summary>Returns <c>null</c> when no such code exists. Read-only: this service never writes to the database.</summary>
    Task<RedirectTarget?> FindAsync(ShortCode code, CancellationToken ct);
}
