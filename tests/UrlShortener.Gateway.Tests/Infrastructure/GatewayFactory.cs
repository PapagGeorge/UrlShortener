using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace UrlShortener.Gateway.Tests.Infrastructure;

/// <summary>
/// The real Gateway pipeline pointed at two fake backends. Tests simulate a client address with the <c>X-Test-Remote-Ip</c> header
/// (TestServer has no socket), applied before any forwarded-headers handling, exactly where Kestrel would have set it.
/// </summary>
public sealed class GatewayFactory(string writeAddress, string readAddress, IDictionary<string, string?>? settings = null) : WebApplicationFactory<Program>
{
    public const string RemoteIpHeader = "X-Test-Remote-Ip";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ReverseProxy:Clusters:write:Destinations:write-service:Address", writeAddress);
        builder.UseSetting("ReverseProxy:Clusters:read:Destinations:read-service:Address", readAddress);
        builder.UseSetting("ReverseProxy:Clusters:write:HealthCheck:Active:Enabled", "false");
        builder.UseSetting("ReverseProxy:Clusters:read:HealthCheck:Active:Enabled", "false");

        foreach (var (key, value) in settings ?? new Dictionary<string, string?>())
        {
            builder.UseSetting(key, value);
        }

        builder.ConfigureServices(services => services.AddSingleton<IStartupFilter, SimulatedRemoteIpFilter>());
    }

    public HttpClient CreateNonRedirectingClient() =>
        CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    private sealed class SimulatedRemoteIpFilter : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use(async (context, nextMiddleware) =>
            {
                if (context.Request.Headers.TryGetValue(RemoteIpHeader, out var value) && IPAddress.TryParse(value, out var address))
                {
                    context.Connection.RemoteIpAddress = address;
                }

                await nextMiddleware();
            });
            next(app);
        };
    }
}

public sealed class GatewayFixture : IAsyncLifetime
{
    private static int _ipCounter;

    public FakeBackend Write { get; private set; } = null!;

    public FakeBackend Read { get; private set; } = null!;

    public GatewayFactory Factory { get; private set; } = null!;

    public HttpClient Client { get; private set; } = null!;

    /// <summary>A fresh client address per call, so tests sharing one Gateway never share a rate-limit bucket.</summary>
    public static string NewClientIp()
    {
        var n = Interlocked.Increment(ref _ipCounter);
        return $"10.{(n >> 16) & 255}.{(n >> 8) & 255}.{n & 255}";
    }

    public async Task InitializeAsync()
    {
        Write = await FakeBackend.StartWriteAsync();
        Read = await FakeBackend.StartReadAsync();
        Factory = new GatewayFactory(Write.Address, Read.Address);
        Client = Factory.CreateNonRedirectingClient();
    }

    public async Task DisposeAsync()
    {
        Client.Dispose();
        await Factory.DisposeAsync();
        await Write.DisposeAsync();
        await Read.DisposeAsync();
    }
}
