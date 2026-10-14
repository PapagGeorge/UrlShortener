using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using UrlShortener.Domain;

namespace UrlShortener.WriteService.Infrastructure.Persistence;

/// <summary>
/// Maps the Domain entity from the outside, so the Domain stays free of EF Core.
/// The Read Service maps the same table with its own context; keep the two in step (its integration tests build the schema from these migrations).
/// </summary>
internal sealed class ShortUrlConfiguration : IEntityTypeConfiguration<ShortUrl>
{
    public void Configure(EntityTypeBuilder<ShortUrl> builder)
    {
        builder.ToTable("short_urls");

        builder.HasKey(x => x.ShortCode).HasName("pk_short_urls");

        builder.Property(x => x.ShortCode)
            .HasColumnName("short_code")
            .HasConversion(new ShortCodeConverter())
            .HasMaxLength(DomainRules.AliasMaxLength)
            .UseCollation("C");   // byte-wise: the default collation may fold case, and codes are case-sensitive

        builder.Property(x => x.OriginalUrl)
            .HasColumnName("original_url")
            .HasConversion(new OriginalUrlConverter())
            .HasColumnType("text")
            .IsRequired();

        builder.Property(x => x.CreatedAt)
            .HasColumnName("created_at")
            .HasColumnType("timestamp with time zone");

        builder.Property(x => x.ExpiresAt)
            .HasColumnName("expires_at")
            .HasColumnType("timestamp with time zone");

        builder.Property(x => x.CreatedBy)
            .HasColumnName("created_by")
            .HasColumnType("text");

        // Used only by the cleanup job. Partial, because most rows never expire.
        builder.HasIndex(x => x.ExpiresAt)
            .HasDatabaseName("ix_short_urls_expires_at")
            .HasFilter("expires_at IS NOT NULL");
    }

    private sealed class ShortCodeConverter()
        : ValueConverter<ShortCode, string>(code => code.Value, text => ShortCode.Create(text).Value);

    private sealed class OriginalUrlConverter()
        : ValueConverter<OriginalUrl, string>(url => url.Value, text => OriginalUrl.Create(text).Value);
}
