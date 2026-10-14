using System.Net;
using System.Text.Json;
using UrlShortener.ReadService.Application;

namespace UrlShortener.ReadService.IntegrationTests.Api;

/// <summary>GET /{shortCode} over HTTP against in-memory ports (no Docker).</summary>
public sealed class RedirectEndpointTests : IClassFixture<ReadApiFactory>
{
    private readonly ReadApiFactory _factory;
    private readonly HttpClient _client;

    public RedirectEndpointTests(ReadApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateNonRedirectingClient();
    }

    private void Store(string code, string url, DateTimeOffset? expiresAt = null) =>
        _factory.Repository.Items[code] = new RedirectTarget(url, expiresAt);

    [Fact]
    public async Task Get_KnownCode_Returns302WithTheOriginalUrl()
    {
        Store("0aZ9xYb", "https://example.com/some/very/long/url");

        var response = await _client.GetAsync("/0aZ9xYb");

        response.StatusCode.ShouldBe(HttpStatusCode.Found);   // 302, never 301
        response.Headers.Location!.OriginalString.ShouldBe("https://example.com/some/very/long/url");
        (await response.Content.ReadAsStringAsync()).ShouldBeEmpty();
    }

    [Fact]
    public async Task Get_Redirect_IsNeverCacheable()
    {
        Store("0aZ9xY1", "https://example.com/a");

        var response = await _client.GetAsync("/0aZ9xY1");

        response.Headers.CacheControl!.NoStore.ShouldBeTrue();
    }

    [Fact]
    public async Task Get_Alias_Redirects()
    {
        Store("my-custom-alias", "https://example.com/alias");

        (await _client.GetAsync("/my-custom-alias")).StatusCode.ShouldBe(HttpStatusCode.Found);
    }

    [Fact]
    public async Task Get_UrlWithEncodingQueryAndFragment_IsPreservedExactly()
    {
        const string url = "https://example.com/a%20b/%CE%B1?x=1&y=%2F#frag";
        Store("0aZ9xY2", url);

        var response = await _client.GetAsync("/0aZ9xY2");

        response.Headers.Location!.OriginalString.ShouldBe(url);
    }

    [Fact]
    public async Task Head_Request_GetsTheSameRedirectWithoutABody()
    {
        Store("0aZ9xY3", "https://example.com/head");

        var response = await _client.SendAsync(new HttpRequestMessage(HttpMethod.Head, "/0aZ9xY3"));

        response.StatusCode.ShouldBe(HttpStatusCode.Found);
        response.Headers.Location!.OriginalString.ShouldBe("https://example.com/head");
    }

    [Fact]
    public async Task Get_UnknownCode_Returns404ProblemWithTheErrorCode()
    {
        var response = await _client.GetAsync("/0000000");

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        response.Content.Headers.ContentType!.MediaType.ShouldBe("application/problem+json");
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("code").GetString().ShouldBe("ShortUrl.NotFound");
        response.Headers.CacheControl!.NoStore.ShouldBeTrue();
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("has%20space")]
    [InlineData("abc-def")]
    [InlineData("1234567890123456789012345678901")]
    public async Task Get_MalformedCode_Returns404WithoutTouchingTheCacheOrTheDatabase(string code)
    {
        var repositoryCalls = _factory.Repository.Calls;
        var cacheReads = _factory.Cache.Reads;

        var response = await _client.GetAsync($"/{code}");

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        _factory.Repository.Calls.ShouldBe(repositoryCalls);
        _factory.Cache.Reads.ShouldBe(cacheReads);
    }

    [Fact]
    public async Task Get_ExpiredUrl_Returns410Gone()
    {
        Store("0aZ9xY4", "https://example.com/old", expiresAt: ReadApiFactory.Now.AddSeconds(-1));

        var response = await _client.GetAsync("/0aZ9xY4");

        response.StatusCode.ShouldBe(HttpStatusCode.Gone);
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("code").GetString().ShouldBe("ShortUrl.Expired");
        response.Headers.Location.ShouldBeNull();
    }

    [Fact]
    public async Task Get_UrlExpiringInTheFuture_StillRedirects()
    {
        Store("0aZ9xY5", "https://example.com/soon", expiresAt: ReadApiFactory.Now.AddMinutes(1));

        (await _client.GetAsync("/0aZ9xY5")).StatusCode.ShouldBe(HttpStatusCode.Found);
    }

    [Fact]
    public async Task Get_IsCaseSensitive()
    {
        Store("Abcdefgh", "https://example.com/upper");

        (await _client.GetAsync("/Abcdefgh")).StatusCode.ShouldBe(HttpStatusCode.Found);
        (await _client.GetAsync("/abcdefgh")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Get_CacheMiss_FillsTheCacheWithATtlCappedByTheExpiry()
    {
        Store("0aZ9xY6", "https://example.com/cached", expiresAt: ReadApiFactory.Now.AddMinutes(10));

        await _client.GetAsync("/0aZ9xY6");

        var (target, ttl) = _factory.Cache.Items["0aZ9xY6"];
        target.OriginalUrl.ShouldBe("https://example.com/cached");
        ttl.ShouldBe(TimeSpan.FromMinutes(10));
    }

    [Fact]
    public async Task Get_CacheHit_NeverReachesTheDatabase()
    {
        _factory.Cache.Items["0aZ9xY7"] = (new RedirectTarget("https://example.com/from-cache", null), TimeSpan.FromHours(1));
        var before = _factory.Repository.Calls;

        var response = await _client.GetAsync("/0aZ9xY7");

        response.Headers.Location!.OriginalString.ShouldBe("https://example.com/from-cache");
        _factory.Repository.Calls.ShouldBe(before);
    }

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("data:text/html,<script>1</script>")]
    [InlineData("file:///etc/passwd")]
    [InlineData("//evil.example/path")]
    public async Task Get_StoredUrlWithAnUnsafeScheme_IsNeverRedirectedTo(string url)
    {
        Store("0aZ9xY8", url);

        var response = await _client.GetAsync("/0aZ9xY8");

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        response.Headers.Location.ShouldBeNull();
    }

    [Fact]
    public async Task Get_WhenTheDatabaseFails_Returns500ProblemWithoutLeakingTheMessage()
    {
        using var factory = new ReadApiFactory();
        factory.Repository.FailWith = new InvalidOperationException("secret-connection-detail");
        using var client = factory.CreateNonRedirectingClient();

        var response = await client.GetAsync("/0aZ9xYb");

        response.StatusCode.ShouldBe(HttpStatusCode.InternalServerError);
        response.Content.Headers.ContentType!.MediaType.ShouldBe("application/problem+json");
        (await response.Content.ReadAsStringAsync()).ShouldNotContain("secret-connection-detail");
    }

    [Fact]
    public async Task Get_ManyConcurrentRequests_AllRedirect()
    {
        Store("0aZ9xY9", "https://example.com/busy");

        var responses = await Task.WhenAll(Enumerable.Range(0, 100).Select(_ => _client.GetAsync("/0aZ9xY9")));

        responses.ShouldAllBe(r => r.StatusCode == HttpStatusCode.Found);
    }

    [Fact]
    public async Task Health_Returns200()
    {
        (await _client.GetAsync("/health")).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Post_OnACode_Returns405BecauseCreatingBelongsToTheWriteService()
    {
        (await _client.PostAsync("/0aZ9xYb", null)).StatusCode.ShouldBe(HttpStatusCode.MethodNotAllowed);
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/a/b")]
    public async Task Get_NonCodePaths_Return404(string path)
    {
        (await _client.GetAsync(path)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }
}
