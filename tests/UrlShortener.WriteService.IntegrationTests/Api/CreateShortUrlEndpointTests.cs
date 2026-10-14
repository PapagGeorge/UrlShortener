using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace UrlShortener.WriteService.IntegrationTests.Api;

/// <summary>The whole POST /urls pipeline over HTTP, against in-memory ports (no Docker).</summary>
public sealed class CreateShortUrlEndpointTests : IClassFixture<WriteApiFactory>
{
    private const string LongUrl = "https://example.com/some/very/long/url";

    private readonly WriteApiFactory _factory;
    private readonly HttpClient _client;

    public CreateShortUrlEndpointTests(WriteApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    private static async Task<JsonElement> Json(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

    private Task<HttpResponseMessage> Post(object body) => _client.PostAsJsonAsync("/urls", body);

    [Fact]
    public async Task Post_UrlOnly_Returns201WithAGeneratedSevenCharacterCode()
    {
        var response = await Post(new { originalUrl = LongUrl });

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var shortUrl = (await Json(response)).GetProperty("shortUrl").GetString()!;
        shortUrl.ShouldStartWith($"{WriteApiFactory.PublicBaseUrl}/");
        shortUrl[(WriteApiFactory.PublicBaseUrl.Length + 1)..].Length.ShouldBe(7);
        response.Headers.Location!.ToString().ShouldBe(shortUrl);
    }

    [Fact]
    public async Task Post_StoresTheOriginalUrl()
    {
        var response = await Post(new { originalUrl = "https://example.com/stored?x=1" });

        var code = (await Json(response)).GetProperty("shortUrl").GetString()!.Split('/').Last();
        _factory.Repository.Items[code].OriginalUrl.Value.ShouldBe("https://example.com/stored?x=1");
    }

    [Fact]
    public async Task Post_WithAlias_UsesTheAliasAsTheCode()
    {
        var response = await Post(new { originalUrl = LongUrl, alias = "my-endpoint-alias" });

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        (await Json(response)).GetProperty("shortUrl").GetString().ShouldBe($"{WriteApiFactory.PublicBaseUrl}/my-endpoint-alias");
    }

    [Fact]
    public async Task Post_AliasAlreadyTaken_Returns409ProblemWithTheErrorCode()
    {
        await Post(new { originalUrl = LongUrl, alias = "taken-alias-1" });

        var second = await Post(new { originalUrl = "https://example.com/other", alias = "taken-alias-1" });

        second.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        second.Content.Headers.ContentType!.MediaType.ShouldBe("application/problem+json");
        (await Json(second)).GetProperty("code").GetString().ShouldBe("ShortUrl.AliasTaken");
    }

    [Fact]
    public async Task Post_TwoRequests_GetDifferentCodes()
    {
        var a = (await Json(await Post(new { originalUrl = LongUrl }))).GetProperty("shortUrl").GetString();
        var b = (await Json(await Post(new { originalUrl = LongUrl }))).GetProperty("shortUrl").GetString();

        a.ShouldNotBe(b);   // no deduplication
    }

    [Fact]
    public async Task Post_ExpirationWithAnOffset_IsAcceptedAndStoredAsUtc()
    {
        var expiresAt = DateTimeOffset.UtcNow.AddDays(2).ToOffset(TimeSpan.FromHours(2));

        var response = await Post(new { originalUrl = LongUrl, expiresAt });

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var code = (await Json(response)).GetProperty("shortUrl").GetString()!.Split('/').Last();
        var stored = _factory.Repository.Items[code].ExpiresAt!.Value;
        stored.Offset.ShouldBe(TimeSpan.Zero);
        stored.ShouldBe(expiresAt);
    }

    [Fact]
    public async Task Post_InvalidUrl_Returns400WithTheErrorOnTheOriginalUrlField()
    {
        var response = await Post(new { originalUrl = "javascript:alert(1)" });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType!.MediaType.ShouldBe("application/problem+json");
        var body = await Json(response);
        body.GetProperty("errors").GetProperty("OriginalUrl").GetArrayLength().ShouldBe(1);
        body.GetProperty("status").GetInt32().ShouldBe(400);
    }

    [Fact]
    public async Task Post_EverythingInvalid_ReportsEveryField()
    {
        var response = await Post(new { originalUrl = "nope", alias = "x", expiresAt = DateTimeOffset.UtcNow.AddDays(-1) });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var errors = (await Json(response)).GetProperty("errors");
        errors.EnumerateObject().Select(p => p.Name).ShouldBe(["OriginalUrl", "Alias", "ExpiresAt"], ignoreOrder: true);
    }

    [Fact]
    public async Task Post_UrlPointingAtOurOwnHost_Returns400()
    {
        var response = await Post(new { originalUrl = $"{WriteApiFactory.PublicBaseUrl}/abc1234" });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await Json(response)).GetProperty("errors").GetProperty("OriginalUrl").GetRawText().ShouldContain("must not point to this service");
    }

    [Fact]
    public async Task Post_ReservedAlias_Returns400()
    {
        var response = await Post(new { originalUrl = LongUrl, alias = "swagger" });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Post_MalformedJson_Returns400()
    {
        var response = await _client.PostAsync("/urls", new StringContent("{ not json", System.Text.Encoding.UTF8, "application/json"));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Post_EmptyBody_Returns400()
    {
        var response = await _client.PostAsync("/urls", new StringContent(string.Empty, System.Text.Encoding.UTF8, "application/json"));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Post_EmptyObject_Returns400BecauseTheUrlIsRequired()
    {
        var response = await Post(new { });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await Json(response)).GetProperty("errors").TryGetProperty("OriginalUrl", out _).ShouldBeTrue();
    }

    [Fact]
    public async Task Post_ManyConcurrentRequests_AllSucceedWithUniqueCodes()
    {
        var responses = await Task.WhenAll(Enumerable.Range(0, 50).Select(i => Post(new { originalUrl = $"https://example.com/{i}" })));

        responses.ShouldAllBe(r => r.StatusCode == HttpStatusCode.Created);
        var codes = await Task.WhenAll(responses.Select(async r => (await Json(r)).GetProperty("shortUrl").GetString()));
        codes.Distinct().Count().ShouldBe(50);
    }
}
