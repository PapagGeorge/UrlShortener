using Microsoft.EntityFrameworkCore;
using Npgsql;
using UrlShortener.Domain;
using UrlShortener.WriteService.Application.Ports;

namespace UrlShortener.WriteService.Infrastructure.Persistence;

public sealed class ShortUrlWriteRepository(WriteDbContext context) : IShortUrlWriteRepository
{
    public async Task<bool> TryAddAsync(ShortUrl shortUrl, CancellationToken ct)
    {
        context.ShortUrls.Add(shortUrl);
        try
        {
            await context.SaveChangesAsync(ct);
            return true;
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // The primary key is the final arbiter: a taken alias (or a colliding generated code) is an expected outcome, never a 500.
            context.Entry(shortUrl).State = EntityState.Detached;
            return false;
        }
    }

    public async Task<int> DeleteExpiredAsync(DateTimeOffset now, int batchSize, CancellationToken ct)
    {
        var utcNow = now.ToUniversalTime();   // Npgsql only accepts offset 0 for timestamptz parameters

        return await context.Database.ExecuteSqlInterpolatedAsync(
            $"""
             DELETE FROM short_urls
             WHERE short_code IN (
                 SELECT short_code FROM short_urls
                 WHERE expires_at <= {utcNow}
                 LIMIT {batchSize})
             """,
            ct);
    }
}
