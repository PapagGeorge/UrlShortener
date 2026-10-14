using UrlShortener.Domain;
using UrlShortener.ReadService.Infrastructure.Persistence;

namespace UrlShortener.ReadService.IntegrationTests.Persistence;

[Trait("Category", "Integration")]
[Collection(ReadStackCollection.Name)]
public sealed class ShortUrlReadRepositoryTests(ReadStackFixture stack) : IAsyncLifetime
{
    public Task InitializeAsync() => stack.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    private async Task<UrlShortener.ReadService.Application.RedirectTarget?> Find(string code)
    {
        await using var context = stack.CreateReadContext();
        return await new ShortUrlReadRepository(context).FindAsync(ShortCode.Create(code).Value, CancellationToken.None);
    }

    [Fact]
    public async Task FindAsync_ExistingCode_ReturnsTheUrlAndExpiryWrittenByTheWriteService()
    {
        var expiresAt = new DateTimeOffset(2030, 5, 6, 7, 8, 9, TimeSpan.FromHours(3));
        await stack.SeedAsync("0aZ9xYb", "https://example.com/a?x=1", expiresAt);

        var target = await Find("0aZ9xYb");

        target.ShouldNotBeNull();
        target.OriginalUrl.ShouldBe("https://example.com/a?x=1");
        target.ExpiresAt.ShouldBe(expiresAt);
    }

    [Fact]
    public async Task FindAsync_WithoutExpiration_ReturnsNull()
    {
        await stack.SeedAsync("0aZ9xYb", "https://example.com/a");

        (await Find("0aZ9xYb"))!.ExpiresAt.ShouldBeNull();
    }

    [Fact]
    public async Task FindAsync_UnknownCode_ReturnsNull()
    {
        (await Find("0000000")).ShouldBeNull();
    }

    [Fact]
    public async Task FindAsync_IsCaseSensitive()
    {
        await stack.SeedAsync("Abcdefgh", "https://example.com/upper");
        await stack.SeedAsync("abcdefgh", "https://example.com/lower");

        (await Find("Abcdefgh"))!.OriginalUrl.ShouldBe("https://example.com/upper");
        (await Find("abcdefgh"))!.OriginalUrl.ShouldBe("https://example.com/lower");
        (await Find("ABCDEFGH")).ShouldBeNull();
    }

    [Fact]
    public async Task FindAsync_FindsAliasesAndGeneratedCodesAlike()
    {
        await stack.SeedAsync("my-custom-alias", "https://example.com/alias");
        await stack.SeedAsync("0aZ9xYb", "https://example.com/generated");

        (await Find("my-custom-alias")).ShouldNotBeNull();
        (await Find("0aZ9xYb")).ShouldNotBeNull();
    }
}
