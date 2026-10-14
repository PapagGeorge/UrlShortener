using Microsoft.EntityFrameworkCore;
using UrlShortener.Domain;

namespace UrlShortener.WriteService.Infrastructure.Persistence;

/// <summary>The Write Service owns the <c>short_urls</c> table and all of its migrations.</summary>
public sealed class WriteDbContext(DbContextOptions<WriteDbContext> options) : DbContext(options)
{
    public DbSet<ShortUrl> ShortUrls => Set<ShortUrl>();

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(WriteDbContext).Assembly);
}
