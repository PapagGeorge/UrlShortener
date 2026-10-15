using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace UrlShortener.WriteService.IntegrationTests.FullStack;

/// <summary>The real service against real Postgres and Redis. Runs in CI only (the dev machine has no Docker).</summary>
[Trait("Category", "Integration")]
[Collection(FullStackCollection.Name)]
public sealed class CreateShortUrlFullStackTests(FullStackFixture stack) : IAsyncLifetime
{
    public async Task InitializeAsync()
    {
        // Start one instance first so the schema exists, then clean up the data of earlier tests.
        using var warmUp = stack.StartInstance().CreateClient();
        await stack.ResetAsync();
        ErrorCapture.Clear();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private static async Task<string> Code(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("shortUrl").GetString()!.Split('/').Last();

    private static Task<HttpResponseMessage> Create(HttpClient client, object body) => client.PostAsJsonAsync("/urls", body);

    private async Task<int> RowCount()
    {
        await using var context = stack.CreateContext();
        return await context.ShortUrls.CountAsync();
    }

    private async Task<long> CounterValue() => (long)await stack.Redis.GetDatabase().StringGetAsync("urlshortener:counter");

    [Fact]
    public async Task Startup_AppliesTheMigrations()
    {
        await using var context = stack.CreateContext();

        (await context.Database.GetAppliedMigrationsAsync()).ShouldNotBeEmpty();
        (await context.Database.GetPendingMigrationsAsync()).ShouldBeEmpty();
    }

    [Fact]
    public async Task Post_GeneratedCode_IsStoredInPostgres()
    {
        using var client = stack.StartInstance().CreateClient();

        var response = await Create(client, new { originalUrl = "https://example.com/real?x=1", expiresAt = DateTimeOffset.UtcNow.AddDays(1) });

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var code = await Code(response);
        code.Length.ShouldBe(7);
        await using var context = stack.CreateContext();
        var row = await context.ShortUrls.AsNoTracking().SingleAsync();
        row.ShortCode.Value.ShouldBe(code);
        row.OriginalUrl.Value.ShouldBe("https://example.com/real?x=1");
        row.ExpiresAt.ShouldNotBeNull();
    }

    [Fact]
    public async Task Post_SameAliasTwice_SecondIs409FromTheRealPrimaryKey()
    {
        using var client = stack.StartInstance().CreateClient();

        var first = await Create(client, new { originalUrl = "https://example.com/1", alias = "real-alias-1" });
        var second = await Create(client, new { originalUrl = "https://example.com/2", alias = "real-alias-1" });

        first.StatusCode.ShouldBe(HttpStatusCode.Created);
        second.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await RowCount()).ShouldBe(1);
    }

    [Fact]
    public async Task Post_AliasesDifferingOnlyByCase_AreBothAccepted()
    {
        using var client = stack.StartInstance().CreateClient();

        (await Create(client, new { originalUrl = "https://example.com/1", alias = "CaseSensitive" })).StatusCode.ShouldBe(HttpStatusCode.Created);
        (await Create(client, new { originalUrl = "https://example.com/2", alias = "casesensitive" })).StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    [Fact]
    public async Task Post_ManyConcurrentRequests_ProduceUniqueCodesFromOneCounterBatch()
    {
        using var client = stack.StartInstance().CreateClient();

        var responses = await Task.WhenAll(Enumerable.Range(0, 60).Select(i => Create(client, new { originalUrl = $"https://example.com/{i}" })));

        var failed = responses.FirstOrDefault(r => r.StatusCode != HttpStatusCode.Created);
        var summary = string.Join(", ", responses.GroupBy(r => (int)r.StatusCode).Select(g => $"{g.Key}: {g.Count()}"));
        (failed is null).ShouldBeTrue($"statuses [{summary}]; first failure body: {(failed is null ? "" : await failed.Content.ReadAsStringAsync())}; server errors: {ErrorCapture.Describe()}");
        (await Task.WhenAll(responses.Select(Code))).Distinct().Count().ShouldBe(60);
        (await RowCount()).ShouldBe(60);
        (await CounterValue()).ShouldBe(1000L);   // a single reserved batch
    }

    [Fact]
    public async Task Post_TwoInstancesSharingRedisAndPostgres_NeverProduceTheSameCode()
    {
        using var a = stack.StartInstance().CreateClient();
        using var b = stack.StartInstance().CreateClient();

        var responses = await Task.WhenAll(Enumerable.Range(0, 40).SelectMany(i => new[]
        {
            Create(a, new { originalUrl = $"https://example.com/a{i}" }),
            Create(b, new { originalUrl = $"https://example.com/b{i}" }),
        }));

        var firstBad = responses.FirstOrDefault(r => r.StatusCode != HttpStatusCode.Created);
        (firstBad is null).ShouldBeTrue($"first failure: {(firstBad is null ? "" : (int)firstBad.StatusCode + " " + await firstBad.Content.ReadAsStringAsync())}; server errors: {ErrorCapture.Describe()}");
        (await Task.WhenAll(responses.Select(Code))).Distinct().Count().ShouldBe(80);
        (await RowCount()).ShouldBe(80);
        // Each instance reserved its own batch of 1000 ids.
        (await CounterValue()).ShouldBe(2000L);
    }

    [Fact]
    public async Task Post_WhenRedisIsDown_AliasesStillWorkAndGeneratedCodesFailCleanly()
    {
        using var client = stack.StartInstance("redis-down.invalid:6379,connectTimeout=1000,syncTimeout=1000").CreateClient();

        var alias = await Create(client, new { originalUrl = "https://example.com/alias", alias = "works-without-redis" });
        var generated = await Create(client, new { originalUrl = "https://example.com/generated" });

        alias.StatusCode.ShouldBe(HttpStatusCode.Created);
        generated.StatusCode.ShouldBe(HttpStatusCode.InternalServerError);
        generated.Content.Headers.ContentType!.MediaType.ShouldBe("application/problem+json");
    }

    [Fact]
    public async Task Ready_WithPostgresAndRedisUp_Is200()
    {
        using var client = stack.StartInstance().CreateClient();

        (await client.GetAsync("/health/ready")).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Ready_WhenRedisIsDown_Is503BecauseGeneratedCodesCannotBeIssued()
    {
        using var client = stack.StartInstance("redis-down.invalid:6379,connectTimeout=1000,syncTimeout=1000").CreateClient();

        (await client.GetAsync("/health/ready")).StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
        (await client.GetAsync("/health")).StatusCode.ShouldBe(HttpStatusCode.OK);
    }
}
