using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using UrlShortener.Domain;
using UrlShortener.WriteService.Infrastructure.Persistence;

namespace UrlShortener.WriteService.UnitTests.Persistence;

/// <summary>
/// Verifies the EF model without a database (the dev machine has no Docker). Nothing here opens a connection;
/// the real-Postgres behaviour is covered by the integration tests that run in CI.
/// </summary>
public sealed class WriteDbContextModelTests : IDisposable
{
    private readonly WriteDbContext _context = new(
        new DbContextOptionsBuilder<WriteDbContext>().UseNpgsql("Host=model-tests-never-connect.invalid;Database=model_tests").Options);

    private IEntityType Entity => _context.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(ShortUrl))!;

    public void Dispose() => _context.Dispose();

    [Fact]
    public void ShortUrl_MapsToTheShortUrlsTable()
    {
        Entity.GetTableName().ShouldBe("short_urls");
    }

    [Theory]
    [InlineData(nameof(ShortUrl.ShortCode), "short_code")]
    [InlineData(nameof(ShortUrl.OriginalUrl), "original_url")]
    [InlineData(nameof(ShortUrl.CreatedAt), "created_at")]
    [InlineData(nameof(ShortUrl.ExpiresAt), "expires_at")]
    [InlineData(nameof(ShortUrl.CreatedBy), "created_by")]
    public void Properties_UseSnakeCaseColumnNames(string property, string column)
    {
        var storeObject = StoreObjectIdentifier.Table("short_urls");

        Entity.FindProperty(property)!.GetColumnName(storeObject).ShouldBe(column);
    }

    [Fact]
    public void ShortCode_IsTheVarchar30PrimaryKeyWithByteWiseCollation()
    {
        var key = Entity.FindPrimaryKey().ShouldNotBeNull();
        var property = key.Properties.ShouldHaveSingleItem();

        property.Name.ShouldBe(nameof(ShortUrl.ShortCode));
        property.GetMaxLength().ShouldBe(30);
        property.GetColumnType().ShouldBe("character varying(30)");
        property.GetCollation().ShouldBe("C");   // case-sensitive, so Abcdefgh and abcdefgh are different codes
    }

    [Fact]
    public void OriginalUrl_IsRequiredTextWithoutAnIndex()
    {
        var property = Entity.FindProperty(nameof(ShortUrl.OriginalUrl))!;

        property.IsNullable.ShouldBeFalse();
        property.GetColumnType().ShouldBe("text");
        Entity.GetIndexes().ShouldNotContain(i => i.Properties.Contains(property));   // no dedup, so no lookup by URL
    }

    [Fact]
    public void Timestamps_AreTimestamptz_AndOnlyExpiresAtAndCreatedByAreNullable()
    {
        Entity.FindProperty(nameof(ShortUrl.CreatedAt))!.GetColumnType().ShouldBe("timestamp with time zone");
        Entity.FindProperty(nameof(ShortUrl.ExpiresAt))!.GetColumnType().ShouldBe("timestamp with time zone");
        Entity.FindProperty(nameof(ShortUrl.CreatedAt))!.IsNullable.ShouldBeFalse();
        Entity.FindProperty(nameof(ShortUrl.ExpiresAt))!.IsNullable.ShouldBeTrue();
        Entity.FindProperty(nameof(ShortUrl.CreatedBy))!.IsNullable.ShouldBeTrue();
    }

    [Fact]
    public void ExpiresAt_HasAPartialIndexForTheCleanupJob()
    {
        var index = Entity.GetIndexes().ShouldHaveSingleItem();

        index.Properties.ShouldHaveSingleItem().Name.ShouldBe(nameof(ShortUrl.ExpiresAt));
        index.GetDatabaseName().ShouldBe("ix_short_urls_expires_at");
        index.GetFilter().ShouldBe("expires_at IS NOT NULL");
    }

    [Fact]
    public void Migrations_MatchTheModel()
    {
        // Fails when the model changed but nobody added a migration (EF compares the snapshot, no database needed).
        _context.Database.HasPendingModelChanges().ShouldBeFalse();
    }
}
