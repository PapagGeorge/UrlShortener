using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using UrlShortener.Gateway.Options;
using UrlShortener.Gateway.RateLimiting;
using Yarp.ReverseProxy.Configuration;

namespace UrlShortener.Gateway.Tests;

/// <summary>Pins the shipped appsettings.json: these are the production routes.</summary>
public sealed class ConfigurationTests : IClassFixture<ConfigurationTests.ShippedConfigFactory>
{
    private readonly ShippedConfigFactory _factory;

    public ConfigurationTests(ShippedConfigFactory factory) => _factory = factory;

    public sealed class ShippedConfigFactory : Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder) => builder.UseEnvironment("Testing");
    }

    private IProxyConfig Config() => _factory.Services.GetRequiredService<IProxyConfigProvider>().GetConfig();

    [Fact]
    public void CreateRoute_IsPostOnlyRateLimitedAndBodyLimitedAndTargetsTheWriteService()
    {
        var route = Config().Routes.Single(r => r.RouteId == "create");

        route.ClusterId.ShouldBe("write");
        route.Match.Path.ShouldBe("/urls");
        route.Match.Methods.ShouldBe(["POST"]);
        route.RateLimiterPolicy.ShouldBe("create-url");
        route.MaxRequestBodySize.ShouldBe(8192);
    }

    [Fact]
    public void RedirectRoute_IsReadOnlyNeverRateLimitedAndHasLowerPriorityThanCreate()
    {
        var config = Config();
        var create = config.Routes.Single(r => r.RouteId == "create");
        var redirect = config.Routes.Single(r => r.RouteId == "redirect");

        redirect.ClusterId.ShouldBe("read");
        redirect.Match.Methods.ShouldBe(["GET", "HEAD"], ignoreOrder: true);
        redirect.RateLimiterPolicy.ShouldBeNull();
        redirect.Order!.Value.ShouldBeGreaterThan(create.Order!.Value);
        redirect.Match.Path!.ShouldContain("regex");
    }

    [Fact]
    public void OnlyTwoRoutesExist_SoNothingElseCanBeProxied()
    {
        Config().Routes.Select(r => r.RouteId).ShouldBe(["create", "redirect"], ignoreOrder: true);
    }

    [Theory]
    [InlineData("write", "http://write-service:8080/")]
    [InlineData("read", "http://read-service:8080/")]
    public void Clusters_PointAtTheComposeServiceNames(string cluster, string address)
    {
        Config().Clusters.Single(c => c.ClusterId == cluster).Destinations!.Single().Value.Address.ShouldBe(address);
    }

    [Theory]
    [InlineData("write")]
    [InlineData("read")]
    public void Clusters_ActivelyHealthCheckTheServicesHealthEndpoint(string cluster)
    {
        var active = Config().Clusters.Single(c => c.ClusterId == cluster).HealthCheck!.Active!;

        active.Enabled.ShouldBe(true);
        active.Path.ShouldBe("/health");
        active.Interval.ShouldBe(TimeSpan.FromSeconds(10));
    }

    [Fact]
    public void RedirectsHaveATighterTimeoutThanCreation()
    {
        var clusters = Config().Clusters;

        clusters.Single(c => c.ClusterId == "read").HttpRequest!.ActivityTimeout!.Value
            .ShouldBeLessThan(clusters.Single(c => c.ClusterId == "write").HttpRequest!.ActivityTimeout!.Value);
    }

    [Fact]
    public void ShippedDefaults_Are30PerMinute_AndNoTrustedProxies()
    {
        _factory.Services.GetRequiredService<IOptions<RateLimitOptions>>().Value.PermitLimit.ShouldBe(30);
        _factory.Services.GetRequiredService<IOptions<RateLimitOptions>>().Value.Window.ShouldBe(TimeSpan.FromMinutes(1));
        var trusted = _factory.Services.GetRequiredService<IOptions<TrustedProxyOptions>>().Value;
        trusted.KnownProxies.ShouldBeEmpty();
        trusted.KnownNetworks.ShouldBeEmpty();
    }

    [Fact]
    public void WithNoTrustedProxies_ForwardedHeaderProcessingIsOff()
    {
        // An empty list would otherwise mean "trust everyone" to ASP.NET Core.
        var options = _factory.Services.GetRequiredService<IOptions<Microsoft.AspNetCore.Builder.ForwardedHeadersOptions>>().Value;

        options.ForwardedHeaders.ShouldBe(Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.None);
    }

    private sealed class BrokenConfigFactory(string key, string value) : Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting(key, value);
        }
    }

    [Theory]
    [InlineData("RateLimit:PermitLimit", "0")]
    [InlineData("RateLimit:Window", "00:00:00")]
    [InlineData("ForwardedHeaders:KnownProxies:0", "not-an-ip")]
    [InlineData("ForwardedHeaders:KnownNetworks:0", "0.0.0.0/0")]
    public void InvalidSettings_AreRejectedByStartupValidation_NamingTheSetting(string key, string value)
    {
        // Deterministic: runs the same ValidateOnStart check the host runs, without starting a host.
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { [key] = value }).Build();
        var services = new ServiceCollection();
        services.AddGatewayRateLimiting(configuration);
        services.AddGatewayForwardedHeaders(configuration);
        using var provider = services.BuildServiceProvider();

        var failure = Should.Throw<OptionsValidationException>(() => provider.GetRequiredService<IStartupValidator>().Validate());

        var setting = string.Join(':', key.Split(':').Where(part => !int.TryParse(part, out _)));   // messages name the list, not the index
        failure.Message.ShouldContain(setting);
    }

    [Fact]
    public void AnInvalidSetting_StopsTheHostFromStarting()
    {
        // Which exception surfaces first is a startup race (other hosted services are torn down at the same time), so only the fact is asserted here.
        using var broken = new BrokenConfigFactory("RateLimit:PermitLimit", "0");

        Should.Throw<Exception>(() => broken.CreateClient());
    }
}
