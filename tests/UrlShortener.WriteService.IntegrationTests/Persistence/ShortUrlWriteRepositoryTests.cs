using Microsoft.EntityFrameworkCore;
using UrlShortener.Domain;
using UrlShortener.WriteService.Infrastructure.Persistence;

namespace UrlShortener.WriteService.IntegrationTests.Persistence;

[Trait("Category", "Integration")]
[Collection(PostgresCollection.Name)]
public sealed class ShortUrlWriteRepositoryTests(PostgresFixture postgres) : IAsyncLifetime
{
    private static readonly DateTimeOffset Now = new(2026, 10, 14, 12, 0, 0, TimeSpan.Zero);

    public Task InitializeAsync() => postgres.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    private static ShortUrl NewShortUrl(string code, string url = "https://example.com/a", DateTimeOffset? expiresAt = null, string? createdBy = null) =>
        ShortUrl.Create(ShortCode.Create(code).Value, OriginalUrl.Create(url).Value, Now, expiresAt, createdBy).Value;

    private async Task<bool> TryAdd(ShortUrl shortUrl)
    {
        await using var context = postgres.CreateContext();
        return await new ShortUrlWriteRepository(context).TryAddAsync(shortUrl, CancellationToken.None);
    }

    private async Task<ShortUrl?> Find(string code)
    {
        await using var context = postgres.CreateContext();
        var shortCode = ShortCode.Create(code).Value;
        return await context.ShortUrls.AsNoTracking().SingleOrDefaultAsync(s => s.ShortCode == shortCode);
    }

    [Fact]
    public async Task TryAddAsync_NewUrl_ReturnsTrueAndPersistsEveryField()
    {
        var expiresAt = Now.AddDays(3);

        (await TryAdd(NewShortUrl("0aZ9xYb", "https://example.com/some/path?q=1", expiresAt, "giorgos"))).ShouldBeTrue();

        var stored = (await Find("0aZ9xYb")).ShouldNotBeNull();
        stored.OriginalUrl.Value.ShouldBe("https://example.com/some/path?q=1");
        stored.CreatedAt.ShouldBe(Now);
        stored.ExpiresAt.ShouldBe(expiresAt);
        stored.CreatedBy.ShouldBe("giorgos");
    }

    [Fact]
    public async Task TryAddAsync_WithoutOptionalFields_StoresNulls()
    {
        await TryAdd(NewShortUrl("0aZ9xYb"));

        var stored = (await Find("0aZ9xYb")).ShouldNotBeNull();
        stored.ExpiresAt.ShouldBeNull();
        stored.CreatedBy.ShouldBeNull();
    }

    [Fact]
    public async Task TryAddAsync_ExpirationWithNonUtcOffset_RoundTripsAsTheSameInstant()
    {
        var expiresAt = new DateTimeOffset(2026, 10, 20, 9, 30, 0, TimeSpan.FromHours(2));

        await TryAdd(NewShortUrl("0aZ9xYb", expiresAt: expiresAt));

        (await Find("0aZ9xYb"))!.ExpiresAt.ShouldBe(expiresAt);
    }

    [Fact]
    public async Task TryAddAsync_DuplicateCode_ReturnsFalseAndKeepsTheOriginalRow()
    {
        await TryAdd(NewShortUrl("my-custom-alias", "https://example.com/first"));

        (await TryAdd(NewShortUrl("my-custom-alias", "https://example.com/second"))).ShouldBeFalse();

        (await Find("my-custom-alias"))!.OriginalUrl.Value.ShouldBe("https://example.com/first");
    }

    [Fact]
    public async Task TryAddAsync_AfterADuplicate_TheSameContextCanStillSaveAnotherUrl()
    {
        await using var context = postgres.CreateContext();
        var repository = new ShortUrlWriteRepository(context);
        await repository.TryAddAsync(NewShortUrl("my-custom-alias"), CancellationToken.None);

        (await repository.TryAddAsync(NewShortUrl("my-custom-alias"), CancellationToken.None)).ShouldBeFalse();
        (await repository.TryAddAsync(NewShortUrl("another-alias"), CancellationToken.None)).ShouldBeTrue();
    }

    [Fact]
    public async Task TryAddAsync_CodesThatDifferOnlyByCase_AreDifferentCodes()
    {
        (await TryAdd(NewShortUrl("Abcdefgh"))).ShouldBeTrue();
        (await TryAdd(NewShortUrl("abcdefgh"))).ShouldBeTrue();

        (await Find("Abcdefgh")).ShouldNotBeNull();
        (await Find("abcdefgh")).ShouldNotBeNull();
        (await Find("ABCDEFGH")).ShouldBeNull();
    }

    [Fact]
    public async Task TryAddAsync_ManyConcurrentRequestsForTheSameAlias_ExactlyOneWins()
    {
        var results = await Task.WhenAll(Enumerable.Range(0, 20)
            .Select(i => Task.Run(() => TryAdd(NewShortUrl("contested-alias", $"https://example.com/{i}")))));

        results.Count(won => won).ShouldBe(1);
    }

    [Fact]
    public async Task DeleteExpiredAsync_DeletesOnlyUrlsExpiredAtOrBeforeNow()
    {
        await TryAdd(NewShortUrl("0000001", expiresAt: Now.AddHours(1)));          // will be expired
        await TryAdd(NewShortUrl("0000002", expiresAt: Now.AddMinutes(30)));       // will be expired
        await TryAdd(NewShortUrl("0000003", expiresAt: Now.AddDays(1)));           // still valid
        await TryAdd(NewShortUrl("0000004"));                                       // never expires
        var checkAt = Now.AddHours(1);                                              // exactly at the first expiry

        int deleted;
        await using (var context = postgres.CreateContext())
        {
            deleted = await new ShortUrlWriteRepository(context).DeleteExpiredAsync(checkAt, 1000, CancellationToken.None);
        }

        deleted.ShouldBe(2);
        (await Find("0000001")).ShouldBeNull();
        (await Find("0000002")).ShouldBeNull();
        (await Find("0000003")).ShouldNotBeNull();
        (await Find("0000004")).ShouldNotBeNull();
    }

    [Fact]
    public async Task DeleteExpiredAsync_StopsAtTheBatchSize()
    {
        for (var i = 1; i <= 5; i++)
        {
            await TryAdd(NewShortUrl($"000000{i}", expiresAt: Now.AddMinutes(i)));
        }

        await using var context = postgres.CreateContext();
        var repository = new ShortUrlWriteRepository(context);
        var first = await repository.DeleteExpiredAsync(Now.AddDays(1), 2, CancellationToken.None);
        var second = await repository.DeleteExpiredAsync(Now.AddDays(1), 2, CancellationToken.None);
        var third = await repository.DeleteExpiredAsync(Now.AddDays(1), 2, CancellationToken.None);

        (first, second, third).ShouldBe((2, 2, 1));
    }

    [Fact]
    public async Task DeleteExpiredAsync_NothingExpired_ReturnsZero()
    {
        await TryAdd(NewShortUrl("0000001", expiresAt: Now.AddDays(1)));

        await using var context = postgres.CreateContext();
        (await new ShortUrlWriteRepository(context).DeleteExpiredAsync(Now, 1000, CancellationToken.None)).ShouldBe(0);
    }
}
