using System.Diagnostics;
using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace UrlShortener.ReadService.IntegrationTests.Api;

public sealed class ReadStackApiFactory(string postgres, string redis) : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Postgres", postgres);
        builder.UseSetting("ConnectionStrings:Redis", redis);
    }
}

/// <summary>
/// The real Read Service, connected as the SELECT-only database user, against real Postgres and Redis.
/// Rows are created by the real Write Service repository. Runs in CI only (the dev machine has no Docker).
/// </summary>
[Trait("Category", "Integration")]
[Collection(ReadStackCollection.Name)]
public sealed class ReadFullStackTests(ReadStackFixture stack) : IAsyncLifetime
{
    private readonly List<ReadStackApiFactory> _factories = [];

    public Task InitializeAsync() => stack.ResetAsync();

    public async Task DisposeAsync()
    {
        foreach (var factory in _factories)
        {
            await factory.DisposeAsync();
        }
    }

    private HttpClient Client(string? redis = null)
    {
        var factory = new ReadStackApiFactory(stack.ReadConnectionString, redis ?? stack.RedisConnectionString);
        _factories.Add(factory);
        return factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
    }

    [Fact]
    public async Task Get_RowCreatedByTheWriteSide_Redirects_AndTheCacheIsFilled()
    {
        await stack.SeedAsync("0aZ9xYb", "https://example.com/target?x=1");
        using var client = Client();

        var response = await client.GetAsync("/0aZ9xYb");

        response.StatusCode.ShouldBe(HttpStatusCode.Found);
        response.Headers.Location!.OriginalString.ShouldBe("https://example.com/target?x=1");
        (await stack.Redis.GetDatabase().KeyExistsAsync("url:0aZ9xYb")).ShouldBeTrue();
    }

    [Fact]
    public async Task Get_SecondRequest_IsServedFromRedisEvenAfterTheRowIsDeleted()
    {
        await stack.SeedAsync("0aZ9xYb", "https://example.com/target");
        using var client = Client();
        await client.GetAsync("/0aZ9xYb");

        await stack.DeleteRowAsync("0aZ9xYb");

        (await client.GetAsync("/0aZ9xYb")).StatusCode.ShouldBe(HttpStatusCode.Found);
    }

    [Fact]
    public async Task Get_ExpiredUrl_Returns410AndIsNotCached()
    {
        await stack.SeedAsync("0aZ9xYb", "https://example.com/old", expiresAt: DateTimeOffset.UtcNow.AddSeconds(1), createdAt: DateTimeOffset.UtcNow);
        await Task.Delay(TimeSpan.FromSeconds(1.5));
        using var client = Client();

        var response = await client.GetAsync("/0aZ9xYb");

        response.StatusCode.ShouldBe(HttpStatusCode.Gone);
        (await stack.Redis.GetDatabase().KeyExistsAsync("url:0aZ9xYb")).ShouldBeFalse();
    }

    [Fact]
    public async Task Get_UnknownCode_Returns404()
    {
        using var client = Client();

        (await client.GetAsync("/0000000")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Get_IsCaseSensitiveAgainstTheRealDatabase()
    {
        await stack.SeedAsync("Abcdefgh", "https://example.com/upper");
        using var client = Client();

        (await client.GetAsync("/Abcdefgh")).StatusCode.ShouldBe(HttpStatusCode.Found);
        (await client.GetAsync("/abcdefgh")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Head_Redirects()
    {
        await stack.SeedAsync("0aZ9xYb", "https://example.com/target");
        using var client = Client();

        var response = await client.SendAsync(new HttpRequestMessage(HttpMethod.Head, "/0aZ9xYb"));

        response.StatusCode.ShouldBe(HttpStatusCode.Found);
    }

    [Fact]
    public async Task Get_ManyConcurrentRequests_AllRedirect()
    {
        await stack.SeedAsync("0aZ9xYb", "https://example.com/busy");
        using var client = Client();

        var responses = await Task.WhenAll(Enumerable.Range(0, 200).Select(_ => client.GetAsync("/0aZ9xYb")));

        var failed = responses.FirstOrDefault(r => r.StatusCode != HttpStatusCode.Found);
        var summary = string.Join(", ", responses.GroupBy(r => (int)r.StatusCode).Select(g => $"{g.Key}: {g.Count()}"));
        (failed is null).ShouldBeTrue($"statuses [{summary}]; first failure body: {(failed is null ? "" : await failed.Content.ReadAsStringAsync())}");
    }

    [Fact]
    public async Task Get_WhenRedisIsDown_StillRedirectsFromTheDatabaseAndStaysFast()
    {
        await stack.SeedAsync("0aZ9xYb", "https://example.com/target");
        using var client = Client(redis: "redis-down.invalid:6379");
        (await client.GetAsync("/0aZ9xYb")).StatusCode.ShouldBe(HttpStatusCode.Found);   // warm-up: the first request pays the one-off connection attempt

        var started = Stopwatch.GetTimestamp();
        var response = await client.GetAsync("/0aZ9xYb");
        var elapsed = Stopwatch.GetElapsedTime(started);

        response.StatusCode.ShouldBe(HttpStatusCode.Found);
        elapsed.ShouldBeLessThan(TimeSpan.FromMilliseconds(500), $"a redirect with Redis down took {elapsed.TotalMilliseconds:F0} ms");
    }
}
