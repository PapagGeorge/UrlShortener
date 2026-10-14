using System.Net;
using UrlShortener.Gateway.Options;

namespace UrlShortener.Gateway.Tests.Options;

public sealed class GatewayOptionsTests
{
    private readonly RateLimitOptionsValidator _rateLimit = new();
    private readonly TrustedProxyOptionsValidator _proxies = new();

    [Fact]
    public void RateLimit_DefaultsAre30PerMinute()
    {
        var options = new RateLimitOptions();

        options.PermitLimit.ShouldBe(30);
        options.Window.ShouldBe(TimeSpan.FromMinutes(1));
        _rateLimit.Validate(null, options).Succeeded.ShouldBeTrue();
    }

    [Theory]
    [InlineData(0, 60)]
    [InlineData(-1, 60)]
    [InlineData(10_001, 60)]
    [InlineData(30, 0)]
    [InlineData(30, -5)]
    [InlineData(30, 86_401)]
    public void RateLimit_OutOfRange_IsInvalid(int permits, int windowSeconds)
    {
        var options = new RateLimitOptions { PermitLimit = permits, Window = TimeSpan.FromSeconds(windowSeconds) };

        _rateLimit.Validate(null, options).Failed.ShouldBeTrue();
    }

    [Fact]
    public void TrustedProxies_DefaultToNone_SoForwardedHeadersAreNeverTrusted()
    {
        var options = new TrustedProxyOptions();

        options.KnownProxies.ShouldBeEmpty();
        options.KnownNetworks.ShouldBeEmpty();
        _proxies.Validate(null, options).Succeeded.ShouldBeTrue();
    }

    [Fact]
    public void TrustedProxies_ValidAddressesAndNetworks_AreParsed()
    {
        var options = new TrustedProxyOptions { KnownProxies = ["10.0.0.5", "2001:db8::1"], KnownNetworks = ["172.16.0.0/12", "fd00::/8"] };

        _proxies.Validate(null, options).Succeeded.ShouldBeTrue();
        options.ParseProxies().ShouldBe([IPAddress.Parse("10.0.0.5"), IPAddress.Parse("2001:db8::1")]);
        options.ParseNetworks().Select(n => n.ToString()).ShouldBe(["172.16.0.0/12", "fd00::/8"]);
    }

    [Theory]
    [InlineData("not-an-ip")]
    [InlineData("10.0.0.256")]
    [InlineData("")]
    public void TrustedProxies_InvalidAddress_IsRejectedAtStartup(string address)
    {
        var result = _proxies.Validate(null, new TrustedProxyOptions { KnownProxies = [address] });

        result.Failed.ShouldBeTrue();
        result.FailureMessage!.ShouldContain("ForwardedHeaders:KnownProxies");
    }

    [Theory]
    [InlineData("10.0.0.0")]          // no prefix length
    [InlineData("10.0.0.0/33")]
    [InlineData("10.0.0.0/abc")]
    [InlineData("nonsense/8")]
    public void TrustedProxies_InvalidNetwork_IsRejectedAtStartup(string network)
    {
        var result = _proxies.Validate(null, new TrustedProxyOptions { KnownNetworks = [network] });

        result.Failed.ShouldBeTrue();
        result.FailureMessage!.ShouldContain("ForwardedHeaders:KnownNetworks");
    }

    [Theory]
    [InlineData("0.0.0.0/0")]
    [InlineData("::/0")]
    public void TrustedProxies_TrustingTheWholeInternet_IsRejected(string network)
    {
        // Trusting everyone would let any client spoof X-Forwarded-For and walk around the rate limit.
        var result = _proxies.Validate(null, new TrustedProxyOptions { KnownNetworks = [network] });

        result.Failed.ShouldBeTrue();
        result.FailureMessage!.ShouldContain("every address");
    }
}
