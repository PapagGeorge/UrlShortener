using System.Net;
using System.Text;
using UrlShortener.Gateway.Tests.Infrastructure;

namespace UrlShortener.Gateway.Tests;

public sealed class RoutingTests(GatewayFixture gateway) : IClassFixture<GatewayFixture>
{
    private HttpRequestMessage Request(HttpMethod method, string path, HttpContent? content = null)
    {
        var request = new HttpRequestMessage(method, path) { Content = content };
        request.Headers.Add(GatewayFactory.RemoteIpHeader, GatewayFixture.NewClientIp());
        return request;
    }

    private static StringContent Json(string body) => new(body, Encoding.UTF8, "application/json");

    [Fact]
    public async Task PostUrls_GoesToTheWriteServiceWithItsBody()
    {
        var before = gateway.Write.Requests.Count;

        var response = await gateway.Client.SendAsync(Request(HttpMethod.Post, "/urls", Json("""{"originalUrl":"https://example.com/a"}""")));

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        (await response.Content.ReadAsStringAsync()).ShouldContain("shortUrl");
        var forwarded = gateway.Write.Requests.Skip(before).Single();
        forwarded.Method.ShouldBe("POST");
        forwarded.PathAndQuery.ShouldBe("/urls");
        forwarded.BodyLength.ShouldBe("""{"originalUrl":"https://example.com/a"}""".Length);
        forwarded.Headers["Content-Type"].ShouldStartWith("application/json");
    }

    [Theory]
    [InlineData("GET")]
    [InlineData("HEAD")]
    public async Task GetOrHeadOnACode_GoesToTheReadService_AndTheRedirectIsPassedThroughUntouched(string method)
    {
        var before = gateway.Read.Requests.Count;

        var response = await gateway.Client.SendAsync(Request(new HttpMethod(method), "/0aZ9xYb"));

        response.StatusCode.ShouldBe(HttpStatusCode.Found);
        response.Headers.Location!.OriginalString.ShouldBe("https://example.com/target");
        response.Headers.CacheControl!.NoStore.ShouldBeTrue();
        var forwarded = gateway.Read.Requests.Skip(before).Single();
        forwarded.Method.ShouldBe(method);
        forwarded.PathAndQuery.ShouldBe("/0aZ9xYb");
    }

    [Theory]
    [InlineData("my-custom-alias")]
    [InlineData("123456789012345678901234567890")]   // 30 characters: the longest alias
    [InlineData("0000000")]
    public async Task ValidCodes_ReachTheReadService(string code)
    {
        var before = gateway.Read.Requests.Count;

        (await gateway.Client.SendAsync(Request(HttpMethod.Get, $"/{code}"))).StatusCode.ShouldBe(HttpStatusCode.Found);

        gateway.Read.Requests.Count.ShouldBe(before + 1);
    }

    [Theory]
    [InlineData("/abc")]                                  // too short
    [InlineData("/abcdef")]                               // 6 characters
    [InlineData("/1234567890123456789012345678901")]      // 31 characters
    [InlineData("/has%20space")]
    [InlineData("/dot.dot.dot")]
    [InlineData("/a/b")]
    [InlineData("/")]
    public async Task ThingsThatAreNotCodes_NeverReachABackend(string path)
    {
        var writeBefore = gateway.Write.Requests.Count;
        var readBefore = gateway.Read.Requests.Count;

        var response = await gateway.Client.SendAsync(Request(HttpMethod.Get, path));

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        gateway.Write.Requests.Count.ShouldBe(writeBefore);
        gateway.Read.Requests.Count.ShouldBe(readBefore);
    }

    [Theory]
    [InlineData("GET", "/urls")]
    [InlineData("PUT", "/urls")]
    [InlineData("DELETE", "/urls")]
    [InlineData("PATCH", "/urls")]
    [InlineData("POST", "/0aZ9xYb")]      // creating belongs to POST /urls only
    [InlineData("DELETE", "/0aZ9xYb")]
    [InlineData("PUT", "/0aZ9xYb")]
    public async Task OtherMethodsOnKnownPaths_NeverReachABackend(string method, string path)
    {
        var writeBefore = gateway.Write.Requests.Count;
        var readBefore = gateway.Read.Requests.Count;

        var response = await gateway.Client.SendAsync(Request(new HttpMethod(method), path, Json("{}")));

        ((int)response.StatusCode).ShouldBeInRange(404, 405);
        gateway.Write.Requests.Count.ShouldBe(writeBefore);
        gateway.Read.Requests.Count.ShouldBe(readBefore);
    }

    [Fact]
    public async Task Health_IsAnsweredByTheGatewayItself()
    {
        var writeBefore = gateway.Write.Requests.Count;
        var readBefore = gateway.Read.Requests.Count;

        var response = await gateway.Client.GetAsync("/health");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).ShouldBe("Healthy");
        gateway.Write.Requests.Count.ShouldBe(writeBefore);
        gateway.Read.Requests.Count.ShouldBe(readBefore);
    }

    [Fact]
    public async Task QueryStringOnARedirect_IsForwarded()
    {
        var before = gateway.Read.Requests.Count;

        await gateway.Client.SendAsync(Request(HttpMethod.Get, "/0aZ9xYb?utm_source=mail"));

        gateway.Read.Requests.Skip(before).Single().PathAndQuery.ShouldBe("/0aZ9xYb?utm_source=mail");
    }
}
