using System.Net;
using System.Text;
using System.Text.Json;
using UrlShortener.Gateway.Tests.Infrastructure;

namespace UrlShortener.Gateway.Tests;

public sealed class RateLimitTests(GatewayFixture gateway) : IClassFixture<GatewayFixture>
{
    private Task<HttpResponseMessage> Post(string clientIp, string? forwardedFor = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/urls") { Content = new StringContent("{}", Encoding.UTF8, "application/json") };
        request.Headers.Add(GatewayFactory.RemoteIpHeader, clientIp);
        if (forwardedFor is not null)
        {
            request.Headers.Add("X-Forwarded-For", forwardedFor);
        }

        return gateway.Client.SendAsync(request);
    }

    private Task<HttpResponseMessage> Get(string clientIp)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/0aZ9xYb");
        request.Headers.Add(GatewayFactory.RemoteIpHeader, clientIp);
        return gateway.Client.SendAsync(request);
    }

    [Fact]
    public async Task The31stCreateInAMinute_Returns429WithRetryAfterAndAProblemBody()
    {
        var ip = GatewayFixture.NewClientIp();
        var before = gateway.Write.Requests.Count;

        for (var i = 0; i < 30; i++)
        {
            (await Post(ip)).StatusCode.ShouldBe(HttpStatusCode.Created, $"request {i + 1} should pass");
        }

        var limited = await Post(ip);

        limited.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
        int.Parse(limited.Headers.GetValues("Retry-After").Single()).ShouldBeInRange(1, 60);
        limited.Content.Headers.ContentType!.MediaType.ShouldBe("application/problem+json");
        JsonDocument.Parse(await limited.Content.ReadAsStringAsync()).RootElement.GetProperty("code").GetString().ShouldBe("RateLimit.Exceeded");
        gateway.Write.Requests.Count.ShouldBe(before + 30);   // the rejected request never reached the Write Service
    }

    [Fact]
    public async Task EachClientAddress_HasItsOwnBudget()
    {
        var busy = GatewayFixture.NewClientIp();
        var other = GatewayFixture.NewClientIp();
        for (var i = 0; i < 31; i++)
        {
            await Post(busy);
        }

        (await Post(busy)).StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
        (await Post(other)).StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    [Fact]
    public async Task Redirects_AreNeverRateLimited()
    {
        var ip = GatewayFixture.NewClientIp();

        var responses = await Task.WhenAll(Enumerable.Range(0, 150).Select(_ => Get(ip)));

        responses.ShouldAllBe(r => r.StatusCode == HttpStatusCode.Found);
    }

    [Fact]
    public async Task Redirects_DoNotConsumeTheCreateBudget()
    {
        var ip = GatewayFixture.NewClientIp();
        await Task.WhenAll(Enumerable.Range(0, 100).Select(_ => Get(ip)));

        for (var i = 0; i < 30; i++)
        {
            (await Post(ip)).StatusCode.ShouldBe(HttpStatusCode.Created);
        }
    }

    [Fact]
    public async Task ASpoofedForwardedForHeader_CannotBuyExtraRequests_WhenTheSenderIsNotATrustedProxy()
    {
        var ip = GatewayFixture.NewClientIp();

        for (var i = 0; i < 30; i++)
        {
            await Post(ip, forwardedFor: $"198.51.100.{i}");   // a different "client" every time
        }

        (await Post(ip, forwardedFor: "198.51.100.200")).StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
    }

    [Fact]
    public async Task IPv6AddressesOfTheSame64Prefix_ShareOneBudget()
    {
        var prefix = $"2001:db8:{Random.Shared.Next(0x1000, 0xffff):x}:{Random.Shared.Next(0x1000, 0xffff):x}";
        for (var i = 0; i < 30; i++)
        {
            await Post($"{prefix}::{i + 1:x}");
        }

        (await Post($"{prefix}:aaaa:bbbb:cccc:dddd")).StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
    }

    [Fact]
    public async Task APost_ThatIsRejected_StillAnswersWithoutTouchingTheBackendCounter()
    {
        var ip = GatewayFixture.NewClientIp();
        for (var i = 0; i < 30; i++)
        {
            await Post(ip);
        }

        var before = gateway.Write.Requests.Count;
        await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => Post(ip)));

        gateway.Write.Requests.Count.ShouldBe(before);
    }
}
