using Microsoft.EntityFrameworkCore;
using UrlShortener.Domain;
using UrlShortener.ReadService.Application;
using UrlShortener.ReadService.Application.Ports;

namespace UrlShortener.ReadService.Infrastructure.Persistence;

public sealed class ShortUrlReadRepository(ReadDbContext context) : IShortUrlReadRepository
{
    public Task<RedirectTarget?> FindAsync(ShortCode code, CancellationToken ct) =>
        BuildQuery(context, code).FirstOrDefaultAsync(ct);

    /// <summary>A primary-key lookup that fetches just the URL and the expiry. Exact (case-sensitive) match on the code.</summary>
    internal static IQueryable<RedirectTarget?> BuildQuery(ReadDbContext context, ShortCode code) =>
        context.ShortUrls
            .AsNoTracking()
            .Where(row => row.ShortCode == code.Value)
            .Select(row => new RedirectTarget(row.OriginalUrl, row.ExpiresAt))
            .Take(1);
}
