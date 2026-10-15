using System.Net;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using UrlShortener.ServiceDefaults;

namespace UrlShortener.Gateway.Tests.ServiceDefaults;

/// <summary>Liveness (<c>/health</c>) never touches dependencies; readiness (<c>/health/ready</c>) runs the registered probes.</summary>
public sealed class ReadinessTests
{
    private static async Task<WebApplication> StartApp(Action<IHealthChecksBuilder>? configure = null)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.AddServiceDefaults();
        configure?.Invoke(builder.Services.AddHealthChecks());

        var app = builder.Build();
        app.UseServiceDefaults();
        app.MapDefaultEndpoints();
        await app.StartAsync();
        return app;
    }

    [Fact]
    public async Task Ready_WithNoProbes_Is200()
    {
        await using var app = await StartApp();

        (await app.GetTestClient().GetAsync("/health/ready")).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Ready_WhenEveryProbeSucceeds_Is200()
    {
        await using var app = await StartApp(checks => checks.AddReadinessProbe("db", (_, _) => Task.CompletedTask));

        (await app.GetTestClient().GetAsync("/health/ready")).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Ready_WhenAProbeThrows_Is503_AndTheResponseNeverLeaksTheMessage()
    {
        await using var app = await StartApp(checks =>
            checks.AddReadinessProbe("db", (_, _) => throw new InvalidOperationException("Host=secret-db;Password=hunter2")));

        var response = await app.GetTestClient().GetAsync("/health/ready");

        response.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
        (await response.Content.ReadAsStringAsync()).ShouldNotContain("hunter2");
    }

    [Fact]
    public async Task Ready_WhenAProbeHangs_Is503AfterTheTimeout()
    {
        await using var app = await StartApp(checks => checks.AddReadinessProbe(
            "slow", async (_, ct) => await Task.Delay(TimeSpan.FromSeconds(30), ct), timeout: TimeSpan.FromMilliseconds(200)));

        var started = DateTime.UtcNow;
        var response = await app.GetTestClient().GetAsync("/health/ready");

        response.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
        (DateTime.UtcNow - started).ShouldBeLessThan(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task Live_NeverRunsTheProbes_SoAnOutageDoesNotRestartTheContainer()
    {
        await using var app = await StartApp(checks => checks.AddReadinessProbe("db", (_, _) => throw new InvalidOperationException()));

        (await app.GetTestClient().GetAsync("/health")).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Ready_FailingProbeIsReportedByNameOnlyInTheLogsNotTheBody()
    {
        await using var app = await StartApp(checks => checks.AddReadinessProbe("redis", (_, _) => throw new InvalidOperationException()));

        var body = await (await app.GetTestClient().GetAsync("/health/ready")).Content.ReadAsStringAsync();

        body.ShouldBe("Unhealthy");
    }
}
