using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using UrlShortener.Domain;
using UrlShortener.ReadService.Infrastructure.Persistence;

namespace UrlShortener.ReadService.UnitTests.Persistence;

/// <summary>Verifies the read-side EF model without a database (nothing here connects).</summary>
public sealed class ReadDbContextTests : IDisposable
{
    private readonly ReadDbContext _context = new(
        new DbContextOptionsBuilder<ReadDbContext>().UseNpgsql("Host=read-model-tests.invalid;Database=x").Options);

    private IEntityType Entity => _context.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(ShortUrlRow))!;

    public void Dispose() => _context.Dispose();

    [Fact]
    public void Model_MapsTheTableTheWriteServiceOwns()
    {
        Entity.GetTableName().ShouldBe("short_urls");
    }

    [Theory]
    [InlineData(nameof(ShortUrlRow.ShortCode), "short_code")]
    [InlineData(nameof(ShortUrlRow.OriginalUrl), "original_url")]
    [InlineData(nameof(ShortUrlRow.ExpiresAt), "expires_at")]
    public void Model_UsesTheSameColumnNamesAsTheWriteSide(string property, string column)
    {
        Entity.FindProperty(property)!.GetColumnName(StoreObjectIdentifier.Table("short_urls")).ShouldBe(column);
    }

    [Fact]
    public void Model_OnlyMapsWhatARedirectNeeds()
    {
        Entity.GetProperties().Select(p => p.Name).ShouldBe(
            [nameof(ShortUrlRow.ShortCode), nameof(ShortUrlRow.OriginalUrl), nameof(ShortUrlRow.ExpiresAt)], ignoreOrder: true);
    }

    [Fact]
    public void ShortCode_IsTheKeyWithTheSameByteWiseCollationAsTheWriteSide()
    {
        var key = Entity.FindPrimaryKey()!.Properties.ShouldHaveSingleItem();

        key.Name.ShouldBe(nameof(ShortUrlRow.ShortCode));
        key.GetMaxLength().ShouldBe(30);
        key.GetCollation().ShouldBe("C");
    }

    [Fact]
    public void ExpiresAt_IsNullableTimestamptz()
    {
        var property = Entity.FindProperty(nameof(ShortUrlRow.ExpiresAt))!;

        property.IsNullable.ShouldBeTrue();
        property.GetColumnType().ShouldBe("timestamp with time zone");
    }

    [Fact]
    public void SaveChanges_Throws_BecauseThisServiceNeverWrites()
    {
        _context.ShortUrls.Add(new ShortUrlRow { ShortCode = "0aZ9xYb", OriginalUrl = "https://example.com" });

        Should.Throw<InvalidOperationException>(() => _context.SaveChanges()).Message.ShouldContain("read-only");
    }

    [Fact]
    public async Task SaveChangesAsync_Throws_BecauseThisServiceNeverWrites()
    {
        _context.ShortUrls.Add(new ShortUrlRow { ShortCode = "0aZ9xYb", OriginalUrl = "https://example.com" });

        (await Should.ThrowAsync<InvalidOperationException>(() => _context.SaveChangesAsync())).Message.ShouldContain("read-only");
    }

    [Fact]
    public void RedirectQuery_SelectsOnlyTheUrlAndTheExpiry_FilteredByTheExactCode()
    {
        var sql = ShortUrlReadRepository.BuildQuery(_context, ShortCode.Create("0aZ9xYb").Value).ToQueryString();

        sql.ShouldContain("original_url");
        sql.ShouldContain("expires_at");
        sql.ShouldContain("short_code");
        sql.ShouldNotContain("created_at");
        sql.ShouldNotContain("created_by");
        sql.ShouldContain("LIMIT");   // at most one row
    }
}
