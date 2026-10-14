using System.Net;
using System.Text;
using UrlShortener.Gateway.Tests.Infrastructure;

namespace UrlShortener.Gateway.Tests;

/// <summary>With a trusted proxy configured, the client address comes from X-Forwarded-For. Without one, it never does.</summary>
public sealed class ForwardedHeadersTests : IAsyncLifetime
{
    private const string TrustedProxy = "203.0.113.50";

    private FakeBackend _write = null!;
    private FakeBackend _read = null!;
    private GatewayFactory _trusting = null!;
    private GatewayFactory _default = null!;

    public async Task InitializeAsync()
    {
        _write = await FakeBackend.StartWriteAsync();
        _read = await FakeBackend.StartReadAsync();
        _trusting = new GatewayFactory(_write.Address, _read.Address, new Dictionary<string, string?> { ["ForwardedHeaders:KnownProxies:0"] = TrustedProxy });
        _default = new GatewayFactory(_write.Address, _read.Address);
    }

    public async Task DisposeAsync()
    {
        await _trusting.DisposeAsync();
        await _default.DisposeAsync();
        await _write.DisposeAsync();
        await _read.DisposeAsync();
    }

    private static Task<HttpResponseMessage> Post(HttpClient client, string remoteIp, string? forwardedFor)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/urls") { Content = new StringContent("{}", Encoding.UTF8, "application/json") };
        request.Headers.Add(GatewayFactory.RemoteIpHeader, remoteIp);
        if (forwardedFor is not null)
        {
            request.Headers.Add("X-Forwarded-For", forwardedFor);
        }

        return client.SendAsync(request);
    }

    [Fact]
    public async Task BehindATrustedProxy_EachRealClientHasItsOwnBudget()
    {
        using var client = _trusting.CreateNonRedirectingClient();
        var clientA = GatewayFixture.NewClientIp();
        var clientB = GatewayFixture.NewClientIp();

        for (var i = 0; i < 30; i++)
        {
            (await Post(client, TrustedProxy, clientA)).StatusCode.ShouldBe(HttpStatusCode.Created);
        }

        (await Post(client, TrustedProxy, clientA)).StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
        (await Post(client, TrustedProxy, clientB)).StatusCode.ShouldBe(HttpStatusCode.Created);   // not punished for A's traffic
    }

    [Fact]
    public async Task ATrustedProxysForwardedHeader_IsNotHonouredWhenSentByAnyoneElse()
    {
        using var client = _trusting.CreateNonRedirectingClient();
        var attacker = GatewayFixture.NewClientIp();

        for (var i = 0; i < 30; i++)
        {
            await Post(client, attacker, forwardedFor: GatewayFixture.NewClientIp());   // spoofing a new client each time
        }

        (await Post(client, attacker, forwardedFor: GatewayFixture.NewClientIp())).StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
    }

    [Fact]
    public async Task WithNoTrustedProxiesConfigured_TheForwardedHeaderIsIgnoredEvenFromTheProxyAddress()
    {
        using var client = _default.CreateNonRedirectingClient();

        for (var i = 0; i < 30; i++)
        {
            await Post(client, TrustedProxy, forwardedFor: GatewayFixture.NewClientIp());
        }

        (await Post(client, TrustedProxy, forwardedFor: GatewayFixture.NewClientIp())).StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
    }

    [Fact]
    public async Task TheBackend_SeesTheRealClientAddress_NotASpoofedOne()
    {
        using var client = _default.CreateNonRedirectingClient();
        var real = GatewayFixture.NewClientIp();
        var before = _write.Requests.Count;

        await Post(client, real, forwardedFor: "6.6.6.6, 7.7.7.7");

        var forwarded = _write.Requests.Skip(before).Single();
        forwarded.Headers["X-Forwarded-For"].ShouldBe(real);
        forwarded.Headers["X-Forwarded-For"].ShouldNotContain("6.6.6.6");
    }

    [Fact]
    public async Task TheBackend_SeesTheClientBehindATrustedProxy()
    {
        using var client = _trusting.CreateNonRedirectingClient();
        var realClient = GatewayFixture.NewClientIp();
        var before = _write.Requests.Count;

        await Post(client, TrustedProxy, forwardedFor: realClient);

        _write.Requests.Skip(before).Single().Headers["X-Forwarded-For"].ShouldBe(realClient);
    }
}
