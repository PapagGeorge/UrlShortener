using Microsoft.EntityFrameworkCore;

namespace UrlShortener.ReadService.Infrastructure.Persistence;

/// <summary>
/// The Read Service's view of the <c>short_urls</c> table. It does not own the schema: the Write Service does, and it is the only one with
/// migrations (so this project must never contain any). Only the columns a redirect needs are mapped. It also connects with a SELECT-only
/// database user, and refuses to save as a second line of defence.
/// </summary>
public sealed class ReadDbContext(DbContextOptions<ReadDbContext> options) : DbContext(options)
{
    internal DbSet<ShortUrlRow> ShortUrls => Set<ShortUrlRow>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ShortUrlRow>(row =>
        {
            row.ToTable("short_urls");
            row.HasKey(x => x.ShortCode);
            row.Property(x => x.ShortCode).HasColumnName("short_code").HasMaxLength(30).UseCollation("C");
            row.Property(x => x.OriginalUrl).HasColumnName("original_url").HasColumnType("text");
            row.Property(x => x.ExpiresAt).HasColumnName("expires_at").HasColumnType("timestamp with time zone");
        });
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess) => throw ReadOnly();

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default) =>
        throw ReadOnly();

    private static InvalidOperationException ReadOnly() => new("The Read Service database context is read-only.");
}

internal sealed class ShortUrlRow
{
    public string ShortCode { get; init; } = null!;

    public string OriginalUrl { get; init; } = null!;

    public DateTimeOffset? ExpiresAt { get; init; }
}
